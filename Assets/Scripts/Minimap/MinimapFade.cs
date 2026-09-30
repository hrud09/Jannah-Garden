using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Feeds the UI/MinimapFade shader on the minimap's <see cref="RawImage"/> so the map dissolves
/// instead of ending in hard edges: everything past the playable world melts into a single fog
/// colour, and the square render texture melts into the frame at the panel border.
///
/// The minimap camera turns with the player, so the world rect it covers is passed to the shader
/// as rotated axes rather than an axis-aligned rectangle.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MinimapFade : MonoBehaviour
{
    private static readonly int MapOriginId = Shader.PropertyToID("_MapOrigin");
    private static readonly int MapRightId = Shader.PropertyToID("_MapRight");
    private static readonly int MapUpId = Shader.PropertyToID("_MapUp");
    private static readonly int WorldBoundsId = Shader.PropertyToID("_WorldBounds");

    [Header("References")]
    [Tooltip("The camera that renders the minimap. Falls back to the MinimapBehaviour in the parents.")]
    [SerializeField] private Camera minimapCamera;
    [Tooltip("Material using the UI/MinimapFade shader. A per-instance copy is made at runtime.")]
    [SerializeField] private Material fadeMaterial;

    [Header("World Bounds")]
    [Tooltip("Read the playable bounds from the active terrain instead of the values below.")]
    [SerializeField] private bool useTerrainBounds = true;
    [Tooltip("World-space X/Z minimum of the playable area. Used when 'Use Terrain Bounds' is off.")]
    [SerializeField] private Vector2 worldMin = new Vector2(-100f, -100f);
    [Tooltip("World-space X/Z maximum of the playable area. Used when 'Use Terrain Bounds' is off.")]
    [SerializeField] private Vector2 worldMax = new Vector2(100f, 100f);

    [Header("Camera Clear Colour")]
    [Tooltip("Match the minimap camera's background to the shader's outside colour so the two never seam.")]
    [SerializeField] private bool matchCameraBackground = true;

    private RawImage rawImage;
    private Material runtimeMaterial;
    private Vector4 bounds;

    private void Awake()
    {
        rawImage = GetComponent<RawImage>();

        Material source = fadeMaterial != null ? fadeMaterial : rawImage.material;
        if (source == null || source.shader == null || !source.shader.name.Contains("MinimapFade"))
        {
            Debug.LogWarning("MinimapFade: no UI/MinimapFade material assigned — the fade is disabled.", this);
            enabled = false;
            return;
        }

        runtimeMaterial = new Material(source) { name = source.name + " (Instance)" };
        rawImage.material = runtimeMaterial;
    }

    private void Start()
    {
        RefreshBounds();
        ResolveCamera();
    }

    private void LateUpdate()
    {
        if (minimapCamera == null)
        {
            ResolveCamera();
            if (minimapCamera == null) return;
        }

        Transform camTransform = minimapCamera.transform;

        // The minimap camera is orthographic and points straight down, so its right/up axes lie in
        // the world XZ plane and define the rect the render texture covers.
        float halfHeight = minimapCamera.orthographicSize;
        float halfWidth = halfHeight * minimapCamera.aspect;

        Vector3 right = camTransform.right;
        Vector3 up = camTransform.up;
        Vector3 position = camTransform.position;

        runtimeMaterial.SetVector(MapOriginId, new Vector4(position.x, position.z, 0f, 0f));
        runtimeMaterial.SetVector(MapRightId, new Vector4(right.x * halfWidth * 2f, right.z * halfWidth * 2f, 0f, 0f));
        runtimeMaterial.SetVector(MapUpId, new Vector4(up.x * halfHeight * 2f, up.z * halfHeight * 2f, 0f, 0f));
        runtimeMaterial.SetVector(WorldBoundsId, bounds);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    /// <summary>
    /// Re-reads the playable bounds. Call this after streaming in or resizing the terrain.
    /// </summary>
    public void RefreshBounds()
    {
        Vector2 min = worldMin;
        Vector2 max = worldMax;

        if (useTerrainBounds)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                min = new Vector2(origin.x, origin.z);
                max = new Vector2(origin.x + size.x, origin.z + size.z);
            }
        }

        bounds = new Vector4(min.x, min.y, max.x, max.y);
    }

    private void ResolveCamera()
    {
        if (minimapCamera != null) return;

        MinimapBehaviour behaviour = GetComponentInParent<MinimapBehaviour>();
        if (behaviour != null)
        {
            minimapCamera = behaviour.MinimapCamera;
        }

        // The shader already paints everything past the world bounds, but matching the clear
        // colour means a narrow fade distance can never reveal a seam of the old flat grey.
        if (matchCameraBackground && minimapCamera != null)
        {
            minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            minimapCamera.backgroundColor = runtimeMaterial.GetColor("_OutsideColor");
        }
    }
}
