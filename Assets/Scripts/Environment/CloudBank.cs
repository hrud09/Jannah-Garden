using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the ring of fake-volumetric cloud puffs that walls off the garden's horizon.
///
/// Shape of the solution: concentric rings of upright billboards sitting beyond the terrain's
/// 200x200 footprint, densest at the outer edge so nothing past the boundary is visible, thinning
/// to wisps on the inside so the wall reads as weather rather than a painted backdrop.
///
/// Three things make it cheap enough for the 0.8-render-scale mobile tier:
///  * One mesh per sector, not one GameObject per puff - a whole ring is a handful of draw calls.
///  * Rings are cut into angular sectors, so Unity frustum-culls the ~two thirds of the bank that
///    is behind the camera, and sorts the survivors back-to-front by sector centre for free.
///  * Layer opacity and tint ride in vertex colour, so every sector of every layer shares one
///    material and stays SRP-batched.
///
/// Meshes are generated, never serialised (HideFlags.DontSave): the bank costs nothing in the
/// scene file or the player build, and rebuilds in a few milliseconds on load.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Jannah/Environment/Cloud Bank")]
public class CloudBank : MonoBehaviour
{
    public enum Placement
    {
        /// <summary>Puffs packed around a ring - the horizon wall.</summary>
        Ring,

        /// <summary>Puffs scattered across a disc - mist drifting through the garden itself.</summary>
        Disc,
    }

    [System.Serializable]
    public class Layer
    {
        public string name = "Layer";
        public bool enabled = true;

        [Tooltip("Ring packs puffs around the perimeter to wall off the horizon. Disc scatters " +
                 "them across the interior for drifting ground mist.")]
        public Placement placement = Placement.Ring;

        [Tooltip("Optional material for this layer only. Leave empty to use the bank's shared " +
                 "material. Mist layers want their own so they can enable turbulence and near-fade " +
                 "without paying for them on the horizon wall, which covers far more screen.")]
        public Material material;

        [Header("Placement")]
        [Tooltip("Distance from the bank centre to the middle of this ring, in metres. Keep this " +
                 "clear of the terrain: the 200x200 terrain reaches ~141m from its centre at the corners.")]
        public float radius = 155f;

        [Tooltip("Random inward/outward spread of each puff around the ring radius.")]
        public float radiusJitter = 18f;

        [Tooltip("Height of the lowest row of puffs.")]
        public float baseHeight = -8f;

        [Tooltip("Height of the highest row of puffs.")]
        public float topHeight = 46f;

        [Tooltip("Stacked rows of puffs between base and top. More rows = a taller, less gappy wall. " +
                 "Ring placement only.")]
        [Range(1, 8)] public int rows = 3;

        [Header("Disc Placement")]
        [Tooltip("Disc only: puffs are scattered between this radius and 'radius'. Zero fills the " +
                 "whole interior.")]
        public float innerRadius;

        [Tooltip("Disc only: how many puffs to scatter. Mist is meant to be sparse, so this is a " +
                 "direct count rather than being derived from a seal requirement like Ring is.")]
        public int discCount = 140;

        [Tooltip("Disc only: sit each puff above the terrain instead of at an absolute height. " +
                 "Height is measured against the HIGHEST ground under the puff's footprint, so a " +
                 "card can never punch through a hillside - the pipeline has no depth texture, so " +
                 "there is no soft-particle fade to hide an intersection.")]
        public bool followTerrain = true;

        [Tooltip("Disc only: metres between the highest ground under the puff and the puff's " +
                 "lower edge. Also keeps mist from swallowing the first-person camera.")]
        public float groundClearance = 5f;

        [Tooltip("Disc only: extra random height on top of the clearance.")]
        public float heightJitter = 6f;

        [Header("Puff Size")]
        public float minWidth = 34f;
        public float maxWidth = 58f;

        [Tooltip("Vertical squash. Below 1 gives the flattened, wind-sheared look of a distant bank.")]
        public float heightRatio = 0.62f;

        [Header("Density")]
        [Tooltip("How far a puff advances along the ring, as a fraction of its own width. Lower " +
                 "packs more puffs into the ring and seals the horizon harder. 0.35 ~= 3x overlap.")]
        [Range(0.1f, 1.5f)] public float angularSpacing = 0.35f;

