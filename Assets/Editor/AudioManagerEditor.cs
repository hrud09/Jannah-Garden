using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[CustomEditor(typeof(AudioManager))]
public class AudioManagerEditor : Editor
{
    /// <summary>Roots searched for clips, in order. Later roots do not override earlier ones.</summary>
    private static readonly string[] SearchFolders =
    {
        "Assets/Audio"
    };

    /// <summary>
    /// Effects that must never be given a clip. Locked-zone crossings are deliberately silent:
    /// entering locked ground, walking through it and leaving it all play nothing.
    /// </summary>
    private static readonly HashSet<SoundEffect> Silent = new HashSet<SoundEffect>
    {
        SoundEffect.AreaEnterLocked,
    };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        AudioManager audioManager = (AudioManager)target;

        GUILayout.Space(15);

        GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f);
        if (GUILayout.Button("Populate Sounds From Library Map", GUILayout.Height(35)))
        {
            PopulateSounds(audioManager, overwriteExisting: false);
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(4);

        GUI.backgroundColor = new Color(0.9f, 0.6f, 0.3f);
        if (GUILayout.Button("Re-apply Map (overwrite existing clips)", GUILayout.Height(26)))
        {
            bool ok = EditorUtility.DisplayDialog(
                "Overwrite every mapped clip?",
                "This replaces the clip, variations, volume, pitch, cooldown and category on every "
                + "effect listed in AudioLibraryMap, discarding hand-tuning on those rows.\n\n"
                + "Effects not in the map are left untouched.",
                "Overwrite", "Cancel");

            if (ok) PopulateSounds(audioManager, overwriteExisting: true);
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(4);

        if (GUILayout.Button("Report Unmapped / Silent Effects", GUILayout.Height(26)))
        {
            ReportCoverage(audioManager);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  MENU ENTRY POINTS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Populates the AudioManager in the open scene without needing it selected. Fills only empty
    /// rows, so hand-tuned clips survive; use the inspector's overwrite button to force a re-apply.
    /// </summary>
    [MenuItem("Jannah/Audio/Populate Sound Library")]
    private static void PopulateFromMenu()
    {
        AudioManager manager = FindManagerInScene();
        if (manager == null) return;

        Selection.activeGameObject = manager.gameObject;
        PopulateSounds(manager, overwriteExisting: false);
        MarkSceneDirty(manager);
    }

    [MenuItem("Jannah/Audio/Re-apply Sound Library (overwrite)")]
    private static void RepopulateFromMenu()
    {
        AudioManager manager = FindManagerInScene();
        if (manager == null) return;

        Selection.activeGameObject = manager.gameObject;
        PopulateSounds(manager, overwriteExisting: true);
        MarkSceneDirty(manager);
    }

    [MenuItem("Jannah/Audio/Report Unmapped Effects")]
    private static void ReportFromMenu()
    {
        AudioManager manager = FindManagerInScene();
        if (manager != null) ReportCoverage(manager);
    }

    private static AudioManager FindManagerInScene()
    {
        // Includes inactive objects: the manager is often parked on a disabled bootstrap root.
        AudioManager manager = UnityEngine.Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            EditorUtility.DisplayDialog("No AudioManager",
                "No AudioManager was found in the open scene. Open the garden scene first.", "OK");
        }

        return manager;
    }

    private static void MarkSceneDirty(AudioManager manager)
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  POPULATE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs the populate against the AudioManager in the open scene. Public so automation can call
    /// it with <paramref name="showDialogs"/> false; a modal would block a headless invocation.
    /// Returns a one-line summary.
    /// </summary>
    public static string PopulateInScene(bool overwriteExisting, bool showDialogs)
    {
        AudioManager manager = UnityEngine.Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
        if (manager == null) return "No AudioManager in the open scene.";

        string summary = PopulateSounds(manager, overwriteExisting, showDialogs);
        MarkSceneDirty(manager);
        return summary;
    }

    private static string PopulateSounds(AudioManager manager, bool overwriteExisting, bool showDialogs = true)
    {
        Dictionary<string, AudioClip> clipsByName = BuildClipIndex();

        if (clipsByName.Count == 0)
        {
            string none = "No AudioClips found under: " + string.Join(", ", SearchFolders);
            if (showDialogs) EditorUtility.DisplayDialog("No Audio Clips", none, "OK");
            return none;
        }

        Undo.RecordObject(manager, "Populate Sounds");

        // Preserve whatever is already wired up, keyed by effect.
        Dictionary<SoundEffect, Sound> existing = new Dictionary<SoundEffect, Sound>();
        if (manager.sounds != null)
        {
            foreach (Sound s in manager.sounds)
                if (s != null && !existing.ContainsKey(s.effect)) existing[s.effect] = s;
        }

        List<Sound> result = new List<Sound>();
        int filled = 0, kept = 0, unmapped = 0;
        List<string> missingClips = new List<string>();

        foreach (SoundEffect effect in (SoundEffect[])Enum.GetValues(typeof(SoundEffect)))
        {
            // Retired ids get an empty row rather than a guessed clip. The id stays in the enum so
            // every effect after it keeps its serialized value.
            if (Silent.Contains(effect))
            {
                result.Add(new Sound { effect = effect });
                continue;
            }

            Sound sound;
            bool isNew = !existing.TryGetValue(effect, out sound);
            if (isNew) sound = new Sound { effect = effect };

            AudioLibraryMap.Entry entry;
            bool mapped = AudioLibraryMap.Table.TryGetValue(effect, out entry);

            if (!mapped)
            {
                // Not in the curated table: fall back to the old keyword guess, but only to fill a
                // genuinely empty row. A guessed clip is better than silence and worse than a choice.
                if (sound.clip == null)
                {
                    sound.clip = GuessClipFor(effect, clipsByName);
                    if (sound.clip != null) filled++; else unmapped++;
                }
                else kept++;

                result.Add(sound);
                continue; // falls through to the next effect
            }

            bool alreadyHasClip = sound.clip != null;
            if (alreadyHasClip && !overwriteExisting)
            {
                kept++;
                result.Add(sound);
                continue;
            }

            List<AudioClip> resolved = new List<AudioClip>();
            foreach (string name in entry.clips)
            {
                AudioClip clip;
                if (clipsByName.TryGetValue(Normalize(name), out clip) && !resolved.Contains(clip))
                    resolved.Add(clip);
            }

            if (resolved.Count == 0)
            {
                missingClips.Add(effect + "  ->  " + string.Join(", ", entry.clips));
                unmapped++;
                result.Add(sound);
                continue;
            }

            sound.clip = resolved[0];
            sound.variations = resolved.Count > 1 ? resolved.Skip(1).ToArray() : new AudioClip[0];

            AudioLibraryMap.Shaping shape = entry.shaping;
            sound.volume = shape.volume;
            sound.pitch = shape.pitch;
            sound.pitchVariance = shape.pitchVariance;
            sound.cooldown = shape.cooldown;
            sound.category = shape.category;
            sound.spatial = shape.spatial;

            filled++;
            result.Add(sound);
        }

        manager.sounds = result.ToArray();
        manager.RefreshLookup();
        EditorUtility.SetDirty(manager);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"[AudioManagerEditor] Populated {result.Count} effects "
                       + $"-- {filled} assigned, {kept} left as-is, {unmapped} still silent.");

        if (missingClips.Count > 0)
        {
            log.AppendLine();
            log.AppendLine("Mapped but no matching file in the project (drop the clip into "
                           + "Assets/Audio/SFX Packs/ and re-run):");
            foreach (string m in missingClips) log.AppendLine("  " + m);
        }

        Debug.Log(log.ToString());

        string summary = $"Effects: {result.Count} | Assigned: {filled} | Left as-is: {kept} "
                       + $"| Still silent: {unmapped} | Unresolved rows: {missingClips.Count}";

        if (showDialogs)
        {
            EditorUtility.DisplayDialog("Populate Sounds",
                summary.Replace(" | ", "\n")
                + (missingClips.Count > 0 ? "\n\nSee the Console for the unresolved rows." : ""),
                "OK");
        }

        return summary;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  COVERAGE REPORT
    // ═════════════════════════════════════════════════════════════════════════

    private static void ReportCoverage(AudioManager manager)
    {
        Dictionary<SoundEffect, Sound> byEffect = new Dictionary<SoundEffect, Sound>();
        if (manager.sounds != null)
        {
            foreach (Sound s in manager.sounds)
                if (s != null && !byEffect.ContainsKey(s.effect)) byEffect[s.effect] = s;
        }

        List<string> silent = new List<string>();
        List<string> single = new List<string>();

        int intentionallySilent = 0;

        foreach (SoundEffect effect in (SoundEffect[])Enum.GetValues(typeof(SoundEffect)))
        {
            // Retired ids are meant to be clipless, so they are not a coverage gap.
            if (Silent.Contains(effect))
            {
                intentionallySilent++;
                continue;
            }

            Sound s;
            if (!byEffect.TryGetValue(effect, out s) || s.ClipCount == 0)
            {
                silent.Add(effect.ToString());
                continue;
            }

            if (s.ClipCount == 1 && s.pitchVariance <= 0f) single.Add(effect.ToString());
        }

        StringBuilder sb = new StringBuilder();
        int total = Enum.GetValues(typeof(SoundEffect)).Length - intentionallySilent;
        sb.AppendLine($"[AudioManagerEditor] Coverage: {total - silent.Count} of {total} "
                      + $"effects audible ({intentionallySilent} retired, silent by design).");

        if (silent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SILENT (no clip assigned):");
            foreach (string e in silent) sb.AppendLine("  " + e);
        }

        if (single.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Single clip and no pitch variance -- will sound identical on every repeat:");
            foreach (string e in single) sb.AppendLine("  " + e);
        }

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Audio Coverage",
            $"Audible: {Enum.GetValues(typeof(SoundEffect)).Length - silent.Count}"
            + $" / {Enum.GetValues(typeof(SoundEffect)).Length}\nSilent: {silent.Count}"
            + $"\nNo variation: {single.Count}\n\nFull list in the Console.", "OK");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CLIP INDEX
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Filename stem -> clip, for every AudioClip under <see cref="SearchFolders"/>.
    ///
    /// The UI packs ship the same sound three times (.wav/.ogg/.mp3) under one stem, so format
    /// preference decides the winner: WAV first for the shortest decode on a one-shot, then OGG,
    /// then MP3. Apple resource-fork directories are skipped -- they contain junk files whose names
    /// collide with the real ones.
    /// </summary>
    private static Dictionary<string, AudioClip> BuildClipIndex()
    {
        Dictionary<string, AudioClip> index = new Dictionary<string, AudioClip>();
        Dictionary<string, int> rank = new Dictionary<string, int>();

        List<string> validFolders = SearchFolders.Where(AssetDatabase.IsValidFolder).ToList();
        if (validFolders.Count == 0) return index;

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", validFolders.ToArray()))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("__MACOSX")) continue;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            string key = Normalize(Path.GetFileNameWithoutExtension(path));
            int score = FormatRank(Path.GetExtension(path));

            int currentScore;
            if (!index.ContainsKey(key) || (rank.TryGetValue(key, out currentScore) && score > currentScore))
            {
                index[key] = clip;
                rank[key] = score;
            }
        }

        return index;
    }

    private static int FormatRank(string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".wav":  return 3;
            case ".ogg":  return 2;
            case ".mp3":  return 1;
            default:      return 0;
        }
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    // ═════════════════════════════════════════════════════════════════════════
    //  FALLBACK KEYWORD GUESS (only for effects absent from AudioLibraryMap)
    // ═════════════════════════════════════════════════════════════════════════

    private static AudioClip GuessClipFor(SoundEffect effect, Dictionary<string, AudioClip> clips)
    {
        // Split the enum name into words ("TreasureBoxOpen" -> treasure, box, open) and score a
        // clip by how many of them its filename contains.
        List<string> words = SplitCamelCase(effect.ToString());
        if (words.Count == 0) return null;

        AudioClip best = null;
        int bestScore = 0;

        foreach (KeyValuePair<string, AudioClip> kv in clips)
        {
            int score = words.Count(w => kv.Key.Contains(w));
            if (score > bestScore)
            {
                bestScore = score;
                best = kv.Value;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static List<string> SplitCamelCase(string name)
    {
        List<string> words = new List<string>();
        StringBuilder current = new StringBuilder();

        foreach (char c in name)
        {
            if (char.IsUpper(c) && current.Length > 0)
            {
                words.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
            current.Append(c);
        }

        if (current.Length > 0) words.Add(current.ToString().ToLowerInvariant());

        // One- and two-letter fragments match almost everything and would drown the real signal.
        return words.Where(w => w.Length > 2).ToList();
    }
}
