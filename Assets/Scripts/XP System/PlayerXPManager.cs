using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerXPManager : MonoBehaviour
{
    public int xpLevel = 1;
    public float currentXP = 0;

    [SerializeField] private float xpToNextLevel = 100;


    [Header("UI Reference")]
    public TMP_Text xpLevelText;
    public Slider xpSlider;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
