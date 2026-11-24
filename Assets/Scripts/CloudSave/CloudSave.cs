using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Keeps chosen game saves in Firestore, against the signed-in player's account, talking to Firestore
/// directly over its REST API. The only thing borrowed from the Flutter app is the player's login token
/// (see <see cref="FirebaseSession"/>); every read and write happens here.
///
/// <para>Each save is one document, <c>users/{uid}/gameSaves/{key}</c>, holding the save's JSON text
/// plus the time it was saved and a counter.</para>
///
/// <para><b>Using it from a manager</b> is two calls:</para>
/// <list type="bullet">
/// <item><see cref="Save{T}"/> in place of <c>SaveSystem.Save</c>. It writes the local file straight
/// away (so the game still loads instantly and works offline) and uploads a couple of seconds later.</item>
/// <item><see cref="Register"/> in Awake, <i>before</i> loading, with a callback that re-applies a save.
/// It is called when a newer copy arrives from the cloud — the player played on another device, or
/// reinstalled — with the JSON text (read it with <c>SaveSystem.FromJson</c>), or with null when a
/// different account signed in and has no save for that key (reset to defaults).</item>
/// </list>
///
/// <para><b>Which copy wins.</b> Every key carries the time it was saved and a counter. When the cloud
/// is read, the newer copy of each key wins: a newer cloud copy is written to the local file and handed
/// to the manager, a newer local copy is uploaded. Two exceptions guard against losing progress:</para>
/// <list type="bullet">
/// <item>The first time this device hears from an account (fresh install, reinstall, new sign-in), the
/// cloud wins every key it has. A fresh install writes default state at startup — starting areas, a new
/// treasure box cycle — and that must never count as "newer" than real progress.</item>
/// <item>Nothing is uploaded until the account's saves have been read successfully. A failed or offline
/// read must not be mistaken for an empty account and overwritten.</item>
/// </list>
///
/// <para>An upload only counts as done when Firestore confirms it. Until then the key stays pending and
/// is retried — after a short wait, on the next save, or on the next resume — so a write cut off by the
/// app going to the background is not lost.</para>
/// </summary>
public class CloudSave : MonoBehaviour
{
    // ─── Tuning ───────────────────────────────────────────────────────────────

    /// <summary>A burst of saves within this many seconds becomes one upload per key.</summary>
    private const float PushDelaySeconds = 2f;

    /// <summary>Wait before retrying a failed read or upload.</summary>
    private const float RetrySeconds = 15f;

    /// <summary>Read attempts per start/resume before waiting for the next resume.</summary>
    private const int SyncMaxAttempts = 6;

    private const int RequestTimeoutSeconds = 20;

    /// <summary>Local key the bookkeeping is stored under. Never uploaded itself.</summary>
    private const string MetaKey = "cloud_save_meta";

    // ─── Bookkeeping (persisted) ──────────────────────────────────────────────

    [Serializable]
    private class KeyMeta
    {
        public string key;
        public long savedAtMs;
        public int revision;

        /// <summary>Changed locally and not yet confirmed written to Firestore.</summary>
        public bool pending;
    }

    [Serializable]
    private class Meta
    {
        /// <summary>The account the local saves belong to. Empty until the cloud is first read.</summary>
        public string ownerUid = string.Empty;

        /// <summary>True once this device has completed a sync with <see cref="ownerUid"/>.</summary>
        public bool everSynced;

        public List<KeyMeta> keys = new List<KeyMeta>();
    }

    /// <summary>One save as stored in the cloud.</summary>
    private class RemoteEntry
    {
        public string key;
        public string json;
        public long savedAtMs;
        public int revision;
    }

    // ─── Firestore REST shapes ────────────────────────────────────────────────

    [Serializable] private class FsString { public string stringValue; }

    /// <summary>Firestore's REST API carries 64-bit integers as strings.</summary>
    [Serializable] private class FsInt { public string integerValue; }

    [Serializable]
    private class FsFields
    {
        public FsString json;
        public FsInt savedAtMs;
        public FsInt revision;
    }

