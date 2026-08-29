using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Runtime replacement for the scene-baked "Environment Generated" dressing. Reads the layout
/// recorded by PrePlacedLayoutBaker and spawns each Shop Item prefab through
/// <see cref="AddressableItemLoader"/>, so the example decorations download from the Remote
/// Addressables group on demand instead of being serialized into the player build (which was
/// duplicating every placed item's meshes/textures into sharedassets on top of its remote bundle).
///
/// Distinct prefabs are downloaded and spawned one at a time (not all fired off together in
/// Start()) — with ~30+ distinct Remote bundles here, requesting them all at once saturated
/// bandwidth/CPU and then dumped several heavy (multi-million-triangle) Instantiate calls into
/// the same frame as their downloads landed together, which is what read as the game "hanging"
/// after the garden loaded. Going one prefab at a time trades total reveal time for a smooth,
/// visible stream of decorations popping in instead of one big stall.
///
/// Spawned instances are normalized the same way EnvironmentGeneratorWindow normalized the baked
/// ones: <see cref="PlaceableItem"/>'s Awake is allowed to run (it adds a collider and lifts the
/// GFX onto the ground plane), then the component is stripped — dressing is not a player-owned
/// economy item — and a <see cref="PrePlacedAsset"/> marker takes its place so the interaction
/// system points the player at the Shop.
///
/// A failed download (offline first run, catalog/bundle mismatch) just leaves that decoration out;
/// AddressableItemLoader already logs the error and the garden stays playable.
/// </summary>
public class PrePlacedAssetSpawner : MonoBehaviour
{
    private const string RootName = "Environment Generated (Runtime)";

    [Tooltip("Layout baked from the scene by Tools > Environment > Bake Pre-Placed Layout.")]
    public PrePlacedLayoutData layout;

    private Transform _root;

    private void Start()
    {
        if (layout == null || layout.entries == null || layout.entries.Count == 0)
        {
            Debug.LogWarning("[PrePlacedAssetSpawner] No layout assigned — no pre-placed dressing will spawn.");
            return;
        }

        _root = new GameObject(RootName).transform;
        _root.SetParent(transform, false);

        // One entry list per distinct prefab; every instance of that prefab spawns from the
        // single cached template once its bundle is ready (AddressableItemLoader coalesces and
        // caches by GUID, so this also shares the download with the Shop preview/placement flow).
        var entriesByGuid = new Dictionary<string, List<PrePlacedLayoutData.Entry>>();
        foreach (PrePlacedLayoutData.Entry entry in layout.entries)
        {
            if (string.IsNullOrEmpty(entry.assetGuid)) continue;

            if (!entriesByGuid.TryGetValue(entry.assetGuid, out List<PrePlacedLayoutData.Entry> list))
            {
                list = new List<PrePlacedLayoutData.Entry>();
                entriesByGuid[entry.assetGuid] = list;
            }
            list.Add(entry);
        }

        StartCoroutine(SpawnAllRoutine(entriesByGuid));
    }

    private IEnumerator SpawnAllRoutine(Dictionary<string, List<PrePlacedLayoutData.Entry>> entriesByGuid)
    {
        foreach (KeyValuePair<string, List<PrePlacedLayoutData.Entry>> group in entriesByGuid)
        {
            var reference = new AssetReferenceGameObject(group.Key);

            GameObject loadedPrefab = null;
            bool loadDone = false;
            AddressableItemLoader.LoadAsync(reference, prefab =>
            {
                loadedPrefab = prefab;
                loadDone = true;
            });

            while (!loadDone) yield return null;

            // Loader already logged the failure; skip this prefab's placements.
            if (loadedPrefab == null || _root == null) continue;

            foreach (PrePlacedLayoutData.Entry entry in group.Value)
            {
                SpawnOne(loadedPrefab, entry);
                // Spread multiple instances of the same prefab across frames too, so a decoration
                // used many times over doesn't reintroduce a one-frame Instantiate burst.
                yield return null;
            }
        }
    }

    private void SpawnOne(GameObject prefab, PrePlacedLayoutData.Entry entry)
    {
        GameObject go = Instantiate(prefab, entry.position, entry.rotation, _root);
        go.transform.localScale = entry.scale;

        PlaceableItem placeable = go.GetComponent<PlaceableItem>();
        if (placeable != null)
        {
            // Its Awake already ran during Instantiate and grounded the GFX; the floating timer UI
            // is economy-item furniture the dressing doesn't need.
            Transform timerArea = go.transform.Find("Timer Area");
            if (timerArea != null) Destroy(timerArea.gameObject);

            // Immediate rather than deferred so PlaceableItem.Start (timer/save tracking) can never
            // run on a decoration. Its OnDestroy also unregisters the object from
            // RuntimeEnvironmentGenerator, which OnEnable may have registered it with.
            DestroyImmediate(placeable);
        }

        // RuntimeEnvironmentGenerator's registration sync can deactivate a far-away object during
        // the component's OnEnable, and the strip above then orphans that state — dressing manages
        // no timers, so it simply stays active like the baked instances did.
        if (!go.activeSelf) go.SetActive(true);

        if (go.GetComponent<PrePlacedAsset>() == null)
        {
            go.AddComponent<PrePlacedAsset>();
        }
    }
}
