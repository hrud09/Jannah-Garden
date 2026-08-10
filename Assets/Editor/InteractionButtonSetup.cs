using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the per-interaction buttons in the open scene.
///
/// The garden shipped with a single "Open Button" that stood in for every interaction, so walking up to
/// a question orb still read "open". This splits it into one button per kind by copying that button —
/// same canvas, same sprite, same spot — and relabelling each copy, then wiring them to
/// <see cref="PlayersInteractionManager"/>. Copying rather than building from scratch is what keeps the
/// new buttons in the game's own art instead of Unity's default white box.
///
/// Safe to run more than once: fields already pointing at a button are left alone.
/// </summary>
public static class InteractionButtonSetup
{
    /// <summary>One button to wire up: which field it fills, what it is called, and what it says.</summary>
    private struct ButtonSpec
    {
        public string fieldName;
        public string objectName;
        public string label;
    }

    private static readonly ButtonSpec[] Specs =
    {
        new ButtonSpec { fieldName = "treasureBoxInteractButton", objectName = "Open Button",   label = "open"   },
        new ButtonSpec { fieldName = "orbInteractButton",         objectName = "Answer Button", label = "answer" },
        new ButtonSpec { fieldName = "placedItemInteractButton",  objectName = "Manage Button", label = "manage" },
    };

    [MenuItem("Tools/Setup Interaction Buttons")]
    public static void SetupInteractionButtons()
    {
        PlayersInteractionManager manager =
            Object.FindFirstObjectByType<PlayersInteractionManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            EditorUtility.DisplayDialog(
                "Setup Interaction Buttons",
                "No PlayersInteractionManager in the open scene. Open the Jannah Garden scene and try again.",
                "OK");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);

        Button template = FindTemplateButton(serialized);
        if (template == null)
        {
            EditorUtility.DisplayDialog(
                "Setup Interaction Buttons",
                "No button to copy. Assign one existing interaction button on the PlayersInteractionManager " +
                "first — the new buttons are made from it so they match the rest of the UI.",
                "OK");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup Interaction Buttons");

        List<string> created = new List<string>();
        List<string> reused = new List<string>();

        AdoptSharedButton(serialized, template, reused);

        foreach (ButtonSpec spec in Specs)
        {
            SerializedProperty property = serialized.FindProperty(spec.fieldName);
            if (property == null) continue;

            if (property.objectReferenceValue is Button existing)
            {
                if (!reused.Contains(existing.name)) reused.Add(existing.name);
                continue;
            }

            property.objectReferenceValue = CreateCopy(template, spec);
            created.Add(spec.objectName);
        }

        // ApplyModifiedProperties registers its own undo entry for the manager.
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.DisplayDialog("Setup Interaction Buttons", BuildSummary(created, reused), "OK");
    }

    /// <summary>
    /// The button the scene already has said "open" — it is the treasure box button in all but name.
    /// Moving it into that field and clearing the shared one stops it being shown for orbs and placed
    /// items as well. Only the shared field is migrated; a scene that has already been split is left be.
    /// </summary>
    private static void AdoptSharedButton(SerializedObject serialized, Button template, List<string> reused)
    {
        SerializedProperty shared = serialized.FindProperty("itemInteractButton");
        SerializedProperty treasureBox = serialized.FindProperty("treasureBoxInteractButton");

        if (shared == null || treasureBox == null) return;
        if (shared.objectReferenceValue != template) return;
        if (treasureBox.objectReferenceValue != null) return;

        ButtonSpec spec = Specs[0];

        treasureBox.objectReferenceValue = template;
        shared.objectReferenceValue = null;

        Undo.RecordObject(template.gameObject, "Setup Interaction Buttons");
        template.gameObject.name = spec.objectName;

        TMP_Text label = template.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            Undo.RecordObject(label, "Setup Interaction Buttons");
            label.text = spec.label;
            EditorUtility.SetDirty(label);
        }

        reused.Add(spec.objectName);
    }

    /// <summary>
    /// The button the copies are made from. The shared one comes first because that is the button the
    /// scene has actually been using; failing that, any per-kind button already wired up will do.
    /// </summary>
    private static Button FindTemplateButton(SerializedObject serialized)
    {
        string[] order =
        {
            "itemInteractButton",
            "treasureBoxInteractButton",
            "orbInteractButton",
            "placedItemInteractButton"
        };

        foreach (string fieldName in order)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property != null && property.objectReferenceValue is Button button) return button;
        }

        return null;
    }

    /// <summary>
    /// A copy of the template sitting beside it on the same canvas, at the same position — only one
    /// button is ever on screen at a time, so they are meant to overlap.
    /// </summary>
    private static Button CreateCopy(Button template, ButtonSpec spec)
    {
        GameObject copy = Object.Instantiate(template.gameObject, template.transform.parent);
        Undo.RegisterCreatedObjectUndo(copy, "Setup Interaction Buttons");

        copy.name = spec.objectName;
        copy.SetActive(false); // The manager turns it on when the player looks at the right thing.

        RectTransform source = (RectTransform)template.transform;
        RectTransform rect = (RectTransform)copy.transform;
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.anchoredPosition = source.anchoredPosition;
        rect.sizeDelta = source.sizeDelta;
        rect.localScale = source.localScale;

        Button button = copy.GetComponent<Button>();

        // The template's click is wired in code, but a designed scene may have added persistent calls
        // in the Inspector — those belong to the interaction it was copied from, not this one.
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        TMP_Text label = copy.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = spec.label;

        return button;
    }

    private static string BuildSummary(List<string> created, List<string> reused)
    {
        string summary = created.Count > 0
            ? "Created: " + string.Join(", ", created)
            : "Nothing new to create — all three buttons were already wired.";

        if (reused.Count > 0)
        {
            summary += "\nAlready wired: " + string.Join(", ", reused);
        }

        summary += "\n\nThey share one spot on the canvas — only one shows at a time. Give each its own " +
                   "icon in the Inspector to tell them apart at a glance.";

        return summary;
    }
}
