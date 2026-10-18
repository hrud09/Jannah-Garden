using System;
using System.Globalization;

/// <summary>
/// Serializable profile info for a single fellow (another user shown in the garden).
///
/// This is the shape the dummy JSON in Resources/fellow_profiles.txt uses. When the real
/// backend/Flutter system lands, keep these field names (they mirror
/// <see cref="FlutterIntegration.UserProfilePayload"/>) and the view layer needs no changes.
/// </summary>
[Serializable]
public class FellowProfileData
{
    public string userId;
    public string userName;

    /// <summary>ISO-8601 date ("2024-03-15") for when the user joined.</summary>
    public string memberSince;

    public int noorCoins;

    /// <summary>
    /// Avatar location. Either an http(s) URL (real users, from Flutter) or a Resources-relative path
    /// (dummy data, e.g. "Profiles/avatar_01"). Empty means "no avatar" — the card shows its default.
    /// </summary>
    public string profileImagePath;

    /// <summary>
    /// How far along the referral chain this fellow sits, counting from the player: 1 is adjacent (the
    /// person who invited the player, or someone the player invited), 2 is one step further, and so on.
    /// Always positive — <see cref="relation"/> says which direction. 0 when Flutter does not send it
    /// (older app builds), in which case every fellow ranks equally.
    /// </summary>
    public int level;

    /// <summary>
    /// Which way along the chain this fellow sits: <see cref="FellowRelation.Upline"/> when they invited
    /// the player, <see cref="FellowRelation.Downline"/> when the player invited them.
    ///
    /// A chain is a two-way relationship, but the roster used to carry only one side of it: Flutter sent
    /// the player's downline and nothing else, so a user who had invited nobody yet stood in an empty
    /// garden even though the person who brought them into the app was one hop away. Empty or missing
    /// means downline, which is what every app build before this sent.
    /// </summary>
    public string relation;

    /// <summary>True when the player invited this fellow themselves. Mirrors <c>level == 1</c> downline.</summary>
    public bool isDirect;

    /// <summary>How many people this fellow has invited in turn. 0 when not sent.</summary>
    public int referralCount;

    /// <summary>True when this fellow is above the player in the chain — they brought the player in.</summary>
    public bool IsUpline =>
        string.Equals(relation, FellowRelation.Upline, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one person who invited the player. There is exactly one of these (or none, for an account
    /// that joined without a referral code), and it is the single most meaningful card in the garden.
    /// </summary>
    public bool IsDirectInviter => IsUpline && level == 1;

    /// <summary>
    /// The name to put on the card. Flutter sends "" for an account that never set a display name, and a
    /// blank card reads as a bug, so fall back to something neutral.
    /// </summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(userName)
            ? LocalizationManager.Instance != null
                ? LocalizationManager.Instance.GetOrDefault("fellowship.unnamed_fellow", "A fellow")
                : "A fellow"
            : userName.Trim();

    /// <summary>
    /// Parses <see cref="memberSince"/>. Returns false when the field is missing or malformed,
    /// so callers can fall back rather than showing a bogus date.
    /// </summary>
    public bool TryGetMemberSince(out DateTime date)
    {
        return DateTime.TryParse(
            memberSince,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}

/// <summary>
/// Values <see cref="FellowProfileData.relation"/> can carry. Lower-case on the wire, and compared
/// case-insensitively, so a Flutter build that sends "Upline" still works.
/// </summary>
public static class FellowRelation
{
    /// <summary>This fellow invited the player (directly, or further up the chain).</summary>
    public const string Upline = "upline";

    /// <summary>The player invited this fellow (directly, or further down the chain).</summary>
    public const string Downline = "downline";
}

/// <summary>
/// JsonUtility cannot deserialize a top-level array, so the JSON is wrapped in this object.
/// </summary>
[Serializable]
public class FellowProfileList
{
    public FellowProfileData[] fellows;
}
