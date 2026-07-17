using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Attach to any GameObject with Button components to trigger scene loads.
/// All loads are routed through <see cref="LoadingScreenManager"/> when available,
/// otherwise fall back to a direct synchronous load.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    [Header("Scene Buttons")]
    [Tooltip("The button to assign the load Outer Garden scene method to. " +
             "If left empty, it will try to get a Button component on the same GameObject.")]
    public Button loadSceneButton;

    [Tooltip("The button to assign the load Jannah Garden scene method to.")]
    public Button jannahGardenButton;

    [Tooltip("The button to assign the load Innate Sense scene method to.")]
    public Button innateSenseButton;

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

        if (jannahGardenButton != null)
        {
            jannahGardenButton.onClick.AddListener(LoadJannahGarden);
        }

        if (innateSenseButton != null)
        {
            innateSenseButton.onClick.AddListener(LoadInnateSense);
        }
    }

    /// <summary>
    /// Loads the OuterGarden scene.
    /// </summary>
    public void LoadOuterGarden()
    {
        LoadSceneSafe("Outer Garden");
    }

    /// <summary>
    /// Loads the Jannah Garden scene.
    /// </summary>
    public void LoadJannahGarden()
    {
        LoadSceneSafe("Jannah Garden");
    }

    /// <summary>
    /// Loads the Innate Sense scene.
    /// </summary>
    public void LoadInnateSense()
    {
        LoadSceneSafe("Innate Sense");
    }

    /// <summary>
    /// Generic loader: routes through LoadingScreenManager if available,
    /// otherwise falls back to a direct synchronous SceneManager.LoadScene.
    /// </summary>
    public void LoadSceneSafe(string sceneName)
    {
        if (LoadingScreenManager.Instance != null)
        {
            LoadingScreenManager.Instance.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning($"SceneLoader: LoadingScreenManager not found. Loading '{sceneName}' directly.", this);
            SceneManager.LoadScene(sceneName);
        }
    }

    private void OnDestroy()
    {
        // Clean up the listener when the object is destroyed
        if (loadSceneButton != null)
        {
            loadSceneButton.onClick.RemoveListener(LoadOuterGarden);
        }

        if (jannahGardenButton != null)
        {
            jannahGardenButton.onClick.RemoveListener(LoadJannahGarden);
        }

        if (innateSenseButton != null)
        {
            innateSenseButton.onClick.RemoveListener(LoadInnateSense);
        }
    }
}
