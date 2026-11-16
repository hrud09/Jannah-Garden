using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace JannahGarden.EditorTools
{
    /// <summary>
    /// Adds the ornate kit's decor to large popup panels: a rosette medallion
    /// straddling the top edge and gold vine flourishes in the bottom corners.
    ///
    /// The decor can't live inside the 9-slice panel sprite (it would stretch),
    /// so it is placed as child Images under a "JG_Decor" container. The
    /// container is the panel's first child, so all panel content draws over
    /// it; it ignores layout groups and never takes raycasts. Idempotent: panels
    /// that already carry JG_Decor are skipped. Run Preview first.
    /// </summary>
    public static class JannahUIDecorate
    {
        const string KitFolder = "Assets/2D Assets/UI/Jannah UI Kit";
        const string DecorName = "JG_Decor";

        static readonly HashSet<string> PanelSprites = new HashSet<string>
        {
            "jg_panel", "jg_panel_green", "jg_panel_sage", "jg_panel_cream",
        };

        // Panels that are not popups (full-screen ad overlay) never get decor.
        static readonly string[] ExcludeNames = { "Ads" };

        // Only popup-sized plates get decor; smaller panels (chips, rows,
        // sub-sections) would be crowded by it. Canvas units.
        const float MinWidth = 480f;
        const float MinHeight = 360f;

        [MenuItem("Jannah/UI Kit/Decorate Panels in Scene (Preview)")]
        static void PreviewScene() => RunScene(dryRun: true);

        [MenuItem("Jannah/UI Kit/Decorate Panels in Scene (Apply)")]
        static void ApplyScene() => RunScene(dryRun: false);

        [MenuItem("Jannah/UI Kit/Decorate Panels in UI Prefabs (Preview)")]
        static void PreviewPrefabs() => RunPrefabs(dryRun: true);

        [MenuItem("Jannah/UI Kit/Decorate Panels in UI Prefabs (Apply)")]
        static void ApplyPrefabs() => RunPrefabs(dryRun: false);

        [MenuItem("Jannah/UI Kit/Remove Panel Decor from Scene")]
        static void RemoveFromScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == DecorName)
                    {
                        Undo.DestroyObjectImmediate(t.gameObject);
                        n++;
                    }
            if (n > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Decorate] Removed {n} decor container(s) from '{scene.name}'.");
        }

        // ------------------------------------------------------- drivers ---
        static void RunScene(bool dryRun)
        {
            var art = LoadArt();
            if (art == null) return;

            var scene = EditorSceneManager.GetActiveScene();
            var log = new StringBuilder();
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    count += Decorate(canvas.transform, art, dryRun, log, skipNested: false);

            if (!dryRun && count > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Decorate] {(dryRun ? "PREVIEW" : "APPLIED")} — scene '{scene.name}': " +
                      $"{count} panel(s){(dryRun ? "" : ". Save the scene to keep them")}\n{log}");
        }

        static void RunPrefabs(bool dryRun)
        {
            var art = LoadArt();
            if (art == null) return;

            var log = new StringBuilder();
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int n = Decorate(root.transform, art, dryRun, log, skipNested: true);
                    if (n > 0)
                    {
                        count += n;
                        if (!dryRun) PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Debug.Log($"[Decorate] {(dryRun ? "PREVIEW" : "APPLIED")} — prefabs: {count} panel(s)\n{log}");
        }

        // ---------------------------------------------------------- core ---
        class Art
        {
            public Sprite Medallion, Flourish;
        }

        static Art LoadArt()
        {
            var art = new Art
            {
                Medallion = AssetDatabase.LoadAssetAtPath<Sprite>(KitFolder + "/jg_deco_medallion.png"),
                Flourish = AssetDatabase.LoadAssetAtPath<Sprite>(KitFolder + "/jg_deco_flourish.png"),
            };
            if (art.Medallion == null || art.Flourish == null)
            {
                Debug.LogError("[Decorate] Decor sprites missing. Run Tools/ui_kit/generate.py, " +
                               "then Jannah/UI Kit/Reimport Sprites.");
                return null;
            }
            return art;
        }

        static int Decorate(Transform root, Art art, bool dryRun, StringBuilder log, bool skipNested)
        {
            int n = 0;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || !PanelSprites.Contains(img.sprite.name)) continue;
                if (skipNested && IsNested(root, img.gameObject)) continue;
                if (img.transform.Find(DecorName) != null) continue;
                if (System.Array.Exists(ExcludeNames, x => img.name.Contains(x))) continue;

                var rt = (RectTransform)img.transform;
                var size = rt.rect.size;
                if (size.x < MinWidth || size.y < MinHeight) continue;

                // A mask on the panel would clip the medallion where it rises
                // above the top edge, so masked panels get the corner vines only.
                bool masked = img.GetComponent<Mask>() != null || img.GetComponent<RectMask2D>() != null;
                float k = DecorScale(size);
                // A title sitting at the top centre would be crowded by the medallion.
                bool titled = TextNear(rt, MedallionRect(rt, 128f * k * 1.3f));
                bool medallion = !masked && !titled;

                log.AppendLine($"  {PathOf(img.transform)}  ({size.x:0}x{size.y:0})" +
                               (masked ? "  [masked: no medallion]" : titled ? "  [title at top: no medallion]" : ""));
                n++;
                if (dryRun) continue;

                Build(rt, art, k, medallion);
            }
            return n;
        }

        /// <summary>Decor scales with the panel, within limits.</summary>
        static float DecorScale(Vector2 size) =>
            Mathf.Clamp(Mathf.Min(size.x / 820f, size.y / 560f), 0.75f, 1.6f);

        /// <summary>The medallion's footprint in the panel's local space.</summary>
        static Rect MedallionRect(RectTransform panel, float s)
        {
            var r = panel.rect;
            return new Rect(r.center.x - s / 2f, r.yMax - s / 2f, s, s);
        }

        /// <summary>True if any text-like graphic under the panel overlaps `zone`.</summary>
        static bool TextNear(RectTransform panel, Rect zone)
        {
            var corners = new Vector3[4];
            foreach (var g in panel.GetComponentsInChildren<MaskableGraphic>(true))
            {
                if (g is Image || g is RawImage) continue;
                var grt = g.rectTransform;
                grt.GetWorldCorners(corners);
                Vector2 min = panel.InverseTransformPoint(corners[0]);
                Vector2 max = panel.InverseTransformPoint(corners[2]);
                var r = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                                        Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
                if (r.Overlaps(zone)) return true;
            }
            return false;
        }

        static void Build(RectTransform panel, Art art, float k, bool medallion)
        {
            var container = new GameObject(DecorName, typeof(RectTransform), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(container, "Decorate Panel");
            var crt = (RectTransform)container.transform;
            crt.SetParent(panel, false);
            crt.SetAsFirstSibling();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            container.GetComponent<LayoutElement>().ignoreLayout = true;
            container.layer = panel.gameObject.layer;

            if (medallion)
            {
                var med = Child(crt, "Medallion", art.Medallion, 128f * k);
                med.anchorMin = med.anchorMax = new Vector2(0.5f, 1f);
                med.pivot = new Vector2(0.5f, 0.5f);
                med.anchoredPosition = Vector2.zero;
            }

            float f = 180f * k;
            var left = Child(crt, "FlourishL", art.Flourish, f);
            left.anchorMin = left.anchorMax = new Vector2(0f, 0f);
            left.pivot = Vector2.zero;
            left.anchoredPosition = new Vector2(-0.23f * f, -0.22f * f);

            // Mirrored: pivot stays bottom-left and X scale flips it leftward
            // from the bottom-right anchor.
            var right = Child(crt, "FlourishR", art.Flourish, f);
            right.anchorMin = right.anchorMax = new Vector2(1f, 0f);
            right.pivot = Vector2.zero;
            right.localScale = new Vector3(-1f, 1f, 1f);
            right.anchoredPosition = new Vector2(0.23f * f, -0.22f * f);

            EditorUtility.SetDirty(panel.gameObject);
        }

        static RectTransform Child(RectTransform parent, string name, Sprite sprite, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return rt;
        }

        static bool IsNested(Transform root, GameObject go)
        {
            var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            return outer != null && outer != root.gameObject;
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
