using System;
using System.Collections;
using System.Text;
using FlutterIntegration;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Who the player is, as far as Firebase is concerned: their uid and a short-lived ID token that
/// <see cref="CloudSave"/> sends with every Firestore request.
///
/// <para><b>In the app</b> the player has already signed in inside Flutter, so the token comes from
/// there: Unity asks with <c>REQUEST_AUTH_TOKEN</c>, Flutter answers (and pushes again whenever the
/// token refreshes or the account changes) with <c>UPDATE_AUTH_TOKEN</c>. That is the only thing the
/// save system still needs from Flutter — reading and writing the saves happens here, directly.</para>
///
/// <para><b>Without the app</b> (Play mode, a standalone build) it can sign in anonymously through the
/// Firebase Auth REST API, if <see cref="CloudSaveSettings.signInAnonymouslyWithoutFlutter"/> is on.</para>
/// </summary>
public static class FirebaseSession
{
    /// <summary>Refresh this long before the token actually expires.</summary>
    private const int ExpirySafetySeconds = 120;

    /// <summary>How long to wait for Flutter to answer a token request.</summary>
    private const float FlutterAnswerTimeoutSeconds = 6f;

#if UNITY_EDITOR || !UNITY_IOS && !UNITY_ANDROID
    private const string AnonRefreshTokenPref = "CloudSave.AnonRefreshToken";
#endif

    public static string Uid { get; private set; } = string.Empty;
    public static string IdToken { get; private set; } = string.Empty;
    private static DateTime _expiresAtUtc = DateTime.MinValue;

    /// <summary>True when Flutter has told us nobody is signed in.</summary>
    public static bool SignedOut { get; private set; }

    /// <summary>Raised when the uid changes (sign-in, sign-out, another account).</summary>
    public static event Action<string> OnUserChanged;

    private static bool _awaitingFlutter;

