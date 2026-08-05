using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using FlutterIntegration;

/// <summary>
/// Photo mode: one tap on the camera button takes a clean screenshot of the garden — no HUD in the
/// shot — flashes the screen, and opens a preview the player can share or save to their gallery.
///
/// The capture itself is the fiddly part. Screen-space-overlay canvases are drawn straight into the
/// backbuffer and are not rendered by any camera, so there is no layer mask or culling trick that
/// leaves them out: the UI has to actually be switched off for the frame being grabbed. That is why
/// every root Canvas is disabled, the frame is allowed to finish, the pixels are read, and everything
/// is switched back on — all inside a single coroutine, so the player never sees a UI-less frame.
///
/// Sharing goes through Flutter. Unity writes a PNG into its own storage and sends the path across the
/// bridge; the host app owns the share sheet and the gallery permission. See flutter_bridge_guide.md.
/// </summary>
public class PhotoModeManager : MonoBehaviour
{
    public static PhotoModeManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Trigger")]
    [Tooltip("The camera button on the HUD. Taking a photo is a single tap — there is no separate framing mode.")]
    public Button photoButton;

    [Header("Flash")]
    [Tooltip("Full-screen white image, alpha 0, raycast target off. Flashed after the shot is taken.")]
    public Image flashImage;

    [Tooltip("How bright the flash gets.")]
    [Range(0f, 1f)]
    public float flashPeakAlpha = 0.85f;

    [Tooltip("How long the flash takes to fade out.")]
    [Range(0.05f, 1.5f)]
    public float flashFadeDuration = 0.45f;

    [Header("Preview")]
    [Tooltip("Root of the preview. A full-screen image that dims the garden and swallows taps, so the " +
             "player cannot drive the character around behind the open window.")]
    public GameObject previewPanel;

    [Tooltip("The framed window inside the dimmer. This is what scales in when a photo is taken; " +
             "falls back to the root above if left empty.")]
    public RectTransform previewWindow;

    [Tooltip("Displays the photo. Set to Preserve Aspect so any screen shape fits the frame.")]
    public Image previewImage;

    [Tooltip("Optional caption above the photo.")]
    public TMP_Text previewTitle;

    [Header("Preview Buttons")]
    public Button shareButton;
    public Button saveButton;
    public Button closePreviewButton;

    [Header("Sharing")]
    [Tooltip("Text suggested to the share sheet alongside the image.")]
    [TextArea(1, 3)]
    public string shareCaption = "My Jannah Garden 🌿";

    [Tooltip("Title shown above the photo in the preview window.")]
    public string previewHeading = "Your Jannah Garden";

    [Header("Storage")]
    [Tooltip("How many photos to keep on the device. The oldest are deleted beyond this, so a player " +
             "taking hundreds of shots does not slowly fill their storage.")]
    [Range(1, 50)]
    public int keepLastPhotos = 8;

    // ─── State ────────────────────────────────────────────────────────────────

    private const string FilePrefix = "JannahGarden_";
    private const string FileExtension = ".png";

    private bool _isCapturing;
    private Texture2D _photoTexture;
    private Sprite _photoSprite;
    private string _photoPath;
    private int _photoWidth;
    private int _photoHeight;

    /// <summary>True from the moment the shutter fires until the UI is back on screen.</summary>
    public bool IsCapturing => _isCapturing;

    /// <summary>True while the preview window is up.</summary>
    public bool IsPreviewOpen => previewPanel != null && previewPanel.activeSelf;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (photoButton != null) photoButton.onClick.AddListener(TakePhoto);
        if (shareButton != null) shareButton.onClick.AddListener(SharePhoto);
        if (saveButton != null) saveButton.onClick.AddListener(SavePhoto);
        if (closePreviewButton != null) closePreviewButton.onClick.AddListener(ClosePreview);

        if (previewPanel != null) previewPanel.SetActive(false);

        if (flashImage != null)
        {
            flashImage.raycastTarget = false;
            SetFlashAlpha(0f);
        }

        FlutterBridge.OnPhotoActionResult += HandlePhotoActionResult;

