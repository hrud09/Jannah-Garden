using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JannahGarden.EditorTools
{
    /// <summary>
    /// Renders UI canvases to PNGs for design review.
    ///
    /// ScreenCapture only grabs whatever the Game view happens to be drawing,
    /// which is racy from editor scripting. This instead points a temporary
    /// camera at the canvases and renders them synchronously, one popup at a
    /// time, so every panel can be reviewed without entering play mode.
    ///
    /// The scene is modified while capturing (panels are switched on, canvases
    /// are temporarily put in camera mode) and everything is restored at the
    /// end, so do not save the scene during a capture run.
    /// </summary>
    public static class JannahUICapture
    {
        const string OutDir = "Temp/ui_shots";
        // Match the Game view's authored aspect. Rendering at a different one
        // shifts anchored layouts and makes reviews misleading.
        const int Width = 2960;
        const int Height = 1440;

        [MenuItem("Jannah/UI Kit/Capture UI Panels")]
        public static void CaptureAll()
        {
            Directory.CreateDirectory(OutDir);

            var scene = EditorSceneManager.GetActiveScene();
            Transform all = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "All Canvas") all = go.transform;
            if (all == null) { Debug.LogError("[Capture] 'All Canvas' not found."); return; }

            var canvases = new List<Canvas>();
            foreach (Transform c in all)
            {
                var cv = c.GetComponent<Canvas>();
                if (cv != null) canvases.Add(cv);
            }

            // Remember everything we are about to disturb.
            var canvasState = new Dictionary<Canvas, (RenderMode mode, Camera cam, float dist)>();
            var activeState = new Dictionary<GameObject, bool>();
            foreach (var cv in canvases) canvasState[cv] = (cv.renderMode, cv.worldCamera, cv.planeDistance);
            foreach (var t in all.GetComponentsInChildren<Transform>(true))
                activeState[t.gameObject] = t.gameObject.activeSelf;

            var camGo = new GameObject("~UICaptureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Mid grey so both cream and deep-green art stay readable in review.
            cam.backgroundColor = new Color(0.35f, 0.37f, 0.36f, 1f);
            cam.cullingMask = 1 << LayerMask.NameToLayer("UI");
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };

            try
            {
                foreach (var cv in canvases)
                {
                    cv.renderMode = RenderMode.ScreenSpaceCamera;
                    cv.worldCamera = cam;
                    cv.planeDistance = 50f;
                }

                var popup = all.Find("Pop Up Canvas");
                var tutorial = all.Find("TutorialCanvas");
                var opening = all.Find("Opening Canvas");
                if (tutorial != null) tutorial.gameObject.SetActive(false);
                if (opening != null) opening.gameObject.SetActive(false);

                // HUD on its own first.
                foreach (Transform p in popup) p.gameObject.SetActive(false);
                Render(cam, rt, "00_hud");

                // Then each popup in turn, over the HUD as the player sees it.
                int i = 1;
                foreach (Transform p in popup)
                {
                    foreach (Transform q in popup) q.gameObject.SetActive(false);
                    OpenDeep(p);
                    Render(cam, rt, $"{i:00}_{Sanitise(p.name)}");
                    i++;
                }
            }
            finally
            {
                foreach (var kv in canvasState)
                {
                    kv.Key.renderMode = kv.Value.mode;
                    kv.Key.worldCamera = kv.Value.cam;
                    kv.Key.planeDistance = kv.Value.dist;
                }
                foreach (var kv in activeState)
                    if (kv.Key != null) kv.Key.SetActive(kv.Value);

                RenderTexture.active = null;
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            Debug.Log($"[Capture] Wrote panel renders to {OutDir}");
        }

        /// <summary>Turns a popup on along with the container children it needs.</summary>
        static void OpenDeep(Transform t)
        {
            t.gameObject.SetActive(true);
            foreach (Transform c in t)
            {
                var n = c.name;
                if (n.Contains("Panel") || n.Contains("BG") || n.Contains("Holder")
                    || n.Contains("Content") || n.Contains("Window") || n.Contains("Preview"))
                    OpenDeep(c);
            }
        }

        static void Render(Camera cam, RenderTexture rt, string name)
        {
            Canvas.ForceUpdateCanvases();
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static string Sanitise(string s) => s.Replace(" ", "_").Replace("/", "_");
    }
}
