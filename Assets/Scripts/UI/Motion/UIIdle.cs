using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Idle "life" for UI that wants attention: a slow breath, a bob, a sway, a wiggle every few seconds,
/// and a sweep of light across the sprite. Effects can be combined, and switched on and off at
/// runtime with <see cref="Playing"/> (they ease in and out instead of snapping).
///
/// <para><b>Plays nicely with other motion.</b> Idle is applied as an offset on top of whatever pose
/// the element already has: each frame it removes what it added last frame, unless something else
/// (a press squash, a punch, a reveal) has written the transform since, in which case that new value
/// becomes the base. So a breathing button still squashes when pressed, and returns to breathing.</para>
///
/// <para>Stops completely with Reduce Motion (except the shimmer, which isn't motion), while the
/// object is disabled, and when <see cref="Playing"/> is false.</para>
/// </summary>
[DisallowMultipleComponent]
public class UIIdle : MonoBehaviour
{
    [Flags]
    public enum Effect
    {
        None = 0,
        Breathe = 1,
        Bob = 2,
        Sway = 4,
        Wiggle = 8,
        Shimmer = 16,
    }

    public Effect effects = Effect.Breathe;

    [Tooltip("The transform that moves. Defaults to this one.")]
    public Transform visual;

    [Tooltip("The graphic that shimmers. Defaults to the Image on this object.")]
    public Graphic shimmerGraphic;

    [Header("Breathe / Bob / Sway")]
    public float breathePeriod = 2.4f;
    [Tooltip("Scale change at the top of a breath (0.025 = 2.5%).")]
    public float breatheAmount = 0.025f;
    public float bobPeriod = 2.8f;
    public float bobPixels = 6f;
    public float swayPeriod = 3.4f;
    public float swayDegrees = 3f;

    [Header("Wiggle")]
    [Tooltip("Seconds between wiggles.")]
    public float wiggleInterval = 6f;
    public float wiggleDegrees = 9f;

    [Header("Shimmer")]
    public Color shimmerColor = new Color(1f, 0.97f, 0.85f, 0.55f);
    public float shimmerPeriod = 3.2f;

    /// <summary>Switches the effects on or off; they fade over a fraction of a second.</summary>
    public bool Playing
    {
        get => _playing;
        set => _playing = value;
    }

    [SerializeField] private bool _playing = true;

    private float _weight;          // 0..1 fade of every effect, so toggling never snaps
    private float _phase;           // de-syncs multiple idles so they don't breathe in lockstep
    private float _nextWiggle;
    private float _wiggleStart = -10f;

    private Vector3 _restScale = Vector3.one;
    private Vector3 _lastScale, _addedScaleFactor = Vector3.one;
    private Vector2 _lastPos, _addedPos;
    private float _lastRotZ, _addedRotZ;
    private bool _wroteLastFrame;

    private Material _shimmerMaterial;
    private Material _originalMaterial;
    private static Material _shimmerBase;

    private static readonly int ShimmerIntensityId = Shader.PropertyToID("_ShimmerIntensity");

    // ------------------------------------------------------------------ setup

    /// <summary>Adds (or fetches) an idle on <paramref name="go"/> with the given effects.</summary>
    public static UIIdle Ensure(GameObject go, Effect effects)
    {
        if (go == null) return null;
        if (!go.TryGetComponent(out UIIdle idle)) idle = go.AddComponent<UIIdle>();
        idle.SetEffects(effects);
        return idle;
    }

    public void SetEffects(Effect value)
    {
        if (effects == value && (_shimmerMaterial != null) == ((value & Effect.Shimmer) != 0)) return;
        effects = value;
        if (isActiveAndEnabled) ApplyShimmer();
    }

    private Transform Visual => visual != null ? visual : transform;

    private void OnEnable()
    {
        _phase = UnityEngine.Random.value * 10f;
        _nextWiggle = Time.unscaledTime + UnityEngine.Random.Range(1f, wiggleInterval);
        _wroteLastFrame = false;
        _weight = 0f;
        Vector3 s = Visual.localScale;
        _restScale = s == Vector3.zero ? Vector3.one : s;
        ApplyShimmer();
    }

    private void OnDisable()
    {
        RemoveOffsets();
        RemoveShimmer();
    }

    private void OnDestroy() => RemoveShimmer();

    // ------------------------------------------------------------------ motion

