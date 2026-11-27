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
        _nextPathSolve = 0f; // new target: solve on the next frame
        
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
                // A path solve per frame is wasted work at walking pace: re-solve ~5x a second, or as soon as
                // the player has moved a metre, and read the corners into a reused buffer (path.corners
                // allocates a fresh array on every read).
                Vector3 here = transform.position;
                if (Time.time >= _nextPathSolve || (here - _lastSolveFrom).sqrMagnitude > 1f)
                {
                    _nextPathSolve = Time.time + 0.2f;
                    _lastSolveFrom = here;

                    NavMesh.CalculatePath(here, _targetPosition, NavMesh.AllAreas, _path);
                    int n = _path.GetCornersNonAlloc(_corners);
                    if (n == _corners.Length)
                    {
                        _corners = new Vector3[_corners.Length * 4];
                        n = _path.GetCornersNonAlloc(_corners);
                    }

                    if (n > 0)
                    {
                        pathLineRenderer.positionCount = n;
                        for (int i = 0; i < n; i++) pathLineRenderer.SetPosition(i, _corners[i] + Vector3.up * pathYOffset);
                    }
                    else
                    {
                        // Fallback to straight line if no navmesh path found
                        pathLineRenderer.positionCount = 2;
                        pathLineRenderer.SetPosition(0, here + Vector3.up * pathYOffset);
                        pathLineRenderer.SetPosition(1, _targetPosition + Vector3.up * pathYOffset);
                    }
                }
            }
        }
    }

    private Vector3[] _corners = new Vector3[64];
    private float _nextPathSolve;
    private Vector3 _lastSolveFrom = new Vector3(float.MaxValue, 0f, 0f);
}
