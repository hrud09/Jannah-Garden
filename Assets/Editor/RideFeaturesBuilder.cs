using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Builds the riding extras into the open scene, in the game's existing UI style:
/// <list type="bullet">
/// <item><b>Altitude Stick</b> — the pegasus's up/down control: the movement joystick's dial with only
/// its up and down arrows, and its teal knob, in the bottom-right corner as the joystick's mirror.</item>
/// <item><b>Ride Prompt Panel</b> — the popup for ride ads, Auto Travel and the Orb Run, copied from the
/// treasure-box confirmation panel so it matches the other popups.</item>
/// <item><b>Orb Run HUD</b> — orbs collected and time left, top centre, on a deep ribbon.</item>
/// <item><b>Jungle Orb</b> prefab — the question-mark orb's glow without the question mark.</item>
/// <item>Portrait icons of the camel, pegasus and orb, rendered from their prefabs.</item>
/// <item><b>Ride Features</b> — the scene object holding <see cref="RideFeatures"/> and
/// <see cref="JungleOrbRun"/>, wired to all of the above.</item>
/// </list>
/// Safe to re-run: everything it made before is replaced. Save the scene afterwards.
/// </summary>
public static class RideFeaturesBuilder
{
    private const string UiFolder = "Assets/2D Assets/UI/UI Elements";
    private const string KitFolder = "Assets/2D Assets/UI/Jannah UI Kit";
    private const string IconFolder = "Assets/2D Assets/UI/Ride Icons";
    private const string OrbPrefabPath = "Assets/Prefabs/Jungle Orb.prefab";
    private const string QuestionOrbPath = "Assets/Prefabs/Question Mark Orb.prefab";
    private const string TreasurePanelPath = "Assets/Prefabs/UI/Tresure Box Open Confirmation Panel.prefab";
    private const string CamelPrefabPath = "Assets/3D Assets/Camel ypdate/Camel.prefab";
    private const string PegasusPrefabPath = "Assets/3D Assets/Unity_Export_Pegasus/Pegasus.prefab";
    private const string FontPath = "Assets/Packs and Plugins/GUI-CasualFantasy/ResourcesData/Fonts/TMP_LINESeedSans.asset";

    private static readonly Color Cream = new Color32(0xFA, 0xF6, 0xEE, 0xFF);
    private static readonly Color Gold = new Color32(0xD4, 0xA5, 0x50, 0xFF);

    [MenuItem("Jannah/Animals/Build Ride Features")]
    public static void Build()
    {
        GameObject allCanvas = GameObject.Find("All Canvas");
        if (allCanvas == null)
        {
            Debug.LogError("[RideFeaturesBuilder] 'All Canvas' not found — open the Jannah Garden scene first.");
            return;
        }

        ConfigureSprite(UiFolder + "/VerticalAxis_Outline.png", UiFolder + "/AllAxis_Outline.png");

        GameObject orbPrefab = BuildOrbPrefab();
        Sprite camelIcon = RenderIcon(CamelPrefabPath, "Ride_Camel", "Idle_01", 0.3f, 35f);
        Sprite pegasusIcon = RenderIcon(PegasusPrefabPath, "Ride_Pegasus", "Pegasus_Idle_Wings", 0.5f, 35f);
        Sprite orbIcon = RenderIcon(OrbPrefabPath, "Ride_Orb", null, 0f, 10f);
        Sprite coinIcon = AssetDatabase.LoadAssetAtPath<Sprite>(UiFolder + "/jg_noor_coin_fly.png");

        Transform popUp = allCanvas.transform.Find("Pop Up Canvas");
        Transform buttons = allCanvas.transform.Find("Interactable Button Canvas");
        Transform hud = allCanvas.transform.Find("The Dynamic HUD Canvas");

        RidePromptPanel panel = BuildPromptPanel(popUp);
        AltitudeStick stick = BuildAltitudeStick(buttons);
        (GameObject hudRoot, TMP_Text count, TMP_Text timer) = BuildOrbHud(hud, orbIcon);

        GameObject managers = GameObject.Find("All Managing Scripts");
        Transform parent = managers != null ? managers.transform : null;
        GameObject hub = FindOrCreate(parent, "Ride Features");

        RideFeatures features = GetOrAdd<RideFeatures>(hub);
        features.promptPanel = panel;
        features.altitudeStick = stick;
        features.camelIcon = camelIcon;
        features.pegasusIcon = pegasusIcon;
        features.orbIcon = orbIcon;
        features.coinIcon = coinIcon;

        JungleOrbRun run = GetOrAdd<JungleOrbRun>(hub);
        run.orbPrefab = orbPrefab;
        run.hud = hudRoot;
        run.countText = count;
        run.timerText = timer;

        EditorUtility.SetDirty(features);
        EditorUtility.SetDirty(run);
        EditorSceneManager.MarkSceneDirty(hub.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[RideFeaturesBuilder] Built the altitude stick, ride prompt panel, Orb Run HUD, Jungle Orb and icons. Save the scene.");
    }

    // ─── Altitude stick ─────────────────────────────────────────────────────────

    private static AltitudeStick BuildAltitudeStick(Transform canvas)
    {
        DestroyChild(canvas, "Altitude Stick");

        var root = new GameObject("Altitude Stick", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)root.transform;
        rt.SetParent(canvas, false);
        // The joystick's dial rests 256 in from the bottom-left corner; this mirrors it on the right,
        // nudged in and up to keep clear of the action buttons' column.
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(240f, 240f);
        rt.anchoredPosition = new Vector2(-300f, 250f);

        var bg = root.GetComponent<Image>();
        bg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiFolder + "/VerticalAxis_Outline.png");
        bg.raycastTarget = true;

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var hrt = (RectTransform)handle.transform;
        hrt.SetParent(rt, false);
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.sizeDelta = new Vector2(116f, 116f);
        var himg = handle.GetComponent<Image>();
        himg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiFolder + "/Handle_Plain.png");
        himg.raycastTarget = false;

