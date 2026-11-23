using System;
using UnityEngine;

/// <summary>
/// Marks a UI element that samples <c>_CameraOpaqueTexture</c> (the UI_Blur backdrop). While at least
/// one of these is active, <see cref="MobileEnvironmentOptimizer"/> lets the main camera produce the
/// opaque-colour copy; the rest of the time the copy is skipped.
///
/// The copy is a full-screen blit that also splits the frame into two render passes, which on a
/// tile-based phone GPU costs a resolve + reload of the whole colour buffer every frame - and the
/// only things in the game that read it are these occasional blurred panels.
/// </summary>
[DisallowMultipleComponent]
public class OpaqueTextureRequest : MonoBehaviour
{
    public static int ActiveCount { get; private set; }

    /// <summary>Raised whenever <see cref="ActiveCount"/> changes.</summary>
    public static event Action Changed;

    private void OnEnable()
    {
        ActiveCount++;
        Changed?.Invoke();
    }

    private void OnDisable()
    {
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
        Changed?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveCount = 0;
        Changed = null;
    }
}
