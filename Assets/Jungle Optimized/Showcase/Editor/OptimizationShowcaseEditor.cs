using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JannahGarden.JungleOptimized
{
    [CustomEditor(typeof(OptimizationShowcase))]
    public class OptimizationShowcaseEditor : Editor
    {
        private bool analyzed;
        private EnvironmentContentStats original, optimized;

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var showcase = (OptimizationShowcase)target;

            EditorGUILayout.HelpBox(
                "Press Play, then use the panel in the Game view (or the buttons below):\n" +
                "  Original / Optimized / Split  -  switch versions, split = both at once, drag the line\n" +
                "  Benchmark  -  measures both versions from the current view\n" +
                "  Full Report  -  benchmark + screenshots + pixel-difference, saved as Report.html\n" +
                "Demo only: don't keep both jungles in the game scene you ship.",
                MessageType.Info);

            DrawDefaultInspector();

            if (GUILayout.Button("Add viewpoint from Scene view camera"))
                AddViewpointFromSceneView(showcase);
            EditorGUILayout.Space(10f);

            if (Application.isPlaying) DrawPlayControls(showcase);
            else DrawContentComparison(showcase);
        }

        private void DrawPlayControls(OptimizationShowcase s)
        {
            EditorGUILayout.LabelField("Live controls", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(s.IsBusy))
            {
                EditorGUILayout.BeginHorizontal();
                ModeButton(s, "Original", OptimizationShowcase.ViewMode.Original);
                ModeButton(s, "Optimized", OptimizationShowcase.ViewMode.Optimized);
                ModeButton(s, "Split", OptimizationShowcase.ViewMode.Split);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Benchmark", GUILayout.Height(28f))) s.RunBenchmark();
                if (GUILayout.Button("Full Report", GUILayout.Height(28f))) s.RunFullReport();
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(s.Status)) EditorGUILayout.LabelField(s.Status, EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(s.LastReportFolder) && GUILayout.Button("Open last report folder"))
                EditorUtility.RevealInFinder(Path.Combine(s.LastReportFolder, "Report.html"));

            if (s.viewpoints != null && s.viewpoints.Length > 0)
            {
                using (new EditorGUI.DisabledScope(s.IsBusy))
                    if (GUILayout.Button("Camera: " + (s.CurrentView < 0 ? "free" : "viewpoint " + (s.CurrentView + 1)) + "  (click for next)"))
                        s.NextView();
            }

            var a = s.OriginalResult;
            var b = s.OptimizedResult;
            if (a.valid && b.valid)
            {
                EditorGUILayout.Space(6f);
                Header(s.ViewResults.Count > 1 ? "Benchmark, average of " + s.ViewResults.Count + " views" : "Benchmark (per frame)");
                Row("Draw calls", a.drawCalls, b.drawCalls, true);
                Row("Batches", a.batches, b.batches, true);
                Row("SetPass calls", a.setPassCalls, b.setPassCalls, true);
                Row("Triangles", a.triangles, b.triangles, true);
                Row("Shadow casters", a.shadowCasters, b.shadowCasters, true);
                Row("Objects rendered", a.visibleRenderers, b.visibleRenderers, true);
                Row("Lantern lights", a.activeLights, b.activeLights, true);
                Row("Frame time (ms)", a.avgFrameMs, b.avgFrameMs, true, true);
                Row("FPS", a.avgFps, b.avgFps, false, true);
            }
            if (s.NoticeablePixelPercent >= 0f)
                EditorGUILayout.LabelField("Screen identical", (100f - s.NoticeablePixelPercent).ToString("0.0") + "% of pixels");
        }

        private static void ModeButton(OptimizationShowcase s, string label, OptimizationShowcase.ViewMode mode)
        {
            var old = GUI.backgroundColor;
            if (s.Mode == mode) GUI.backgroundColor = new Color(0.45f, 0.9f, 0.5f);
            if (GUILayout.Button(label, GUILayout.Height(28f))) s.SetMode(mode);
            GUI.backgroundColor = old;
        }

        private void DrawContentComparison(OptimizationShowcase s)
        {
            EditorGUILayout.LabelField("Content comparison (no Play needed)", EditorStyles.boldLabel);
            if (GUILayout.Button(analyzed ? "Re-analyze" : "Analyze original vs optimized", GUILayout.Height(26f)))
            {
                original = EnvironmentContentStats.Analyze(s.originalObjects.Where(o => o != null));
                optimized = EnvironmentContentStats.Analyze(s.optimizedObjects.Where(o => o != null));
                analyzed = true;
            }
            if (!analyzed) return;

            Header("Whole jungle");
            Row("Renderers (all LODs)", original.renderers, optimized.renderers, true);
            Row("Draw items, all on screen", original.drawItems, optimized.drawItems, true);
            Row("Triangles, closest LOD", original.triangles, optimized.triangles, true);
            Row("Shadow-casting objects", original.shadowCasters, optimized.shadowCasters, true);
            Row("GameObjects", original.gameObjects, optimized.gameObjects, true);
            Row("Max particles", original.maxParticles, optimized.maxParticles, true);
            Row("Unique meshes", original.uniqueMeshes, optimized.uniqueMeshes, true);
            Row("Mesh memory (MB)", original.meshBytes / 1048576.0, optimized.meshBytes / 1048576.0, true, true);
            Row("Materials", original.uniqueMaterials, optimized.uniqueMaterials, true);
            EditorGUILayout.LabelField("Live draw calls, FPS, screenshots and the HTML report need Play mode.", EditorStyles.wordWrappedMiniLabel);
        }

        private static void Header(string title)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.MinWidth(150f));
            EditorGUILayout.LabelField("Original", EditorStyles.boldLabel, GUILayout.Width(80f));
            EditorGUILayout.LabelField("Optimized", EditorStyles.boldLabel, GUILayout.Width(80f));
            EditorGUILayout.LabelField("Change", EditorStyles.boldLabel, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }

        private static void Row(string label, double before, double after, bool lowerIsBetter, bool decimals = false)
        {
            string change = OptimizationShowcase.ChangeText(before, after, lowerIsBetter, out string css)
                .Replace("&minus;", "−").Replace("&ndash;", "-");
            var style = new GUIStyle(EditorStyles.label);
            if (css == "good") style.normal.textColor = new Color(0.25f, 0.75f, 0.35f);
            else if (css == "bad") style.normal.textColor = new Color(0.9f, 0.35f, 0.3f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.MinWidth(150f));
            EditorGUILayout.LabelField(OptimizationShowcase.Num(before, decimals), GUILayout.Width(80f));
            EditorGUILayout.LabelField(OptimizationShowcase.Num(after, decimals), GUILayout.Width(80f));
            EditorGUILayout.LabelField(change, style, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================== menu

        private const string ShowcaseName = "Optimization Showcase (demo)";

        [MenuItem("Tools/Jungle Optimized/Set Up Showcase In Open Scene", false, 30)]
        public static void SetUpShowcase()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JungleOptimizedBuilder.LayoutPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Optimization Showcase", "Jungle (Optimized).prefab was not found. Build it first " +
                    "(Tools > Jungle Optimized > Build From Open Scene).", "OK");
                return;
            }

            GameObject optimized = FindPrefabInstance(scene, prefab);
            GameObject original = FindOriginalJungle(scene, optimized);
            if (original == null)
            {
                EditorUtility.DisplayDialog("Optimization Showcase", "This scene has no original 'Environment/Jungle' to compare against.\n\n" +
                    "Open a scene that still contains the original jungle (e.g. 'Jannah Garden shop asset Zaman v_2'), " +
                    "or keep the original (disabled) next to the optimized one after swapping.", "OK");
                return;
            }

            bool inBuild = EditorBuildSettings.scenes.Any(x => x.enabled && x.path == scene.path);
            if (inBuild && !Application.isPlaying && !EditorUtility.DisplayDialog("Optimization Showcase",
                    "'" + scene.name + "' is in the Build Settings.\n\nThe showcase keeps BOTH jungles in the scene, which costs memory " +
                    "in a real build. That's fine for a demo, but remove the original jungle and the showcase object before shipping.",
                    "Set up anyway", "Cancel"))
                return;

            Undo.SetCurrentGroupName("Set up optimization showcase");
            int undoGroup = Undo.GetCurrentGroup();

            if (optimized == null)
            {
                optimized = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(optimized, "Add optimized jungle");
                if (original.transform.parent != null)
                {
                    optimized.transform.SetParent(original.transform.parent, true);
                    optimized.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
                }
            }

            // In edit mode the scene shows the original; the showcase switches versions in Play mode.
            Undo.RecordObject(original, "Showcase");
            original.SetActive(true);
            Undo.RecordObject(optimized, "Showcase");
            optimized.SetActive(false);

            // Other optimizers (e.g. Utils/Mobile Environment Optimizer) would also optimize the original.
            var keepOff = new System.Collections.Generic.List<GameObject>();
            var skipped = new System.Collections.Generic.List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MobileEnvironmentOptimizer o in root.GetComponentsInChildren<MobileEnvironmentOptimizer>(true))
                {
                    if (o.transform.IsChildOf(optimized.transform)) continue;
                    GameObject go = o.gameObject;
                    bool dedicated = go.transform.childCount == 0 && go.GetComponents<Component>().Length == 2;
                    if (dedicated) keepOff.Add(go);
                    else skipped.Add(go.name);
                }
            }

            OptimizationShowcase showcase = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                showcase = root.GetComponentInChildren<OptimizationShowcase>(true);
                if (showcase != null) break;
            }
            if (showcase == null)
            {
                var go = new GameObject(ShowcaseName);
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Add showcase");
                showcase = Undo.AddComponent<OptimizationShowcase>(go);
            }

            Undo.RecordObject(showcase, "Showcase");
            showcase.originalObjects = new[] { original };
            showcase.optimizedObjects = new[] { optimized };
            showcase.keepOffObjects = keepOff.ToArray();
            if (showcase.viewCamera == null) showcase.viewCamera = FindMainCamera(scene);
            if (showcase.viewpoints == null || showcase.viewpoints.All(v => v == null))
                showcase.viewpoints = CreateDefaultViewpoints(showcase);
            EditorUtility.SetDirty(showcase);

            Undo.CollapseUndoOperations(undoGroup);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = showcase.gameObject;

            string msg = "[Optimization Showcase] Ready. Original: '" + PathOf(original) + "', optimized: '" + PathOf(optimized) + "'" +
                         (keepOff.Count > 0 ? ", kept off during the demo: " + string.Join(", ", keepOff.Select(PathOf)) : "") + ". Press Play.";
            if (skipped.Count > 0)
                msg += " Note: these objects also have a MobileEnvironmentOptimizer but hold other things, so they were left on: " + string.Join(", ", skipped);
            Debug.Log(msg, showcase);
        }

        [MenuItem("Tools/Jungle Optimized/Open Showcase Reports Folder", false, 31)]
        public static void OpenReportsFolder()
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "OptimizationShowcase");
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder + Path.DirectorySeparatorChar);
        }

        // Three views of this jungle (shared terrain in the Zaman scene and Jannah Garden.unity):
        // name, x, z, yaw, pitch. Height = terrain + eye height.
        private static readonly (string name, float x, float z, float yaw, float pitch)[] DefaultViews =
        {
            ("Inside the jungle", 110f, 50f, 200f, 5f),
            ("Lantern path", 62f, 47f, 95f, 4f),
            ("Garden edge, looking into the jungle", 132f, 112f, 210f, 6f),
        };
        private const float EyeHeight = 1.7f;

        private static Transform ViewpointParent(OptimizationShowcase showcase)
        {
            Transform parent = showcase.transform.Find("Viewpoints");
            if (parent != null) return parent;
            var go = new GameObject("Viewpoints");
            Undo.RegisterCreatedObjectUndo(go, "Add viewpoints");
            go.transform.SetParent(showcase.transform, false);
            return go.transform;
        }

        private static Transform[] CreateDefaultViewpoints(OptimizationShowcase showcase)
        {
            Transform parent = ViewpointParent(showcase);
            var result = new System.Collections.Generic.List<Transform>();
            foreach (var v in DefaultViews)
            {
                var p = new Vector3(v.x, 0f, v.z);
                p.y = GroundHeight(p) + EyeHeight;
                var go = new GameObject(v.name);
                Undo.RegisterCreatedObjectUndo(go, "Add viewpoint");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(v.pitch, v.yaw, 0f));
                result.Add(go.transform);
            }
            return result.ToArray();
        }

        private static float GroundHeight(Vector3 p)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, size = t.terrainData.size;
                if (p.x >= o.x && p.x <= o.x + size.x && p.z >= o.z && p.z <= o.z + size.z)
                    return t.SampleHeight(p) + o.y;
            }
            return Physics.Raycast(new Vector3(p.x, 1000f, p.z), Vector3.down, out RaycastHit hit, 2000f) ? hit.point.y : 0f;
        }

        private static void AddViewpointFromSceneView(OptimizationShowcase showcase)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
            {
                EditorUtility.DisplayDialog("Optimization Showcase", "Open a Scene view and frame the shot you want first.", "OK");
                return;
            }
            Transform parent = ViewpointParent(showcase);
            int index = (showcase.viewpoints?.Length ?? 0) + 1;
            var go = new GameObject("View " + index);
            Undo.RegisterCreatedObjectUndo(go, "Add viewpoint");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);

            Undo.RecordObject(showcase, "Add viewpoint");
            var list = (showcase.viewpoints ?? new Transform[0]).Where(t => t != null).ToList();
            list.Add(go.transform);
            showcase.viewpoints = list.ToArray();
            EditorUtility.SetDirty(showcase);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(showcase.gameObject.scene);
        }

        private static GameObject FindPrefabInstance(Scene scene, GameObject prefab)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) &&
                        PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject) == prefab)
                        return t.gameObject;
            return null;
        }

        private static GameObject FindOriginalJungle(Scene scene, GameObject optimized)
        {
            GameObject fallback = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Jungle") continue;
                    if (optimized != null && (t == optimized.transform || t.IsChildOf(optimized.transform))) continue;
                    if (t.parent != null && t.parent.name == "Environment") return t.gameObject;
                    if (fallback == null) fallback = t.gameObject;
                }
            return fallback;
        }

        private static Camera FindMainCamera(Scene scene)
        {
            Camera any = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                {
                    if (c.CompareTag("MainCamera")) return c;
                    if (any == null && c.targetTexture == null) any = c;
                }
            return any;
        }

        private static string PathOf(GameObject go)
        {
            string path = go.name;
            for (Transform t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path;
        }
    }
}
