using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace JannahGarden.EditorTools
{
    /// <summary>
    /// Re-skins the game's UI onto the Jannah UI kit.
    ///
    /// The kit (Tools/ui_kit/generate.py) is derived from the original badge
    /// art, so this maps every sprite from the bought GUI packs onto its
    /// closest kit equivalent, normalises tints, and moves Latin text onto the
    /// softer LINE Seed Sans face. Localised (Noto) fonts are deliberately left
    /// alone so the bn/ar/ur shaping path is untouched.
    ///
    /// Run the Preview command first; it reports every change without touching
    /// the scene.
    /// </summary>
    public static class JannahUIRestyle
    {
        const string KitFolder = "Assets/2D Assets/UI/Jannah UI Kit";
        const string LatinFontPath =
            "Assets/Packs and Plugins/GUI-CasualFantasy/ResourcesData/Fonts/TMP_LINESeedSans.asset";

        // -------------------------------------------------------- palette ---
        static readonly Color Cream = Hex("FAF6EE");
        static readonly Color DeepGreen = Hex("2F4536");
        static readonly Color Gold = Hex("D4A550");

        /// <summary>Kit sprites light enough to need dark text on top.</summary>
        static readonly HashSet<string> LightSprites = new HashSet<string>
        {
            "jg_panel_cream", "jg_btn_cream", "jg_btn_circle_cream",
            "jg_toggle_bg", "jg_slider_handle",
        };

        // ------------------------------------------------------- mappings ---
        /// <summary>Old pack sprite -> kit sprite.</summary>
        static readonly Dictionary<string, string> SpriteMap = new Dictionary<string, string>
        {
            // buttons ---------------------------------------------------------
            { "Button_01_Mian_l_Bg_Green",  "jg_btn_green" },
            { "Button_01_Mian_l_Bg_Red",    "jg_btn_clay" },
            { "Button_01_Mian_l_Bg_Yellow", "jg_btn_gold" },
            { "Button_01_Mian_l_Bg_Orange", "jg_btn_gold" },
            { "Button_01_Mian_l_Bg_Blue",   "jg_btn_sage" },
            { "Button_01_Mian_l_Bg_Sky",    "jg_btn_sage" },
            { "Button_01_Mian_l_Bg_Pink",   "jg_btn_sage" },
            { "Button_01_Mian_l_Bg_Purple", "jg_btn_deep" },
            { "Button_01_Mian_s_Bg_Purple", "jg_btn_deep" },
            { "Button_01_Mian_s_Bg_Sky",    "jg_btn_sage" },
            { "Button_01_Mian_s_Bg_Yellow", "jg_btn_gold" },
            { "Button_01_Mian_s_Bg_Dark",   "jg_btn_deep" },
            { "button_green",               "jg_btn_green" },
            { "button_red",                 "jg_btn_clay" },
            { "button_orange",              "jg_btn_gold" },
            { "button_sky",                 "jg_btn_sage" },
            { "Button01_Demo_Red",          "jg_btn_clay" },
            { "Button_Convex_Rectangle_01_Green",   "jg_btn_green" },
            { "Button_Convex_Rectangle_01_Gray",    "jg_btn_sage" },
            { "Button_Convex_Rectangle_01_H58_Gray","jg_btn_sage" },
            { "Button_Convex_Circle_01_Red",        "jg_btn_circle_clay" },
            { "Button_01_Mian_l_Bg_Mint",           "jg_btn_green" },
            { "Button_Square02_White1",             "jg_btn_cream" },

            // panels / frames -------------------------------------------------
            { "frame_stageframe_03_Demo_purple", "jg_panel" },
            { "frame_stageframe_03_Demo_yellow", "jg_panel" },
            { "frame_stageframe_04_s",           "jg_card" },
            { "CardFrame_Rectangle_01_Purple_Bg","jg_panel" },
            { "CardFrame_Rectangle_01_Blue_Bg",  "jg_panel" },
            { "ListFrame_01_InnerGlow",          "jg_panel" },
            { "frame_itemframe_02_White1",       "jg_panel_cream" },
            { "08_Battel_VS",                    "jg_panel" },

            // recessed plates / list backgrounds ------------------------------
            { "StageFrame_01_Bg",                  "jg_inset" },
            { "BaseFrame_Border_Rectangle_H50_Bg", "jg_inset" },
            { "tutorial_chat_bg",                  "jg_inset" },
            { "ItemFrame05_Demo_Bg_n",             "jg_inset" },
            { "equip_frame_empty_0",               "jg_inset" },
            { "txt_input_field_d",                 "jg_inset" },
            { "frame_itemframe_01_White1",         "jg_panel_cream" },
            { "ItemFrame_Square_02_White_Glow",    "jg_card" },
            { "Slider_Basic01_White_Fill",         "jg_slider_fill" },

            // cards / chips ---------------------------------------------------
            { "RankingFrame_2nd",                   "jg_card" },
            { "BaseFrame_Convex_Tapered_01_Yellow", "jg_card" },
            { "BaseFrame_Convex_Crimped_01_Single_Blue", "jg_btn_circle_deep" },

            // ribbons / tabs --------------------------------------------------
            { "PassFrame_Rectangle_02_Gold", "jg_ribbon_gold" },
            { "Title_Ribbon_01_Green",       "jg_ribbon_gold" },
            { "Title_Flag_01_Orange",        "jg_ribbon_gold" },
            { "Title_Flag_01_Yellow",        "jg_ribbon_gold" },
            { "TabMenu_Top_White_Focus",     "jg_tab_active" },

            // Hollow frames. These MUST map to a hollow kit sprite: the source
            // art has a transparent centre and sits in front of other content,
            // so mapping one to a solid plate paints over whatever is behind it.
            // Tools/ui_kit/audit_mappings.py checks this invariant.
            { "ProfileFrame_01_BorderDeco_Gold",          "jg_frame_window" },
            { "frame_stageframe_04_s_glow",               "jg_frame_window" },
            { "frame_listframe_01_Demo_s",                "jg_frame_window" },
            { "SkillFrame_01_Border_Yellow",              "jg_frame_window" },
            { "BaseFrame_Border_Rectangle_H60_InnerGlow", "jg_frame_window_s" },

            // controls --------------------------------------------------------
            { "Toggle01_Demo_ChenkIcon_Green",    "jg_toggle_check" },
            { "Slider_Basic01_Fill_Yellow",       "jg_slider_fill" },
            { "Slider_Play_02_Border",            "jg_slider_track" },
            { "BaseFrame_Convex_Circle_01_Green", "jg_slider_handle" },
            { "BaseFrame_Convex_Circle_01_Gray",  "jg_slider_handle" },

            // scrims ----------------------------------------------------------
            { "frame_stageframe_05_s_glow",      "jg_scrim" },
            { "CardFrame_Hexagon_01_Yellow_Bg",  "jg_scrim" },
        };

        /// <summary>
        /// Bespoke, already on-brand art and functional sprites. Never touched.
        /// </summary>
        static readonly HashSet<string> Keep = new HashSet<string>
        {
            // custom Noor coin bar and XP bar, already sage + gold
            "E1943005-78E1-43D5-B071-3EAB98AB2133",
            "2B3C3E27-4A4B-466B-98BA-7EC500AB56B0",
            // the badge strip this redesign is based on
            "aerial-view-badge", "camera-icon-badge", "explore-garden-badge",
            "settings-gear-badge", "tutorial-help-badge", "shop-badge", "chest-badge",
            // masks and functional bits
            "UIMask", "shadow_bottom", "AllAxis_Outline", "Handle_Plain",
        };

        // --------------------------------------------------------- driver ---
        [MenuItem("Jannah/UI Kit/Restyle Active Scene (Preview)")]
        public static void PreviewScene() => RunScene(dryRun: true);

        [MenuItem("Jannah/UI Kit/Restyle Active Scene (Apply)")]
        public static void ApplyScene() => RunScene(dryRun: false);

        [MenuItem("Jannah/UI Kit/Restyle UI Prefabs (Preview)")]
        public static void PreviewPrefabs() => RunPrefabs(dryRun: true);

        [MenuItem("Jannah/UI Kit/Restyle UI Prefabs (Apply)")]
        public static void ApplyPrefabs() => RunPrefabs(dryRun: false);

        static void RunPrefabs(bool dryRun)
        {
            var kit = LoadKit();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LatinFontPath);
            var report = new Report();
            int touched = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // Only prefabs that actually carry UI graphics.
                    if (root.GetComponentsInChildren<Image>(true).Length == 0 &&
                        root.GetComponentsInChildren<TMP_Text>(true).Length == 0)
                        continue;

                    int before = report.Swaps.Count + report.Texts.Count;
                    // Only style graphics this prefab owns. Anything coming from
                    // a nested prefab is styled when that source prefab is
                    // processed, and instances inherit it — otherwise every
                    // shop item would carry a redundant per-instance override.
                    Restyle(root.transform, kit, font, dryRun, report, skipNested: true);
                    bool changed = report.Swaps.Count + report.Texts.Count > before;

                    if (changed)
                    {
                        touched++;
                        if (!dryRun) PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString(dryRun, $"{touched} prefab(s)"));
        }

        static void RunScene(bool dryRun)
        {
            var kit = LoadKit();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LatinFontPath);
            if (font == null) Debug.LogWarning($"[Restyle] Latin font not found at {LatinFontPath}; text will keep its current font.");

            var scene = EditorSceneManager.GetActiveScene();
            var report = new Report();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    Restyle(canvas.transform, kit, font, dryRun, report);
            }

            if (!dryRun)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"[Restyle] Applied to '{scene.name}'. Save the scene to keep the changes.");
            }
            Debug.Log(report.ToString(dryRun, scene.name));
        }

        // ---------------------------------------------------------- core -----
        static void Restyle(Transform root, Dictionary<string, Sprite> kit,
                            TMP_FontAsset font, bool dryRun, Report report,
                            bool skipNested = false)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (skipNested && IsNested(root, img.gameObject)) continue;
                RestyleImage(img, kit, dryRun, report);
            }

            foreach (var txt in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (skipNested && IsNested(root, txt.gameObject)) continue;
                RestyleText(txt, font, dryRun, report);
            }
        }

        /// <summary>True when the object comes from a prefab nested inside this one.</summary>
        static bool IsNested(Transform root, GameObject go)
        {
            var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            return outer != null && outer != root.gameObject;
        }

        static void RestyleImage(Image img, Dictionary<string, Sprite> kit, bool dryRun, Report report)
        {
            if (img.sprite == null) return;
            var name = img.sprite.name;
            if (Keep.Contains(name)) { report.Kept.Add(name); return; }

            // Unity's stock Background/UISprite/Knob are shared across sliders,
            // toggles and scrollbars, so the sprite name alone can't decide.
            var target = ResolveUnityDefault(img, name) ?? (SpriteMap.TryGetValue(name, out var m) ? m : null);
            if (target == null) { report.Unmapped.Add(name); return; }

            if (!kit.TryGetValue(target, out var sprite))
            {
                report.Missing.Add(target);
                return;
            }

            // A scrim keeps its authored opacity; everything else drops its tint
            // so the kit art shows its own colour.
            var newColor = target == "jg_scrim"
                ? new Color(1f, 1f, 1f, Mathf.Max(img.color.a, 0.55f))
                : Color.white;

            report.Swaps.Add($"{Path(img.transform)}: {name} -> {target}");

            if (dryRun) return;

            Undo.RecordObject(img, "Restyle UI");
            img.sprite = sprite;
            img.color = newColor;
            img.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            if (img.type == Image.Type.Sliced) img.pixelsPerUnitMultiplier = 1f;
            EditorUtility.SetDirty(img);
        }

        /// <summary>Disambiguates Unity's built-in UI sprites by owning control.</summary>
        static string ResolveUnityDefault(Image img, string name)
        {
            if (name != "Background" && name != "UISprite" && name != "Knob" && name != "Checkmark")
                return null;

            // Walked by hand rather than GetComponentInParent: most of these
            // panels are inactive in the scene, and that call skips them.
            Slider slider = null;
            Scrollbar scrollbar = null;
            Toggle toggle = null;
            for (var t = img.transform; t != null; t = t.parent)
            {
                if (slider == null) slider = t.GetComponent<Slider>();
                if (scrollbar == null) scrollbar = t.GetComponent<Scrollbar>();
                if (toggle == null) toggle = t.GetComponent<Toggle>();
            }

            if (name == "Checkmark") return "jg_toggle_check";
            if (name == "Knob") return scrollbar != null ? "jg_scroll_handle" : "jg_slider_handle";

            // Inside a Slider, "UISprite" is the fill and "Background" the track.
            if (slider != null) return name == "UISprite" ? "jg_slider_fill" : "jg_slider_track";
            if (scrollbar != null) return name == "UISprite" ? "jg_scroll_handle" : "jg_scroll_track";
            if (toggle != null) return "jg_toggle_bg";

            return "jg_inset";
        }

        static void RestyleText(TMP_Text txt, TMP_FontAsset font, bool dryRun, Report report)
        {
            var current = txt.font != null ? txt.font.name : "";
            // Leave the Noto faces alone: they are driven by the localisation
            // registry and shaping breaks if they are swapped here.
            bool isLatinPackFont = current.StartsWith("LilitaOne") || current.StartsWith("Afacad")
                                   || current.StartsWith("Germania") || current.StartsWith("Alata");

            var target = TextColorFor(txt);
            bool colorChanges = txt.color != target;
            bool fontChanges = isLatinPackFont && font != null && txt.font != font;
            bool onKitFont = font != null && txt.font == font;

            // LINE Seed Sans is noticeably wider than Lilita One at the same
            // point size, so labels authored to just fit now wrap or clip
            // ("Vibrati/on", "TREASUR/E"). Auto-sizing caps at the authored size
            // and shrinks only when needed — which also absorbs the wider
            // Bengali and Arabic strings.
            bool needsAutoSize = (fontChanges || onKitFont) && !txt.enableAutoSizing;

            if (!colorChanges && !fontChanges && !needsAutoSize) return;

            report.Texts.Add($"{Path(txt.transform)}: " +
                             (fontChanges ? $"{current} -> {font.name} " : "") +
                             (needsAutoSize ? "autosize " : "") +
                             (colorChanges ? $"#{ColorUtility.ToHtmlStringRGB(txt.color)} -> #{ColorUtility.ToHtmlStringRGB(target)}" : ""));

            if (dryRun) return;

            Undo.RecordObject(txt, "Restyle UI");
            if (fontChanges)
            {
                txt.font = font;
                txt.fontSharedMaterial = font.material;
            }
            if (needsAutoSize)
            {
                float authored = txt.fontSize;
                txt.enableAutoSizing = true;
                txt.fontSizeMax = authored;
                txt.fontSizeMin = Mathf.Max(12f, authored * 0.5f);
            }
            txt.color = target;
            EditorUtility.SetDirty(txt);
        }

        /// <summary>
        /// Cream on the dark and mid ramps (as the badge icons are), deep green
        /// on cream surfaces, gold for text that was already an accent colour.
        /// </summary>
        static Color TextColorFor(TMP_Text txt)
        {
            Color.RGBToHSV(txt.color, out _, out float s, out float v);
            if (s > 0.25f && v > 0.4f) return Gold;

            for (var t = txt.transform; t != null; t = t.parent)
            {
                var img = t.GetComponent<Image>();
                if (img == null || img.sprite == null) continue;
                if (LightSprites.Contains(img.sprite.name)) return DeepGreen;
                if (img.sprite.name.StartsWith("jg_")) return Cream;
            }
            return Cream;
        }

        // --------------------------------------------------------- helpers ---
        static Dictionary<string, Sprite> LoadKit()
        {
            var kit = new Dictionary<string, Sprite>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { KitFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) kit[sprite.name] = sprite;
            }
            return kit;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        class Report
        {
            public readonly List<string> Swaps = new List<string>();
            public readonly List<string> Texts = new List<string>();
            public readonly List<string> Unmapped = new List<string>();
            public readonly List<string> Missing = new List<string>();
            public readonly List<string> Kept = new List<string>();

            public string ToString(bool dryRun, string sceneName)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[Restyle] {(dryRun ? "PREVIEW" : "APPLIED")} — scene '{sceneName}'");
                sb.AppendLine($"  sprite swaps : {Swaps.Count}");
                sb.AppendLine($"  text changes : {Texts.Count}");
                sb.AppendLine($"  kept on purpose: {Kept.Distinct().Count()} distinct");
                if (Missing.Count > 0)
                    sb.AppendLine($"  !! kit sprite missing: {string.Join(", ", Missing.Distinct())}");
                if (Unmapped.Count > 0)
                    sb.AppendLine($"  unmapped (left as-is): {string.Join(", ", Unmapped.Distinct().OrderBy(x => x))}");
                return sb.ToString();
            }
        }
    }
}
