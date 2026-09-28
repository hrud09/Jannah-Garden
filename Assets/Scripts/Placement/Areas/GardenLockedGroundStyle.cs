using UnityEngine;

/// <summary>
/// How one of the two ground overlays looks — the grey over locked ground, or the green over the
/// ground the player has unlocked. Two assets of this same type, because the two answer the same
/// question in opposite directions and there is nothing about their look that differs in kind: a
/// wash, a lattice, a rim and a pulse, authored to say "not yours yet" or "build here".
///
/// <para>Its own asset rather than fields on the overlay, because "what does not-yours-yet look like"
/// is an art decision and the overlay is a piece of machinery. Pulling it out means the look can be
/// tried, swapped and kept without touching the scene: make a second asset, drag it in, compare, keep
/// the one that reads better. The values reach the material directly, so a change shows up while the
/// game is running rather than on the next entry into play mode.</para>
///
/// <para>Create with <c>Assets ▸ Create ▸ Jannah Garden ▸ Locked Ground Style</c>. Leaving the style
/// empty on the overlay is fine — the material's own values are used, which is what shipped before
/// styles existed.</para>
/// </summary>
[CreateAssetMenu(fileName = "LockedGroundStyle", menuName = "Jannah Garden/Locked Ground Style")]
public class GardenLockedGroundStyle : ScriptableObject
{
    [Header("Wash")]
    [Tooltip("The flat tint laid over locked ground. Its alpha is how much of the garden's own colour " +
             "still reads through; at 1 the ground underneath disappears entirely, which reads as a " +
             "hole in the world rather than as ground somebody else's.")]
    public Color fill = new Color(0.32f, 0.32f, 0.35f, 0.72f);

    [Header("Grid")]
    [Tooltip("The lattice drawn over the wash, on the same cells the placement grid uses so the lines " +
             "carry straight across the boundary between unlocked and locked ground. Darker than the " +
             "wash reads as surveyed ground; lighter than it disappears against lit grass at any " +
             "distance. Its alpha is how strongly a line sits over the wash.")]
    public Color gridLine = new Color(0.10f, 0.10f, 0.12f, 0.78f);

    [Tooltip("Every Nth line, drawn heavier so the lattice has a readable rhythm instead of dissolving " +
             "into an even mesh when seen from across the garden.")]
    public Color gridMajorLine = new Color(0.06f, 0.06f, 0.08f, 0.92f);

    [Tooltip("How many cells apart the heavier lines fall. Matches the placement grid's own major " +
             "spacing so the two lattices stay in step.")]
    [Range(1f, 16f)]
    public float majorEvery = 4f;

    [Tooltip("Line thickness in pixels, not metres: a line that holds its screen width stays legible " +
             "underfoot and fades to an even tone in the distance rather than aliasing.")]
    [Range(0.25f, 6f)]
    public float lineWidth = 1.1f;

    [Tooltip("Thickness of the heavier lines, in pixels.")]
    [Range(0.25f, 8f)]
    public float majorLineWidth = 1.8f;

    [Header("Rim")]
    [Tooltip("The bright border traced around the outside of the covered ground. This is what makes " +
             "one area read as a place with an edge rather than as a tint that happens to stop.")]
    public Color rim = new Color(0.72f, 1f, 0.6f, 1f);

    [Tooltip("How strongly the border reads. 0 turns it off entirely, which is what the grey over " +
             "locked ground wants: the locked half should recede, not be outlined.")]
    [Range(0f, 3f)]
    public float rimStrength;

    [Tooltip("Width of the border as a fraction of one grid cell. The mesh carries a corner-to-corner " +
             "gradient, so anything above about 0.8 spreads the glow across the whole boundary cell.")]
    [Range(0.05f, 1f)]
    public float rimWidth = 0.55f;

    [Header("Pulse")]
    [Tooltip("How much the overlay breathes, as a fraction of its own alpha. 0 holds still. A little " +
             "of it says the ground is being offered right now; a lot of it is a strobe.")]
    [Range(0f, 1f)]
    public float pulse;

    [Tooltip("Breaths per second. Slow reads as alive; fast reads as an error state.")]
    [Range(0.05f, 3f)]
    public float pulseSpeed = 0.6f;

    private static readonly int FillId = Shader.PropertyToID("_Color");
    private static readonly int LineId = Shader.PropertyToID("_LineColor");
    private static readonly int MajorLineId = Shader.PropertyToID("_MajorLineColor");
    private static readonly int MajorEveryId = Shader.PropertyToID("_MajorEvery");
    private static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
    private static readonly int MajorLineWidthId = Shader.PropertyToID("_MajorLineWidth");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
    private static readonly int RimWidthId = Shader.PropertyToID("_RimWidth");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");

    /// <summary>
    /// Writes this style onto <paramref name="material"/>.
    ///
    /// <para>Cell size and grid origin are deliberately not touched: they come from the real
    /// <see cref="GardenGrid"/> through <c>PlacementGridView</c>, because the lattice is only worth
    /// drawing if it sits on the cells the game actually snaps to.</para>
    ///
    /// <para>Fade and strength are deliberately not touched either: those belong to the overlay's own state —
    /// which radius it is covering, whether a placement is open — and a style that quietly overwrote
    /// them would make the grey pop to full brightness the moment someone edited a colour.</para>
    /// </summary>
    public void ApplyTo(Material material)
    {
        if (material == null) return;

        material.SetColor(FillId, fill);
        material.SetColor(LineId, gridLine);
        material.SetColor(MajorLineId, gridMajorLine);
        material.SetFloat(MajorEveryId, Mathf.Max(1f, majorEvery));
        material.SetFloat(LineWidthId, Mathf.Max(0.01f, lineWidth));
        material.SetFloat(MajorLineWidthId, Mathf.Max(0.01f, majorLineWidth));

        material.SetColor(RimColorId, rim);
        material.SetFloat(RimStrengthId, Mathf.Max(0f, rimStrength));
        material.SetFloat(RimWidthId, Mathf.Clamp(rimWidth, 0.05f, 1f));
        material.SetFloat(PulseId, Mathf.Clamp01(pulse));
        material.SetFloat(PulseSpeedId, Mathf.Max(0.01f, pulseSpeed));
    }
}
