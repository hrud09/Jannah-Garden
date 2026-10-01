using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// The "+N Noor Coins / +N XP" row that sits under the "Mash'Allah" praise once a question is answered
/// correctly (<see cref="MCQManager"/>) or a dhikr is completed (<see cref="DhikrManager"/>).
///
/// Everything about how the row *looks* — font, size, colour/gradient, spacing, position under the
/// praise — lives on the scene objects this component points at, so it can be restyled in the editor
/// without touching code. This component only decides which chips are visible, fills in their numbers,
/// and plays the entry animation.
///
/// Sits on a child of the panel's Mash'Allah label, so the managers' existing "hide the finale label"
/// cleanup takes the row down with it even if <see cref="Hide"/> is somehow missed.
/// </summary>
[DisallowMultipleComponent]
public class RewardFinaleDisplay : MonoBehaviour
{
    [Header("Chips")]
    [Tooltip("Label for the Noor Coins reward. Hidden automatically when the task granted no coins.")]
    public TextMeshProUGUI coinText;
    [Tooltip("Label for the XP reward. Hidden automatically when the task granted no XP.")]
    public TextMeshProUGUI xpText;
    [Tooltip("Optional divider between the two chips. Only shown when both rewards are present.")]
    public GameObject separator;
    [Tooltip("Optional nudge under the chips telling the player there are more orbs to find out in the " +
             "garden. Leave empty on panels that shouldn't show it.")]
    public TextMeshProUGUI hintText;

    [Header("Localization Keys")]
    [Tooltip("Format key for the coin chip, e.g. \"reward.coins\" -> \"+{0} Noor Coins\".")]
    public string coinLocalizationKey = "reward.coins";
    [Tooltip("Format key for the XP chip, e.g. \"reward.xp\" -> \"+{0} XP\".")]
    public string xpLocalizationKey = "reward.xp";
    [Tooltip("Key for the \"find more orbs\" nudge under the chips.")]
    public string hintLocalizationKey = "reward.find_more_orbs";

    [Header("Animation")]
    [Tooltip("Wait before the row appears, so the praise's own punch-scale lands first.")]
    public float entryDelay = 0.25f;
    public float entryDuration = 0.45f;
    [Tooltip("Extra delay applied to the second chip, so the two don't pop in together.")]
    public float chipStagger = 0.12f;
    [Tooltip("Extra delay before the \"find more orbs\" nudge fades in, so it lands after the chips.")]
    public float hintDelay = 0.35f;
    [Tooltip("How long the numbers take to roll up from zero. Set to 0 to show the final value instantly.")]
    public float countUpDuration = 0.7f;
    [Tooltip("Scale the chips start at before springing to full size.")]
    public float chipEntryScale = 0.7f;
    public float rowPunchStrength = 0.08f;

    private CanvasGroup cachedGroup;
    private CanvasGroup cachedHintGroup;

    // Resolved lazily rather than in Awake: the row starts inactive in the scene, so Awake hasn't run
    // by the time the first Show call arrives.
    private CanvasGroup Group
    {
        get
        {
            if (cachedGroup == null) cachedGroup = GetComponent<CanvasGroup>();
            return cachedGroup;
        }
    }

    /// <summary>Reveals the row for the rewards just granted. Chips with nothing to show are hidden,
    /// and a task that granted nothing at all leaves the praise standing on its own.</summary>
    public void Show(int coins, float xp)
    {
        if (coins <= 0 && xp <= 0f)
        {
            Hide();
            return;
        }

        gameObject.SetActive(true);

        bool showCoins = coins > 0;
        bool showXP = xp > 0f;

        if (coinText != null) coinText.gameObject.SetActive(showCoins);
        if (xpText != null) xpText.gameObject.SetActive(showXP);
        if (separator != null) separator.SetActive(showCoins && showXP);

        if (Group != null)
        {
            Group.DOKill();
            Group.alpha = 0f;
            Group.DOFade(1f, entryDuration).SetDelay(entryDelay);
        }

        transform.DOKill();
        transform.localScale = Vector3.one;
        if (rowPunchStrength > 0f)
        {
            transform.DOPunchScale(Vector3.one * rowPunchStrength, 0.4f, 8, 1f).SetDelay(entryDelay);
        }

        int chipIndex = 0;
        if (showCoins) AnimateChip(coinText, coinLocalizationKey, coins, chipIndex++);
        if (showXP) AnimateChip(xpText, xpLocalizationKey, xp, chipIndex);

        ShowHint(chipIndex);
    }

