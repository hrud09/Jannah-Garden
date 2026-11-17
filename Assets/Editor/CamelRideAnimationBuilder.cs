using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the camel's ridden gaits and wires them into Camel.controller.
///
/// The camel model ships one locomotion clip (WalkCycle) and six idles. Riding needs more than that, so
/// the extra gaits are derived from the walk itself, which keeps every one of them on the same 39-frame
/// cycle and lets them blend in step:
/// <list type="bullet">
/// <item><b>Camel_RideRun</b> — the walk's joint swings exaggerated into a long, loping stride, with the
/// body bouncing twice per cycle.</item>
/// <item><b>Camel_RideShuffle</b> — the same swings damped into short steps, for turning on the spot and
/// creeping forward.</item>
/// </list>
/// They sit in a 1D blend tree, <c>RideMove</c>, driven by the float <c>RideGait</c>
/// (0 = shuffle, 1 = walk, 2 = run). <c>RideBack</c> plays WalkCycle in reverse, and <c>RideIdle</c> is a
/// calm standing idle with no grazing, so the rider's view isn't dragged to the ground.
/// GrazingAnimal crossfades to these by name while ridden.
///
/// Safe to re-run: the clips are regenerated in place and the states are replaced.
/// </summary>
public static class CamelRideAnimationBuilder
{
    private const string ModelPath = "Assets/3D Assets/Camel ypdate/Camel.fbx";
    private const string ControllerPath = "Assets/3D Assets/Camel ypdate/Camel.controller";
    private const string OutputFolder = "Assets/3D Assets/Camel ypdate/Riding";

    public const string GaitParameter = "RideGait";
    public const string MoveState = "RideMove";
    public const string BackState = "RideBack";
    public const string IdleState = "RideIdle";

    /// <summary>How much further each joint swings at a run than at a walk.</summary>
    private const float RunSwing = 1.45f;
    /// <summary>Body bounce at a run, metres (twice per cycle).</summary>
    private const float RunBounce = 0.09f;
    /// <summary>How far each joint swings while shuffling, relative to a walk.</summary>
    private const float ShuffleSwing = 0.5f;

    [MenuItem("Jannah/Animals/Build Camel Ride Animations")]
    public static void Build()
    {
        AnimationClip walk = LoadClip("WalkCycle");
        AnimationClip idle = LoadClip("Idle_01");
        if (walk == null || idle == null)
        {
            Debug.LogError("[CamelRideAnimationBuilder] WalkCycle / Idle_01 not found in " + ModelPath);
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder("Assets/3D Assets/Camel ypdate", "Riding");
        }

        Vector3 bounceAxis = FindBounceAxis(walk);

        AnimationClip run = SaveClip(Derive(walk, "Camel_RideRun", RunSwing, RunBounce, bounceAxis));
        AnimationClip shuffle = SaveClip(Derive(walk, "Camel_RideShuffle", ShuffleSwing, 0f, bounceAxis));

        WireController(walk, run, shuffle, idle);

        AssetDatabase.SaveAssets();
        Debug.Log("[CamelRideAnimationBuilder] Built Camel_RideRun and Camel_RideShuffle and wired " +
                  MoveState + " / " + BackState + " / " + IdleState + " into Camel.controller.");
    }

