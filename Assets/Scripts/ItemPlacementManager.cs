using UnityEngine;
using UnityEngine.UI;

public class ItemPlacementManager : MonoBehaviour
{
    public RectTransform crosshairRect;
    public TerrainCollider terrainCollider;
    public Button placeButton;

    private GameObject currentPlacedObject;

    private void Start()
    {
        if (placeButton != null)
        {
            placeButton.onClick.AddListener(PlaceItem);
            placeButton.gameObject.SetActive(false); // Hide the place button by default
        }
    }

    private void Update()
    {
        if (currentPlacedObject != null)
        {
            UpdatePlacementPosition();
        }
    }

    /// <summary>
    /// Spawns the specified item prefab and prepares it for placement.
    /// </summary>
    public void StartPlacement(ShopItemData itemData)
    {
        if (itemData == null || itemData.itemPrefab == null) return;

        // If there's an existing object being previewed, destroy it
        if (currentPlacedObject != null)
        {
            Destroy(currentPlacedObject);
        }

        // Spawn the item preview
        currentPlacedObject = Instantiate(itemData.itemPrefab);

        // Position it initially
        UpdatePlacementPosition();

        // Activate the placement confirmation button
        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Projects a ray from the camera through the crosshair onto the terrain collider,
    /// updating the position of the preview object.
    /// </summary>
    private void UpdatePlacementPosition()
    {
        if (currentPlacedObject == null || terrainCollider == null || crosshairRect == null) return;

        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // Cast a ray from camera through crosshair screen space position
        Ray ray = mainCam.ScreenPointToRay(crosshairRect.position);
        RaycastHit hit;

        // Raycast specifically against the TerrainCollider
        if (terrainCollider.Raycast(ray, out hit, 1000f))
        {
            currentPlacedObject.transform.position = hit.point;
        }
    }

    /// <summary>
    /// Confirms placement of the current item, detaching it from the placement controls.
    /// </summary>
    public void PlaceItem()
    {
        if (currentPlacedObject == null) return;

        // Finalize placement at current position
        currentPlacedObject = null;

        // Deactivate placement confirmation button
        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(false);
        }
    }
}
