using UnityEngine;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

/// <summary>
/// Local save files, one per key, written as JSON into <see cref="Application.persistentDataPath"/>.
///
/// <para>This used to write <c>.dat</c> files with <c>BinaryFormatter</c>. That does not survive the
/// trip to device: iOS (and Android) builds run on IL2CPP, where BinaryFormatter's reflection-driven
/// (de)serialisation of generic collections fails — and because every failure here was caught and
/// logged, a broken save looked exactly like a fresh start on every launch. JsonUtility is Unity's own
/// serialiser and works the same in the editor and on every player platform.</para>
///
/// <para>Old <c>.dat</c> files are still read once, when no JSON file exists for the key, and are
/// converted on the spot. If the old file cannot be read (it never could on iOS) it is left alone and
/// the key loads as empty, exactly as it did before.</para>
///
/// <para>Most callers want <see cref="CloudSave.Save{T}"/> instead of <see cref="Save{T}"/> — it writes
/// here too, and also carries the data to Firebase through Flutter.</para>
/// </summary>
public static class SaveSystem
{
    private const string JsonExtension = ".json";
    private const string TempExtension = ".json.tmp";
    private const string LegacyExtension = ".dat";

    /// <summary>
    /// Every value is stored inside this box, so a bare <c>int</c> or <c>List&lt;string&gt;</c> — which
    /// JsonUtility refuses as a top-level value — saves the same way as a class.
    /// </summary>
    [Serializable]
    private class JsonBox<T>
    {
        public T value;
    }

    private static string PathFor(string key, string extension) =>
        Path.Combine(Application.persistentDataPath, key + extension);

    // ─── Typed API ────────────────────────────────────────────────────────────

    /// <summary>Saves any JsonUtility-serialisable value under <paramref name="key"/>.</summary>
    public static void Save<T>(string key, T data)
    {
        SaveRaw(key, ToJson(data));
    }

    /// <summary>
    /// Loads the value saved under <paramref name="key"/>, or <c>default</c> when there is none or it
    /// cannot be read.
    /// </summary>
    public static T Load<T>(string key)
    {
        string json = LoadRaw(key);
        if (json != null) return FromJson<T>(json, key);

        return TryMigrateLegacy(key, out T migrated) ? migrated : default;
    }

    /// <summary>True when something is saved under <paramref name="key"/>, in either format.</summary>
    public static bool Exists(string key)
    {
        return File.Exists(PathFor(key, JsonExtension))
            || File.Exists(PathFor(key, TempExtension))
            || File.Exists(PathFor(key, LegacyExtension));
    }

    /// <summary>Deletes whatever is saved under <paramref name="key"/>.</summary>
    public static void Delete(string key)
    {
        TryDelete(PathFor(key, JsonExtension));
        TryDelete(PathFor(key, TempExtension));
        TryDelete(PathFor(key, LegacyExtension));
    }

    /// <summary>Deletes every save file this system wrote and wipes PlayerPrefs.</summary>
    public static void ClearAll()
    {
        string directory = Application.persistentDataPath;
        if (Directory.Exists(directory))
        {
            foreach (string pattern in new[] { "*" + JsonExtension, "*" + TempExtension, "*" + LegacyExtension })
            {
                foreach (string file in Directory.GetFiles(directory, pattern)) TryDelete(file);
            }
        }

        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[SaveSystem] Cleared all save data (save files & PlayerPrefs).");
    }

    // ─── JSON helpers (shared with CloudSave) ─────────────────────────────────

    /// <summary>The exact text <see cref="Save{T}"/> would write for <paramref name="data"/>.</summary>
    public static string ToJson<T>(T data) => JsonUtility.ToJson(new JsonBox<T> { value = data });

    /// <summary>Reads text produced by <see cref="ToJson{T}"/>. <c>default</c> when it cannot be parsed.</summary>
    public static T FromJson<T>(string json, string keyForLogs = null)
    {
        if (string.IsNullOrEmpty(json)) return default;

        try
        {
            JsonBox<T> box = JsonUtility.FromJson<JsonBox<T>>(json);
            return box != null ? box.value : default;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Could not parse saved data{(keyForLogs != null ? $" for '{keyForLogs}'" : "")}: {e.Message}");
            return default;
        }
    }

