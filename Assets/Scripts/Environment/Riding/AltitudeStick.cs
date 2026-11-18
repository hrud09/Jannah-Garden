using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Up/down control for flying mounts, styled as the movement joystick's partner: the same gold-rimmed
/// dial (with only its up and down arrows) and teal knob, sitting in the bottom-right corner as the
/// joystick's mirror image.
///
/// Drag the knob up to climb (or take off) and down to sink (or land); how far it is pushed sets how
/// hard. Letting go springs it back to the middle and holds the height. It feeds the same vertical
/// input Space / left Ctrl do on the keyboard (<see cref="IdyllicFantasyNature.PlayerMovement.SetInspectorVerticalInput"/>).
///
/// Shown only while riding something that can fly; <see cref="PlayersInteractionManager"/> toggles it.
/// </summary>
public class AltitudeStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Tooltip("The dial the knob moves inside. Defaults to this object's RectTransform.")]
    public RectTransform background;

    [Tooltip("The knob.")]
    public RectTransform handle;

    [Tooltip("How far the knob travels from the middle, as a fraction of the dial's half-height.")]
    [Range(0.2f, 1f)]
    public float handleRange = 0.62f;

    [Tooltip("Pushes smaller than this (fraction of full travel) count as no input.")]
    [Range(0f, 0.5f)]
    public float deadZone = 0.12f;

    [Tooltip("How quickly the knob springs back to the middle when let go (per second).")]
    public float returnSpeed = 14f;

    /// <summary>Current output, -1 (down) .. 1 (up).</summary>
    public float Value { get; private set; }

    private IdyllicFantasyNature.PlayerMovement _movement;
    private Camera _eventCamera;
    private bool _held;
    private float _knob; // -1..1, the knob's visual position.

    private void Awake()
    {
        if (background == null) background = (RectTransform)transform;
    }

    private void OnDisable()
    {
        Release();
        _knob = 0f;
        PlaceKnob();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _held = true;
        _eventCamera = eventData.pressEventCamera;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_held) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, _eventCamera,
                out Vector2 local))
        {
            return;
        }

        float travel = Mathf.Max(1f, background.rect.height * 0.5f * handleRange);
        // The rect's pivot may not be its centre; measure from the centre.
        float fromCentre = local.y - background.rect.center.y;
        _knob = Mathf.Clamp(fromCentre / travel, -1f, 1f);

        float magnitude = Mathf.InverseLerp(deadZone, 1f, Mathf.Abs(_knob));
        Value = Mathf.Sign(_knob) * magnitude;
        PlaceKnob();
        Push(Value);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Release();
    }

    private void Release()
    {
        if (!_held && Value == 0f) return;
        _held = false;
        Value = 0f;
        Push(0f);
    }

    private void Update()
    {
        if (_held)
        {
            // Re-send each frame: other code (Inspector Mode exiting, a hidden button) may zero the input.
            Push(Value);
            return;
        }

        if (Mathf.Abs(_knob) > 0.001f)
        {
            _knob = Mathf.MoveTowards(_knob, 0f, returnSpeed * Time.unscaledDeltaTime);
            PlaceKnob();
        }
    }

    private void PlaceKnob()
    {
        if (handle == null || background == null) return;
        float travel = background.rect.height * 0.5f * handleRange;
        handle.anchoredPosition = new Vector2(0f, _knob * travel);
    }

    private void Push(float value)
    {
        if (_movement == null) _movement = FindFirstObjectByType<IdyllicFantasyNature.PlayerMovement>();
        if (_movement != null) _movement.SetInspectorVerticalInput(value);
    }
}
