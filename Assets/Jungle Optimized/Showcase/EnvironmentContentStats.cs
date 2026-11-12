using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Counts what a set of environment hierarchies contains (objects, triangles, shadow casters, lights,
/// mesh memory...). Pure runtime API, so it works in the editor, in Play mode and on a device.
/// Used by <see cref="OptimizationShowcase"/> to compare the original and the optimized jungle.
/// </summary>
[System.Serializable]
public struct EnvironmentContentStats
{
    public int gameObjects;
    public int renderers;          // every renderer (all LOD levels)
    public int lodGroups;
    public int drawItems;          // renderers x sub-meshes at the closest LOD = draw calls if everything were on screen
    public long triangles;         // closest-LOD triangles if everything were on screen
    public long vertices;
    public int shadowCasters;      // closest-LOD renderers that cast shadows
    public int lights;             // enabled point/spot lights
    public int particleSystems;
    public long maxParticles;
    public int uniqueMeshes;
    public long meshBytes;         // estimated GPU vertex + index buffer size of the unique meshes
    public int uniqueMaterials;
    public int uniqueShaders;

    public static EnvironmentContentStats Analyze(IEnumerable<GameObject> roots)
    {
        var s = new EnvironmentContentStats();
        var meshes = new List<Mesh>();
        var meshSet = new HashSet<int>();
        var materials = new HashSet<int>();
        var shaders = new HashSet<int>();
        var inLodGroup = new HashSet<int>();
        var closest = new List<Renderer>();

        foreach (GameObject root in roots)
        {
            if (root == null) continue;

            s.gameObjects += root.GetComponentsInChildren<Transform>(true).Length;

            foreach (LODGroup lg in root.GetComponentsInChildren<LODGroup>(true))
            {
                s.lodGroups++;
                LOD[] lods = lg.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    if (lods[i].renderers == null) continue;
                    foreach (Renderer r in lods[i].renderers)
                    {
                        if (r == null) continue;
                        inLodGroup.Add(r.GetInstanceID());
                    }
                }
                // Closest level that actually has renderers.
                for (int i = 0; i < lods.Length; i++)
                {
                    bool any = false;
                    if (lods[i].renderers != null)
                        foreach (Renderer r in lods[i].renderers)
                            if (r != null && r.enabled) { closest.Add(r); any = true; }
                    if (any) break;
                }
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                s.renderers++;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    materials.Add(m.GetInstanceID());
                    if (m.shader != null) shaders.Add(m.shader.GetInstanceID());
                }

                Mesh mesh = MeshOf(r);
                if (mesh != null && meshSet.Add(mesh.GetInstanceID())) meshes.Add(mesh);

                if (r.enabled && !inLodGroup.Contains(r.GetInstanceID())) closest.Add(r);
            }

            foreach (Light l in root.GetComponentsInChildren<Light>(true))
                if (l.enabled && (l.type == LightType.Point || l.type == LightType.Spot)) s.lights++;

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                s.particleSystems++;
                s.maxParticles += ps.main.maxParticles;
            }
        }

        foreach (Renderer r in closest)
        {
            if (r.shadowCastingMode != ShadowCastingMode.Off) s.shadowCasters++;

            Mesh mesh = MeshOf(r);
            if (mesh == null)
            {
                s.drawItems++; // particles, trails...
                continue;
            }
            int subMeshes = Mathf.Max(1, Mathf.Min(mesh.subMeshCount, r.sharedMaterials.Length));
            s.drawItems += subMeshes;
            s.vertices += mesh.vertexCount;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) s.triangles += mesh.GetIndexCount(i) / 3;
        }

        foreach (Mesh m in meshes) s.meshBytes += EstimateGpuBytes(m);

        s.uniqueMeshes = meshes.Count;
        s.uniqueMaterials = materials.Count;
        s.uniqueShaders = shaders.Count;
        return s;
    }

    public static Mesh MeshOf(Renderer r)
    {
        if (r is MeshRenderer)
        {
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }
        var smr = r as SkinnedMeshRenderer;
        return smr != null ? smr.sharedMesh : null;
    }

    /// <summary>Vertex buffers + index buffer. Works on non-readable meshes.</summary>
    public static long EstimateGpuBytes(Mesh m)
    {
        long bytes = 0;
        for (int s = 0; s < m.vertexBufferCount; s++) bytes += (long)m.vertexCount * m.GetVertexBufferStride(s);
        long indices = 0;
        for (int i = 0; i < m.subMeshCount; i++) indices += m.GetIndexCount(i);
        bytes += indices * (m.indexFormat == IndexFormat.UInt16 ? 2 : 4);
        return bytes;
    }
}
