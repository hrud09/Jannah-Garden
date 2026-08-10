using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Attach to any GameObject and fill in the Scene Buttons list: each entry pairs a
/// Button with the scene it should load. All loads are routed through
/// <see cref="LoadingScreenManager"/> when available, otherwise fall back to a
/// direct synchronous load.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    [System.Serializable]
    public class SceneButton
    {
        [Tooltip("The button that triggers the load. If left empty and this is the only " +
                 "entry, a Button on the same GameObject is used.")]
        public Button button;

        [Tooltip("Name of the scene to load. Must match the scene file name and be added " +
                 "to Build Settings. e.g. \"Jannah Garden\", \"Outer Garden 25%\".")]
        public string sceneName;
    }

    [Header("Scene Buttons")]
    [Tooltip("One entry per button: assign the button and type the scene it loads.")]
    public SceneButton[] sceneButtons;

    [Tooltip("Scene names loaded via Addressables when LoadingScreenManager is unavailable. " +
             "Must match the entries configured on LoadingScreenManager.addressableSceneNames.")]
    public string[] addressableSceneNames;

    // Keeps the generated listeners around so they can be removed again in OnDestroy
    // without wiping listeners added elsewhere (e.g. in the inspector).
    private readonly List<(Button button, UnityAction action)> registeredListeners = new List<(Button, UnityAction)>();

    private void Awake()
    {
        if (sceneButtons == null || sceneButtons.Length == 0)
        {
            Debug.LogWarning("SceneLoader: no scene buttons configured.", this);
            return;
        }

        for (int i = 0; i < sceneButtons.Length; i++)
        {
            SceneButton entry = sceneButtons[i];
            if (entry == null)
            {
                continue;
            }

            // Convenience fallback: a lone entry with no button assigned falls back to a
            // Button on this GameObject, matching the old single-button behaviour.
            if (entry.button == null && sceneButtons.Length == 1)
            {
                entry.button = GetComponent<Button>();
            }

            if (entry.button == null)
            {
                Debug.LogWarning($"SceneLoader: entry {i} ('{entry.sceneName}') has no button assigned.", this);
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.sceneName))
            {
                Debug.LogWarning($"SceneLoader: entry {i} ({entry.button.name}) has no scene name set.", this);
                continue;
            }

            string sceneName = entry.sceneName;
            UnityAction action = () => LoadSceneSafe(sceneName);
            entry.button.onClick.AddListener(action);
            registeredListeners.Add((entry.button, action));
        }
    }

    /// <summary>
    /// Loads a scene by name: routes through LoadingScreenManager if available,
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
            if (addressableSceneNames != null && System.Array.IndexOf(addressableSceneNames, sceneName) >= 0)
            {
                Addressables.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadScene(sceneName);
            }
        }
    }

    /// <summary>
    /// Loads the scene configured on the entry at the given index. Useful for wiring a
    /// button's OnClick directly in the inspector instead of relying on Awake.
    /// </summary>
    public void LoadSceneAtIndex(int index)
    {
        if (sceneButtons == null || index < 0 || index >= sceneButtons.Length || sceneButtons[index] == null)
        {
            Debug.LogWarning($"SceneLoader: no scene button at index {index}.", this);
            return;
        }

        LoadSceneSafe(sceneButtons[index].sceneName);
    }

    private void OnDestroy()
    {
        // Clean up only the listeners this component added.
        foreach ((Button button, UnityAction action) in registeredListeners)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        registeredListeners.Clear();
    }
}
