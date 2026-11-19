using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Sets up the Pegasus: import settings, the flight clips it doesn't ship with, its animator controller
/// and a ready-to-place prefab.
///
/// The model comes with four in-place clips — Idle, Walk, Run (gallop, wings held back) and Run_Flap
/// (gallop with a wing beat per stride). Flying needs more, so these are derived bone by bone from them:
/// <list type="bullet">
/// <item><b>Pegasus_Fly</b> — Run_Flap's wing beat, with the legs tucked up under the body (held at the
/// gallop's most gathered moment) and the body's gallop heave damped down to a gentle rise and fall.</item>
/// <item><b>Pegasus_Glide</b> — wings held spread wide (Run_Flap's most open moment) with only a slow
/// ripple through them, legs tucked.</item>
/// <item><b>Pegasus_Hover</b> — the wing beat with the legs hanging down and paddling slightly, for
/// rising, sinking and hanging in the air.</item>
/// <item><b>Pegasus_Idle_Wings</b> — the standing idle with the wings slowly opening, held, and folded
/// again, for the ambient animals.</item>
/// </list>
/// The controller has the ambient states GrazingAnimal plays (Idle, Idle_Wings, Walk), the ridden ground
/// states (RideIdle, RideMove blend tree on RideGait, RideBack), TakeOff (Run_Flap: the gallop into the
/// air and back down) and FlyMove, a blend tree on FlyBlend: 0 = hover, 1 = fly, 2 = glide.
///
/// Safe to re-run: clips are regenerated in place, and the controller and prefab rebuilt (the prefab keeps
/// its GUID, so scene instances stay linked).
/// </summary>
public static class PegasusAnimationBuilder
{
    private const string Folder = "Assets/3D Assets/Unity_Export_Pegasus";
    private const string ModelPath = Folder + "/Pegasus.fbx";
    private const string ClipFolder = Folder + "/Animations";
    private const string ControllerPath = Folder + "/Pegasus.controller";
    private const string PrefabPath = Folder + "/Pegasus.prefab";

    public const string GaitParameter = "RideGait";
    public const string FlyParameter = "FlyBlend";

    private static readonly string[] SourceClips = { "Pegasus_Idle", "Pegasus_Walk", "Pegasus_Run", "Pegasus_Run_Flap" };

    [MenuItem("Jannah/Animals/Build Pegasus")]
    public static void Build()
    {
        ConfigureImport();
        ConfigureMaterials();

        AnimationClip idle = LoadClip("Pegasus_Idle");
        AnimationClip walk = LoadClip("Pegasus_Walk");
        AnimationClip run = LoadClip("Pegasus_Run");
        AnimationClip flap = LoadClip("Pegasus_Run_Flap");
        if (idle == null || walk == null || run == null || flap == null)
        {
            Debug.LogError("[PegasusAnimationBuilder] Pegasus clips not found in " + ModelPath);
            return;
        }

        if (!AssetDatabase.IsValidFolder(ClipFolder)) AssetDatabase.CreateFolder(Folder, "Animations");

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        GameObject probe = Object.Instantiate(model);
        probe.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            float tuckTime = FindTuckTime(probe, flap);
            float spreadTime = FindSpreadTime(probe, flap);

            AnimationClip fly = SaveClip(DeriveFly(flap, tuckTime));
            AnimationClip glide = SaveClip(DeriveGlide(flap, tuckTime, spreadTime));
            AnimationClip hover = SaveClip(DeriveHover(flap, idle));
            AnimationClip idleWings = SaveClip(DeriveIdleWings(idle, flap, spreadTime));

            AnimatorController controller = BuildController(idle, idleWings, walk, run, flap, hover, fly, glide);
            BuildPrefab(model, controller);

            Debug.Log($"[PegasusAnimationBuilder] Built flight clips (legs tucked at {tuckTime:0.000}s, wings " +
                      $"spread at {spreadTime:0.000}s of Run_Flap), Pegasus.controller and Pegasus.prefab.");
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }

