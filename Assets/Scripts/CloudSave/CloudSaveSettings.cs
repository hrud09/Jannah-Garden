using UnityEngine;

/// <summary>
/// Where <see cref="CloudSave"/> keeps player saves. Optional: without an asset at
/// <c>Resources/CloudSaveSettings</c> the defaults below are used, which point at the Amal app's own
/// Firebase project — the one players sign into, and the only one their login token is valid for.
/// </summary>
[CreateAssetMenu(fileName = "CloudSaveSettings", menuName = "Jannah Garden/Cloud Save Settings")]
public class CloudSaveSettings : ScriptableObject
{
    public const string DefaultProjectId = "amal-app-production";

    [Tooltip("Firebase project id. Must be the project the Flutter app signs players into, or every " +
             "request is rejected: a login token is only valid in its own project.")]
    public string projectId = DefaultProjectId;

    [Header("Editor / standalone testing")]
    [Tooltip("With no Flutter app around (Play mode, a standalone build), sign in anonymously so cloud " +
             "saves can be tested. Creates a throwaway anonymous user in the project above and writes " +
             "real documents under it. Requires Anonymous sign-in to be enabled in Firebase Auth.")]
    public bool signInAnonymouslyWithoutFlutter;

    [Tooltip("Web API key of the project above (Firebase console → Project settings). Only used for the " +
             "anonymous test sign-in; the shipping game gets its login from the Flutter app.")]
    public string webApiKey = "";

    private static CloudSaveSettings _cached;

    /// <summary>The settings asset, or an in-memory default when none exists.</summary>
    public static CloudSaveSettings Current
    {
        get
        {
            if (_cached != null) return _cached;

            _cached = Resources.Load<CloudSaveSettings>("CloudSaveSettings");
            if (_cached == null)
            {
                _cached = CreateInstance<CloudSaveSettings>();
                _cached.hideFlags = HideFlags.HideAndDontSave;
            }

            return _cached;
        }
    }

    public string ProjectIdOrDefault => string.IsNullOrWhiteSpace(projectId) ? DefaultProjectId : projectId.Trim();
}
