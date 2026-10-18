using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

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

    [Tooltip("Optional. A small label saying how this fellow is connected to the player - \"Invited you\" for the person who brought them into the app, \"You invited\" for their own direct referrals. Leave unassigned to show no relation at all; the card then looks exactly as it did before.")]
    public TMP_Text relationText;

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

    /// <summary>
    /// Downloaded avatars, shared across every card. Two fellows with the same avatar URL — and the same
    /// fellow respawned onto a new point — reuse one texture instead of re-downloading.
    /// </summary>
    private static readonly Dictionary<string, Sprite> remoteAvatarCache = new Dictionary<string, Sprite>();

    /// <summary>
    /// URLs currently downloading. Several cards can bind the same avatar in the same frame (the roster
    /// is spawned in one loop), and without this each would start its own request for the same bytes.
    /// </summary>
    private static readonly HashSet<string> avatarDownloadsInFlight = new HashSet<string>();

    /// <summary>
    /// Ceiling on cached avatar textures. Each is an uncompressed RGBA32 texture the game never releases
    /// on its own, and the roster can be hundreds of fellows deep across repeated visits — on a 2 GB
    /// device that is a slow walk into the OOM killer. Well above <c>maxProfiles</c>, so a single garden
    /// visit never evicts an avatar it is still showing.
    /// </summary>
    private const int MaxCachedAvatars = 32;

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

        // Both labels go through WorldTextLocalizer rather than LocalizedRendering: this card is a
        // canvas-less world-space object, and LocalizedRendering's Bengali path swaps in a Graphic that
        // only draws under a Canvas — the label would disappear instead of shaping. A fellow's name is
        // also user data, so its font follows the name's own script, not the UI language.
        WorldTextLocalizer.SetText(nameText, data.DisplayName);
        WorldTextLocalizer.SetText(memberSinceText, FormatMemberSince(data));

        if (noorCoinCountText != null)
            noorCoinCountText.text = data.noorCoins.ToString("N0", CultureInfo.InvariantCulture);

        ApplyRelationLabel(data);

        ApplyProfilePicture(data.profileImagePath);
    }

    /// <summary>
    /// Writes the "how do I know this person" line, if the prefab has a label for it.
    ///
    /// Only the two adjacent links get a label. Further up or down the chain the honest answer is
    /// "a friend of a friend of a friend", which is noise on a card this small, so those stay blank and
    /// the label is hidden entirely rather than left as an empty gap.
    /// </summary>
    private void ApplyRelationLabel(FellowProfileData data)
    {
        if (relationText == null) return;

        LocalizationManager loc = LocalizationManager.Instance;
        string label = string.Empty;

        if (data.IsDirectInviter)
            label = loc != null ? loc.GetOrDefault("fellowship.relation_inviter", "Invited you") : "Invited you";
        else if (!data.IsUpline && (data.isDirect || data.level == 1))
            label = loc != null ? loc.GetOrDefault("fellowship.relation_invited", "You invited") : "You invited";

        bool show = !string.IsNullOrEmpty(label);
        relationText.gameObject.SetActive(show);

        if (show) WorldTextLocalizer.SetText(relationText, label);
    }

    /// <summary>
    /// Avatars arrive one of two ways: a Resources path (dummy/local data) or an http(s) URL (real user
    /// avatars from Flutter). Remote ones show the default while they download.
    /// </summary>
    private void ApplyProfilePicture(string path)
    {
        if (profilePicture == null) return;

        if (string.IsNullOrEmpty(path))
        {
            profilePicture.sprite = defaultProfilePicture;
            return;
        }

        if (IsRemote(path))
        {
            if (remoteAvatarCache.TryGetValue(path, out Sprite cached) && cached != null)
            {
                profilePicture.sprite = cached;
                return;
            }

            profilePicture.sprite = defaultProfilePicture;

            // Someone else is already fetching these bytes; their completion fills the cache and this
            // card picks the sprite up on its next bind. Not worth a second request for one frame.
            if (avatarDownloadsInFlight.Contains(path)) return;

            // StartCoroutine throws on an inactive object — a pooled card bound before it is re-enabled.
            if (isActiveAndEnabled) StartCoroutine(DownloadAvatar(path));
            else Debug.LogWarning($"[FellowProfileObject] '{name}' is inactive; skipping avatar download.", this);

            return;
        }

        profilePicture.sprite = ResolveProfileSprite(path);
    }

    private static bool IsRemote(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private IEnumerator DownloadAvatar(string url)
    {
        avatarDownloadsInFlight.Add(url);

        Texture2D texture;

        // Scoped so the request (and its download buffer) is disposed before the sprite is published —
        // the texture itself outlives it.
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            avatarDownloadsInFlight.Remove(url);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[FellowProfileObject] Failed to download avatar '{url}': {request.error}. Keeping the default avatar.",
                    this);
                yield break;
            }

            texture = DownloadHandlerTexture.GetContent(request);
        }

        if (texture == null) yield break;

        TrimAvatarCache();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));

        remoteAvatarCache[url] = sprite;

        // The card may have been rebound to a different fellow (or pooled away) mid-download.
        if (profilePicture != null && Data != null && Data.profileImagePath == url)
        {
            profilePicture.sprite = sprite;
        }
    }

    /// <summary>
    /// Drops cached avatars (and their textures) until the cache is back under
    /// <see cref="MaxCachedAvatars"/>. Anything a live card is still displaying is kept: destroying its
    /// texture would blank the card it is on.
    /// </summary>
    private static void TrimAvatarCache()
    {
        if (remoteAvatarCache.Count < MaxCachedAvatars) return;

        HashSet<string> inUse = new HashSet<string>();
        // Inactive cards are included: a pooled-away card still holds its Data, and it will show that
        // avatar again the moment it is respawned.
        FellowProfileObject[] cards =
            FindObjectsByType<FellowProfileObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (FellowProfileObject card in cards)
        {
            if (card.Data != null && !string.IsNullOrEmpty(card.Data.profileImagePath))
                inUse.Add(card.Data.profileImagePath);
        }

        List<string> evictable = new List<string>();
        foreach (KeyValuePair<string, Sprite> entry in remoteAvatarCache)
        {
            if (!inUse.Contains(entry.Key)) evictable.Add(entry.Key);
        }

        foreach (string url in evictable)
        {
            if (remoteAvatarCache.Count < MaxCachedAvatars) break;

            if (remoteAvatarCache.TryGetValue(url, out Sprite sprite) && sprite != null)
            {
                // The texture is ours (created by the download handler) and nothing else references it.
                if (sprite.texture != null) Destroy(sprite.texture);
                Destroy(sprite);
            }

            remoteAvatarCache.Remove(url);
        }
    }

    /// <summary>
    /// Renders the join date as e.g. "Member since Mar 2024".
    /// </summary>
    private string FormatMemberSince(FellowProfileData data)
    {
        LocalizationManager loc = LocalizationManager.Instance;

        if (!data.TryGetMemberSince(out DateTime joined))
            return loc != null
                ? loc.GetOrDefault("fellowship.member_since_unknown", unknownMemberSinceText)
                : unknownMemberSinceText;

        string month = joined.ToString("MMM yyyy", CultureInfo.InvariantCulture);

        return loc != null ? loc.Get("fellowship.member_since_format", month) : $"Member since {month}";
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
