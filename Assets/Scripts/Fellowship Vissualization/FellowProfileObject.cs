using System;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// View for a single fellow's floating profile card in the garden.
///
/// Everything on the card lives in world space: the visuals are SpriteRenderers and the labels are
/// 3D TextMeshPro. Attach to the "Fellow Profile" root and wire the references to the children:
///   Fellow Profile            → this component
///   ├── Name Text             → nameText
///   ├── Member Since Text     → memberSinceText
///   ├── Noor Coin Icon        → noorCoinIcon
///   ├── Noor Coin Count Text  → noorCoinCountText
///   └── Profile Picture BG    → profilePictureBG
///       └── Circle            → profilePicture
///
/// The card is populated purely through <see cref="Bind"/>, so the data source can be swapped
/// (dummy JSON today, backend/Flutter later) without touching the prefab.
/// </summary>
public class FellowProfileObject : MonoBehaviour
{
    [Header("Profile References")]
    public TMP_Text nameText;
    public TMP_Text memberSinceText;
    public SpriteRenderer noorCoinIcon;
    public TMP_Text noorCoinCountText;
    public SpriteRenderer profilePictureBG;

    [Tooltip("The 'Circle' child under Profile Picture BG that displays the avatar.")]
    public SpriteRenderer profilePicture;

    [Header("Fallbacks")]
    [Tooltip("Shown when a fellow has no avatar, or the sprite path fails to resolve.")]
    public Sprite defaultProfilePicture;

    [Tooltip("Shown in place of the join date when a profile's memberSince is missing or malformed.")]
    public string unknownMemberSinceText = "Member since —";

    [Header("Billboard")]
    [Tooltip("Rotates the card to face the camera each frame. Leave on for world-space cards.")]
    public bool faceCamera = true;

    [Tooltip("What gets rotated to face the camera. Defaults to this transform.")]
    public Transform billboardRoot;

    /// <summary>The data currently displayed. Null until <see cref="Bind"/> is called.</summary>
    public FellowProfileData Data { get; private set; }

    /// <summary>Set by <see cref="FellowshipVisualizationManager"/> so the point can be freed on despawn.</summary>
    [HideInInspector] public Transform spawnPoint;

    private Camera cachedCamera;

    private void Awake()
    {
        if (billboardRoot == null) billboardRoot = transform;
    }

    /// <summary>
    /// Populates the card from a profile. Safe to call repeatedly (e.g. on a pooled object).
    /// </summary>
    public void Bind(FellowProfileData data)
    {
        Data = data;

        if (data == null)
        {
            Debug.LogWarning($"[FellowProfileObject] Bind called with null data on '{name}'.", this);
            return;
        }

        if (nameText != null)
            nameText.text = data.userName;

        if (memberSinceText != null)
            memberSinceText.text = FormatMemberSince(data);

        if (noorCoinCountText != null)
            noorCoinCountText.text = data.noorCoins.ToString("N0", CultureInfo.InvariantCulture);

        if (profilePicture != null)
            profilePicture.sprite = ResolveProfileSprite(data.profileImagePath);
    }

    /// <summary>
    /// Renders the join date as e.g. "Member since Mar 2024", routed through the localization
    /// dictionary so the label can be translated.
    /// </summary>
    private string FormatMemberSince(FellowProfileData data)
    {
        if (!data.TryGetMemberSince(out DateTime joined))
            return unknownMemberSinceText;

        const string key = "Member since {0}";
        string format = JannahGarden.Localization.LocalizationManager.Instance.GetTranslation(key, key);

        string joinedLabel = joined.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        return string.Format(format, joinedLabel);
    }

    /// <summary>
    /// Loads the avatar from Resources, falling back to <see cref="defaultProfilePicture"/> when the
    /// path is empty or does not resolve. Resources.Load caches internally, so repeated calls are cheap.
    /// </summary>
    private Sprite ResolveProfileSprite(string path)
    {
        if (string.IsNullOrEmpty(path))
            return defaultProfilePicture;

        Sprite loaded = Resources.Load<Sprite>(path);
        if (loaded == null)
        {
            Debug.LogWarning(
                $"[FellowProfileObject] No sprite at Resources path '{path}' for '{Data?.userName}'. Using the default avatar.",
                this);
            return defaultProfilePicture;
        }

        return loaded;
    }

    private void LateUpdate()
    {
        if (!faceCamera || billboardRoot == null) return;

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
            if (cachedCamera == null) return;
        }

        // Match the camera's rotation rather than LookAt-ing it: sprites and TMP text face +Z, so
        // pointing +Z back at the camera lands them 180° around Y (mirrored/back-facing).
        billboardRoot.rotation = cachedCamera.transform.rotation;
    }
}
