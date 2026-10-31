using UnityEngine;

public class QuestionMarkOrb : MonoBehaviour
{
    public GameObject questionMark;
    [HideInInspector] public Transform spawnPoint; // Set by QuestionMarkOrbManager

    void Start()
    {
        // Orbs are pooled, so Start runs once per instance; OnEnable would re-announce every reuse.
        GameSfx.PlayAt(SoundEffect.OrbAppear, transform);
    }

    private void OnEnable()
    {
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
