using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// View for a single fellow's floating profile card in the garden.
///
/// Everything on the card lives in world space: the visuals are SpriteRenderers and the labels are
/// 3D TextMeshPro. Attach to the "Fellow Profile" root and wire the references to the children:
///   Fellow Profile            → this component
///   ├── Name Text             → nameText
///   ├── Member Since Text     → memberSinceText
///   ├── Noor Coin Icon        → noorCoinIcon
///   ├── Noor Coin Count Text  → noorCoinCountText
///   └── Profile Picture BG    → profilePictureBG
///       └── Circle            → profilePicture
///
/// The card is populated purely through <see cref="Bind"/>, so the data source can be swapped
/// (dummy JSON today, backend/Flutter later) without touching the prefab.
/// </summary>
public class FellowProfileObject : MonoBehaviour
{
    [Header("Profile References")]
    public TMP_Text nameText;
    public TMP_Text memberSinceText;
    public SpriteRenderer noorCoinIcon;
    public TMP_Text noorCoinCountText;
    public SpriteRenderer profilePictureBG;

    [Tooltip("The 'Circle' child under Profile Picture BG that displays the avatar.")]
    public SpriteRenderer profilePicture;

    [Header("Fallbacks")]
    [Tooltip("Shown when a fellow has no avatar, or the sprite path fails to resolve.")]
    public Sprite defaultProfilePicture;

    [Tooltip("Shown in place of the join date when a profile's memberSince is missing or malformed.")]
    public string unknownMemberSinceText = "Member since —";

    [Header("Billboard")]
    [Tooltip("Rotates the card to face the camera each frame. Leave on for world-space cards.")]
    public bool faceCamera = true;

    [Tooltip("What gets rotated to face the camera. Defaults to this transform.")]
    public Transform billboardRoot;

    /// <summary>The data currently displayed. Null until <see cref="Bind"/> is called.</summary>
    public FellowProfileData Data { get; private set; }

    /// <summary>Set by <see cref="FellowshipVisualizationManager"/> so the point can be freed on despawn.</summary>
    [HideInInspector] public Transform spawnPoint;

    private Camera cachedCamera;

    /// <summary>
    /// Downloaded avatars, shared across every card. Two fellows with the same avatar URL — and the same
    /// fellow respawned onto a new point — reuse one texture instead of re-downloading.
    /// </summary>
    private static readonly Dictionary<string, Sprite> remoteAvatarCache = new Dictionary<string, Sprite>();

    private void Awake()
    {
        if (billboardRoot == null) billboardRoot = transform;
    }

    /// <summary>
    /// Populates the card from a profile. Safe to call repeatedly (e.g. on a pooled object).
    /// </summary>
    public void Bind(FellowProfileData data)
    {
        Data = data;

        if (data == null)
        {
            Debug.LogWarning($"[FellowProfileObject] Bind called with null data on '{name}'.", this);
            return;
        }

        if (nameText != null)
            nameText.text = data.userName;

        if (memberSinceText != null)
            memberSinceText.text = FormatMemberSince(data);

        if (noorCoinCountText != null)
            noorCoinCountText.text = data.noorCoins.ToString("N0", CultureInfo.InvariantCulture);

        ApplyProfilePicture(data.profileImagePath);
    }

    /// <summary>
    /// Avatars arrive one of two ways: a Resources path (dummy/local data) or an http(s) URL (real user
    /// avatars from Flutter). Remote ones show the default while they download.
    /// </summary>
    private void ApplyProfilePicture(string path)
    {
        if (profilePicture == null) return;

        if (string.IsNullOrEmpty(path))
        {
            profilePicture.sprite = defaultProfilePicture;
            return;
        }

        if (IsRemote(path))
        {
            if (remoteAvatarCache.TryGetValue(path, out Sprite cached) && cached != null)
            {
                profilePicture.sprite = cached;
                return;
            }

            profilePicture.sprite = defaultProfilePicture;

            // StartCoroutine throws on an inactive object — a pooled card bound before it is re-enabled.
            if (isActiveAndEnabled) StartCoroutine(DownloadAvatar(path));
            else Debug.LogWarning($"[FellowProfileObject] '{name}' is inactive; skipping avatar download.", this);

            return;
        }

        profilePicture.sprite = ResolveProfileSprite(path);
    }

    private static bool IsRemote(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private IEnumerator DownloadAvatar(string url)
    {
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(
                $"[FellowProfileObject] Failed to download avatar '{url}': {request.error}. Keeping the default avatar.",
                this);
            yield break;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));

        remoteAvatarCache[url] = sprite;

        // The card may have been rebound to a different fellow (or pooled away) mid-download.
        if (profilePicture != null && Data != null && Data.profileImagePath == url)
        {
            profilePicture.sprite = sprite;
        }
    }

    /// <summary>
    /// Renders the join date as e.g. "Member since Mar 2024", routed through the localization
    /// dictionary so the label can be translated.
    /// </summary>
    private string FormatMemberSince(FellowProfileData data)
    {
        if (!data.TryGetMemberSince(out DateTime joined))
            return unknownMemberSinceText;

        const string key = "Member since {0}";
        string format = JannahGarden.Localization.LocalizationManager.Instance.GetTranslation(key, key);

        string joinedLabel = joined.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        return string.Format(format, joinedLabel);
    }

    /// <summary>
    /// Loads the avatar from Resources, falling back to <see cref="defaultProfilePicture"/> when the
    /// path is empty or does not resolve. Resources.Load caches internally, so repeated calls are cheap.
    /// </summary>
    private Sprite ResolveProfileSprite(string path)
    {
        if (string.IsNullOrEmpty(path))
            return defaultProfilePicture;

        Sprite loaded = Resources.Load<Sprite>(path);
        if (loaded == null)
        {
            Debug.LogWarning(
                $"[FellowProfileObject] No sprite at Resources path '{path}' for '{Data?.userName}'. Using the default avatar.",
                this);
            return defaultProfilePicture;
        }

        return loaded;
    }

    private void LateUpdate()
    {
        if (!faceCamera || billboardRoot == null) return;

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
            if (cachedCamera == null) return;
        }

        // Match the camera's rotation rather than LookAt-ing it: sprites and TMP text face +Z, so
        // pointing +Z back at the camera lands them 180° around Y (mirrored/back-facing).
        billboardRoot.rotation = cachedCamera.transform.rotation;
    }
}
