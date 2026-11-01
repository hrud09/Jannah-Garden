using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Drives the Jannah Garden opening the first time the scene is entered in a given app session:
/// the closed door artwork is shown with an "Open" button in the middle of the screen, and only
/// when the player presses it does the door-opening cinematic play. When the video ends the panel
/// fades out on its own and the player is in the garden — there is no skip step any more.
///
/// Attach to the "Opening Canvas" root; <see cref="OpenDoor"/> can also be wired to a UI button
/// directly from the inspector (the button referenced by <see cref="openButton"/> is wired in code).
///
/// "openingSceneContents" (the parent of Door Image / Intro Video / Open Button) is kept
/// inactive by default in the editor so those full-screen UI elements don't sit on top of
/// the scene while working in it — this script is what turns it on, only when the intro is
/// actually about to run.
/// </summary>
public class IntroVideoController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Parent of Door Image / Intro Video / Open Button. Kept inactive by default " +
             "(in the editor) so the intro UI doesn't block working in the scene; this script " +
             "activates it only when the intro is about to run.")]
    public GameObject openingSceneContents;
    public VideoPlayer videoPlayer;
    public RawImage videoImage;
    public AspectRatioFitter aspectRatioFitter;
    public GameObject doorImage;

    [Tooltip("Centre-screen button the player presses to open the door and start the cinematic.")]
    [FormerlySerializedAs("skipButton")]
    public Button openButton;

    public CanvasGroup canvasGroup;

    [Header("Behaviour")]
    [Tooltip("If true, the intro only runs once per app session; later (re)loads of this scene skip straight past it.")]
    public bool playOncePerSession = true;

    [Tooltip("Video playback speed. 1 = normal, 1.5 = 50% faster.")]
    public float playbackSpeed = 1.5f;

    [Tooltip("If true, the intro video plays without audio.")]
    public bool mute = false;

    [Tooltip("Fallback render texture size used when the VideoPlayer targets a RenderTexture at runtime.")]
    public Vector2Int renderTextureSize = new Vector2Int(1920, 1080);

    public float fadeOutDuration = 0.6f;

    [Tooltip("Safety net: if the video hasn't started playing this many seconds after Open is " +
             "pressed, go straight into the garden. Without a skip button this is the only way " +
             "out of a video that fails to prepare on a given device.")]
    public float playbackStartTimeout = 8f;

    private static bool s_hasPlayedThisSession;

    // Explicitly reset on every session start (app launch, or Editor Play Mode entry) rather
    // than relying on domain reload to clear the static field — with "Reload Domain" disabled
    // in Editor > Project Settings > Enter Play Mode Settings, static fields survive between
    // Play sessions, which otherwise made the intro skip itself on every run after the first.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetSessionState()
    {
        s_hasPlayedThisSession = false;
    }

    private RenderTexture _runtimeTexture;
    private bool _ending;
    private bool _prepared;
    private bool _openRequested;

    private void Awake()
    {
        AutoResolveReferences();

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Start hidden/non-interactive; BeginIntro() is the only thing that turns this on.
        if (openingSceneContents != null) openingSceneContents.SetActive(false);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;

        if (playOncePerSession && s_hasPlayedThisSession)
        {
            EndImmediate();
            return;
        }

        BeginIntro();
    }

    /// <summary>
    /// Shows the closed door with the Open button and starts preparing the video in the
    /// background, so that pressing Open can start playback immediately.
    /// </summary>
    private void BeginIntro()
    {
        if (openingSceneContents != null) openingSceneContents.SetActive(true);

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;

        // Toggle the RawImage's own enabled state (not the GameObject's) since the
        // VideoPlayer/AudioSource live on the same GameObject and must stay active to prepare.
        if (videoImage != null) videoImage.enabled = false;
        if (doorImage != null) doorImage.SetActive(true);

        if (openButton != null)
        {
            openButton.gameObject.SetActive(true);
            openButton.interactable = true;
            openButton.onClick.AddListener(OpenDoor);
        }

        if (videoPlayer == null) return;

        if (videoPlayer.renderMode == VideoRenderMode.RenderTexture && videoPlayer.targetTexture == null)
        {
            _runtimeTexture = new RenderTexture(renderTextureSize.x, renderTextureSize.y, 0);
            videoPlayer.targetTexture = _runtimeTexture;
            if (videoImage != null) videoImage.texture = _runtimeTexture;
        }

        AudioSource audioSource = videoPlayer.GetComponent<AudioSource>();
        if (videoPlayer.audioOutputMode == VideoAudioOutputMode.AudioSource && audioSource != null)
        {
            videoPlayer.SetTargetAudioSource(0, audioSource);
            audioSource.mute = mute;
        }

        videoPlayer.playOnAwake = false;
        videoPlayer.playbackSpeed = playbackSpeed;
        videoPlayer.SetDirectAudioMute(0, mute);
        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.loopPointReached += OnVideoFinished;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.Prepare();
    }

    /// <summary>
    /// Fills in any inspector references left unassigned by locating the standard child
    /// hierarchy this panel is built with: openingSceneContents, and under it,
    /// Door Image / Intro Video / Open Button.
    /// </summary>
    private void AutoResolveReferences()
    {
        if (openingSceneContents == null)
        {
            Transform contentsTrans = transform.Find("openingSceneContents");
            if (contentsTrans != null) openingSceneContents = contentsTrans.gameObject;
        }

        Transform searchRoot = openingSceneContents != null ? openingSceneContents.transform : transform;

        if (videoPlayer == null) videoPlayer = searchRoot.GetComponentInChildren<VideoPlayer>(true);
        if (videoImage == null) videoImage = searchRoot.GetComponentInChildren<RawImage>(true);
        if (aspectRatioFitter == null) aspectRatioFitter = searchRoot.GetComponentInChildren<AspectRatioFitter>(true);
        if (openButton == null) openButton = searchRoot.GetComponentInChildren<Button>(true);

        if (doorImage == null)
        {
            Transform doorTrans = searchRoot.Find("Door Image");
            if (doorTrans != null) doorImage = doorTrans.gameObject;
        }
    }

    /// <summary>
    /// Wired to the Open button. Hides the button and plays the door-opening cinematic; if the
    /// video is still preparing, playback starts as soon as it is ready (the door artwork stays
    /// up in the meantime, so there is no black flash).
    /// </summary>
    public void OpenDoor()
    {
        if (_ending || _openRequested) return;
        _openRequested = true;

        if (openButton != null)
        {
            openButton.interactable = false;
            openButton.onClick.RemoveListener(OpenDoor);
            openButton.gameObject.SetActive(false);
        }

        // No video to play (missing reference or a load failure) — go straight into the garden.
        if (videoPlayer == null)
        {
            EndIntro();
            return;
        }

        if (_prepared) StartPlayback();
        // Otherwise OnPrepared() picks this up via _openRequested.

        StartCoroutine(FailsafeAfterOpen());
    }

    /// <summary>
    /// If the cinematic never actually starts (the video failed to prepare on this device, for
    /// instance), don't leave the player staring at a door they can't get through.
    /// </summary>
    private IEnumerator FailsafeAfterOpen()
    {
        float t = 0f;
        while (t < playbackStartTimeout)
        {
            if (_ending) yield break;
            if (videoPlayer != null && videoPlayer.isPlaying && videoPlayer.frame > 0) yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_ending) yield break;

        Debug.LogWarning("[IntroVideoController] Intro video didn't start within " +
                         playbackStartTimeout + "s of pressing Open; entering the garden anyway.");
        EndIntro();
    }

    private void OnPrepared(VideoPlayer vp)
    {
        _prepared = true;

        if (aspectRatioFitter != null && vp.width > 0 && vp.height > 0)
        {
            aspectRatioFitter.aspectRatio = (float)vp.width / vp.height;
        }

        vp.playbackSpeed = playbackSpeed;

        if (_openRequested) StartPlayback();
    }

    private void StartPlayback()
    {
        if (_ending || videoPlayer == null) return;

        // Start playback while still hidden behind the door image, and only reveal the
        // video once it has an actual decoded frame ready. This avoids swapping to the
        // RawImage a frame (or more) before the video texture has anything in it, which
        // would otherwise show a black/stale flash between the door image and the video.
        videoPlayer.Play();
        StartCoroutine(RevealVideoOnceFrameReady(videoPlayer));
    }

    private IEnumerator RevealVideoOnceFrameReady(VideoPlayer vp)
    {
        while (vp != null && vp.isPlaying && vp.frame <= 0)
        {
            yield return null;
        }

        if (_ending) yield break;

        if (doorImage != null) doorImage.SetActive(false);
        if (videoImage != null) videoImage.enabled = true;
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        EndIntro();
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogWarning("[IntroVideoController] Intro video error: " + message);

        // Before Open is pressed the door just stays up and the button keeps working — the
        // failsafe in OpenDoor() handles it from there. Once playback was requested there is
        // nothing left to show, so go into the garden.
        if (_openRequested) EndIntro();
    }

    private void EndIntro()
    {
        if (_ending) return;
        _ending = true;
        s_hasPlayedThisSession = true;

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnPrepared;
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived -= OnVideoError;
            if (videoPlayer.isPlaying) videoPlayer.Stop();
        }

        if (openButton != null) openButton.onClick.RemoveListener(OpenDoor);

        StartCoroutine(FadeOutAndDisable());
    }

    private IEnumerator FadeOutAndDisable()
    {
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            float start = canvasGroup.alpha;
            float t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, 0f, t / fadeOutDuration);
                yield return null;
            }
            canvasGroup.alpha = 0f;
        }

        ReleaseRuntimeTexture();
        if (openingSceneContents != null) openingSceneContents.SetActive(false);
    }

    private void EndImmediate()
    {
        _ending = true;
        s_hasPlayedThisSession = true;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }

        if (openingSceneContents != null) openingSceneContents.SetActive(false);
    }

    private void ReleaseRuntimeTexture()
    {
        if (_runtimeTexture == null) return;

        if (videoPlayer != null && videoPlayer.targetTexture == _runtimeTexture)
        {
            videoPlayer.targetTexture = null;
        }
        if (videoImage != null && videoImage.texture == _runtimeTexture)
        {
            videoImage.texture = null;
        }

        _runtimeTexture.Release();
        Destroy(_runtimeTexture);
        _runtimeTexture = null;
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnPrepared;
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived -= OnVideoError;
        }
        if (openButton != null)
        {
            openButton.onClick.RemoveListener(OpenDoor);
        }

        ReleaseRuntimeTexture();
    }
}
