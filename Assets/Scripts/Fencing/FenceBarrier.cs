using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// The ring of fence posts shown around an item while it grows (see
/// <see cref="ItemPlacementManager.SpawnFenceBarrierFor"/>). Spawned already positioned/rotated to match
/// the placed item; each post then drops onto the ground under its own animation.
/// </summary>
public class FenceBarrier : MonoBehaviour
{
    public Transform fenceHolderParent;
    public GameObject[] surroundingFences;

    [Tooltip("Rigidbody on each entry of surroundingFences, same order/length - kept kinematic while the " +
        "fence is standing and released (isKinematic = false) so physics throws it down for the despawn animation.")]
    public Rigidbody[] surroundingFenceRigidbodies;

    [Header("Drop-In Animation")]
    [Tooltip("How high above its resting spot each fence starts its fall from.")]
    public float dropHeight = 4f;
    [Tooltip("Seconds between one fence starting its fall and the next one starting - what makes them land one by one rather than all at once.")]
    public float dropStagger = 0.12f;
    [Tooltip("How long a single fence's fall takes, before its ground-impact squash.")]
    public float dropFallDuration = 0.3f;
    [Tooltip("How long the ground-impact squash/rebound takes once a fence lands.")]
    public float dropImpactDuration = 0.15f;
    [Tooltip("How hard a fence squashes on impact, as a fraction of its scale.")]
    public float dropImpactStrength = 0.3f;

    private Coroutine _dropRoutine;

    [Header("Despawn (Dramatic Fall) Animation")]
    [Tooltip("Seconds between one fence's rigidbody being released and the next one's - staggers the " +
        "collapse instead of the whole ring dropping in unison.")]
    public float despawnStagger = 0.08f;
    [Tooltip("Outward+upward impulse given to each fence the instant it's released, so the ring bursts apart " +
        "instead of just crumbling straight down.")]
    public float despawnImpulseStrength = 2f;
    [Tooltip("Random torque applied alongside the impulse so fences tumble end-over-end rather than falling flat.")]
    public float despawnTorqueStrength = 4f;
    [Tooltip("How long physics gets to play out, after the last fence is released, before the whole barrier is despawned back to the pool.")]
    public float despawnSettleDuration = 1.2f;

    private Coroutine _despawnRoutine;
    private Vector3[] _restLocalPositions;
    private Quaternion[] _restLocalRotations;

    public Transform[] timerSignBoardReferenceTransforms;

    private void Awake()
    {
        if (surroundingFences == null) return;

        _restLocalPositions = new Vector3[surroundingFences.Length];
        _restLocalRotations = new Quaternion[surroundingFences.Length];
        for (int i = 0; i < surroundingFences.Length; i++)
        {
            if (surroundingFences[i] == null) continue;
            _restLocalPositions[i] = surroundingFences[i].transform.localPosition;
            _restLocalRotations[i] = surroundingFences[i].transform.localRotation;
        }
    }