        AssetDatabase.SaveAssets();

        // The controller is deleted and recreated at the same path (same GUID). When the prefab's saved
        // reference doesn't change, the prefab in memory can keep pointing at the deleted controller,
        // leaving every Pegasus with "Animator does not have an AnimatorController". Reimport to re-resolve.
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
    }

    // ─── Import ─────────────────────────────────────────────────────────────────

    /// <summary>Loops every clip and names them without Blender's "Rig|" prefix (see the model's README).</summary>
    private static void ConfigureImport()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        bool dirty = false;

        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            dirty = true;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        foreach (ModelImporterClipAnimation clip in clips)
        {
            string clean = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
            if (clip.name != clean || !clip.loopTime || clip.loopPose || !clip.lockRootRotation ||
                !clip.lockRootHeightY || !clip.lockRootPositionXZ)
            {
                clip.name = clean;
                clip.loopTime = true;
                clip.loopPose = false; // The clips already loop exactly.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                dirty = true;
            }
        }

        if (dirty)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// Normal maps marked as such, and the embedded materials pulled out into editable assets: the
    /// feathers are single-sided cards, so they're drawn from both sides, and the untextured mane gets a
    /// light colour that suits the white coat.
    /// </summary>
    private static void ConfigureMaterials()
    {
        foreach (string name in new[] { "body_normal", "feathers_normal", "harness_normal" })
        {
            var tex = (TextureImporter)AssetImporter.GetAtPath(Folder + "/Textures/" + name + ".png");
            if (tex != null && tex.textureType != TextureImporterType.NormalMap)
            {
                tex.textureType = TextureImporterType.NormalMap;
                tex.SaveAndReimport();
            }
        }

        const string materialFolder = Folder + "/Materials";
        if (!AssetDatabase.IsValidFolder(materialFolder)) AssetDatabase.CreateFolder(Folder, "Materials");

        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        bool remapped = false;
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
        {
            string path = materialFolder + "/Pegasus_" + embedded.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(embedded) { name = "Pegasus_" + embedded.name };
                AssetDatabase.CreateAsset(material, path);
            }

            if (embedded.name.StartsWith("feathers"))
            {
                material.SetFloat("_Cull", 0f); // Both sides.
                material.doubleSidedGI = true;
            }
            else if (embedded.name == "HAIRS" && material.GetTexture("_BaseMap") == null)
            {
                material.SetColor("_BaseColor", new Color(0.95f, 0.93f, 0.88f));
                material.SetFloat("_Smoothness", 0.25f);
            }
            EditorUtility.SetDirty(material);

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
            remapped = true;
        }

        if (remapped) importer.SaveAndReimport();
    }

    private static AnimationClip LoadClip(string name)
    {
        // Skip Unity's hidden "__preview__<take>" clips: they also end in "|<name>" but are the raw takes,
        // without the import settings: not looping and, for the gallops, twice the clip's real length
        // (half a second of motion, then half a second frozen). States built on them play once and stall.
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        return clips.FirstOrDefault(c => c.name == name) ??
               clips.FirstOrDefault(c => c.name.EndsWith("|" + name));
    }

    // ─── Key moments in the flapping gallop ─────────────────────────────────────

    /// <summary>The time in <paramref name="flap"/> when the hooves are drawn highest up under the body.</summary>
    private static float FindTuckTime(GameObject probe, AnimationClip flap)
    {
        Transform torso = Find(probe, "CTRL_torso");
        Transform[] hooves = { Find(probe, "hoof_front_L"), Find(probe, "hoof_front_R"),
                               Find(probe, "hoof_hind_L"), Find(probe, "hoof_hind_R") };
        if (torso == null || hooves.Any(h => h == null)) return 0f;

        return BestTime(probe, flap, () =>
        {
            float lift = 0f;
            foreach (Transform h in hooves) lift += h.position.y - torso.position.y;
            return lift;
        });
    }

    /// <summary>The time in <paramref name="flap"/> when the wing tips are furthest apart.</summary>
    private static float FindSpreadTime(GameObject probe, AnimationClip flap)
    {
        Transform l = Find(probe, "wing_tip_L"), r = Find(probe, "wing_tip_R");
        if (l == null || r == null) return 0f;
        return BestTime(probe, flap, () => Vector3.Distance(l.position, r.position));
    }

    private static float BestTime(GameObject probe, AnimationClip clip, Func<float> score)
    {
        int frames = Frames(clip);
        float best = float.MinValue, bestTime = 0f;
        for (int i = 0; i < frames; i++)
        {
            float t = clip.length * i / frames;
            clip.SampleAnimation(probe, t);
            float s = score();
            if (s > best) { best = s; bestTime = t; }
        }
        return bestTime;
    }

    private static Transform Find(GameObject root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    }

    // ─── Bone groups ────────────────────────────────────────────────────────────

    private enum Part { Body, Wing, Leg }

    private static Part PartOf(string path)
    {
        string bone = path.Substring(path.LastIndexOf('/') + 1);
        if (bone.StartsWith("wing_")) return Part.Wing;
        if (bone.StartsWith("thigh") || bone.StartsWith("shin") || bone.StartsWith("cannon") ||
            bone.StartsWith("pastern") || bone.StartsWith("hoof") || bone.StartsWith("upperarm") ||
            bone.StartsWith("forearm") || bone.StartsWith("scapula"))
        {
            return Part.Leg;
        }
        return Part.Body;
    }

    // ─── Deriving clips ─────────────────────────────────────────────────────────

    /// <summary>A local pose for one bone: position, rotation, scale.</summary>
    private struct Pose
    {
        public Vector3 P;
        public Quaternion R;
        public Vector3 S;

        public static Pose Blend(Pose a, Pose b, float k)
        {
            return new Pose
            {
                P = Vector3.LerpUnclamped(a.P, b.P, k),
                R = Quaternion.SlerpUnclamped(a.R, b.R, k),
                S = Vector3.LerpUnclamped(a.S, b.S, k),
            };
        }
    }

    /// <summary>Reads one bone's curves out of a clip.</summary>
    private sealed class Track
    {
        private readonly AnimationCurve[] _p = new AnimationCurve[3], _r = new AnimationCurve[4], _s = new AnimationCurve[3];
        private readonly float _length;
        private Pose? _mean;

        public Track(AnimationClip clip, IEnumerable<EditorCurveBinding> bindings)
        {
            _length = clip.length;
            foreach (EditorCurveBinding b in bindings)
            {
                AnimationCurve c = AnimationUtility.GetEditorCurve(clip, b);
                int axis = b.propertyName[b.propertyName.Length - 1] - 'x';
                if (b.propertyName.StartsWith("m_LocalPosition.")) _p[axis] = c;
                else if (b.propertyName.StartsWith("m_LocalScale.")) _s[axis] = c;
                else if (b.propertyName.StartsWith("m_LocalRotation."))
                {
                    _r[b.propertyName.EndsWith("w") ? 3 : axis] = c;
                }
            }
        }

        /// <summary>The pose at <paramref name="t"/>, wrapped round the clip's loop.</summary>
        public Pose At(float t)
        {
            t = Mathf.Repeat(t, _length);
            return new Pose
            {
                P = new Vector3(E(_p[0], t, 0f), E(_p[1], t, 0f), E(_p[2], t, 0f)),
                R = new Quaternion(E(_r[0], t, 0f), E(_r[1], t, 0f), E(_r[2], t, 0f), E(_r[3], t, 1f)).normalized,
                S = new Vector3(E(_s[0], t, 1f), E(_s[1], t, 1f), E(_s[2], t, 1f)),
            };
        }

        /// <summary>The pose averaged over the whole loop — the middle every swing goes either side of.</summary>
        public Pose Mean
        {
            get
            {
                if (_mean.HasValue) return _mean.Value;
                const int n = 24;
                Vector3 p = Vector3.zero, s = Vector3.zero;
                Vector4 r = Vector4.zero;
                Quaternion first = At(0f).R;
                for (int i = 0; i < n; i++)
                {
                    Pose x = At(_length * i / n);
                    p += x.P; s += x.S;
                    Quaternion q = Quaternion.Dot(x.R, first) < 0f ? new Quaternion(-x.R.x, -x.R.y, -x.R.z, -x.R.w) : x.R;
                    r += new Vector4(q.x, q.y, q.z, q.w);
                }
                r.Normalize();
                _mean = new Pose { P = p / n, R = new Quaternion(r.x, r.y, r.z, r.w), S = s / n };
                return _mean.Value;
            }
        }

        public bool HasPosition => _p[0] != null;
        public bool HasRotation => _r[3] != null;
        public bool HasScale => _s[0] != null;

        private static float E(AnimationCurve c, float t, float fallback) => c != null ? c.Evaluate(t) : fallback;
    }

    private static Dictionary<string, Track> Tracks(AnimationClip clip)
    {
        return AnimationUtility.GetCurveBindings(clip)
            .Where(b => b.type == typeof(Transform))
            .GroupBy(b => b.path)
            .ToDictionary(g => g.Key, g => new Track(clip, g));
    }

    /// <summary>
    /// A new looping clip of <paramref name="length"/> seconds, every bone of <paramref name="template"/>
    /// posed by <paramref name="pose"/>(path, part, time).
    /// </summary>
    private static AnimationClip Compose(string name, AnimationClip template, float length,
                                         Func<string, Part, float, Pose> pose)
    {
        var clip = new AnimationClip { name = name, frameRate = template.frameRate, legacy = false };
        int frames = Mathf.Max(2, Mathf.RoundToInt(length * template.frameRate));
        Dictionary<string, Track> reference = Tracks(template);

        foreach (var entry in reference)
        {
            string path = entry.Key;
            Part part = PartOf(path);

            var p = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var r = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var s = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            Quaternion previous = Quaternion.identity;

            for (int i = 0; i <= frames; i++)
            {
                float t = length * i / frames;
                // The last key repeats the first exactly, so the loop is seamless.
                Pose x = pose(path, part, i == frames ? 0f : t);
                Quaternion q = x.R;
                if (i > 0 && Quaternion.Dot(q, previous) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                previous = q;

                for (int a = 0; a < 3; a++) { p[a].AddKey(t, x.P[a]); s[a].AddKey(t, x.S[a]); }
                r[0].AddKey(t, q.x); r[1].AddKey(t, q.y); r[2].AddKey(t, q.z); r[3].AddKey(t, q.w);
            }

            string[] axes = { "x", "y", "z" };
            if (entry.Value.HasPosition)
                for (int a = 0; a < 3; a++) Set(clip, path, "m_LocalPosition." + axes[a], p[a]);
            if (entry.Value.HasRotation)
            {
                for (int a = 0; a < 3; a++) Set(clip, path, "m_LocalRotation." + axes[a], r[a]);
                Set(clip, path, "m_LocalRotation.w", r[3]);
            }
            if (entry.Value.HasScale)
                for (int a = 0; a < 3; a++) Set(clip, path, "m_LocalScale." + axes[a], s[a]);
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void Set(AnimationClip clip, string path, string property, AnimationCurve curve)
    {
        for (int k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0f);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }

    /// <summary>
    /// Flapping flight: Run_Flap's wings, legs gathered up (just a hint of paddling left in them) and the
    /// gallop's body heave softened so the beat lifts the body rather than bouncing it.
    /// </summary>
    private static AnimationClip DeriveFly(AnimationClip flap, float tuckTime)
    {
        Dictionary<string, Track> f = Tracks(flap);
        return Compose("Pegasus_Fly", flap, flap.length, (path, part, t) =>
        {
            Track tr = f[path];
            switch (part)
            {
                case Part.Wing: return tr.At(t);
                case Part.Leg: return Pose.Blend(tr.At(tuckTime), tr.At(t), 0.12f);
                default: return Pose.Blend(tr.Mean, tr.At(t), 0.35f);
            }
        });
    }

    /// <summary>
    /// Gliding: wings held open with a slow ripple, legs tucked, body nearly still. Two seconds long so it
    /// reads as calm, and so blending in from the beat slows the wings down rather than snapping them.
    /// </summary>
    private static AnimationClip DeriveGlide(AnimationClip flap, float tuckTime, float spreadTime)
    {
        Dictionary<string, Track> f = Tracks(flap);
        float length = flap.length * 4f;
        return Compose("Pegasus_Glide", flap, length, (path, part, t) =>
        {
            Track tr = f[path];
            // One wing-beat's worth of motion stretched over the whole glide, kept very small.
            float slow = t / length * flap.length;
            switch (part)
            {
                case Part.Wing: return Pose.Blend(tr.At(spreadTime), tr.At(slow + spreadTime), 0.1f);
                case Part.Leg: return Pose.Blend(tr.At(tuckTime), tr.At(slow), 0.06f);
                default: return Pose.Blend(tr.Mean, tr.At(slow), 0.2f);
            }
        });
    }

    /// <summary>Hovering: the full wing beat, legs hanging down from the standing pose and paddling a little.</summary>
    private static AnimationClip DeriveHover(AnimationClip flap, AnimationClip idle)
    {
        Dictionary<string, Track> f = Tracks(flap);
        Dictionary<string, Track> i = Tracks(idle);
        return Compose("Pegasus_Hover", flap, flap.length, (path, part, t) =>
        {
            Track tr = f[path];
            switch (part)
            {
                case Part.Wing: return tr.At(t);
                case Part.Leg:
                    return i.TryGetValue(path, out Track stand)
                        ? Pose.Blend(stand.At(0f), tr.At(t), 0.18f)
                        : tr.At(t);
                default: return Pose.Blend(tr.Mean, tr.At(t), 0.45f);
            }
        });
    }

    /// <summary>
    /// The standing idle with a wing stretch through the middle: open over a second, held, folded back.
    /// Same length as the idle so the rest of the body is untouched.
    /// </summary>
    private static AnimationClip DeriveIdleWings(AnimationClip idle, AnimationClip flap, float spreadTime)
    {
        Dictionary<string, Track> i = Tracks(idle);
        Dictionary<string, Track> f = Tracks(flap);
        float length = idle.length;
        return Compose("Pegasus_Idle_Wings", idle, length, (path, part, t) =>
        {
            Pose rest = i[path].At(t);
            if (part != Part.Wing || !f.TryGetValue(path, out Track wing)) return rest;

            // 0 → 1 → 0 across the clip, with a long hold at the top.
            float k = Mathf.Clamp01(Mathf.Sin(t / length * Mathf.PI) * 1.6f);
            k = k * k * (3f - 2f * k);
            Pose open = Pose.Blend(wing.At(spreadTime), wing.At(spreadTime + t * 0.15f), 0.15f);
            return Pose.Blend(rest, open, k * 0.85f);
        });
    }

    private static AnimationClip SaveClip(AnimationClip clip)
    {
        string path = ClipFolder + "/" + clip.name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(clip, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static int Frames(AnimationClip clip) => Mathf.Max(2, Mathf.RoundToInt(clip.length * clip.frameRate));

    // ─── Controller ─────────────────────────────────────────────────────────────

    private static AnimatorController BuildController(AnimationClip idle, AnimationClip idleWings, AnimationClip walk,
        AnimationClip run, AnimationClip flap, AnimationClip hover, AnimationClip fly, AnimationClip glide)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(GaitParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(FlyParameter, AnimatorControllerParameterType.Float);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idleState = machine.AddState("Idle", new Vector3(300f, 0f, 0f));
        idleState.motion = idle;
        machine.defaultState = idleState;

        machine.AddState("Idle_Wings", new Vector3(300f, 70f, 0f)).motion = idleWings;
        machine.AddState("Walk", new Vector3(300f, 140f, 0f)).motion = walk;

        machine.AddState("RideIdle", new Vector3(560f, 0f, 0f)).motion = idle;

        AnimatorState move = controller.CreateBlendTreeInController("RideMove", out BlendTree moveTree, 0);
        moveTree.name = "RideMove";
        moveTree.blendType = BlendTreeType.Simple1D;
        moveTree.blendParameter = GaitParameter;
        moveTree.useAutomaticThresholds = false;
        moveTree.AddChild(walk, 0f); // Shuffling round on the spot is just a slow walk.
        moveTree.AddChild(walk, 1f);
        moveTree.AddChild(run, 2f);

        AnimatorState back = machine.AddState("RideBack", new Vector3(560f, 140f, 0f));
        back.motion = walk;
        back.speed = -1f;

        machine.AddState("TakeOff", new Vector3(820f, 0f, 0f)).motion = flap;

        controller.CreateBlendTreeInController("FlyMove", out BlendTree flyTree, 0);
        flyTree.name = "FlyMove";
        flyTree.blendType = BlendTreeType.Simple1D;
        flyTree.blendParameter = FlyParameter;
        flyTree.useAutomaticThresholds = false;
        flyTree.AddChild(hover, 0f);
        flyTree.AddChild(fly, 1f);
        flyTree.AddChild(glide, 2f);

        foreach (ChildAnimatorState s in machine.states) s.state.writeDefaultValues = true;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    // ─── Prefab ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A prefab variant of the model with the controller and GrazingAnimal on its root. The Animator has
    /// to stay on the model's own root: the clips' bone paths start from there.
    /// </summary>
    private static void BuildPrefab(GameObject model, AnimatorController controller)
    {
        bool fresh = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null;
        GameObject root = fresh
            ? (GameObject)PrefabUtility.InstantiatePrefab(model)
            : PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            root.name = "Pegasus";

            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var animal = root.GetComponent<GrazingAnimal>();
            if (animal == null) animal = root.AddComponent<GrazingAnimal>();
            if (fresh) ApplyPegasusDefaults(animal);

            if (fresh)
            {
                foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    // Spread wings reach well past the rest pose's box; keep them from culling early.
                    Bounds b = r.localBounds;
                    b.Expand(1.5f);
                    r.localBounds = b;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            if (fresh) Object.DestroyImmediate(root);
            else PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Speeds and states for a pegasus, set once when the prefab is first made.</summary>
    private static void ApplyPegasusDefaults(GrazingAnimal a)
    {
        a.idleStates = new[] { "Idle", "Idle", "Idle_Wings" };
        a.walkState = "Walk";
        a.idleDuration = new Vector2(5f, 12f);
        a.wanderRadius = 10f;
        a.walkChance = 0.4f;
        // Walk matches 0.94 m/s and the gallop 7.2 m/s at the model's own size; the import scales it 1.5x.
        a.walkSpeed = 1.41f;
        a.turnSpeed = 60f;
        a.personalSpace = 6f;
        a.placedItemClearance = 2f;

        a.rideable = true;
        a.seatBoneName = "spine_02";
        // The dip of the saddle, measured off the harness mesh in the idle pose (between pommel and cantle).
        a.seatOffset = new Vector3(0f, 2.38f, -0.45f);
        a.rideWalkSpeed = 2.4f;
        a.rideRunSpeed = 8f;
        a.rideReverseSpeed = 1f;
        a.rideTurnSpeed = 75f;
        a.maxRideAnimationSpeed = 1.8f;
        a.runStrideScale = 7.66f; // 10.8 m/s gallop ÷ 1.41 m/s walk.
        a.shuffleStrideScale = 1f;

        a.canFly = true;
    }
}
