using DG.Tweening;
using UnityEngine;

public class QuestionMarkOrb : MonoBehaviour
{
    public GameObject questionMark;

    [Header("Idle")]
    [Tooltip("How far the question mark floats up and down (world units).")]
    public float bobHeight = 0.12f;
    public float bobPeriod = 2.6f;
    [Tooltip("Scale change at the top of each slow pulse (0.06 = 6%).")]
    public float pulseAmount = 0.06f;

    private Vector3 _markRestPos;
    private Vector3 _markRestScale = Vector3.one;
    private bool _markRestCaptured;
    private float _idlePhase;
    [HideInInspector] public Transform spawnPoint; // Set by QuestionMarkOrbManager

    void Start()
    {
        // Orbs are pooled, so Start runs once per instance; OnEnable would re-announce every reuse.
        GameSfx.PlayAt(SoundEffect.OrbAppear, transform);
    }

    private void Awake() => CaptureMarkRest();

    private void CaptureMarkRest()
    {
        if (_markRestCaptured || questionMark == null) return;
        _markRestCaptured = true;
        _markRestPos = questionMark.transform.localPosition;
        _markRestScale = questionMark.transform.localScale;
    }

    /// <summary>The question mark floats and pulses gently, so orbs read as alive from across the garden.</summary>
    private void Update()
    {
        if (questionMark == null || !_markRestCaptured) return;
        Transform mark = questionMark.transform;
        FaceCamera(mark);
        if (UIMotion.ReduceMotion || DOTween.IsTweening(mark)) return;

        float t = Time.time + _idlePhase;
        float wave = Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.1f, bobPeriod));
        mark.localPosition = _markRestPos + Vector3.up * (wave * bobHeight);
        // The pulse runs at a different rate from the bob, so the two never lock into one rhythm.
        float pulse = 1f + Mathf.Sin(t * Mathf.PI * 2f / (bobPeriod * 0.77f)) * pulseAmount;
        mark.localScale = _markRestScale * pulse;
    }

    /// <summary>
    /// The scroll is flat, so it turns (yaw only) to keep its question mark toward the camera;
    /// edge-on it would vanish into a thin line.
    /// </summary>
    private static void FaceCamera(Transform mark)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 away = mark.position - cam.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) return;
        mark.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    private void OnEnable()
    {
        CaptureMarkRest();
        _idlePhase = Random.value * 10f;
        if (questionMark != null && _markRestCaptured)
        {
            questionMark.transform.DOKill();
            questionMark.transform.localPosition = _markRestPos;
            questionMark.transform.localScale = _markRestScale;
        }

        // Respawns come back through the pool rather than through Start, and a replacement orb
        // appearing is exactly as worth hearing as the first one. _announced keeps the two paths
        // from doubling up on the very first enable.
        if (_announced) GameSfx.PlayAt(SoundEffect.OrbAppear, transform);
        _announced = true;
    }

    /// <summary>Set once the orb has been announced, so Start and OnEnable cannot both sound it.</summary>
    private bool _announced;

    public void SetFocus(bool isFocused)
    {
    }

    public void OpenQuiz()
    {
        GameSfx.Play(SoundEffect.QuestionMarkOrbOpen);

        // A squash-and-pop on the tap, so the orb reacts before the panel covers it.
        if (questionMark != null && _markRestCaptured && !UIMotion.ReduceMotion)
        {
            Transform mark = questionMark.transform;
            mark.DOKill();
            mark.localScale = _markRestScale;
            DOTween.Sequence().SetLink(questionMark).SetTarget(mark)
                .Append(mark.DOScale(Vector3.Scale(_markRestScale, new Vector3(1.25f, 0.75f, 1.25f)), 0.08f).SetEase(Ease.OutQuad))
                .Append(mark.DOScale(_markRestScale * 1.2f, 0.12f).SetEase(Ease.OutQuad))
                .Append(mark.DOScale(_markRestScale, 0.25f).SetEase(Ease.OutBack, 2f));
        }

        int chance = Random.Range(0, 2);

        if (chance == 0 && MCQManager.Instance != null)
        {
            MCQManager.Instance.StartQuiz(this);
        }
        else if (chance == 1 && DhikrManager.Instance != null)
        {
            DhikrManager.Instance.StartDhikr(this);
        }
        else if (MCQManager.Instance != null)
        {
            MCQManager.Instance.StartQuiz(this);
        }
    }
}