    public static bool HasValidToken =>
        !string.IsNullOrEmpty(IdToken) && !string.IsNullOrEmpty(Uid)
        && DateTime.UtcNow < _expiresAtUtc.AddSeconds(-ExpirySafetySeconds);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Uid = string.Empty;
        IdToken = string.Empty;
        _expiresAtUtc = DateTime.MinValue;
        SignedOut = false;
        _awaitingFlutter = false;
        OnUserChanged = null;
        FlutterBridge.OnAuthTokenReceived -= HandleFlutterToken;
        FlutterBridge.OnAuthTokenReceived += HandleFlutterToken;
    }

    /// <summary>Forget the token (e.g. Firestore rejected it) so the next request fetches a new one.</summary>
    public static void Invalidate()
    {
        IdToken = string.Empty;
        _expiresAtUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Makes sure <see cref="IdToken"/> is valid, fetching a new one if needed. Yields until done; check
    /// <see cref="HasValidToken"/> afterwards.
    /// </summary>
    public static IEnumerator EnsureToken()
    {
        if (HasValidToken) yield break;

        CloudSaveSettings settings = CloudSaveSettings.Current;

        // Flutter first, when it is there to answer.
        if (FlutterBridge.Instance != null && !UseAnonymousOnly(settings))
        {
            _awaitingFlutter = true;
            FlutterBridge.Instance.RequestAuthToken();

            float waited = 0f;
            while (_awaitingFlutter && waited < FlutterAnswerTimeoutSeconds)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            if (HasValidToken || SignedOut) yield break;
        }

        if (settings.signInAnonymouslyWithoutFlutter) yield return SignInAnonymously(settings);
    }

    /// <summary>On a phone the app always has Flutter; anonymous sign-in is only a testing aid.</summary>
    private static bool UseAnonymousOnly(CloudSaveSettings settings)
    {
#if UNITY_EDITOR
        // The editor has a FlutterBridge but nobody on the other end — don't wait on it.
        return settings.signInAnonymouslyWithoutFlutter;
#else
        return false;
#endif
    }

    // ─── Flutter ──────────────────────────────────────────────────────────────

    private static void HandleFlutterToken(AuthTokenPayload payload)
    {
        _awaitingFlutter = false;
        if (payload == null) return;

        if (string.IsNullOrEmpty(payload.uid) || string.IsNullOrEmpty(payload.idToken))
        {
            Debug.Log("[FirebaseSession] Flutter reports nobody is signed in — cloud saves paused.");
            SignedOut = true;
            Invalidate();
            SetUid(string.Empty);
            return;
        }

        SignedOut = false;
        Apply(payload.uid, payload.idToken, ReadExpiry(payload.idToken));
    }

    // ─── Anonymous (testing) ──────────────────────────────────────────────────

    private static IEnumerator SignInAnonymously(CloudSaveSettings settings)
    {
#if UNITY_EDITOR || !UNITY_IOS && !UNITY_ANDROID
        if (string.IsNullOrWhiteSpace(settings.webApiKey))
        {
            Debug.LogWarning("[FirebaseSession] Anonymous test sign-in is on but CloudSaveSettings has no Web API key.");
            yield break;
        }

        string key = UnityWebRequest.EscapeURL(settings.webApiKey.Trim());
        string refresh = PlayerPrefs.GetString(AnonRefreshTokenPref, string.Empty);

        // Reuse the same anonymous user across sessions, so test saves persist like a real account's.
        if (!string.IsNullOrEmpty(refresh))
        {
            string body = "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(refresh);
            using (var req = PostForm("https://securetoken.googleapis.com/v1/token?key=" + key, body))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var r = JsonUtility.FromJson<RefreshResponse>(req.downloadHandler.text);
                    PlayerPrefs.SetString(AnonRefreshTokenPref, r.refresh_token);
                    Apply(r.user_id, r.id_token, ReadExpiry(r.id_token));
                    yield break;
                }

                Debug.LogWarning($"[FirebaseSession] Anonymous token refresh failed ({req.responseCode}) — signing in afresh.");
            }
        }

        using (var req = PostJson("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + key, "{\"returnSecureToken\":true}"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[FirebaseSession] Anonymous sign-in failed ({req.responseCode}): {req.downloadHandler.text}. " +
                                 "Is Anonymous sign-in enabled in Firebase Auth?");
                yield break;
            }

            var r = JsonUtility.FromJson<SignUpResponse>(req.downloadHandler.text);
            PlayerPrefs.SetString(AnonRefreshTokenPref, r.refreshToken);
            Apply(r.localId, r.idToken, ReadExpiry(r.idToken));
            Debug.Log("[FirebaseSession] Signed in anonymously for testing.");
        }
#else
        yield break;
#endif
    }

    private static UnityWebRequest PostJson(string url, string json)
    {
        var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 20
        };
        req.SetRequestHeader("Content-Type", "application/json");
        return req;
    }

    private static UnityWebRequest PostForm(string url, string body)
    {
        var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 20
        };
        req.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
        return req;
    }

    [Serializable] private class SignUpResponse { public string idToken; public string refreshToken; public string localId; }
    [Serializable] private class RefreshResponse { public string id_token; public string refresh_token; public string user_id; }

    // ─── Common ───────────────────────────────────────────────────────────────

    private static void Apply(string uid, string idToken, DateTime expiresAtUtc)
    {
        IdToken = idToken ?? string.Empty;
        _expiresAtUtc = expiresAtUtc;
        SetUid(uid ?? string.Empty);
    }

    private static void SetUid(string uid)
    {
        if (uid == Uid) return;

        Uid = uid;
        OnUserChanged?.Invoke(uid);
    }

    [Serializable] private class JwtClaims { public long exp; }

    /// <summary>
    /// The token's own expiry, read from its payload. Firebase ID tokens last an hour; if the payload
    /// can't be read, assume 50 minutes from now.
    /// </summary>
    private static DateTime ReadExpiry(string jwt)
    {
        DateTime fallback = DateTime.UtcNow.AddMinutes(50);
        if (string.IsNullOrEmpty(jwt)) return fallback;

        try
        {
            string[] parts = jwt.Split('.');
            if (parts.Length < 2) return fallback;

            string b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));

            long exp = JsonUtility.FromJson<JwtClaims>(json).exp;
            return exp > 0 ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
