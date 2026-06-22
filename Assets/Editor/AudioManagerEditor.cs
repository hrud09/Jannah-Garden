using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(AudioManager))]
public class AudioManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();

        AudioManager audioManager = (AudioManager)target;

        GUILayout.Space(15);
        GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f);
        if (GUILayout.Button("Auto-Populate Sounds", GUILayout.Height(35)))
        {
            PopulateSounds(audioManager);
        }
        GUI.backgroundColor = Color.white;
    }

    private void PopulateSounds(AudioManager manager)
    {
        string searchFolder = "Assets/Audio/SFX Packs";
        if (!AssetDatabase.IsValidFolder(searchFolder))
        {
            EditorUtility.DisplayDialog("Folder Not Found", $"Could not find folder: {searchFolder}. Please make sure it exists.", "OK");
            return;
        }

        // Find all AudioClips in the target directory
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { searchFolder });
        List<AudioClip> allClips = new List<AudioClip>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
            {
                allClips.Add(clip);
            }
        }

        if (allClips.Count == 0)
        {
            EditorUtility.DisplayDialog("No Audio Clips", $"No AudioClips found in {searchFolder}.", "OK");
            return;
        }

        Undo.RecordObject(manager, "Auto-Populate Sounds");

        // Keep a dictionary of existing sounds to preserve customized volume/pitch if possible
        Dictionary<SoundEffect, Sound> existingSoundsMap = new Dictionary<SoundEffect, Sound>();
        if (manager.sounds != null)
        {
            foreach (var sound in manager.sounds)
            {
                if (sound != null && !existingSoundsMap.ContainsKey(sound.effect))
                {
                    existingSoundsMap[sound.effect] = sound;
                }
            }
        }

        // Prepare new list matching the enum
        List<Sound> newSoundsList = new List<Sound>();
        SoundEffect[] enumValues = (SoundEffect[])Enum.GetValues(typeof(SoundEffect));

        int matchedCount = 0;
        int newlyAddedCount = 0;

        foreach (SoundEffect effect in enumValues)
        {
            Sound sound = null;
            
            // Check if we already have this effect mapped
            if (existingSoundsMap.TryGetValue(effect, out Sound existingSound))
            {
                sound = existingSound;
            }
            
            // If the sound exists but has no clip, or is a brand new sound, let's try to match a clip
            if (sound == null || sound.clip == null)
            {
                AudioClip bestClip = FindBestClipForEffect(effect, allClips);
                if (bestClip != null)
                {
                    if (sound == null)
                    {
                        sound = new Sound
                        {
                            effect = effect,
                            clip = bestClip,
                            volume = 1f,
                            pitch = 1f
                        };
                        newlyAddedCount++;
                    }
                    else
                    {
                        sound.clip = bestClip;
                    }
                    matchedCount++;
                }
                else
                {
                    // No clip matched, but we still ensure the element is in the list
                    if (sound == null)
                    {
                        sound = new Sound
                        {
                            effect = effect,
                            clip = null,
                            volume = 1f,
                            pitch = 1f
                        };
                    }
                }
            }

            newSoundsList.Add(sound);
        }

        manager.sounds = newSoundsList.ToArray();
        EditorUtility.SetDirty(manager);

        Debug.Log($"[AudioManagerEditor] Successfully populated Sounds list. Matched: {matchedCount} clips. Newly added elements: {newlyAddedCount}. Total sound effects: {enumValues.Length}.");
        EditorUtility.DisplayDialog("Success", $"Populated Sounds list.\nTotal Sounds: {enumValues.Length}\nMatched Clips: {matchedCount}", "OK");
    }

    private AudioClip FindBestClipForEffect(SoundEffect effect, List<AudioClip> clips)
    {
        string effectName = effect.ToString().ToLowerInvariant();
        
        // Define scoring rules for keywords
        var keywords = GetKeywordsForEffect(effect);

        AudioClip bestMatch = null;
        int bestScore = 0;

        foreach (var clip in clips)
        {
            string clipName = clip.name.ToLowerInvariant();
            int score = 0;

            // Check primary keyword combos or specific heuristics
            foreach (var keyword in keywords)
            {
                if (clipName.Contains(keyword))
                {
                    score += 10;
                }
            }

            // Bonus if exact match or contains complete effect name
            if (clipName.Contains(effectName))
            {
                score += 50;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = clip;
            }
        }

        return bestMatch;
    }

    private List<string> GetKeywordsForEffect(SoundEffect effect)
    {
        switch (effect)
        {
            case SoundEffect.ButtonClick:
                return new List<string> { "click", "btn", "button", "press", "select" };
            case SoundEffect.Walk:
                return new List<string> { "walk", "step", "foot", "run", "movement" };
            case SoundEffect.TreasureBoxOpen:
                return new List<string> { "chest", "box", "open", "unlock" };
            case SoundEffect.QuestionMarkOrbOpen:
                return new List<string> { "orb", "magic", "question", "sparkle" };
            case SoundEffect.QuizSubmit:
                return new List<string> { "submit", "confirm", "accept", "success", "correct" };
            case SoundEffect.QuizClose:
                return new List<string> { "close", "cancel", "fail", "wrong", "exit" };
            case SoundEffect.ItemPlace:
                return new List<string> { "place", "drop", "put", "build", "set" };
            case SoundEffect.ShopOpenClose:
                return new List<string> { "shop", "market", "door", "bell", "open", "close" };
            case SoundEffect.TabSwitch:
                return new List<string> { "tab", "switch", "page", "swipe" };
            case SoundEffect.ItemPurchase:
                return new List<string> { "purchase", "buy", "coin", "gold", "cash", "buy" };
            case SoundEffect.MinimapExpand:
                return new List<string> { "map", "expand", "zoom", "paper" };
            case SoundEffect.MinimapCollapse:
                return new List<string> { "map", "collapse", "zoom" };
            case SoundEffect.DhikrIncrement:
                return new List<string> { "increment", "plus", "tally", "count", "beep", "click" };
            case SoundEffect.DhikrDecrement:
                return new List<string> { "decrement", "minus", "tally", "count", "click" };
            case SoundEffect.DhikrSubmit:
                return new List<string> { "submit", "finish", "done", "complete", "dhikr" };
            case SoundEffect.DhikrClose:
                return new List<string> { "close", "exit" };
            case SoundEffect.TreasureBoxShow:
                return new List<string> { "chest", "show", "appear", "spawn" };
            case SoundEffect.XPGainChartToggle:
                return new List<string> { "chart", "toggle", "xp", "gain" };
            case SoundEffect.ItemInteract:
                return new List<string> { "interact", "use", "touch", "click" };
            case SoundEffect.Run:
                return new List<string> { "run", "fast", "sprint", "footstep" };
            case SoundEffect.Breathing:
                return new List<string> { "breath", "breathing", "sigh", "pant", "grunt", "gasp" };
            default:
                return new List<string>();
        }
    }
}