    private void LateUpdate()
    {
        bool motion = (effects & ~Effect.Shimmer) != 0 && !UIMotion.ReduceMotion;
        float target = _playing && motion ? 1f : 0f;
        _weight = Mathf.MoveTowards(_weight, target, Time.unscaledDeltaTime * 3f);

        if (_shimmerMaterial != null)
            _shimmerMaterial.SetFloat(ShimmerIntensityId, _playing ? 1f : 0f);

        if (_weight <= 0f && !_wroteLastFrame) return;

        Transform v = Visual;
        var rt = v as RectTransform;
        float now = Time.unscaledTime + _phase;

        // Remove last frame's offset, unless someone else has written the transform since.
        Vector3 baseScale = ResolveBaseScale(v);
        Vector2 basePos = rt != null
            ? (_wroteLastFrame && rt.anchoredPosition == _lastPos ? rt.anchoredPosition - _addedPos : rt.anchoredPosition)
            : Vector2.zero;
        float rotZ = v.localEulerAngles.z;
        float baseRot = _wroteLastFrame && Mathf.Approximately(rotZ, _lastRotZ) ? rotZ - _addedRotZ : rotZ;

        if (_weight <= 0f)
        {
            // Faded out: leave the clean base behind and stop touching the transform.
            v.localScale = baseScale;
            if (rt != null) rt.anchoredPosition = basePos;
            v.localRotation = Quaternion.Euler(0f, 0f, baseRot);
            _wroteLastFrame = false;
            return;
        }

        float scale = 1f;
        Vector2 pos = Vector2.zero;
        float rot = 0f;

        if ((effects & Effect.Breathe) != 0)
            scale += Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(0.1f, breathePeriod)) * breatheAmount;
        if ((effects & Effect.Bob) != 0)
            pos.y += Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(0.1f, bobPeriod)) * bobPixels;
        if ((effects & Effect.Sway) != 0)
            rot += Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(0.1f, swayPeriod)) * swayDegrees;
        if ((effects & Effect.Wiggle) != 0)
        {
            if (Time.unscaledTime >= _nextWiggle && _playing)
            {
                _wiggleStart = Time.unscaledTime;
                _nextWiggle = Time.unscaledTime + Mathf.Max(1f, wiggleInterval);
            }
            float w = Time.unscaledTime - _wiggleStart;
            if (w < 0.6f)
            {
                float decay = 1f - w / 0.6f;
                rot += Mathf.Sin(w * 38f) * wiggleDegrees * decay;
                scale += 0.06f * Mathf.Sin(Mathf.Clamp01(w / 0.6f) * Mathf.PI);
            }
        }

        scale = Mathf.Lerp(1f, scale, _weight);
        pos *= _weight;
        rot *= _weight;

        _addedScaleFactor = new Vector3(scale, scale, 1f);
        _addedPos = pos;
        _addedRotZ = rot;

        v.localScale = Vector3.Scale(baseScale, _addedScaleFactor);
        if (rt != null) rt.anchoredPosition = basePos + pos;
        v.localRotation = Quaternion.Euler(0f, 0f, baseRot + rot);

        _lastScale = v.localScale;
        _lastPos = rt != null ? rt.anchoredPosition : Vector2.zero;
        _lastRotZ = v.localEulerAngles.z;
        _wroteLastFrame = true;
    }

    /// <summary>
    /// The scale to breathe around. Our own last write: divide our factor back out. A tween running on
    /// the transform (press squash, punch, reveal): respect its value. Otherwise something has just
    /// written a resting value. Press and punch helpers record "rest" mid-breath, so a value close to
    /// the authored rest snaps back to it (no drift). A value well away from it is a deliberate resize
    /// and becomes the new rest.
    /// </summary>
    private Vector3 ResolveBaseScale(Transform v)
    {
        Vector3 current = v.localScale;
        if (_wroteLastFrame && current == _lastScale) return Divide(current, _addedScaleFactor);
        if (DOTween.IsTweening(v)) return current;

        float off = Mathf.Abs(current.x / Mathf.Max(0.0001f, _restScale.x) - 1f);
        if (off < 0.05f) return _restScale;   // a "rest" recorded mid-breath: snap to the true rest
        if (off < 0.15f) return current;      // a held state (a button kept pressed): leave it be
        _restScale = current;                 // a deliberate resize: breathe around the new size
        return current;
    }

    private void RemoveOffsets()
    {
        if (!_wroteLastFrame) return;
        Transform v = Visual;
        if (v == null) return;
        if (v.localScale == _lastScale) v.localScale = Divide(v.localScale, _addedScaleFactor);
        if (v is RectTransform rt && rt.anchoredPosition == _lastPos) rt.anchoredPosition -= _addedPos;
        if (Mathf.Approximately(v.localEulerAngles.z, _lastRotZ))
            v.localRotation = Quaternion.Euler(0f, 0f, v.localEulerAngles.z - _addedRotZ);
        _wroteLastFrame = false;
    }

    private static Vector3 Divide(Vector3 a, Vector3 b) =>
        new Vector3(a.x / Mathf.Max(0.0001f, b.x), a.y / Mathf.Max(0.0001f, b.y), a.z / Mathf.Max(0.0001f, b.z));

    // ------------------------------------------------------------------ shimmer

    private void ApplyShimmer()
    {
        bool want = (effects & Effect.Shimmer) != 0;
        if (!want)
        {
            RemoveShimmer();
            return;
        }
        if (_shimmerMaterial != null) return;

        Graphic g = shimmerGraphic != null ? shimmerGraphic : GetComponent<Graphic>();
        if (g == null) return;
        if (_shimmerBase == null) _shimmerBase = Resources.Load<Material>("UIShimmer");
        if (_shimmerBase == null || _shimmerBase.shader == null || !_shimmerBase.shader.isSupported) return;

        shimmerGraphic = g;
        _originalMaterial = g.material == g.defaultMaterial ? null : g.material;
        _shimmerMaterial = new Material(_shimmerBase) { name = "UIShimmer (instance)", hideFlags = HideFlags.DontSave };
        _shimmerMaterial.SetColor("_ShimmerColor", shimmerColor);
        _shimmerMaterial.SetFloat("_ShimmerPeriod", shimmerPeriod);
        _shimmerMaterial.SetFloat("_ShimmerOffset", UnityEngine.Random.value * shimmerPeriod);

        Vector4 uvRect = new Vector4(0f, 0f, 1f, 1f);
        if (g is Image img && img.sprite != null)
        {
            Vector4 outer = UnityEngine.Sprites.DataUtility.GetOuterUV(img.sprite); // xMin, yMin, xMax, yMax
            uvRect = new Vector4(outer.x, outer.y, outer.z - outer.x, outer.w - outer.y);
        }
        _shimmerMaterial.SetVector("_UVRect", uvRect);
        g.material = _shimmerMaterial;
    }

    private void RemoveShimmer()
    {
        if (_shimmerMaterial == null) return;
        if (shimmerGraphic != null && shimmerGraphic.material == _shimmerMaterial)
            shimmerGraphic.material = _originalMaterial;
        Destroy(_shimmerMaterial);
        _shimmerMaterial = null;
    }
}