    /// <summary>Takes the row back down, so a panel reopened for a fresh question/dhikr starts clean.</summary>
    public void Hide()
    {
        if (Group != null) Group.DOKill();
        transform.DOKill();

        if (coinText != null) coinText.transform.DOKill();
        if (xpText != null) xpText.transform.DOKill();
        if (hintText != null)
        {
            CanvasGroup hintGroup = HintGroup;
            if (hintGroup != null) hintGroup.DOKill();
            hintText.transform.DOKill();
            hintText.gameObject.SetActive(false);
        }

        gameObject.SetActive(false);
    }

    // Same lazy resolve as Group above: the hint starts inactive, so its Awake hasn't run on first Show.
    private CanvasGroup HintGroup
    {
        get
        {
            if (cachedHintGroup == null && hintText != null) cachedHintGroup = hintText.GetComponent<CanvasGroup>();
            return cachedHintGroup;
        }
    }

    /// <summary>Fades in the "there are more orbs out in the garden" nudge once the chips have landed.
    /// Its own alpha is tweened (rather than the row's) so it trails the numbers instead of arriving
    /// with them.</summary>
    private void ShowHint(int lastChipIndex)
    {
        if (hintText == null) return;

        hintText.gameObject.SetActive(true);

        LocalizationManager loc = LocalizationManager.Instance;
        string text = loc != null ? loc.Get(hintLocalizationKey) : "Find more orbs scattered around Jannah Garden";
        LocalizedRendering.SetText(hintText, text);

        float delay = entryDelay + lastChipIndex * chipStagger + hintDelay;

        // Faded through a CanvasGroup rather than TMP's own alpha: under Bengali the label is rendered by
        // a ShapedTextGraphic child (see LocalizedRendering), which TMP's alpha doesn't reach.
        CanvasGroup hintGroup = HintGroup;
        if (hintGroup == null) return;

        hintGroup.DOKill();
        hintGroup.alpha = 0f;
        hintGroup.DOFade(1f, entryDuration).SetDelay(delay);
    }

    private void AnimateChip(TextMeshProUGUI chip, string localizationKey, float amount, int index)
    {
        if (chip == null) return;

        float delay = entryDelay + index * chipStagger;

        chip.transform.DOKill();
        chip.transform.localScale = Vector3.one * chipEntryScale;
        chip.transform.DOScale(1f, entryDuration).SetEase(Ease.OutBack).SetDelay(delay);

        // Counting up re-shapes the string every frame, which the HarfBuzz path (Bengali) pays real work
        // for — there the final value is set once and the scale/fade entry carries the moment on its own.
        bool shaped = LocalizationManager.Instance != null && LocalizationManager.Instance.CurrentLocale == AppLocale.bn;
        if (shaped || countUpDuration <= 0f)
        {
            SetChipText(chip, localizationKey, amount);
            return;
        }

        SetChipText(chip, localizationKey, 0f);
        DOVirtual.Float(0f, amount, countUpDuration, value => SetChipText(chip, localizationKey, value))
            .SetEase(Ease.OutCubic)
            .SetDelay(delay)
            .SetTarget(chip.transform);
    }

    private void SetChipText(TextMeshProUGUI chip, string localizationKey, float amount)
    {
        if (chip == null) return;

        // Rewards are whole numbers in both panels that show this row; rounding here keeps the count-up
        // from flashing fractional values mid-tween.
        int rounded = Mathf.RoundToInt(amount);
        LocalizationManager loc = LocalizationManager.Instance;
        string text = loc != null ? loc.Get(localizationKey, rounded) : "+" + rounded;

        // Routed through the shared localized path so Arabic/Urdu shaping and the Bengali shaped
        // renderer both work — the chips' translations are real words, not just digits.
        LocalizedRendering.SetText(chip, text);
    }
}
