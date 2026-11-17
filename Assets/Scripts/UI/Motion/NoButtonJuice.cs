using UnityEngine;

/// <summary>
/// Opts this button, and every button under it, out of <see cref="UIButtonJuice"/> — for controls
/// whose scale is part of their function (joystick, slider handles) or that animate themselves.
/// </summary>
[DisallowMultipleComponent]
public class NoButtonJuice : MonoBehaviour { }
