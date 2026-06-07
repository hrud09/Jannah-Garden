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
        if (MCQManager.Instance != null)
        {
            MCQManager.Instance.StartQuiz(this);
        }
    }
}