    private static AnimationClip LoadClip(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name);
    }

    // ─── Deriving gaits ─────────────────────────────────────────────────────────

    /// <summary>
    /// A copy of <paramref name="source"/> where every joint's rotation swings <paramref name="swing"/>
    /// times as far from its average pose, plus an optional two-beat vertical bounce of the whole body.
    /// Sampled every frame so the result loops exactly like the source.
    /// </summary>
    private static AnimationClip Derive(AnimationClip source, string name, float swing, float bounce, Vector3 bounceAxis)
    {
        var clip = new AnimationClip { name = name, frameRate = source.frameRate, legacy = false };

        float length = source.length;
        int frames = Mathf.Max(2, Mathf.RoundToInt(length * source.frameRate));
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(source);

        var byPath = bindings.GroupBy(b => b.path);
        foreach (var group in byPath)
        {
            string path = group.Key;
            var rot = new EditorCurveBinding[4];
            var others = new List<EditorCurveBinding>();

            foreach (EditorCurveBinding b in group)
            {
                switch (b.propertyName)
                {
                    case "m_LocalRotation.x": rot[0] = b; break;
                    case "m_LocalRotation.y": rot[1] = b; break;
                    case "m_LocalRotation.z": rot[2] = b; break;
                    case "m_LocalRotation.w": rot[3] = b; break;
                    default: others.Add(b); break;
                }
            }

            bool hasRotation = rot.All(b => b.propertyName != null);
            if (hasRotation) WriteSwungRotation(source, clip, rot, frames, length, swing);

            foreach (EditorCurveBinding b in others)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, b);

                // The body's bounce rides on the top joint under the rig root.
                if (bounce > 0f && path.EndsWith("/Root_01") && b.propertyName.StartsWith("m_LocalPosition."))
                {
                    int axis = b.propertyName[b.propertyName.Length - 1] - 'x';
                    curve = AddBounce(curve, frames, length, bounce * bounceAxis[axis]);
                }

                AnimationUtility.SetEditorCurve(clip, b, curve);
            }
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void WriteSwungRotation(AnimationClip source, AnimationClip clip, EditorCurveBinding[] rot,
                                           int frames, float length, float swing)
    {
        var curves = rot.Select(b => AnimationUtility.GetEditorCurve(source, b)).ToArray();

        var samples = new Quaternion[frames + 1];
        Vector4 sum = Vector4.zero;
        for (int i = 0; i <= frames; i++)
        {
            float t = length * i / frames;
            var q = new Quaternion(curves[0].Evaluate(t), curves[1].Evaluate(t), curves[2].Evaluate(t), curves[3].Evaluate(t));

            // Keep every sample on the same hemisphere so the average and the slerp take the short way.
            if (i > 0 && Quaternion.Dot(q, samples[0]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            samples[i] = q;
            sum += new Vector4(q.x, q.y, q.z, q.w);
        }

        Vector4 m = sum.normalized;
        var mean = new Quaternion(m.x, m.y, m.z, m.w);

        var outCurves = new AnimationCurve[4];
        for (int c = 0; c < 4; c++) outCurves[c] = new AnimationCurve();

        for (int i = 0; i <= frames; i++)
        {
            Quaternion q = Quaternion.SlerpUnclamped(mean, samples[i], swing);
            if (Quaternion.Dot(q, samples[i]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            float t = length * i / frames;
            outCurves[0].AddKey(t, q.x);
            outCurves[1].AddKey(t, q.y);
            outCurves[2].AddKey(t, q.z);
            outCurves[3].AddKey(t, q.w);
        }

        for (int c = 0; c < 4; c++)
        {
            SmoothAll(outCurves[c]);
            AnimationUtility.SetEditorCurve(clip, rot[c], outCurves[c]);
        }
    }

    private static AnimationCurve AddBounce(AnimationCurve curve, int frames, float length, float amount)
    {
        var result = new AnimationCurve();
        for (int i = 0; i <= frames; i++)
        {
            float t = length * i / frames;
            // Two lifts per stride, lowest as the weight lands.
            float lift = 0.5f - 0.5f * Mathf.Cos(t / length * Mathf.PI * 4f);
            result.AddKey(t, curve.Evaluate(t) + lift * amount);
        }
        SmoothAll(result);
        return result;
    }

    private static void SmoothAll(AnimationCurve curve)
    {
        for (int k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0f);
    }

    /// <summary>
    /// World up expressed in Root_01's parent space, read from the model at rest. The rig's root joint is
    /// flipped, so "up" for the bounce is not simply +Y in the curve.
    /// </summary>
    private static Vector3 FindBounceAxis(AnimationClip walk)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) return Vector3.up;

        GameObject instance = Object.Instantiate(model);
        try
        {
            Transform root = instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Root_01");
            if (root == null || root.parent == null) return Vector3.up;

            Vector3 up = root.parent.InverseTransformDirection(instance.transform.up);
            // Local position curves are in the parent's units; undo its scale too.
            Vector3 s = root.parent.lossyScale;
            return new Vector3(up.x / Mathf.Max(1e-4f, s.x), up.y / Mathf.Max(1e-4f, s.y), up.z / Mathf.Max(1e-4f, s.z))
                   * instance.transform.lossyScale.y;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static AnimationClip SaveClip(AnimationClip clip)
    {
        string path = OutputFolder + "/" + clip.name + ".anim";
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

    // ─── Controller ─────────────────────────────────────────────────────────────

    private static void WireController(AnimationClip walk, AnimationClip run, AnimationClip shuffle, AnimationClip idle)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[CamelRideAnimationBuilder] No controller at " + ControllerPath);
            return;
        }

        if (controller.parameters.All(p => p.name != GaitParameter))
        {
            controller.AddParameter(GaitParameter, AnimatorControllerParameterType.Float);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        foreach (string name in new[] { MoveState, BackState, IdleState })
        {
            ChildAnimatorState old = machine.states.FirstOrDefault(s => s.state.name == name);
            if (old.state != null) machine.RemoveState(old.state);
        }

        // Stale blend trees from earlier runs live as sub-assets of the controller; clear them out.
        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
        {
            if (sub is BlendTree tree && tree.name == MoveState) Object.DestroyImmediate(tree, true);
        }

        AnimatorState move = controller.CreateBlendTreeInController(MoveState, out BlendTree blend, 0);
        blend.name = MoveState;
        blend.blendType = BlendTreeType.Simple1D;
        blend.blendParameter = GaitParameter;
        blend.useAutomaticThresholds = false;
        blend.AddChild(shuffle, 0f);
        blend.AddChild(walk, 1f);
        blend.AddChild(run, 2f);
        move.writeDefaultValues = true;

        AnimatorState back = machine.AddState(BackState, new Vector3(520f, 110f, 0f));
        back.motion = walk;
        back.speed = -1f; // Walking backwards is the walk played in reverse.

        AnimatorState rideIdle = machine.AddState(IdleState, new Vector3(520f, 180f, 0f));
        rideIdle.motion = idle;

        EditorUtility.SetDirty(controller);
    }
}
