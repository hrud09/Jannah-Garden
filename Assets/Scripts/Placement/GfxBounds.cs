using UnityEngine;

/// <summary>
/// Measures a model's physical extent in the space of its own root.
///
/// Two callers need this and need it to agree: <see cref="PlaceableItem.MeasureGfxGroundOffset"/> wants
/// the lowest point so it can plant the model on the ground, and <see cref="ItemFootprint"/> wants the
/// full box so it knows how many cells to claim. Measuring them separately would eventually let a model
/// sit on the ground by one definition and claim cells by another.
///
/// Colliders are read from their local definitions rather than <see cref="Collider.bounds"/>, which is a
/// world-space AABB and would inflate the moment the root is rotated at placement time.
///
/// Works on prefab assets as well as live instances — nothing here needs Awake to have run.
/// </summary>
public static class GfxBounds
{
    /// <summary>
    /// Bounds of <paramref name="content"/>'s collision geometry expressed in <paramref name="root"/>'s
    /// local space, at whatever scale the hierarchy currently carries.
    /// </summary>
    /// <param name="useRenderersIfNoCollider">
    /// Fall back to mesh bounds when the model has no non-trigger colliders. Trigger volumes are always
    /// skipped — they are gameplay reach, not the model's body, and measuring them would both lift the
    /// item off the ground and make it claim cells it does not visually occupy.
    /// </param>
    public static bool TryGetLocalBounds(Transform root, Transform content, bool useRenderersIfNoCollider, out Bounds bounds)
    {
        bounds = default;
        if (root == null || content == null) return false;

        bool any = false;
        Matrix4x4 toRoot = root.worldToLocalMatrix;

        foreach (Collider col in content.GetComponentsInChildren<Collider>(true))
        {
            if (col.isTrigger) continue;
            if (!TryGetColliderLocalBounds(col, out Bounds local)) continue;
            Accumulate(local, toRoot * col.transform.localToWorldMatrix, ref bounds, ref any);
        }

        if (!any && useRenderersIfNoCollider)
        {
            foreach (MeshFilter mf in content.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.TryGetComponent<Renderer>(out _)) continue;
                Accumulate(mf.sharedMesh.bounds, toRoot * mf.transform.localToWorldMatrix, ref bounds, ref any);
            }

            foreach (SkinnedMeshRenderer sm in content.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sm.sharedMesh == null) continue;
                Accumulate(sm.sharedMesh.bounds, toRoot * sm.transform.localToWorldMatrix, ref bounds, ref any);
            }
        }

        return any;
    }

    /// <summary>Collider geometry expressed in its own transform's local space.</summary>
    public static bool TryGetColliderLocalBounds(Collider col, out Bounds local)
    {
        switch (col)
        {
            case MeshCollider mc when mc.sharedMesh != null:
                local = mc.sharedMesh.bounds;
                return true;

            case BoxCollider bc:
                local = new Bounds(bc.center, bc.size);
                return true;

            case SphereCollider sc:
                local = new Bounds(sc.center, Vector3.one * (sc.radius * 2f));
                return true;

            case CapsuleCollider cc:
                Vector3 size = Vector3.one * (cc.radius * 2f);
                // direction: 0 = X, 1 = Y, 2 = Z
                if (cc.direction == 0) size.x = Mathf.Max(cc.height, cc.radius * 2f);
                else if (cc.direction == 1) size.y = Mathf.Max(cc.height, cc.radius * 2f);
                else size.z = Mathf.Max(cc.height, cc.radius * 2f);
                local = new Bounds(cc.center, size);
                return true;

            default:
                local = default;
                return false;
        }
    }

    /// <summary>Folds all 8 transformed corners of <paramref name="local"/> into <paramref name="running"/>.</summary>
    private static void Accumulate(Bounds local, Matrix4x4 m, ref Bounds running, ref bool any)
    {
        Vector3 c = local.center, e = local.extents;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                c.x + (((i & 1) == 0) ? -e.x : e.x),
                c.y + (((i & 2) == 0) ? -e.y : e.y),
                c.z + (((i & 4) == 0) ? -e.z : e.z));

            Vector3 p = m.MultiplyPoint3x4(corner);

            if (!any)
            {
                running = new Bounds(p, Vector3.zero);
                any = true;
            }
            else
            {
                running.Encapsulate(p);
            }
        }
    }
}
