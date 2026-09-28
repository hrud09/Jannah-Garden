using UnityEngine;

/// <summary>
/// How the grey over locked ground looks.
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

    [Header("Hatching")]
    [Tooltip("The diagonal stripes drawn over the wash. Darker than the wash reads as cordoned-off; " +
             "lighter than it disappears against lit grass at any distance.")]
    public Color hatch = new Color(0.10f, 0.10f, 0.12f, 0.78f);

    [Tooltip("Metres from one stripe to the next. Wider stripes survive being seen from across the " +
             "garden; narrower ones read as texture rather than as a barrier.")]
    [Range(0.2f, 6f)]
    public float spacing = 1.3f;

    [Tooltip("Metres of stripe within each repeat. Half the spacing gives even bands; less leaves the " +
             "wash dominant with the stripes as an accent.")]
    [Range(0.05f, 3f)]
    public float width = 0.5f;

    private static readonly int FillId = Shader.PropertyToID("_Color");
    private static readonly int HatchId = Shader.PropertyToID("_HatchColor");
    private static readonly int SpacingId = Shader.PropertyToID("_HatchSpacing");
    private static readonly int WidthId = Shader.PropertyToID("_HatchWidth");

    /// <summary>
    /// Writes this style onto <paramref name="material"/>.
    ///
    /// <para>Fade and strength are deliberately not touched: those belong to the overlay's own state —
    /// which radius it is covering, whether a placement is open — and a style that quietly overwrote
    /// them would make the grey pop to full brightness the moment someone edited a colour.</para>
    /// </summary>
    public void ApplyTo(Material material)
    {
        if (material == null) return;

        material.SetColor(FillId, fill);
        material.SetColor(HatchId, hatch);
        material.SetFloat(SpacingId, Mathf.Max(0.01f, spacing));
        material.SetFloat(WidthId, Mathf.Max(0f, width));
    }
}
