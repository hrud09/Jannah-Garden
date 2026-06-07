using UnityEngine;

public class QuestionMarkOrb : MonoBehaviour
{
    public GameObject questionMark;
    [HideInInspector] public Transform spawnPoint; // Set by QuestionMarkOrbManager

    void Start()
    {
    }

    public void SetFocus(bool isFocused)
    {
    }

    public void OpenQuiz()
    {
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