    [Serializable] private class FsDocument { public string name; public FsFields fields; }
    [Serializable] private class FsWrite { public FsFields fields; }
    [Serializable] private class FsList { public FsDocument[] documents; public string nextPageToken; }

    // ─── State ────────────────────────────────────────────────────────────────

    private static CloudSave _instance;
    private static Meta _meta;
    private static readonly Dictionary<string, Action<string>> Listeners = new Dictionary<string, Action<string>>();

    /// <summary>The JSON last written per key, so a save that changes nothing costs nothing.</summary>
    private static readonly Dictionary<string, string> LastJson = new Dictionary<string, string>();

    /// <summary>Keys with an upload on the wire, so a key is never written twice at once.</summary>
    private static readonly HashSet<string> InFlight = new HashSet<string>();

    private Coroutine _pushRoutine;
    private Coroutine _syncRoutine;
    private Coroutine _retryRoutine;

    /// <summary>The uid the cloud was last read for in this run.</summary>
    private static string _syncedUid = string.Empty;

    /// <summary>
    /// True once the signed-in account's saves have been read during this run. Uploads wait for it.
    /// </summary>
    public static bool IsCloudReady { get; private set; }

    // ─── Bootstrap ────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Domain reload may be off in the editor; start each play session clean.
        _instance = null;
        _meta = null;
        Listeners.Clear();
        LastJson.Clear();
        InFlight.Clear();
        IsCloudReady = false;
        _syncedUid = string.Empty;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        GameObject host = new GameObject("Cloud Save");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<CloudSave>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        FirebaseSession.OnUserChanged += HandleUserChanged;
    }

    private void Start()
    {
        BeginSync("game start");
    }

    private void OnDestroy()
    {
        if (_instance != this) return;

        FirebaseSession.OnUserChanged -= HandleUserChanged;
        _instance = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            // Frames stop once we return from here. Requests already started keep going natively and
            // finish (or fail and stay pending) whenever the app runs again.
            PushPendingNow();
        }
        else
        {
            // The player may have played on another device meanwhile — read the cloud again.
            BeginSync("game resumed");
        }
    }

    private void OnApplicationQuit()
    {
        PushPendingNow();
    }

    /// <summary>Another account signed in (or the first one did): read its saves.</summary>
    private void HandleUserChanged(string uid)
    {
        if (string.IsNullOrEmpty(uid))
        {
            IsCloudReady = false;
            return;
        }

        if (uid != _syncedUid) BeginSync("account changed");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Saves <paramref name="data"/> locally under <paramref name="key"/> and uploads it to the player's
    /// account shortly after. A save identical to the last one is skipped entirely.
    /// </summary>
    public static void Save<T>(string key, T data)
    {
        if (string.IsNullOrEmpty(key)) return;

        string json = SaveSystem.ToJson(data);

        if (!LastJson.TryGetValue(key, out string previous)) previous = SaveSystem.LoadRaw(key);

        KeyMeta meta = GetOrAddKeyMeta(key);

        // Nothing changed: no disk write, no upload, and — importantly — no newer timestamp, so a
        // manager that saves defensively (every pause, every tick) cannot make a stale copy look fresh.
        if (json == previous && meta.savedAtMs > 0)
        {
            LastJson[key] = json;
            return;
        }

        SaveSystem.SaveRaw(key, json);
        LastJson[key] = json;

        meta.savedAtMs = NowMs();
        meta.revision++;
        meta.pending = true;
        SaveMeta();

        SchedulePush();
    }

    /// <summary>
    /// Asks to be told when a newer copy of <paramref name="key"/> arrives from the cloud. Call it before
    /// loading the key, so a copy that lands after the load is not missed. One listener per key; a later
    /// registration replaces an earlier one.
    /// </summary>
    public static void Register(string key, Action<string> onCloudData)
    {
        if (string.IsNullOrEmpty(key) || onCloudData == null) return;

        Listeners[key] = onCloudData;

        // Known from now on, so it is uploaded on the first sync even if this device has never saved it
        // through here (progress from before cloud saves existed).
        GetOrAddKeyMeta(key);
    }

    /// <summary>Stops listening, if <paramref name="onCloudData"/> is still the registered listener.</summary>
    public static void Unregister(string key, Action<string> onCloudData)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (Listeners.TryGetValue(key, out Action<string> current) && current == onCloudData)
        {
            Listeners.Remove(key);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DOWNLOAD
    // ═══════════════════════════════════════════════════════════════════════════

    private void BeginSync(string reason)
    {
        if (_syncRoutine != null) StopCoroutine(_syncRoutine);
        _syncRoutine = StartCoroutine(SyncRoutine(reason));
    }

    private IEnumerator SyncRoutine(string reason)
    {
        // A re-read has to be earned again before uploading — otherwise a save made in the first
        // seconds after resuming could go up before we know whose account this is.
        IsCloudReady = false;

        for (int attempt = 1; attempt <= SyncMaxAttempts; attempt++)
        {
            yield return FirebaseSession.EnsureToken();

            if (FirebaseSession.SignedOut)
            {
                Debug.Log("[CloudSave] Nobody is signed in — playing on this device's saves only.");
                break;
            }

            if (FirebaseSession.HasValidToken)
            {
                string uid = FirebaseSession.Uid;
                var entries = new List<RemoteEntry>();
                long status = 0;
                string error = null;

                yield return ReadAll(uid, entries, (s, e) => { status = s; error = e; });

                if (error == null)
                {
                    ApplyRemote(uid, entries);
                    _syncRoutine = null;
                    yield break;
                }

                Debug.LogWarning($"[CloudSave] Reading saves failed ({reason}, attempt {attempt}/{SyncMaxAttempts}): {status} {error}");

                // Rejected token: get a fresh one rather than retrying with the same.
                if (status == 401 || status == 403) FirebaseSession.Invalidate();
            }
            else
            {
                Debug.LogWarning($"[CloudSave] No login token yet ({reason}, attempt {attempt}/{SyncMaxAttempts}).");
            }

            yield return new WaitForSecondsRealtime(attempt < 3 ? 3f : RetrySeconds);
        }

        Debug.LogWarning("[CloudSave] Could not read the account's saves — playing on this device's copy; " +
                         "changes stay queued and upload once the cloud is reachable (next resume at the latest).");
        _syncRoutine = null;
    }

    /// <summary>Lists every document in the player's gameSaves collection.</summary>
    private static IEnumerator ReadAll(string uid, List<RemoteEntry> into, Action<long, string> done)
    {
        string pageToken = null;

        do
        {
            string url = CollectionUrl(uid) + "?pageSize=300"
                + (string.IsNullOrEmpty(pageToken) ? "" : "&pageToken=" + UnityWebRequest.EscapeURL(pageToken));

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = RequestTimeoutSeconds;
                req.SetRequestHeader("Authorization", "Bearer " + FirebaseSession.IdToken);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    done(req.responseCode, ShortError(req));
                    yield break;
                }

                FsList list = JsonUtility.FromJson<FsList>(req.downloadHandler.text);
                pageToken = list?.nextPageToken;

                if (list?.documents != null)
                {
                    foreach (FsDocument doc in list.documents)
                    {
                        RemoteEntry entry = FromDocument(doc);
                        if (entry != null) into.Add(entry);
                    }
                }
            }
        }
        while (!string.IsNullOrEmpty(pageToken));

        done(200, null);
    }

    private void ApplyRemote(string uid, List<RemoteEntry> entries)
    {
        Meta meta = LoadMeta();

        bool accountSwitched = !string.IsNullOrEmpty(meta.ownerUid) && meta.ownerUid != uid;
        bool cloudWinsAll = accountSwitched || !meta.everSynced;

        var remote = new Dictionary<string, RemoteEntry>();
        foreach (RemoteEntry entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.key) && entry.key != MetaKey) remote[entry.key] = entry;
        }

        if (accountSwitched)
        {
            Debug.Log("[CloudSave] A different account signed in — dropping the previous account's saves on this device.");

            foreach (KeyMeta km in new List<KeyMeta>(meta.keys))
            {
                if (remote.ContainsKey(km.key)) continue;

                SaveSystem.Delete(km.key);
                LastJson.Remove(km.key);
                km.savedAtMs = 0;
                km.revision = 0;
                km.pending = false;
                Notify(km.key, null);
            }
        }

        meta.ownerUid = uid;

        // Cloud → device.
        foreach (RemoteEntry entry in remote.Values)
        {
            KeyMeta local = GetOrAddKeyMeta(entry.key);
            bool hasLocal = local.savedAtMs > 0 || SaveSystem.Exists(entry.key);

            bool cloudIsNewer = cloudWinsAll
                || !hasLocal
                || entry.savedAtMs > local.savedAtMs
                || (entry.savedAtMs == local.savedAtMs && entry.revision > local.revision);

            if (cloudIsNewer)
            {
                string currentJson = SaveSystem.LoadRaw(entry.key);

                local.savedAtMs = entry.savedAtMs;
                local.revision = entry.revision;
                local.pending = false;

                // Same bytes already here (e.g. this device wrote them): adopt the stamp, skip the reload.
                if (currentJson == entry.json) continue;

                SaveSystem.SaveRaw(entry.key, entry.json);
                LastJson[entry.key] = entry.json;

                Debug.Log($"[CloudSave] '{entry.key}' restored from the cloud.");
                Notify(entry.key, entry.json);
            }
            else if (entry.savedAtMs != local.savedAtMs || entry.revision != local.revision)
            {
                // This device has the newer copy — make sure it goes up.
                local.pending = true;
            }
        }

        // Device → cloud: anything the account has never had (first sync of an existing player).
        foreach (KeyMeta km in meta.keys)
        {
            if (remote.ContainsKey(km.key) || !SaveSystem.Exists(km.key)) continue;

            if (km.savedAtMs == 0) km.savedAtMs = 1; // predates cloud saves; any real cloud copy beats it
            km.pending = true;
        }

        meta.everSynced = true;
        SaveMeta();

        _syncedUid = uid;
        IsCloudReady = true;
        Debug.Log($"[CloudSave] Synced with the cloud — {remote.Count} save(s) on the account.");

        PushPendingNow();
    }

    private static RemoteEntry FromDocument(FsDocument doc)
    {
        if (doc == null || string.IsNullOrEmpty(doc.name) || doc.fields == null) return null;

        int slash = doc.name.LastIndexOf('/');
        string key = slash >= 0 ? doc.name.Substring(slash + 1) : doc.name;

        long.TryParse(doc.fields.savedAtMs?.integerValue, out long savedAtMs);
        int.TryParse(doc.fields.revision?.integerValue, out int revision);

        return new RemoteEntry
        {
            key = Uri.UnescapeDataString(key),
            json = doc.fields.json?.stringValue ?? string.Empty,
            savedAtMs = savedAtMs,
            revision = revision
        };
    }

    private static void Notify(string key, string json)
    {
        if (!Listeners.TryGetValue(key, out Action<string> listener)) return;

        try
        {
            listener(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[CloudSave] Applying the cloud copy of '{key}' failed: {e}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UPLOAD
    // ═══════════════════════════════════════════════════════════════════════════

    private static void SchedulePush()
    {
        if (!IsCloudReady || _instance == null) return; // stays pending; uploaded after the next read

        // Going to the background (focus is lost just before the pause callbacks run, and managers save
        // in theirs): no more frames are coming, so start the upload now rather than after a delay.
        if (!Application.isFocused)
        {
            PushPendingNow();
            return;
        }

        if (_instance._pushRoutine != null) _instance.StopCoroutine(_instance._pushRoutine);
        _instance._pushRoutine = _instance.StartCoroutine(_instance.PushAfterDelay());
    }

    private IEnumerator PushAfterDelay()
    {
        // Realtime: the game sits at timeScale 0 behind panels and during ads.
        yield return new WaitForSecondsRealtime(PushDelaySeconds);
        _pushRoutine = null;
        PushPendingNow();
    }

    private static void PushPendingNow()
    {
        if (_instance == null) return;

        if (_instance._pushRoutine != null)
        {
            _instance.StopCoroutine(_instance._pushRoutine);
            _instance._pushRoutine = null;
        }

        if (!IsCloudReady || string.IsNullOrEmpty(_syncedUid)) return;

        foreach (KeyMeta km in LoadMeta().keys)
        {
            if (!km.pending || InFlight.Contains(km.key)) continue;
            _instance.StartCoroutine(_instance.Upload(km.key, _syncedUid));
        }
    }

    private IEnumerator Upload(string key, string uid)
    {
        InFlight.Add(key);
        bool failed = false;

        try
        {
            // Only yield when there is something to wait for: called from a pause callback, this has to
            // put the request on the wire before returning, because the next frame comes after resume.
            if (!FirebaseSession.HasValidToken) yield return FirebaseSession.EnsureToken();

            KeyMeta km = GetOrAddKeyMeta(key);
            string json = SaveSystem.LoadRaw(key);

            if (!FirebaseSession.HasValidToken || FirebaseSession.Uid != uid)
            {
                // Token unavailable, or the account changed underneath us — the next sync decides.
                failed = true;
                yield break;
            }

            if (json == null)
            {
                km.pending = false;
                SaveMeta();
                yield break;
            }

            long sentSavedAt = km.savedAtMs;
            int sentRevision = km.revision;

            var body = new FsWrite
            {
                fields = new FsFields
                {
                    json = new FsString { stringValue = json },
                    savedAtMs = new FsInt { integerValue = sentSavedAt.ToString() },
                    revision = new FsInt { integerValue = sentRevision.ToString() }
                }
            };

            // PATCH without an update mask replaces the whole document, creating it if needed.
            using (var req = new UnityWebRequest(DocumentUrl(uid, key), "PATCH"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = RequestTimeoutSeconds;
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + FirebaseSession.IdToken);

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[CloudSave] Uploading '{key}' failed: {req.responseCode} {ShortError(req)}");
                    if (req.responseCode == 401 || req.responseCode == 403) FirebaseSession.Invalidate();
                    failed = true;
                    yield break;
                }
            }

            // Only clear the flag if nothing newer was saved while this was on the wire.
            if (km.savedAtMs == sentSavedAt && km.revision == sentRevision)
            {
                km.pending = false;
                SaveMeta();
            }

            Debug.Log($"[CloudSave] Uploaded '{key}' ({json.Length} chars).");
        }
        finally
        {
            InFlight.Remove(key);
        }

        // A newer save landed mid-upload: send that too.
        if (!failed && GetOrAddKeyMeta(key).pending) SchedulePush();
        if (failed) ScheduleRetry();
    }

    private static void ScheduleRetry()
    {
        if (_instance == null || _instance._retryRoutine != null) return;
        _instance._retryRoutine = _instance.StartCoroutine(_instance.RetryAfterDelay());
    }

    private IEnumerator RetryAfterDelay()
    {
        yield return new WaitForSecondsRealtime(RetrySeconds);
        _retryRoutine = null;
        PushPendingNow();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private static string CollectionUrl(string uid) =>
        "https://firestore.googleapis.com/v1/projects/" + CloudSaveSettings.Current.ProjectIdOrDefault
        + "/databases/(default)/documents/users/" + Uri.EscapeDataString(uid) + "/gameSaves";

    private static string DocumentUrl(string uid, string key) => CollectionUrl(uid) + "/" + Uri.EscapeDataString(key);

    private static string ShortError(UnityWebRequest req)
    {
        string text = req.downloadHandler?.text;
        if (string.IsNullOrEmpty(text)) return req.error;
        return text.Length <= 300 ? text : text.Substring(0, 300) + "…";
    }

    private static Meta LoadMeta()
    {
        if (_meta != null) return _meta;

        _meta = SaveSystem.Load<Meta>(MetaKey) ?? new Meta();
        if (_meta.keys == null) _meta.keys = new List<KeyMeta>();
        if (_meta.ownerUid == null) _meta.ownerUid = string.Empty;
        return _meta;
    }

    private static void SaveMeta() => SaveSystem.Save(MetaKey, LoadMeta());

    private static KeyMeta GetOrAddKeyMeta(string key)
    {
        Meta meta = LoadMeta();

        foreach (KeyMeta km in meta.keys)
        {
            if (km.key == key) return km;
        }

        var added = new KeyMeta { key = key };
        meta.keys.Add(added);
        return added;
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