        [Range(0f, 2f)] public float opacity = 1f;

        [Tooltip("Opacity of puffs at the innermost edge of this ring, relative to the outer edge. " +
                 "Below 1 makes the bank fade toward the garden - this is the density ramp.")]
        [Range(0f, 1f)] public float innerDensity = 1f;

        [Header("Look")]
        public Color tint = Color.white;

        [Tooltip("Per-puff random brightness variation.")]
        [Range(0f, 0.5f)] public float tintVariation = 0.08f;

        [Range(0f, 1f)] public float opacityVariation = 0.2f;
    }

    [Header("Bank")]
    [Tooltip("Shared material using Jannah/Environment/Volumetric Cloud Bank.")]
    public Material cloudMaterial;

    [Tooltip("Layer the generated renderers are placed on. 'Culling Layer' (18) is rendered by the " +
             "main camera but not the minimap camera, which is what we want - the bank must not " +
             "appear on the top-down minimap.")]
    public string renderLayer = "Culling Layer";

    [Tooltip("Angular slices each ring is cut into. Higher = better frustum culling and depth " +
             "sorting, at one more potential draw call per slice.")]
    [Range(4, 32)] public int sectors = 12;

    [Tooltip("Terrain that Disc layers sit above. Falls back to Terrain.activeTerrain, and to a " +
             "flat plane at this transform's height if there is no terrain at all.")]
    public Terrain terrain;

    [Tooltip("Deterministic layout - the same seed always rebuilds the same sky.")]
    public int seed = 7317;

    [Tooltip("Scales every layer's puff count. Drop below 1 on low-end devices to trade density " +
             "for fill rate; the ring geometry and seal are preserved, just sparser.")]
    [Range(0.25f, 1f)] public float qualityScale = 1f;

    [Header("Layers")]
    public List<Layer> layers = new List<Layer>
    {
        // Heights are tuned against a ~10m eye level: the bank should top out around 15 degrees
        // above the horizon from mid-garden, enough to bury the terrain's own silhouette (the
        // hills reach ~6 degrees) while leaving the sky above it open. Taller than this and the
        // ring reads as full overcast rather than as a distant bank.

        // Inner wisps: thin, low, ramping outward. Softens the terrain boundary into haze.
        new Layer
        {
            name = "Inner Wisps", radius = 150f, radiusJitter = 14f,
            baseHeight = -24f, topHeight = 14f, rows = 3,
            minWidth = 24f, maxWidth = 40f, heightRatio = 0.5f,
            angularSpacing = 0.5f, opacity = 0.5f, innerDensity = 0.15f,
            tint = new Color(0.97f, 0.98f, 1f), tintVariation = 0.06f, opacityVariation = 0.35f,
        },
        // The wall: this is the layer that actually seals the horizon.
        new Layer
        {
            // The base sits well below terrain level on purpose: from the free-fly inspector
            // camera at 25m the player looks *down* past the boundary, and a base near ground
            // level leaves skybox showing under the bank.
            name = "Horizon Wall", radius = 175f, radiusJitter = 16f,
            baseHeight = -45f, topHeight = 40f, rows = 5,
            minWidth = 34f, maxWidth = 54f, heightRatio = 0.62f,
            angularSpacing = 0.22f, opacity = 1f, innerDensity = 0.8f,
            tint = Color.white, tintVariation = 0.07f, opacityVariation = 0.15f,
        },
        // Far towers: taller and further out, so they crest above the wall and give it depth
        // instead of the flat cut-out silhouette a single ring produces.
        new Layer
        {
            name = "Far Towers", radius = 220f, radiusJitter = 24f,
            baseHeight = 0f, topHeight = 62f, rows = 3,
            minWidth = 44f, maxWidth = 76f, heightRatio = 0.85f,
            angularSpacing = 0.45f, opacity = 0.9f, innerDensity = 1f,
            tint = new Color(1f, 0.99f, 0.97f), tintVariation = 0.1f, opacityVariation = 0.2f,
        },
        // Skirt: plugs everything below the wall. Only ever on screen from the free-fly inspector
        // camera looking down over the boundary, where the sight line drops far below the wall's
        // base - from 25m up at 40 degrees down that is ~95m below eye at the ring radius. Huge
        // puffs mean this costs ~40 of them, and it frustum-culls away entirely in normal play.
        new Layer
        {
            name = "Skirt", radius = 160f, radiusJitter = 20f,
            baseHeight = -130f, topHeight = -55f, rows = 2,
            minWidth = 100f, maxWidth = 140f, heightRatio = 1f,
            angularSpacing = 0.4f, opacity = 1f, innerDensity = 1f,
            tint = new Color(0.98f, 0.98f, 1f), tintVariation = 0.08f, opacityVariation = 0.1f,
        },
        // Mist drifting through the garden itself. Disc placement, riding above the terrain, and
        // on its own material so the turbulence and near-fade it needs are not also paid for by
        // the horizon layers above - those cover far more screen and would not benefit.
        // innerDensity below 1 keeps the middle of the garden clear and thickens the mist toward
        // the boundary, so it reads as continuous with the wall rather than as a separate effect.
        new Layer
        {
            // Smaller puffs than the horizon layers on purpose: clearance is measured against the
            // highest ground under the whole footprint, so a wide card on a slope gets pushed far
            // above the low side. Keeping them small keeps the mist hugging the ground.
            name = "Garden Mist", placement = Placement.Disc,
            radius = 140f, innerRadius = 0f, discCount = 160,
            followTerrain = true, groundClearance = 0f, heightJitter = 3f,
            minWidth = 14f, maxWidth = 26f, heightRatio = 0.5f,
            opacity = 0.55f, innerDensity = 0.5f,
            tint = new Color(0.98f, 0.99f, 1f), tintVariation = 0.05f, opacityVariation = 0.4f,
        },
    };

