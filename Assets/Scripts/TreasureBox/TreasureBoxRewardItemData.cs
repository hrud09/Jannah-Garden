using UnityEngine;

[CreateAssetMenu(fileName = "NewTreasureBoxRewardItem", menuName = "Jannah Garden/Treasure Box Reward Item", order = 20)]
public class TreasureBoxRewardItemData : ScriptableObject
{
    [Header("Item Metadata")]
    public Sprite itemIcon;

    [TextArea(1, 3)]
    public string itemName;

    [TextArea(3, 10)]
    public string itemDescription;

    [Header("Asset References")]
    public GameObject itemPrefab; // The real item prefab spawned after placement is confirmed
    [Tooltip("Lightweight ghost/preview prefab shown while the player is positioning the item. Falls back to itemPrefab if left empty.")]
    public GameObject itemPlacementModelPrefab; // Temporary preview shown during placement

    [Header("Item State")]
    public bool isUnlocked = false;

    [Header("Placement Settings")]
    public float placementTimerDuration = 360f; // Required time to fully place the item in the game world in seconds

    [Header("Puzzle Data")]
    public GameObject[] puzzlePieces;
}
