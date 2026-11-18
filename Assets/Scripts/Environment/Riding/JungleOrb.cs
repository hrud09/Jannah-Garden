using UnityEngine;

/// <summary>
/// One Orb Run orb: bobs and turns gently so it reads as something to collect, and shrinks away when
/// collected.
/// </summary>
public class JungleOrb : MonoBehaviour
{
    public float bobHeight = 0.25f;
    public float bobSpeed = 1.6f;
    public float spinSpeed = 40f;

    private Vector3 _base;
    private float _phase;
    private bool _collected;
    private float _shrink = 1f;
    private Vector3 _scale;

    private void Start()
    {
        _base = transform.position;
        _phase = Random.value * Mathf.PI * 2f;
        _scale = transform.localScale;
    }

    public void Collect()
    {
        _collected = true;
    }

    private void Update()
    {
        if (_collected)
        {
            // Pops up and shrinks into the rider.
            _shrink -= Time.deltaTime * 3.5f;
            transform.position += Vector3.up * (Time.deltaTime * 2.5f);
            transform.localScale = _scale * Mathf.Max(0f, _shrink) * (1f + (1f - _shrink) * 0.6f);
            if (_shrink <= 0f) Destroy(gameObject);
            return;
        }

        transform.position = _base + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight);
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }
}
