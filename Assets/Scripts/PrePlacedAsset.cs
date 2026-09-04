using UnityEngine;

/// <summary>
/// Marks scenery baked in by the Environment Generator as an example of a Shop item rather than
/// something the player owns. <see cref="PlayersInteractionManager"/> shows the same "manage" prompt
/// used for owned <see cref="PlaceableItem"/> instances, but tapping it points the player at the Shop
/// instead of opening the relocate/return panel, since there is nothing here for the player to move
/// or return.
/// </summary>
public class PrePlacedAsset : MonoBehaviour
{
    [Header("Renderers")]
    public Renderer[] itemRenderers;

    private bool isHighlighted = false;

    private void Awake()
    {
        if (itemRenderers == null || itemRenderers.Length == 0)
        {
            itemRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    /// <summary>
    /// Draws (or clears) the selection outline, so the player can see which decoration the "manage"
    /// prompt applies to. Mirrors <see cref="PlaceableItem.SetHighlight"/> — same OmniShade URP
    /// properties, same pass names.
    /// </summary>
    public void SetHighlight(bool enable)
    {
        if (isHighlighted == enable) return;
        isHighlighted = enable;

        if (itemRenderers == null) return;

        foreach (var renderer in itemRenderers)
        {
            if (renderer == null) continue;

            foreach (Material mat in renderer.materials)
            {
                if (mat == null || !mat.HasProperty("_Outline")) continue;

                mat.SetFloat("_Outline", enable ? 1f : 0f);

                // "SRPDefaultUnlit" is the outline pass under URP; "Always" under Built-in.
                string outlinePassName = mat.shader.name.Contains("URP") ? "SRPDefaultUnlit" : "Always";
                mat.SetShaderPassEnabled(outlinePassName, enable);

                if (enable)
                {
                    mat.EnableKeyword("OUTLINE");
                    mat.DisableKeyword("OUTLINE_PASS_DISABLED");
                }
                else
                {
                    mat.DisableKeyword("OUTLINE");
                    mat.EnableKeyword("OUTLINE_PASS_DISABLED");
                }
            }
        }
    }
}
