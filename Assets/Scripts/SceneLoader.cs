using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneLoader : MonoBehaviour
{
    [Tooltip("The button to assign the load scene method to. If left empty, it will try to get a Button component on the same GameObject.")]
    public Button loadSceneButton;

    private void Awake()
    {
        // Try to get a Button component on this GameObject if not assigned in the inspector
        if (loadSceneButton == null)
        {
            loadSceneButton = GetComponent<Button>();
        }

        if (loadSceneButton != null)
        {
            // Add a listener to the button to call the LoadOuterGarden method
            loadSceneButton.onClick.AddListener(LoadOuterGarden);
        }
        else
        {
            Debug.LogWarning("SceneLoader: No button assigned or found on this GameObject.", this);
        }
    }

    /// <summary>
    /// Loads the OuterGarden scene.
    /// </summary>
    public void LoadOuterGarden()
    {
        SceneManager.LoadScene("OuterGarden");
    }

    private void OnDestroy()
    {
        // Clean up the listener when the object is destroyed
        if (loadSceneButton != null)
        {
            loadSceneButton.onClick.RemoveListener(LoadOuterGarden);
        }
    }
}
