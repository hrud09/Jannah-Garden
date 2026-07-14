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

    /// <summary>Resources-relative path to the avatar sprite, e.g. "Profiles/avatar_01".</summary>
    public string profileImagePath;

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
/// JsonUtility cannot deserialize a top-level array, so the JSON is wrapped in this object.
/// </summary>
[Serializable]
public class FellowProfileList
{
    public FellowProfileData[] fellows;
}
