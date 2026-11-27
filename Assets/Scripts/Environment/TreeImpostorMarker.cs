using UnityEngine;

/// <summary>
/// Sits on the "__Impostor" child that <c>TreeImpostorBaker</c> adds to a tree. It holds what the baker
/// changed so the impostor can be removed again exactly, and lets the mobile optimizer find impostor
/// LOD groups. It has no Unity messages, so it costs nothing at runtime.
/// </summary>
[DisallowMultipleComponent]
public sealed class TreeImpostorMarker : MonoBehaviour
{
    [Tooltip("Screen-relative transition height the tree's last mesh LOD had before the impostor was appended.")]
    public float originalLastThreshold;

    [Tooltip("Fade mode the LODGroup had before baking.")]
    public LODFadeMode originalFadeMode;
}
