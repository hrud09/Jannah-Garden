using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays the Jannah Garden opening cinematic over the door artwork the first time the
/// scene is entered in a given app session, then fades the panel out to reveal gameplay.
/// Attach to the "Opening Canvas" root; SkipIntro() can be wired to a UI button.
///
/// "openingSceneContents" (the parent of Door Image / Intro Video / Skip Button) is kept
/// inactive by default in the editor so those full-screen UI elements don't sit on top of
/// the scene while working in it — this script is what turns it on, only when the intro is
/// actually about to play.
/// </summary>
public class IntroVideoController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Parent of Door Image / Intro Video / Skip Button. Kept inactive by default " +
             "(in the editor) so the intro UI doesn't block working in the scene; this script " +
             "activates it only when the intro is about to play.")]
    public GameObject openingSceneContents;
    public VideoPlayer videoPlayer;
    public RawImage videoImage;
    public AspectRatioFitter aspectRatioFitter;
    public GameObject doorImage;
    public Button skipButton;
    public CanvasGroup canvasGroup;

    [Header("Behaviour")]
    [Tooltip("If true, the intro only plays once per app session; later (re)loads of this scene skip straight past it.")]
    public bool playOncePerSession = true;

    [Tooltip("Video playback speed. 1 = normal, 1.5 = 50% faster.")]
    public float playbackSpeed = 1.5f;

    [Tooltip("Fallback render texture size used when the VideoPlayer targets a RenderTexture at runtime.")]
    public Vector2Int renderTextureSize = new Vector2Int(1920, 1080);

    public float fadeOutDuration = 0.6f;

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

    private void BeginIntro()
    {
        if (openingSceneContents != null) openingSceneContents.SetActive(true);

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;

        // Toggle the RawImage's own enabled state (not the GameObject's) since the
        // VideoPlayer/AudioSource live on the same GameObject and must stay active to prepare.
        if (videoImage != null) videoImage.enabled = false;
        if (doorImage != null) doorImage.SetActive(true);

        if (skipButton != null) skipButton.onClick.AddListener(SkipIntro);

        if (videoPlayer == null)
        {
            EndIntro();
            return;
        }

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
        }

        videoPlayer.playOnAwake = false;
        videoPlayer.playbackSpeed = playbackSpeed;
        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.loopPointReached += OnVideoFinished;
        videoPlayer.Prepare();
    }

    /// <summary>
    /// Fills in any inspector references left unassigned by locating the standard child
    /// hierarchy this panel is built with: openingSceneContents, and under it,
    /// Door Image / Intro Video / Skip Button.
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
        if (skipButton == null) skipButton = searchRoot.GetComponentInChildren<Button>(true);

        if (doorImage == null)
        {
            Transform doorTrans = searchRoot.Find("Door Image");
            if (doorTrans != null) doorImage = doorTrans.gameObject;
        }
    }

    private void OnPrepared(VideoPlayer vp)
    {
        if (aspectRatioFitter != null && vp.width > 0 && vp.height > 0)
        {
            aspectRatioFitter.aspectRatio = (float)vp.width / vp.height;
        }

        if (doorImage != null) doorImage.SetActive(false);
        if (videoImage != null) videoImage.enabled = true;

        vp.playbackSpeed = playbackSpeed;
        vp.Play();
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        EndIntro();
    }

    /// <summary>Wire this to the Skip button's OnClick.</summary>
    public void SkipIntro()
    {
        EndIntro();
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
            if (videoPlayer.isPlaying) videoPlayer.Stop();
        }

        if (skipButton != null) skipButton.onClick.RemoveListener(SkipIntro);

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
        }
        if (skipButton != null)
        {
            skipButton.onClick.RemoveListener(SkipIntro);
        }

        ReleaseRuntimeTexture();
    }
}
