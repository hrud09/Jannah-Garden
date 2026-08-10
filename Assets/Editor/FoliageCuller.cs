using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tool to thin out mass-placed foliage in the open scene.
/// The Outer Garden scene contains ~18,700 individual foliage prefab instances
/// (leaves, ferns, undergrowth, pebbles, mushrooms, moss), which is the main
/// cause of low FPS on mobile. This tool deletes a configurable fraction of
/// matching instances so you can bring the object count down to a sane budget.
///
/// Everything is registered with Undo, so you can revert with Ctrl+Z.
/// Menu: Tools > Performance > Foliage Culler
/// </summary>
public class FoliageCuller : EditorWindow
{
    // Name fragments that identify the mass-placed ground detail we can safely thin.
    private string _keywords = "Leaves,Fern,Undergrowth,Pebble,Mushroom,Moss";

    // Keep 1 out of every N matches; delete the rest. keepEveryN = 4 -> removes 75%.
    private int _keepEveryN = 4;

    // Deterministic seed so a given ratio always thins the same instances (repeatable).
    private int _seed = 12345;

    // If true, only consider objects under the current Selection (scoped cull).
    private bool _restrictToSelection = false;

    private int _lastMatchCount = -1;

    [MenuItem("Tools/Performance/Foliage Culler")]
    public static void Open()
    {
        GetWindow<FoliageCuller>("Foliage Culler");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Thin mass-placed foliage in the OPEN scene", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Deletes a fraction of matching prefab instances to reduce draw calls.\n" +
            "All deletions are undoable (Ctrl+Z). Save the scene afterwards.",
            MessageType.Info);

        _keywords = EditorGUILayout.TextField(
            new GUIContent("Name contains (CSV)", "Case-insensitive name fragments to match."),
            _keywords);

        _keepEveryN = Mathf.Max(1, EditorGUILayout.IntField(
            new GUIContent("Keep 1 of every N", "N=4 keeps 25% and deletes 75%. N=1 deletes nothing."),
            _keepEveryN));

        _seed = EditorGUILayout.IntField(
            new GUIContent("Random seed", "Deterministic thinning; same seed = same result."),
            _seed);

        _restrictToSelection = EditorGUILayout.Toggle(
            new GUIContent("Restrict to selection", "Only cull under the currently selected object(s)."),
            _restrictToSelection);

        float removePct = _keepEveryN <= 1 ? 0f : (1f - 1f / _keepEveryN) * 100f;
        EditorGUILayout.LabelField($"Will remove ~{removePct:0}% of matches");

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Count Matches (dry run)"))
            {
                var matches = FindMatches();
                _lastMatchCount = matches.Count;
                Debug.Log($"[FoliageCuller] {matches.Count} matching foliage instances found.");
            }

            using (new EditorGUI.DisabledScope(_keepEveryN <= 1))
            {
                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button($"Cull (keep 1 of {_keepEveryN})"))
                {
                    Cull();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        if (_lastMatchCount >= 0)
            EditorGUILayout.LabelField($"Last count: {_lastMatchCount} matches");
    }

    private List<GameObject> FindMatches()
    {
        var keys = new List<string>();
        foreach (var k in _keywords.Split(','))
        {
            var t = k.Trim();
            if (t.Length > 0) keys.Add(t.ToLowerInvariant());
        }

        var results = new List<GameObject>();

        IEnumerable<Transform> roots;
        if (_restrictToSelection && Selection.transforms.Length > 0)
        {
            roots = Selection.transforms;
        }
        else
        {
            var scene = SceneManager.GetActiveScene();
            var list = new List<Transform>();
            foreach (var go in scene.GetRootGameObjects())
                list.Add(go.transform);
            roots = list;
        }

        foreach (var root in roots)
        {
            // includeInactive: true so hidden foliage is counted too.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.gameObject.name.ToLowerInvariant();
                foreach (var key in keys)
                {
                    if (n.Contains(key))
                    {
                        results.Add(t.gameObject);
                        break;
                    }
                }
            }
        }

        return results;
    }

    private void Cull()
    {
        var matches = FindMatches();
        if (matches.Count == 0)
        {
            EditorUtility.DisplayDialog("Foliage Culler", "No matching foliage found in the open scene.", "OK");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "Foliage Culler",
            $"Found {matches.Count} matching instances.\n" +
            $"This will DELETE roughly {(1f - 1f / _keepEveryN) * 100f:0}% of them (keeping 1 of every {_keepEveryN}).\n\n" +
            "This is undoable with Ctrl+Z. Continue?",
            "Cull", "Cancel");
        if (!ok) return;

        // Deterministic Fisher–Yates shuffle, then delete all but every Nth —
        // gives an even spatial thinning rather than deleting in placement order.
        var rng = new System.Random(_seed);
        for (int i = matches.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (matches[i], matches[j]) = (matches[j], matches[i]);
        }

        int deleted = 0;
        Undo.SetCurrentGroupName("Cull Foliage");
        int group = Undo.GetCurrentGroup();

        for (int i = 0; i < matches.Count; i++)
        {
            // Keep index 0, N, 2N ... delete the rest.
            if (i % _keepEveryN == 0) continue;
            var go = matches[i];
            if (go == null) continue;
            Undo.DestroyObjectImmediate(go);
            deleted++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[FoliageCuller] Deleted {deleted} of {matches.Count} foliage instances. " +
                  "Save the scene to keep the change.");
        EditorUtility.DisplayDialog("Foliage Culler",
            $"Deleted {deleted} of {matches.Count} instances.\nSave the scene (Ctrl+S) to keep it.", "OK");
    }
}