        var stick = root.AddComponent<AltitudeStick>();
        stick.background = rt;
        stick.handle = hrt;

        root.SetActive(false);
        return stick;
    }

    // ─── Prompt panel ───────────────────────────────────────────────────────────

    private static RidePromptPanel BuildPromptPanel(Transform popUpCanvas)
    {
        DestroyChild(popUpCanvas, "Ride Prompt Panel");

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(TreasurePanelPath);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(source, popUpCanvas);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "Ride Prompt Panel";
        root.transform.SetAsLastSibling();

        Object.DestroyImmediate(root.GetComponent<TreasureBoxConfirmationPanel>());

        Transform window = root.transform.Find("BG Image");
        Transform content = window.Find("Content");

        Transform rarity = window.Find("Rarity BG");
        if (rarity != null) Object.DestroyImmediate(rarity.gameObject);

        TMP_Text title = window.Find("Name BG").GetComponentInChildren<TMP_Text>(true);
        Transform description = window.Find("Descriptiopn BG");
        TMP_Text[] texts = description.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text body = texts.First(t => !t.name.StartsWith("Ads"));
        TMP_Text note = texts.First(t => t.name.StartsWith("Ads"));

        // Room under the title ribbon, which overlaps the top of the description box.
        var column = description.GetComponent<VerticalLayoutGroup>();
        if (column != null) column.padding.top = Mathf.Max(column.padding.top, 34);

        body.fontStyle = FontStyles.Normal;
        body.fontSize = 30f;
        body.textWrappingMode = TextWrappingModes.Normal;
        note.textWrappingMode = TextWrappingModes.Normal;

        Button primary = content.Find("Buttons/Watch Ad Button").GetComponent<Button>();
        Button secondary = content.Find("Buttons/Subscribe Button").GetComponent<Button>();
        Button close = content.GetComponentsInChildren<Button>(true).First(b => b.name.StartsWith("Close"));
        primary.name = "Primary Button";
        secondary.name = "Secondary Button";
        TMP_Text primaryLabel = primary.GetComponentInChildren<TMP_Text>(true);
        TMP_Text secondaryLabel = secondary.GetComponentInChildren<TMP_Text>(true);

        // Texts are filled in per use; leftover keys would overwrite them on a language change.
        foreach (TMP_Text t in new[] { title, body, note, primaryLabel, secondaryLabel })
        {
            var localized = t.GetComponent<LocalizedText>();
            if (localized != null) Object.DestroyImmediate(localized);
        }

        foreach (Button b in new[] { primary, secondary, close }) ClearPersistent(b);

        // The TV on the primary button, as on the Open button, when it plays an ad.
        var adIcon = new GameObject("Ad Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var art = (RectTransform)adIcon.transform;
        art.SetParent(primary.transform, false);
        art.anchorMin = art.anchorMax = new Vector2(0f, 0.5f);
        art.pivot = new Vector2(0.5f, 0.5f);
        art.anchoredPosition = new Vector2(38f, 4f);
        art.sizeDelta = new Vector2(46f, 46f);
        var adImage = adIcon.GetComponent<Image>();
        adImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiFolder + "/icon_tv.png");
        adImage.preserveAspect = true;
        adImage.raycastTarget = false;
        var plr = (RectTransform)primaryLabel.transform;
        plr.offsetMin = new Vector2(plr.offsetMin.x + 34f, plr.offsetMin.y);

        // A little more room for the two buttons' longer labels ("Ride Now", "Collect 100").
        foreach (Button b in new[] { primary, secondary })
        {
            var brt = (RectTransform)b.transform;
            brt.sizeDelta = new Vector2(280f, brt.sizeDelta.y);
            var le = b.GetComponent<LayoutElement>();
            if (le != null) { le.preferredWidth = 280f; le.minWidth = 280f; }
        }

        Image icon = content.Find("Item Icon Image BG/Item Icon Image").GetComponent<Image>();
        icon.preserveAspect = true;

        var panel = root.AddComponent<RidePromptPanel>();
        panel.dim = root.transform.Find("Dark BG Image").gameObject;
        panel.window = window.gameObject;
        panel.titleText = title;
        panel.bodyText = body;
        panel.noteText = note;
        panel.icon = icon;
        panel.primaryButton = primary;
        panel.primaryLabel = primaryLabel;
        panel.primaryAdIcon = adIcon;
        panel.secondaryButton = secondary;
        panel.secondaryLabel = secondaryLabel;
        panel.closeButton = close;

        panel.dim.SetActive(false);
        window.gameObject.SetActive(false);
        return panel;
    }

    private static void ClearPersistent(Button button)
    {
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }
    }

    // ─── Orb Run HUD ────────────────────────────────────────────────────────────

    private static (GameObject, TMP_Text, TMP_Text) BuildOrbHud(Transform canvas, Sprite orbIcon)
    {
        DestroyChild(canvas, "Orb Run HUD");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        var root = new GameObject("Orb Run HUD", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)root.transform;
        rt.SetParent(canvas, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -28f);
        rt.sizeDelta = new Vector2(460f, 100f);
        var bg = root.GetComponent<Image>();
        bg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KitFolder + "/jg_ribbon_deep.png");
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = false;

        var icon = new GameObject("Orb Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var irt = (RectTransform)icon.transform;
        irt.SetParent(rt, false);
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = new Vector2(-165f, 2f);
        irt.sizeDelta = new Vector2(78f, 78f);
        var iimg = icon.GetComponent<Image>();
        iimg.sprite = orbIcon;
        iimg.preserveAspect = true;
        iimg.raycastTarget = false;

        TMP_Text count = MakeText(rt, "Count Text", font, 44f, new Vector2(-45f, 4f), new Vector2(170f, 70f), Cream);
        var divider = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var drt = (RectTransform)divider.transform;
        drt.SetParent(rt, false);
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
        drt.anchoredPosition = new Vector2(50f, 2f);
        drt.sizeDelta = new Vector2(3f, 50f);
        divider.GetComponent<Image>().color = new Color(Gold.r, Gold.g, Gold.b, 0.8f);
        divider.GetComponent<Image>().raycastTarget = false;
        TMP_Text timer = MakeText(rt, "Timer Text", font, 40f, new Vector2(135f, 4f), new Vector2(150f, 70f), Cream);

        LocalizedRendering.SetPlainText(count, "0 / 10");
        LocalizedRendering.SetPlainText(timer, "4:00");

        root.SetActive(false);
        return (root, count, timer);
    }

    private static TMP_Text MakeText(RectTransform parent, string name, TMP_FontAsset font, float size,
                                     Vector2 pos, Vector2 box, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    // ─── Orb prefab ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The jungle orb (blue glow + stone sphere). Built once from the question-mark orb; that orb has
    /// since become a gold scroll, so an existing jungle orb is kept rather than rebuilt from it.
    /// </summary>
    private static GameObject BuildOrbPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(OrbPrefabPath);
        if (existing != null) return existing;

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(QuestionOrbPath);
        GameObject orb = Object.Instantiate(source);
        orb.name = "Jungle Orb";
        try
        {
            foreach (QuestionMarkOrb q in orb.GetComponentsInChildren<QuestionMarkOrb>(true)) Object.DestroyImmediate(q);
            foreach (Collider c in orb.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            Transform mark = orb.transform.Find("Questionmark");
            if (mark != null) Object.DestroyImmediate(mark.gameObject);
            if (orb.GetComponent<JungleOrb>() == null) orb.AddComponent<JungleOrb>();
            return PrefabUtility.SaveAsPrefabAsset(orb, OrbPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(orb);
        }
    }

    // ─── Icons ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="prefabPath"/> in a three-quarter view on a transparent background and
    /// saves it as a UI sprite. <paramref name="clip"/> (if any) is sampled at
    /// <paramref name="clipTime"/> (fraction of its length) for the pose.
    /// </summary>
    private static Sprite RenderIcon(string prefabPath, string fileName, string clip, float clipTime, float yaw)
    {
        if (!AssetDatabase.IsValidFolder(IconFolder)) AssetDatabase.CreateFolder("Assets/2D Assets/UI", "Ride Icons");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;

        const int size = 512;
        var stage = new GameObject("~IconStage");
        stage.transform.position = new Vector3(0f, 900f, 0f);
        GameObject subject = Object.Instantiate(prefab, stage.transform);
        subject.transform.localPosition = Vector3.zero;
        subject.transform.localRotation = Quaternion.Euler(0f, 180f + yaw, 0f);

        RenderTexture rt = null;
        try
        {
            if (!string.IsNullOrEmpty(clip))
            {
                var animator = subject.GetComponentInChildren<Animator>();
                var controller = animator != null ? animator.runtimeAnimatorController : null;
                AnimationClip c = controller != null ? controller.animationClips.FirstOrDefault(a => a.name == clip) : null;
                if (c != null) c.SampleAnimation(animator.gameObject, c.length * clipTime);
            }

            // Minimap markers and particle glows don't belong in a portrait.
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is SpriteRenderer) r.enabled = false;
            }

            // Frame on the posed mesh itself: renderer bounds are padded (and lag the pose).
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                Mesh mesh = null;
                bool baked = false;
                Matrix4x4 toWorld = r.transform.localToWorldMatrix;
                if (r is SkinnedMeshRenderer skinned)
                {
                    // A bake comes out at world size but in the renderer's frame: the skinning already
                    // carries the scale, so only its position and rotation are left to apply.
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, false);
                    baked = true;
                    toWorld = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else if (r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
                if (mesh == null) continue;

                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 w = toWorld.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
                if (baked) Object.DestroyImmediate(mesh);
            }
            if (!any) b = new Bounds(subject.transform.position, Vector3.one);

            var camGo = new GameObject("~IconCam");
            camGo.transform.SetParent(stage.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            float radius = b.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.8f;
            Vector3 dir = new Vector3(0f, 0.28f, -1f).normalized;
            cam.transform.position = b.center + dir * distance;
            cam.transform.LookAt(b.center);

            // A soft key light so the icon doesn't depend on the scene's time of day.
            var lightGo = new GameObject("~IconLight");
            lightGo.transform.SetParent(stage.transform);
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string path = IconFolder + "/" + fileName + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        finally
        {
            if (rt != null) Object.DestroyImmediate(rt);
            Object.DestroyImmediate(stage);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Gives <paramref name="path"/> the same import settings as the sprite it was made from.</summary>
    private static void ConfigureSprite(string path, string likePath)
    {
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var like = (TextureImporter)AssetImporter.GetAtPath(likePath);
        if (importer == null || like == null) return;

        var settings = new TextureImporterSettings();
        like.ReadTextureSettings(settings);
        importer.SetTextureSettings(settings);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.SaveAndReimport();
    }

    private static void DestroyChild(Transform parent, string name)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            if (parent.GetChild(i).name == name) Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }

    private static GameObject FindOrCreate(Transform parent, string name)
    {
        Transform existing = parent != null ? parent.Find(name) : GameObject.Find(name)?.transform;
        if (existing != null) return existing.gameObject;
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
