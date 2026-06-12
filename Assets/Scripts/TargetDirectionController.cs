using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class TargetDirectionController : MonoBehaviour
{
    public static TargetDirectionController Instance { get; private set; }
    
    public Transform arrowTransform;
    public LineRenderer pathLineRenderer;
    [Header("Minimap Path Settings")]
    public float pathYOffset = 20f;
    public string minimapLayerName = "Minimap";
    
    private Vector3 _targetPosition;
    private bool _isPointing = false;
    private NavMeshPath _path;

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
        
        if (pathLineRenderer != null)
        {
            pathLineRenderer.gameObject.SetActive(false);
            int minimapLayer = LayerMask.NameToLayer(minimapLayerName);
            if (minimapLayer != -1)
            {
                pathLineRenderer.gameObject.layer = minimapLayer;
            }
        }
        
        _path = new NavMeshPath();
    }

    public void PointTo(Vector3 targetPos, float duration = 10f)
    {
        _targetPosition = targetPos;
        _isPointing = true;
        
        if (arrowTransform != null)
            arrowTransform.gameObject.SetActive(true);
            
        if (pathLineRenderer != null)
            pathLineRenderer.gameObject.SetActive(true);

        StopAllCoroutines();
        StartCoroutine(HideArrowAfterSeconds(duration));
    }

    private IEnumerator HideArrowAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _isPointing = false;
        
        if (arrowTransform != null)
            arrowTransform.gameObject.SetActive(false);
            
        if (pathLineRenderer != null)
            pathLineRenderer.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_isPointing)
        {
            if (arrowTransform != null)
            {
                Vector3 direction = _targetPosition - transform.position;
                direction.y = 0f; // Keep rotation along Y axis only
                
                if (direction.sqrMagnitude > 0.001f)
                {
                    arrowTransform.rotation = Quaternion.LookRotation(direction);
                }
            }

            if (pathLineRenderer != null)
            {
                NavMesh.CalculatePath(transform.position, _targetPosition, NavMesh.AllAreas, _path);
                
                if (_path.corners.Length > 0)
                {
                    pathLineRenderer.positionCount = _path.corners.Length;
                    // Offset corners upward for minimap
                    for(int i = 0; i < _path.corners.Length; i++)
                    {
                        pathLineRenderer.SetPosition(i, _path.corners[i] + Vector3.up * pathYOffset);
                    }
                }
                else
                {
                    // Fallback to straight line if no navmesh path found
                    pathLineRenderer.positionCount = 2;
                    pathLineRenderer.SetPosition(0, transform.position + Vector3.up * pathYOffset);
                    pathLineRenderer.SetPosition(1, _targetPosition + Vector3.up * pathYOffset);
                }
            }
        }
    }
}
