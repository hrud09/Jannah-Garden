using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD info button that replays the shop -> buy -> rotate -> place walkthrough on demand
/// (<see cref="GameOnboardingManager.ReplayShopTutorial"/>).
///
/// It lives in each scene's own Interactable Button Canvas rather than on the DontDestroyOnLoad
/// TutorialCanvas, so it drives itself instead of being wired into the manager as a scene reference -
/// a reference like that would go stale the moment Jannah Garden is reloaded (coming back from the
/// Outer Garden), leaving the surviving singleton pointing at a destroyed button.
///
/// Hides itself while onboarding is running - during the first-run flows there is nothing to replay
/// yet, and during a replay the tutorial is already on screen.
/// </summary>
[RequireComponent(typeof(Button))]
public class TutorialReplayButton : MonoBehaviour
{
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(HandleClick);

        // Deliberately not OnEnable/OnDisable: Refresh deactivates this very GameObject, so an
        // OnDisable-scoped subscription would drop the event that is supposed to bring it back.
        GameOnboardingManager.OnTutorialActiveChanged += Refresh;
        Refresh();
    }

    // The manager's Start may run after ours, so re-read once everything is up.
    private void Start() => Refresh();

    private void OnDestroy()
    {
        GameOnboardingManager.OnTutorialActiveChanged -= Refresh;
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);

        if (GameOnboardingManager.Instance == null)
        {
            Debug.LogWarning("[TutorialReplayButton] No GameOnboardingManager in the scene - nothing to replay.");
            return;
        }

        GameOnboardingManager.Instance.ReplayShopTutorial();
    }

    private void Refresh()
    {
        bool show = GameOnboardingManager.IsOnboardingCompleted && !GameOnboardingManager.IsTutorialActive;
        if (gameObject.activeSelf != show) gameObject.SetActive(show);
    }
}
