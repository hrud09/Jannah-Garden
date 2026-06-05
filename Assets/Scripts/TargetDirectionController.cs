using UnityEngine;
using System.Collections;

public class TargetDirectionController : MonoBehaviour
{
    public static TargetDirectionController Instance { get; private set; }
    
    public Transform arrowTransform;
    
    private Vector3 _targetPosition;
    private bool _isPointing = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (arrowTransform != null)
        {
            arrowTransform.gameObject.SetActive(false);
        }
    }

    public void PointTo(Vector3 targetPos, float duration = 10f)
    {
        if (arrowTransform == null) return;

        _targetPosition = targetPos;
        _isPointing = true;
        arrowTransform.gameObject.SetActive(true);

        StopAllCoroutines();
        StartCoroutine(HideArrowAfterSeconds(duration));
    }

    private IEnumerator HideArrowAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _isPointing = false;
        if (arrowTransform != null)
        {
            arrowTransform.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (_isPointing && arrowTransform != null)
        {
            Vector3 direction = _targetPosition - transform.position;
            direction.y = 0f; // Keep rotation along Y axis only
            
            if (direction.sqrMagnitude > 0.001f)
            {
                arrowTransform.rotation = Quaternion.LookRotation(direction);
            }
        }
    }
}