    private const string GeneratedRootName = "__CloudBankGenerated";

    // A regular-ish octagon instead of a quad. A puff's alpha is a radial blob, so the four
    // corners of a quad are always fully transparent - cutting them removes ~20% of the fill
    // for free, and fill is the only thing that actually costs on a billboard cloud bank.
    private static readonly Vector2[] OctagonCorners =
    {
        new Vector2(-0.45f, -1f), new Vector2(0.45f, -1f),
        new Vector2(1f, -0.45f), new Vector2(1f, 0.45f),
        new Vector2(0.45f, 1f), new Vector2(-0.45f, 1f),
        new Vector2(-1f, 0.45f), new Vector2(-1f, -0.45f),
    };

    private Transform generatedRoot;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        // Leave the scene exactly as we found it - nothing generated is ever serialised.
        ClearGenerated();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        // Deferred: OnValidate can fire during serialisation, where DestroyImmediate is illegal.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
            {
                Rebuild();
            }
        };
    }
#endif

    /// <summary>
    /// Changes the puff density and regenerates, for dropping fill cost on weaker devices.
    /// The ring radii, heights and seal are unaffected - the bank still hides the horizon, it
    /// just does it with fewer, sparser puffs. No-ops when the value is unchanged, since a
    /// rebuild reallocates every mesh.
    /// </summary>
    public void SetQualityScale(float scale)
    {
        scale = Mathf.Clamp(scale, 0.25f, 1f);
        if (Mathf.Approximately(scale, qualityScale))
        {
            return;
        }

        qualityScale = scale;
        Rebuild();
    }

    /// <summary>Destroys and regenerates every cloud sector from the current settings.</summary>
    public void Rebuild()
    {
        ClearGenerated();

        if (layers == null || layers.Count == 0)
        {
            return;
        }

        // hideFlags go on the GameObject, never on its Transform: Unity rejects DontSave on a
        // Transform whose GameObject hasn't been flagged first.
        var rootObject = new GameObject(GeneratedRootName) { hideFlags = HideFlags.DontSave };
        generatedRoot = rootObject.transform;
        generatedRoot.SetParent(transform, false);

        int layerIndex = LayerMask.NameToLayer(renderLayer);
        if (layerIndex < 0)
        {
            Debug.LogWarning($"[CloudBank] Layer '{renderLayer}' does not exist; falling back to Default. " +
                             "The bank will then also be drawn by the minimap camera.", this);
            layerIndex = 0;
        }

        Random.State previousState = Random.state;
        Random.InitState(seed);

        try
        {
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null && layers[i].enabled)
                {
                    BuildLayer(layers[i], i, layerIndex);
                }
            }
        }
        finally
        {
            Random.state = previousState;
        }
    }

    private struct PuffData
    {
        public Vector3 centre;
        public float halfWidth;
        public float halfHeight;
        public float phase;
        public float noiseOffset;
        public Color color;
    }

    private void BuildLayer(Layer layer, int layerOrdinal, int unityLayer)
    {
        List<PuffData> puffs = layer.placement == Placement.Ring
            ? GenerateRingPuffs(layer)
            : GenerateDiscPuffs(layer);

        if (puffs.Count == 0)
        {
            return;
        }

        // Bucket by bearing from the bank centre. Each bucket becomes its own renderer, which is
        // what buys per-sector frustum culling and back-to-front sorting from Unity for free.
        int sectorCount = Mathf.Clamp(sectors, 1, puffs.Count);
        var buckets = new List<PuffData>[sectorCount];
        for (int i = 0; i < sectorCount; i++)
        {
            buckets[i] = new List<PuffData>();
        }

        foreach (PuffData puff in puffs)
        {
            float bearing = Mathf.Atan2(puff.centre.z, puff.centre.x) + Mathf.PI;
            int index = Mathf.Clamp(
                (int)(bearing / (Mathf.PI * 2f) * sectorCount), 0, sectorCount - 1);
            buckets[index].Add(puff);
        }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var corners = new List<Vector4>();
        var colors = new List<Color>();
        var indices = new List<int>();

        for (int s = 0; s < sectorCount; s++)
        {
            if (buckets[s].Count == 0)
            {
                continue;
            }

            vertices.Clear();
            uvs.Clear();
            corners.Clear();
            colors.Clear();
            indices.Clear();

            foreach (PuffData puff in buckets[s])
            {
                AppendPuff(vertices, uvs, corners, colors, indices,
                           puff.centre, puff.halfWidth, puff.halfHeight,
                           puff.phase, puff.noiseOffset, puff.color);
            }

            CreateSector(layer, layerOrdinal, s, unityLayer,
                         vertices, uvs, corners, colors, indices);
        }
    }

    private List<PuffData> GenerateRingPuffs(Layer layer)
    {
        var puffs = new List<PuffData>();

        float averageWidth = Mathf.Max(0.01f, (layer.minWidth + layer.maxWidth) * 0.5f);
        float step = Mathf.Max(0.01f, averageWidth * layer.angularSpacing);

        // Derive the count from the geometry rather than trusting a hand-tuned number: this is
        // what guarantees the ring has no gaps for the player to see the void through, at any
        // radius the designer picks.
        int perRow = Mathf.CeilToInt(2f * Mathf.PI * layer.radius / step);
        perRow = Mathf.Max(8, Mathf.RoundToInt(perRow * qualityScale));

        for (int row = 0; row < layer.rows; row++)
        {
            // Offset alternate rows by half a step so the stacking doesn't form visible columns.
            float rowPhase = (row % 2 == 0) ? 0f : 0.5f;
            float rowT = layer.rows == 1 ? 0.5f : row / (float)(layer.rows - 1);
            float rowHeight = Mathf.Lerp(layer.baseHeight, layer.topHeight, rowT);

            for (int i = 0; i < perRow; i++)
            {
                float angle = (i + rowPhase + Random.Range(-0.35f, 0.35f)) / perRow * Mathf.PI * 2f;

                float radialOffset = Random.Range(-layer.radiusJitter, layer.radiusJitter);
                float radius = layer.radius + radialOffset;

                float width = Random.Range(layer.minWidth, layer.maxWidth);

                // The density ramp the brief asks for: puffs sitting toward the inside of the
                // band are thinner, puffs at the outer edge are at full opacity, so the bank
                // dissolves into the garden but is solid against the horizon.
                float radialT = layer.radiusJitter > 0.001f
                    ? Mathf.InverseLerp(-layer.radiusJitter, layer.radiusJitter, radialOffset)
                    : 1f;

                puffs.Add(MakePuff(
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        rowHeight + Random.Range(-1f, 1f) * (layer.topHeight - layer.baseHeight) * 0.18f,
                        Mathf.Sin(angle) * radius),
                    width, layer, radialT));
            }
        }

        return puffs;
    }

    private List<PuffData> GenerateDiscPuffs(Layer layer)
    {
        var puffs = new List<PuffData>();

        int count = Mathf.Max(1, Mathf.RoundToInt(layer.discCount * qualityScale));
        float inner = Mathf.Max(0f, layer.innerRadius);
        float outer = Mathf.Max(inner + 0.01f, layer.radius);

        for (int i = 0; i < count; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;

            // Square-root of the interpolated squared radii, so puffs land with uniform density
            // per unit area. Lerping the radius directly crowds them into the middle.
            float radius = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, Random.value));
            float radialT = Mathf.InverseLerp(inner, outer, radius);

            float width = Random.Range(layer.minWidth, layer.maxWidth);
            float halfHeight = width * layer.heightRatio * 0.5f;

            var flat = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            float localY;

            if (layer.followTerrain)
            {
                Vector3 worldFlat = transform.TransformPoint(flat);
                float groundY = SampleGroundHeight(worldFlat, width * 0.5f);

                // Measured to the puff's LOWER edge, so clearance means what it says regardless
                // of how big the puff is.
                float worldY = groundY + layer.groundClearance + halfHeight
                               + Random.value * layer.heightJitter;
                localY = transform.InverseTransformPoint(new Vector3(worldFlat.x, worldY, worldFlat.z)).y;
            }
            else
            {
                localY = Random.Range(layer.baseHeight, layer.topHeight);
            }

            puffs.Add(MakePuff(new Vector3(flat.x, localY, flat.z), width, layer, radialT));
        }

        return puffs;
    }

    private static PuffData MakePuff(Vector3 centre, float width, Layer layer, float radialT)
    {
        float alpha = layer.opacity * Mathf.Lerp(layer.innerDensity, 1f, radialT) *
                      (1f - Random.value * layer.opacityVariation);

        float brightness = 1f - Random.value * layer.tintVariation;

        return new PuffData
        {
            centre = centre,
            halfWidth = width * 0.5f,
            halfHeight = width * layer.heightRatio * 0.5f,
            phase = Random.value,
            noiseOffset = Random.value * 37f,
            color = new Color(
                layer.tint.r * brightness,
                layer.tint.g * brightness,
                layer.tint.b * brightness,
                Mathf.Clamp01(alpha)),
        };
    }

    /// <summary>
    /// Highest world-space ground height under a puff's footprint. Sampling the HIGHEST point
    /// rather than the centre is what keeps a mist card from punching through a hillside: with
    /// no depth texture in the mobile pipeline there is no soft-particle fade to hide the
    /// intersection, so the geometry has to avoid it outright.
    /// </summary>
    private float SampleGroundHeight(Vector3 worldPosition, float radius)
    {
        Terrain ground = terrain != null ? terrain : Terrain.activeTerrain;
        if (ground == null)
        {
            return transform.position.y;
        }

        float highest = ground.SampleHeight(worldPosition);

        // Deliberately less than the full footprint. Taking the max across the whole width on
        // rolling terrain lifts every card metres above its own centre, and mist floating at
        // canopy height stops reading as ground fog. The shader's _BottomFade feathers the
        // underside, so the occasional clip against a steep slope stays soft.
        float sampleRadius = radius * 0.55f;

        const int Samples = 6;
        for (int i = 0; i < Samples; i++)
        {
            float angle = i / (float)Samples * Mathf.PI * 2f;
            var offset = new Vector3(Mathf.Cos(angle) * sampleRadius, 0f, Mathf.Sin(angle) * sampleRadius);
            highest = Mathf.Max(highest, ground.SampleHeight(worldPosition + offset));
        }

        return highest + ground.transform.position.y;
    }

    private static void AppendPuff(List<Vector3> vertices, List<Vector2> uvs, List<Vector4> corners,
                                   List<Color> colors, List<int> indices,
                                   Vector3 centre, float halfWidth, float halfHeight,
                                   float phase, float noiseOffset, Color color)
    {
        int baseIndex = vertices.Count;

        for (int c = 0; c < OctagonCorners.Length; c++)
        {
            Vector2 corner = OctagonCorners[c];

            // Position is the shared puff centre; the corner offset is applied in the vertex
            // shader against the camera-aligned basis, which is what makes it a billboard.
            vertices.Add(centre);
            uvs.Add(corner * 0.5f + new Vector2(0.5f, 0.5f));
            corners.Add(new Vector4(corner.x * halfWidth, corner.y * halfHeight, phase, noiseOffset));
            colors.Add(color);
        }

        // Fan from corner 0 - six triangles for the octagon.
        for (int t = 1; t < OctagonCorners.Length - 1; t++)
        {
            indices.Add(baseIndex);
            indices.Add(baseIndex + t);
            indices.Add(baseIndex + t + 1);
        }
    }

    private void CreateSector(Layer layer, int layerOrdinal, int sectorIndex, int unityLayer,
                              List<Vector3> vertices, List<Vector2> uvs, List<Vector4> corners,
                              List<Color> colors, List<int> indices)
    {
        var mesh = new Mesh
        {
            name = $"CloudBank_{layer.name}_{sectorIndex}",
            hideFlags = HideFlags.DontSave,
            indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16,
        };

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, corners);
        mesh.SetColors(colors);
        mesh.SetTriangles(indices, 0);

        // Puffs expand around their centre in the vertex shader, so the mesh's own bounds are a
        // zero-thickness ring of points. Pad them by the widest puff or the sector pops out of
        // the frustum the moment its centres leave the view.
        mesh.RecalculateBounds();
        Bounds bounds = mesh.bounds;
        float padding = Mathf.Max(layer.maxWidth, layer.maxWidth * layer.heightRatio);
        bounds.Expand(padding);
        mesh.bounds = bounds;

        var go = new GameObject($"{layer.name} [{sectorIndex}]")
        {
            hideFlags = HideFlags.DontSave,
            layer = unityLayer,
        };
        go.transform.SetParent(generatedRoot, false);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = layer.material != null ? layer.material : cloudMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.allowOcclusionWhenDynamic = false;

        // Nudge inner layers later in the queue so a near ring blends over a far one when their
        // sector centres happen to tie on distance.
        renderer.sortingOrder = -layerOrdinal;
    }

    private void ClearGenerated()
    {
        if (generatedRoot != null)
        {
            DestroySafely(generatedRoot.gameObject);
            generatedRoot = null;
        }

        // Catch roots left behind by a domain reload or a crashed rebuild.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != null && child.name == GeneratedRootName)
            {
                DestroySafely(child.gameObject);
            }
        }
    }

    private static void DestroySafely(GameObject go)
    {
        // Meshes are DontSave but not auto-collected; drop them explicitly or every rebuild in
        // the editor leaks a few thousand vertices until the next domain reload.
        foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh != null)
            {
                DestroyObject(filter.sharedMesh);
            }
        }

        DestroyObject(go);
    }

    private static void DestroyObject(Object target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (layers == null)
        {
            return;
        }

        foreach (Layer layer in layers)
        {
            if (layer == null || !layer.enabled)
            {
                continue;
            }

            Gizmos.color = new Color(0.5f, 0.75f, 1f, 0.6f);
            DrawRing(layer.radius - layer.radiusJitter, layer.baseHeight);
            DrawRing(layer.radius + layer.radiusJitter, layer.baseHeight);
            Gizmos.color = new Color(0.5f, 0.75f, 1f, 0.25f);
            DrawRing(layer.radius, layer.topHeight);
        }
    }

    private void DrawRing(float radius, float height)
    {
        const int Segments = 64;
        Vector3 origin = transform.position + Vector3.up * height;
        Vector3 previous = origin + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= Segments; i++)
        {
            float angle = i / (float)Segments * Mathf.PI * 2f;
            Vector3 current = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }
#endif
}
