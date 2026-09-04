using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The example-garden dressing layout, baked out of the Jannah Garden scene by
/// PrePlacedLayoutBaker (Tools > Environment > Bake Pre-Placed Layout) so the heavy Shop Item
/// prefabs no longer ship inside the player build. Each entry records which Remote-group prefab
/// stood where; <see cref="PrePlacedAssetSpawner"/> re-creates the instances at runtime through
/// <see cref="AddressableItemLoader"/>, which downloads the same bundles the Shop itself uses.
/// </summary>
public class PrePlacedLayoutData : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        [Tooltip("Addressables asset GUID of the placed Shop Item prefab (the Remote group is keyed by GUID).")]
        public string assetGuid;

        [Tooltip("World position the baked instance stood at.")]
        public Vector3 position;

        [Tooltip("World rotation of the baked instance.")]
        public Quaternion rotation;

        [Tooltip("World scale of the baked instance (includes the generator's scale jitter).")]
        public Vector3 scale;
    }

    public List<Entry> entries = new List<Entry>();
}
