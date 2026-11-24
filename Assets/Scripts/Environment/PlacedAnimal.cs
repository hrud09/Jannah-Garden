using UnityEngine;

/// <summary>
/// Bridges a shop-bought animal (camel, pegasus) and the garden placement system.
///
/// An animal shop prefab is a still root that carries the <see cref="PlaceableItem"/> — the spot the
/// player placed it, which is what gets saved, what claims grid cells, and what the distance streamer
/// tracks — with the living <see cref="GrazingAnimal"/> as a child that wanders around that spot and can
/// be ridden away from it. Keeping the two apart means a camel strolling off never drags its saved
/// position or its claimed cells with it.
///
/// The animal only comes to life once the item is really in the garden: on the placement ghost (whose
/// PlaceableItem is disabled) and while the arrival timer runs it stays put and can't be ridden.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlaceableItem))]
public class PlacedAnimal : MonoBehaviour
{
    [Tooltip("The animal child. Found automatically if left empty.")]
    public GrazingAnimal animal;

    private PlaceableItem _placeable;
    private bool _heldOutOfStreamer;

    private void Awake()
    {
        _placeable = GetComponent<PlaceableItem>();
        if (animal == null) animal = GetComponentInChildren<GrazingAnimal>(true);

        // Asleep until Sync decides otherwise, so a freshly spawned ghost never registers as a mount or
        // starts a stroll for the one frame before LateUpdate runs.
        if (animal != null) animal.enabled = false;
    }

    private void OnDisable()
    {
        // Switched off (despawned to the pool, relocated, streamed out): the next Sync after it comes back
        // sees the animal asleep and walks it home before waking it — a pooled instance never keeps its
        // animal wherever a rider last left it.
        if (animal != null) animal.enabled = false;
        _heldOutOfStreamer = false;
    }

    private void LateUpdate()
    {
        Sync();
        KeepRiddenAnimalStreamedIn();
    }

    /// <summary>Placed for real and finished arriving: the animal may wander and be ridden.</summary>
    private bool IsLive => _placeable != null && _placeable.enabled && _placeable.IsFullyPlaced;

    private void Sync()
    {
        if (animal == null) return;

        bool live = IsLive;
        if (animal.enabled == live) return;

        if (!live)
        {
            animal.enabled = false;
            ReturnHome();
        }
        else
        {
            ReturnHome();
            animal.enabled = true;
        }
    }

    /// <summary>Puts the animal back on its placed spot and makes that its home again.</summary>
    private void ReturnHome()
    {
        if (animal == null || animal.IsRidden) return;
        Transform t = animal.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        animal.ResetHome();
    }

    /// <summary>
    /// The distance streamer switches placed items off by the distance from the player to their root.
    /// A ridden animal travels with the player and can easily be carried (or flown) beyond that radius,
    /// which would switch the mount off from under its rider — so it is held out of the streamer for the
    /// ride. Handing it back re-syncs it at once: an animal left far from home is switched off, and walks
    /// home (via Sync) the next time the player comes near its spot.
    /// </summary>
    private void KeepRiddenAnimalStreamedIn()
    {
        RuntimeEnvironmentGenerator streamer = RuntimeEnvironmentGenerator.Instance;
        if (streamer == null || animal == null) return;

        bool ridden = animal.IsRidden;
        if (ridden && !_heldOutOfStreamer)
        {
            streamer.UnregisterObject(gameObject);
            _heldOutOfStreamer = true;
        }
        else if (!ridden && _heldOutOfStreamer)
        {
            _heldOutOfStreamer = false;
            streamer.RegisterObject(gameObject);
        }
    }
}