        PrunePhotoLibrary();
    }

    private void OnDestroy()
    {
        FlutterBridge.OnPhotoActionResult -= HandlePhotoActionResult;

        if (photoButton != null) photoButton.onClick.RemoveListener(TakePhoto);
        if (shareButton != null) shareButton.onClick.RemoveListener(SharePhoto);
        if (saveButton != null) saveButton.onClick.RemoveListener(SavePhoto);
        if (closePreviewButton != null) closePreviewButton.onClick.RemoveListener(ClosePreview);

        if (flashImage != null) flashImage.DOKill();
        if (previewPanel != null) PreviewWindowTransform.DOKill();

        ReleasePhoto();

        if (Instance == this) Instance = null;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Takes a photo of the garden. Safe to hook straight to a UI Button. Ignored while a capture is
    /// already running or the preview is open.
    /// </summary>
    public void TakePhoto()
    {
        if (_isCapturing || IsPreviewOpen) return;
        if (!isActiveAndEnabled) return;

        StartCoroutine(CaptureRoutine());
    }

    /// <summary>Hands the current photo to the app to share.</summary>
    public void SharePhoto()
    {
        if (!HasPhotoOnDisk("share")) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);

        FlutterBridge.Instance.RequestSharePhoto(BuildPayload());
    }

    /// <summary>Asks the app to write the current photo into the device gallery.</summary>
    public void SavePhoto()
    {
        if (!HasPhotoOnDisk("save")) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);

        FlutterBridge.Instance.RequestSavePhoto(BuildPayload());
    }

    /// <summary>Closes the preview. The file stays on the device until it ages out of the library.</summary>
    public void ClosePreview()
    {
        if (previewPanel == null) return;

        PreviewWindowTransform.DOKill();
        previewPanel.SetActive(false);

        // The texture is only needed while it is on screen; a full-screen RGB24 grab is several MB.
        ReleasePhoto();
    }

    // ─── Capture ──────────────────────────────────────────────────────────────

    private IEnumerator CaptureRoutine()
    {
        _isCapturing = true;

        // Any previous shot is about to be replaced.
        ReleasePhoto();

        List<Canvas> hidden = HideAllCanvases();

        // The backbuffer only holds a finished frame at the end of one. Reading before this point
        // gives a partially drawn image, or the previous frame.
        yield return new WaitForEndOfFrame();

        Texture2D shot = null;
        try
        {
            shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
            shot.Apply();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PhotoModeManager] Could not read the screen: {e.Message}");
            if (shot != null) Destroy(shot);
            shot = null;
        }
        finally
        {
            // Whatever happened, the player must not be left with no UI.
            RestoreCanvases(hidden);
        }

        _isCapturing = false;

        if (shot == null)
        {
            ShowToast("Could not take the photo");
            yield break;
        }

        _photoTexture = shot;
        _photoWidth = shot.width;
        _photoHeight = shot.height;

        PlayFlash();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.CameraShutter);

        _photoPath = WritePhoto(shot);

        ShowPreview();
        PrunePhotoLibrary();
    }

    /// <summary>
    /// Switches off every enabled root canvas and returns the ones it touched, so exactly those can be
    /// switched back on. Disabling the Canvas component rather than the GameObject keeps every script
    /// on the UI running — including this coroutine, which lives on one.
    /// </summary>
    private List<Canvas> HideAllCanvases()
    {
        List<Canvas> hidden = new List<Canvas>();

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || !canvas.isRootCanvas || !canvas.enabled) continue;

            canvas.enabled = false;
            hidden.Add(canvas);
        }

        return hidden;
    }

    private static void RestoreCanvases(List<Canvas> hidden)
    {
        if (hidden == null) return;

        foreach (Canvas canvas in hidden)
        {
            if (canvas != null) canvas.enabled = true;
        }
    }

    private void PlayFlash()
    {
        if (flashImage == null) return;

        flashImage.DOKill();
        SetFlashAlpha(flashPeakAlpha);
        flashImage.DOFade(0f, flashFadeDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    private void SetFlashAlpha(float alpha)
    {
        Color color = flashImage.color;
        color.a = alpha;
        flashImage.color = color;
    }

    // ─── Preview ──────────────────────────────────────────────────────────────

    private void ShowPreview()
    {
        if (previewImage != null && _photoTexture != null)
        {
            _photoSprite = Sprite.Create(
                _photoTexture,
                new Rect(0f, 0f, _photoTexture.width, _photoTexture.height),
                new Vector2(0.5f, 0.5f));

            previewImage.sprite = _photoSprite;
            previewImage.preserveAspect = true;
            previewImage.color = Color.white;
        }

        if (previewTitle != null) previewTitle.text = previewHeading;

        if (previewPanel == null) return;

        previewPanel.SetActive(true);

        // Let the window arrive with the flash rather than appearing under it.
        Transform window = PreviewWindowTransform;
        window.DOKill();
        window.localScale = Vector3.one * 0.85f;
        window.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
    }

    private Transform PreviewWindowTransform =>
        previewWindow != null ? (Transform)previewWindow : previewPanel.transform;

    // ─── Files ────────────────────────────────────────────────────────────────

    /// <summary>Writes the PNG and returns its path, or null when it could not be written.</summary>
    private string WritePhoto(Texture2D photo)
    {
        try
        {
            string fileName = FilePrefix
                + System.DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                + FileExtension;
            string path = Path.Combine(Application.persistentDataPath, fileName);

            File.WriteAllBytes(path, photo.EncodeToPNG());

            Debug.Log($"[PhotoModeManager] Photo saved to {path}");
            return path;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PhotoModeManager] Could not write the photo: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Keeps only the most recent <see cref="keepLastPhotos"/> files. Photos are full-screen PNGs of a
    /// few MB each, and nothing else ever deletes them — a player who takes one a day would otherwise
    /// carry every photo they have ever taken.
    /// </summary>
    private void PrunePhotoLibrary()
    {
        try
        {
            string[] files = Directory.GetFiles(Application.persistentDataPath, FilePrefix + "*" + FileExtension);
            if (files.Length <= keepLastPhotos) return;

            // The timestamped names sort chronologically, so the oldest are at the front.
            System.Array.Sort(files, System.StringComparer.Ordinal);

            for (int i = 0; i < files.Length - keepLastPhotos; i++)
            {
                if (files[i] == _photoPath) continue;
                File.Delete(files[i]);
            }
        }
        catch (System.Exception e)
        {
            // Housekeeping is never worth interrupting the player for.
            Debug.LogWarning($"[PhotoModeManager] Could not tidy up old photos: {e.Message}");
        }
    }

    private void ReleasePhoto()
    {
        if (previewImage != null) previewImage.sprite = null;

        if (_photoSprite != null)
        {
            Destroy(_photoSprite);
            _photoSprite = null;
        }

        if (_photoTexture != null)
        {
            Destroy(_photoTexture);
            _photoTexture = null;
        }
    }

    // ─── Flutter ──────────────────────────────────────────────────────────────

    private PhotoSharePayload BuildPayload()
    {
        return new PhotoSharePayload
        {
            filePath = _photoPath,
            caption = shareCaption,
            source = "photo_mode",
            width = _photoWidth,
            height = _photoHeight
        };
    }

    /// <summary>Guards the share/save buttons against a photo that never made it to disk.</summary>
    private bool HasPhotoOnDisk(string action)
    {
        if (string.IsNullOrEmpty(_photoPath))
        {
            Debug.LogWarning($"[PhotoModeManager] Cannot {action} — the photo was not written to disk.");
            ShowToast("This photo could not be saved to the device");
            return false;
        }

        if (FlutterBridge.Instance == null)
        {
            Debug.LogWarning($"[PhotoModeManager] Cannot {action} — no FlutterBridge. The app owns sharing.");
            ShowToast("Sharing is unavailable right now");
            return false;
        }

        return true;
    }

    private void HandlePhotoActionResult(PhotoActionResultPayload result)
    {
        if (result == null) return;

        if (!result.success)
        {
            ShowToast(string.IsNullOrEmpty(result.message) ? "That didn't work" : result.message);
            return;
        }

        ShowToast(result.action == PhotoAction.Save ? "Saved to your gallery" : "Shared");
    }

    private static void ShowToast(string message)
    {
        if (ToastMessageManager.Instance != null) ToastMessageManager.Instance.ShowToast(message);
    }
}