    // ─── Raw API (used by CloudSave, which moves JSON text around untouched) ─

    /// <summary>
    /// Writes <paramref name="json"/> under <paramref name="key"/>. Goes through a temp file and a
    /// rename, so a crash or a kill mid-write leaves the previous save intact instead of a half file.
    /// </summary>
    public static void SaveRaw(string key, string json)
    {
        string path = PathFor(key, JsonExtension);
        string temp = PathFor(key, TempExtension);

        try
        {
            File.WriteAllText(temp, json);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, null);
                }
                catch (Exception)
                {
                    // Not every platform's file system implements Replace. Delete-then-move has a tiny
                    // window with no file at all; LoadRaw covers it by reading the temp file.
                    File.Delete(path);
                    File.Move(temp, path);
                }
            }
            else
            {
                File.Move(temp, path);
            }

            // A save in the new format supersedes the old one for good.
            TryDelete(PathFor(key, LegacyExtension));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Failed to save '{key}' to {path}: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>The JSON text saved under <paramref name="key"/>, or null when there is none.</summary>
    public static string LoadRaw(string key)
    {
        string path = PathFor(key, JsonExtension);
        string temp = PathFor(key, TempExtension);

        try
        {
            if (File.Exists(path)) return File.ReadAllText(path);

            // Killed between deleting the old file and moving the new one into place.
            if (File.Exists(temp)) return File.ReadAllText(temp);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Failed to read '{key}': {e.GetType().Name}: {e.Message}");
        }

        return null;
    }

    // ─── Legacy .dat migration ────────────────────────────────────────────────

    private static bool TryMigrateLegacy<T>(string key, out T value)
    {
        value = default;

        string legacyPath = PathFor(key, LegacyExtension);
        if (!File.Exists(legacyPath)) return false;

        try
        {
#pragma warning disable SYSLIB0011 // BinaryFormatter is obsolete — read-only here, for migration.
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream stream = new FileStream(legacyPath, FileMode.Open, FileAccess.Read))
            {
                value = (T)formatter.Deserialize(stream);
            }
#pragma warning restore SYSLIB0011
        }
        catch (Exception e)
        {
            // Expected on IL2CPP builds — the reason this class moved to JSON. Leave the file so a
            // later, fixed build can still try; the key simply loads empty, as it always did there.
            Debug.LogWarning($"[SaveSystem] Could not read legacy save '{key}.dat' ({e.GetType().Name}: {e.Message}). Starting it fresh.");
            value = default;
            return false;
        }

        // SaveRaw deletes the .dat once the JSON copy is safely written.
        Save(key, value);
        Debug.Log($"[SaveSystem] Migrated legacy save '{key}.dat' to JSON.");
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] Failed to delete {path}: {e.Message}");
        }
    }
}

/// <summary>
/// Serializable stand-in for <see cref="Vector3"/>. Plain public fields, so JsonUtility (and the old
/// BinaryFormatter saves) both read it.
/// </summary>
[System.Serializable]
public struct SerializableVector3
{
    public float x;
    public float y;
    public float z;

    public SerializableVector3(Vector3 v)
    {
        x = v.x;
        y = v.y;
        z = v.z;
    }

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }

    public static implicit operator SerializableVector3(Vector3 v) => new SerializableVector3(v);
    public static implicit operator Vector3(SerializableVector3 sv) => sv.ToVector3();
}

/// <summary>Serializable stand-in for <see cref="Quaternion"/>. See <see cref="SerializableVector3"/>.</summary>
[System.Serializable]
public struct SerializableQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public SerializableQuaternion(Quaternion q)
    {
        x = q.x;
        y = q.y;
        z = q.z;
        w = q.w;
    }

    public Quaternion ToQuaternion()
    {
        return new Quaternion(x, y, z, w);
    }

    public static implicit operator SerializableQuaternion(Quaternion q) => new SerializableQuaternion(q);
    public static implicit operator Quaternion(SerializableQuaternion sq) => sq.ToQuaternion();
}