    /// <summary>
    /// The entry in <see cref="timerSignBoardReferenceTransforms"/> closest to the player, so the timer
    /// sign board faces whichever side of the fence the player is actually standing near.
    /// </summary>
    public Transform ClosestTimerSignBoardReferenceTransform
    {
        get
        {
            if (timerSignBoardReferenceTransforms == null || timerSignBoardReferenceTransforms.Length == 0)
            {
                return null;
            }

            Transform playerTransform = TargetDirectionController.Instance != null
                ? TargetDirectionController.Instance.transform
                : null;
            if (playerTransform == null)
            {
                return timerSignBoardReferenceTransforms[0];
            }

            Transform closest = null;
            float closestSqrDistance = float.MaxValue;
            foreach (Transform candidate in timerSignBoardReferenceTransforms)
            {
                if (candidate == null) continue;

                float sqrDistance = (candidate.position - playerTransform.position).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = candidate;
                }
            }

            return closest;
        }
    }

    private void OnDisable()
    {
        if (_dropRoutine != null)
        {
            StopCoroutine(_dropRoutine);
            _dropRoutine = null;
        }
        if (_despawnRoutine != null)
        {
            StopCoroutine(_despawnRoutine);
            _despawnRoutine = null;
        }

        // Reset for the next time this pooled instance is spawned - otherwise a fence left active (or
        // mid-tumble from the despawn animation) would skip its "activate one by one" reveal on reuse.
        if (surroundingFences == null) return;
        for (int i = 0; i < surroundingFences.Length; i++)
        {
            GameObject fence = surroundingFences[i];
            if (fence != null)
            {
                fence.SetActive(false);
                if (_restLocalPositions != null && i < _restLocalPositions.Length)
                {
                    fence.transform.localPosition = _restLocalPositions[i];
                    fence.transform.localRotation = _restLocalRotations[i];
                }
            }

            if (surroundingFenceRigidbodies != null && i < surroundingFenceRigidbodies.Length)
            {
                Rigidbody rb = surroundingFenceRigidbodies[i];
                if (rb == null) continue;

                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// Grounds every fence at its own XZ position, then plays them dropping into place one after another
    /// from <see cref="dropHeight"/> above - restarts cleanly if called again on a pooled instance.
    /// </summary>
    public void PlayDropInAnimation()
    {
        if (_dropRoutine != null) StopCoroutine(_dropRoutine);
        _dropRoutine = StartCoroutine(AnimateFencesDroppingIn());
    }

    private IEnumerator AnimateFencesDroppingIn()
    {
        if (surroundingFences == null) yield break;

        foreach (GameObject fence in surroundingFences)
        {
            if (fence == null) continue;

            Vector3 groundPos = fence.transform.position;
            groundPos.y = SampleGroundHeight(groundPos);

            // Fences sit deactivated by default so none of them are visible until it's their turn.
            fence.SetActive(true);
            StartCoroutine(AnimateSingleFenceDrop(fence.transform, groundPos));

            yield return new WaitForSeconds(dropStagger);
        }

        _dropRoutine = null;
    }

    /// <summary>
    /// Releases every fence's rigidbody (one after another, per <see cref="despawnStagger"/>) with an
    /// outward/upward impulse and some tumble, letting physics throw the ring apart instead of it just
    /// vanishing. Calls <paramref name="onComplete"/> once physics has had <see cref="despawnSettleDuration"/>
    /// to play out - the caller is expected to return this instance to the pool from that callback.
    /// </summary>
    public void PlayDespawnAnimation(Action onComplete)
    {
        if (_dropRoutine != null)
        {
            StopCoroutine(_dropRoutine);
            _dropRoutine = null;
        }
        if (_despawnRoutine != null) StopCoroutine(_despawnRoutine);
        _despawnRoutine = StartCoroutine(AnimateFencesFallingAway(onComplete));
    }

    private IEnumerator AnimateFencesFallingAway(Action onComplete)
    {
        if (surroundingFenceRigidbodies != null)
        {
            foreach (Rigidbody rb in surroundingFenceRigidbodies)
            {
                if (rb == null) continue;

                Vector3 outward = rb.transform.position - transform.position;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : UnityEngine.Random.insideUnitSphere;

                rb.isKinematic = false;
                rb.AddForce(outward * despawnImpulseStrength + Vector3.up * (despawnImpulseStrength * 0.5f), ForceMode.VelocityChange);
                rb.AddTorque(UnityEngine.Random.insideUnitSphere * despawnTorqueStrength, ForceMode.VelocityChange);

                yield return new WaitForSeconds(despawnStagger);
            }
        }

        yield return new WaitForSeconds(despawnSettleDuration);

        _despawnRoutine = null;
        onComplete?.Invoke();
    }

    /// <summary>
    /// Drops a single fence from <see cref="dropHeight"/> above <paramref name="groundPos"/>, easing in
    /// hard so it lingers up high then slams down, then squashes and rebounds on impact.
    /// </summary>
    private IEnumerator AnimateSingleFenceDrop(Transform fenceTransform, Vector3 groundPos)
    {
        Vector3 baseLocalScale = fenceTransform.localScale;

        float elapsed = 0f;
        while (elapsed < dropFallDuration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / dropFallDuration);

            // Eases in hard (u^3): lingers up high, then slams down at the end.
            float fallT = u * u * u;
            float height = Mathf.Lerp(dropHeight, 0f, fallT);

            fenceTransform.position = groundPos + Vector3.up * height;

            yield return null;
        }

        fenceTransform.position = groundPos;

        elapsed = 0f;
        while (elapsed < dropImpactDuration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / dropImpactDuration);
            float squash = Mathf.Sin(u * Mathf.PI) * dropImpactStrength * (1f - u);

            fenceTransform.localScale = new Vector3(
                baseLocalScale.x * (1f + squash),
                baseLocalScale.y * (1f - squash * 1.5f),
                baseLocalScale.z * (1f + squash));
            fenceTransform.position = groundPos;

            yield return null;
        }

        fenceTransform.localScale = baseLocalScale;
        fenceTransform.position = groundPos;
    }

    private static float SampleGroundHeight(Vector3 world)
    {
        if (GardenGrid.Instance != null && GardenGrid.Instance.IsReady)
        {
            return GardenGrid.Instance.SampleHeight(world);
        }

        Terrain terrain = Terrain.activeTerrain;
        return terrain != null ? terrain.SampleHeight(world) + terrain.transform.position.y : world.y;
    }
}
