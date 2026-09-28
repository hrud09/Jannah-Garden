using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dresses the garden by the shape of its own paths.
///
/// <para>The roads are not objects — they are paint on the terrain's Dirt_Layer. Flood-filling the
/// terrain away from that paint (and away from the sand flats, which are their own kind of edge)
/// turns the path network into a set of closed pockets. Each pocket gets a <see cref="Theme"/>:
/// its own tree palette, its own flower palette, its own density. That is what makes the garden
/// read as a series of places rather than one uniform field of props.</para>
///
/// <para><b>Why ground cover goes to terrain details and only big props become GameObjects:</b>
/// grass and flowers want to be tens of thousands of instances. As GameObjects that is scene bloat
/// and draw calls; as terrain detail instances it is a density value per cell, GPU-instanced, and
/// costs the build nothing. Trees, rocks and bushes are few enough to be real objects — and they
/// need the colliders and LODGroups that details do not carry.</para>
///
/// <para>Everything this tool creates lands under a single <see cref="FoliageRootName"/> object, and
/// the detail layers are cleared before repainting, so a re-run is idempotent and "Clear" genuinely
/// undoes it. Change <see cref="Seed"/> and re-run to re-roll the whole garden.</para>
/// </summary>
public static class GardenRegionScatter
{
    private const string TerrainName = "LandTerrain";
    private const string FoliageRootName = "Scattered Foliage";
    private const string PrefabRoot = "Assets/3D Assets/Idyllic Fantasy Nature/Prefabs/";

    /// <summary>Change this and re-run for a different — but equally deliberate — garden.</summary>
    private const int Seed = 20260926;

    // Terrain layer indices, from the terrain's own layer list.
    private const int LayerDirt = 1;   // the paths
    private const int LayerSand = 2;   // the southern flats

    /// <summary>Above this dirt weight a cell counts as road, and nothing is placed on it.</summary>
    private const float RoadThreshold = 0.35f;
    private const float SandThreshold = 0.5f;

    /// <summary>Surface of the Lake mesh. Land props stay above it; water props stay below.</summary>
    private const float WaterLevel = 3.94f;

    /// <summary>
    /// Footprint of the Lake plane in XZ, resolved once per run.
    ///
    /// <para>Height alone cannot decide what is underwater: plenty of perfectly dry ground elsewhere
    /// on this terrain sits below the lake surface, and testing height globally silently rejected
    /// most of the map. The water rules only mean anything inside the plane's own footprint.</para>
    /// </summary>
    private static Rect _lakeFootprint = Rect.zero;

    private static void ResolveLakeFootprint()
    {
        _lakeFootprint = Rect.zero;
        var env = GameObject.Find("Environment");
        var water = env != null ? env.transform.Find("Water") : null;
        if (water == null) return;
        foreach (var r in water.GetComponentsInChildren<Renderer>(true))
        {
            // The lake surface is the one big flat plane in there; the gazebo floors are not.
            if (r.bounds.size.x < 40f || r.bounds.size.y > 1f) continue;
            _lakeFootprint = new Rect(r.bounds.min.x, r.bounds.min.z, r.bounds.size.x, r.bounds.size.z);
            return;
        }
    }

    // ------------------------------------------------------------------
    // Theme description
    // ------------------------------------------------------------------

    private enum WaterRule { LandOnly, ShoreOnly, SubmergedOnly }

    /// <summary>A weighted prop family: which prefabs, how many per 100 m2, and how they sit.</summary>
    private class Prop
    {
        public string[] Prefabs;
        public float PerHundredSqM;
        /// <summary>Metres to the nearest other prop of any family. Keeps clumps from fusing.</summary>
        public float Spacing = 3f;
        /// <summary>Metres of clearance from painted road, so paths stay walkable and readable.</summary>
        public float RoadClearance = 2.5f;
        public float ScaleMin = 0.9f;
        public float ScaleMax = 1.15f;
        /// <summary>Degrees. Trees refuse cliffs; rocks do not care.</summary>
        public float MaxSlope = 28f;
        /// <summary>Random lean in degrees around X and Z. Rocks like it; trees do not.</summary>
        public float Tilt = 0f;
        public WaterRule Water = WaterRule.LandOnly;
        /// <summary>Only place within this many metres of the terrain edge (0 = anywhere).</summary>
        public float EdgeBandOnly = 0f;
        /// <summary>Bed the object slightly into the ground so it does not look dropped on top.</summary>
        public bool SinkIntoGround = false;
    }

    /// <summary>Ground cover for a pocket: detail prototype indices and instances per square metre.</summary>
    private class Cover
    {
        public int[] Prototypes;
        public float PerSqM;
        /// <summary>Perlin frequency for clumping. Low = broad drifts, high = speckle.</summary>
        public float Clump = 0.06f;
        /// <summary>Noise level below which nothing grows. Higher = more bare ground between drifts.</summary>
        public float Bare = 0.35f;
        public float RoadClearance = 1.0f;
        public WaterRule Water = WaterRule.LandOnly;
    }

    private class Theme
    {
        public string Name;
        /// <summary>Centroid of the pocket this theme was written for; used to match theme to label.</summary>
        public Vector2 Centroid;
        public Cover[] Covers = new Cover[0];
        public Prop[] Props = new Prop[0];
    }

    // Detail prototype indices, in the terrain's own order.
    private const int D_MeadowRed = 0, D_MeadowBlue = 1, D_MeadowBluePurple = 2, D_MeadowPink = 3;
    private const int D_MeadowRedPink = 4, D_MeadowPurple = 5, D_MeadowOrange = 7;
    private const int D_MeadowPurpleRedPink = 8;
    private const int D_Grass1 = 9, D_Grass2 = 10, D_Grass3 = 11;
    private const int D_FlowerBlue1 = 12, D_FlowerBlue2 = 13, D_FlowerOrange = 14, D_FlowerPink = 15;
    private const int D_FlowerYellow = 16, D_FlowerPurple = 17, D_FlowerRed = 18, D_FlowerWhite = 19;
    private const int D_Plant1 = 20, D_Plant2 = 21, D_Plant3 = 22, D_Plant4 = 23;
    private const int D_Plant5 = 24, D_Plant6 = 25, D_Plant7 = 26, D_Plant8 = 27;
    private const int D_MeadowWhite = 28, D_MeadowRedOrange = 29, D_MeadowMixed = 30;
    private const int D_Reeds1 = 31, D_Reeds2 = 32, D_Reeds3 = 33;

    private static readonly int[] AllGrass = { D_Grass1, D_Grass2, D_Grass3 };
    private static readonly int[] AllPlants = { D_Plant1, D_Plant2, D_Plant3, D_Plant4, D_Plant5, D_Plant6, D_Plant7, D_Plant8 };

    private static string[] P(params string[] names) { return names; }
    private static int[] D(params int[] protos) { return protos; }

    private static Theme[] BuildThemes()
    {
        return new[]
        {
            // The land outside the path network: a quiet green frame, thickening into woodland at the
            // map edge so the terrain boundary is never the thing you notice.
            new Theme
            {
                Name = "Outer Wilds", Centroid = new Vector2(98.6f, 132.5f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 2.00f, Clump = 0.05f, Bare = 0.30f },
                    new Cover { Prototypes = D(D_FlowerWhite, D_FlowerYellow), PerSqM = 0.10f, Clump = 0.12f, Bare = 0.55f },
                    new Cover { Prototypes = D(D_Plant2, D_Plant5), PerSqM = 0.06f, Clump = 0.10f, Bare = 0.55f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("Fir_03", "Fir_04", "Fir_05", "BroadleafTree_03_Green"),
                               PerHundredSqM = 1.5f, Spacing = 4.2f, RoadClearance = 3.5f,
                               ScaleMin = 0.85f, ScaleMax = 1.3f, EdgeBandOnly = 18f },
                    new Prop { Prefabs = P("Fir_04", "BroadleafTree_03_Green", "WillowTree_01_Green"),
                               PerHundredSqM = 0.25f, Spacing = 6f, RoadClearance = 3.5f,
                               ScaleMin = 0.85f, ScaleMax = 1.2f },
                    new Prop { Prefabs = P("Bush_03_01", "Bush_03_02"), PerHundredSqM = 0.45f,
                               Spacing = 3f, RoadClearance = 2f, ScaleMin = 0.8f, ScaleMax = 1.25f },
                    new Prop { Prefabs = P("Rock_Medium_02", "Rock_Small_01", "Rock_Small_03", "Stones_01"),
                               PerHundredSqM = 0.3f, Spacing = 3f, RoadClearance = 1.5f,
                               ScaleMin = 0.7f, ScaleMax = 1.3f, MaxSlope = 90f, Tilt = 8f, SinkIntoGround = true },
                },
            },

            // West pocket: spring. Blossom canopy over pink and white ground.
            new Theme
            {
                Name = "Blossom Grove", Centroid = new Vector2(37.6f, 81.2f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 2.20f, Clump = 0.06f, Bare = 0.25f },
                    new Cover { Prototypes = D(D_MeadowPink, D_MeadowWhite), PerSqM = 0.16f, Clump = 0.09f, Bare = 0.40f },
                    new Cover { Prototypes = D(D_FlowerPink, D_FlowerWhite), PerSqM = 0.22f, Clump = 0.14f, Bare = 0.45f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("BlossomTree_03", "BlossomTree_04", "BlossomTree_05"),
                               PerHundredSqM = 1.3f, Spacing = 5.5f, RoadClearance = 3.2f,
                               ScaleMin = 0.85f, ScaleMax = 1.25f },
                    new Prop { Prefabs = P("Bush_03_01", "Bush_03_02"), PerHundredSqM = 0.5f,
                               Spacing = 3f, RoadClearance = 2f, ScaleMin = 0.75f, ScaleMax = 1.1f },
                    new Prop { Prefabs = P("Stones_02", "Stone_Medium_01"), PerHundredSqM = 0.2f,
                               Spacing = 3f, RoadClearance = 1.5f, MaxSlope = 90f, Tilt = 10f, SinkIntoGround = true },
                },
            },

            // The big central pocket, seen from every path around it: deliberately the most open space
            // in the garden. Its colour comes from the ground, not from a canopy.
            new Theme
            {
                Name = "Sunlit Meadow", Centroid = new Vector2(96.7f, 87.8f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 3.00f, Clump = 0.05f, Bare = 0.15f },
                    new Cover { Prototypes = D(D_MeadowMixed, D_MeadowOrange), PerSqM = 0.20f, Clump = 0.07f, Bare = 0.35f },
                    new Cover { Prototypes = D(D_FlowerYellow, D_FlowerWhite), PerSqM = 0.30f, Clump = 0.13f, Bare = 0.40f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("WillowTree_01_Green", "WillowTree_03_Green", "BroadleafTree_03_Green"),
                               PerHundredSqM = 0.35f, Spacing = 8f, RoadClearance = 4f,
                               ScaleMin = 0.9f, ScaleMax = 1.3f },
                    new Prop { Prefabs = P("Bush_03_02"), PerHundredSqM = 0.25f, Spacing = 4f,
                               RoadClearance = 2.5f, ScaleMin = 0.8f, ScaleMax = 1.1f },
                },
            },

            // East pocket: the dark, cool counterweight to the meadow. Conifer shade and boulders.
            new Theme
            {
                Name = "Evergreen Wood", Centroid = new Vector2(153.7f, 95.2f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 1.60f, Clump = 0.07f, Bare = 0.40f },
                    new Cover { Prototypes = AllPlants, PerSqM = 0.35f, Clump = 0.10f, Bare = 0.35f },
                    new Cover { Prototypes = D(D_FlowerWhite, D_FlowerBlue1), PerSqM = 0.07f, Clump = 0.16f, Bare = 0.60f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("Fir_03", "Fir_04", "Fir_05"), PerHundredSqM = 2.4f,
                               Spacing = 4f, RoadClearance = 3.2f, ScaleMin = 0.8f, ScaleMax = 1.35f },
                    new Prop { Prefabs = P("Rock_Big_01", "Rock_Medium_01", "Rock_Medium_03"),
                               PerHundredSqM = 0.35f, Spacing = 5f, RoadClearance = 2.5f,
                               ScaleMin = 0.7f, ScaleMax = 1.2f, MaxSlope = 90f, Tilt = 6f, SinkIntoGround = true },
                    new Prop { Prefabs = P("Branch_01", "Branch_03", "Branch_05"), PerHundredSqM = 0.3f,
                               Spacing = 2.5f, RoadClearance = 1.5f, MaxSlope = 90f, Tilt = 4f },
                },
            },

            // The ring around the Lake. Willows lean over the water, reeds fringe the shore.
            new Theme
            {
                Name = "Lakeside", Centroid = new Vector2(88.0f, 118.7f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 2.40f, Clump = 0.06f, Bare = 0.25f },
                    new Cover { Prototypes = D(D_Reeds1, D_Reeds2, D_Reeds3), PerSqM = 1.1f,
                               Clump = 0.13f, Bare = 0.20f, RoadClearance = 0.8f, Water = WaterRule.ShoreOnly },
                    new Cover { Prototypes = D(D_FlowerBlue1, D_FlowerBlue2), PerSqM = 0.18f, Clump = 0.14f, Bare = 0.45f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("WillowTree_01_Green", "WillowTree_02_Green", "WillowTree_05_Green", "WillowTree_02_Blue"),
                               PerHundredSqM = 1.4f, Spacing = 6f, RoadClearance = 3f,
                               ScaleMin = 0.9f, ScaleMax = 1.3f },
                    new Prop { Prefabs = P("Cattail_01", "Cattail_02", "Cattail_03"), PerHundredSqM = 3.5f,
                               Spacing = 1.6f, RoadClearance = 1f, ScaleMin = 0.8f, ScaleMax = 1.2f,
                               MaxSlope = 60f, Water = WaterRule.ShoreOnly },
                    new Prop { Prefabs = P("Stone_Medium_02", "Stones_03", "Rock_Small_02"),
                               PerHundredSqM = 0.5f, Spacing = 2.5f, RoadClearance = 1.5f,
                               MaxSlope = 90f, Tilt = 10f, SinkIntoGround = true },
                },
            },

            // A small pocket given over to stone, so not every enclosure is green.
            new Theme
            {
                Name = "Rock Garden", Centroid = new Vector2(132.2f, 129.4f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 1.20f, Clump = 0.09f, Bare = 0.45f },
                    new Cover { Prototypes = D(D_Plant3, D_Plant7), PerSqM = 0.30f, Clump = 0.12f, Bare = 0.40f },
                    new Cover { Prototypes = D(D_FlowerPurple, D_FlowerWhite), PerSqM = 0.12f, Clump = 0.18f, Bare = 0.55f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("Rock_Big_01", "Rock_Big_03", "Rock_Medium_01", "Rock_Medium_02", "Rock_Medium_03"),
                               PerHundredSqM = 1.6f, Spacing = 4f, RoadClearance = 2.2f,
                               ScaleMin = 0.6f, ScaleMax = 1.25f, MaxSlope = 90f, Tilt = 9f, SinkIntoGround = true },
                    new Prop { Prefabs = P("Stones_01", "Stones_02", "Stones_03", "Stone_Medium_03"),
                               PerHundredSqM = 1.4f, Spacing = 2f, RoadClearance = 1.2f,
                               MaxSlope = 90f, Tilt = 12f, SinkIntoGround = true },
                    new Prop { Prefabs = P("Fir_05", "Bush_03_01"), PerHundredSqM = 0.4f, Spacing = 4f,
                               RoadClearance = 2.5f, ScaleMin = 0.7f, ScaleMax = 1f },
                },
            },

            // The long band pocket: a purple corridor, the strongest single colour statement in the map.
            new Theme
            {
                Name = "Willow Walk", Centroid = new Vector2(74.1f, 139.4f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 2.40f, Clump = 0.06f, Bare = 0.25f },
                    new Cover { Prototypes = D(D_MeadowPurple, D_MeadowBluePurple, D_MeadowPurpleRedPink),
                               PerSqM = 0.20f, Clump = 0.08f, Bare = 0.35f },
                    new Cover { Prototypes = D(D_FlowerPurple, D_FlowerBlue2), PerSqM = 0.25f, Clump = 0.13f, Bare = 0.42f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("WillowTree_01_Purple", "WillowTree_02_Purple", "WillowTree_03_Purple", "WillowTree_04_Purple", "WillowTree_05_Purple"),
                               PerHundredSqM = 1.5f, Spacing = 5.5f, RoadClearance = 3.2f,
                               ScaleMin = 0.85f, ScaleMax = 1.3f },
                    new Prop { Prefabs = P("BroadleafTree_01_Purple", "BroadleafTree_04_Purple"),
                               PerHundredSqM = 0.4f, Spacing = 6f, RoadClearance = 3.2f,
                               ScaleMin = 0.9f, ScaleMax = 1.2f },
                    new Prop { Prefabs = P("Bush_03_01", "Stones_02"), PerHundredSqM = 0.4f, Spacing = 2.5f,
                               RoadClearance = 1.8f, MaxSlope = 90f, Tilt = 6f },
                },
            },

            // Autumn, kept to one pocket so the warm palette reads as a place you arrive at.
            new Theme
            {
                Name = "Autumn Grove", Centroid = new Vector2(155.7f, 146.0f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 2.00f, Clump = 0.07f, Bare = 0.30f },
                    new Cover { Prototypes = D(D_MeadowRedOrange, D_MeadowRed, D_MeadowOrange),
                               PerSqM = 0.20f, Clump = 0.09f, Bare = 0.38f },
                    new Cover { Prototypes = D(D_FlowerRed, D_FlowerOrange), PerSqM = 0.22f, Clump = 0.14f, Bare = 0.45f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("BroadleafTree_01_Red", "BroadleafTree_03_Red", "BroadleafTree_04_Red", "BroadleafTree_05_Red"),
                               PerHundredSqM = 1.7f, Spacing = 5f, RoadClearance = 3.2f,
                               ScaleMin = 0.85f, ScaleMax = 1.3f },
                    new Prop { Prefabs = P("WillowTree_02_Red", "WillowTree_04_Red"), PerHundredSqM = 0.5f,
                               Spacing = 6f, RoadClearance = 3.2f, ScaleMin = 0.9f, ScaleMax = 1.2f },
                    new Prop { Prefabs = P("Branch_02", "Branch_04", "Branch_06"), PerHundredSqM = 0.5f,
                               Spacing = 2f, RoadClearance = 1.2f, MaxSlope = 90f, Tilt = 5f },
                },
            },

            // The widest northern pocket, left almost treeless on purpose: an uninterrupted sheet of
            // colour gives the eye somewhere to rest between the wooded enclosures.
            new Theme
            {
                Name = "Wildflower Prairie", Centroid = new Vector2(48.7f, 160.4f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 3.60f, Clump = 0.04f, Bare = 0.12f },
                    new Cover { Prototypes = D(D_MeadowMixed, D_MeadowRedPink, D_MeadowBlue, D_MeadowWhite),
                               PerSqM = 0.34f, Clump = 0.07f, Bare = 0.25f },
                    new Cover { Prototypes = D(D_FlowerYellow, D_FlowerRed, D_FlowerPink, D_FlowerWhite),
                               PerSqM = 0.40f, Clump = 0.12f, Bare = 0.35f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("Bush_03_01", "Bush_03_02"), PerHundredSqM = 0.45f,
                               Spacing = 4f, RoadClearance = 2f, ScaleMin = 0.75f, ScaleMax = 1.1f },
                    new Prop { Prefabs = P("BlossomTree_04", "WillowTree_01_Pink"), PerHundredSqM = 0.18f,
                               Spacing = 10f, RoadClearance = 4f, ScaleMin = 0.95f, ScaleMax = 1.25f },
                    new Prop { Prefabs = P("Stones_01", "Stone_Medium_02"), PerHundredSqM = 0.25f,
                               Spacing = 3f, RoadClearance = 1.5f, MaxSlope = 90f, Tilt = 10f, SinkIntoGround = true },
                },
            },

            // Small, cool and blue — a shaded alcove off the busier northern paths.
            new Theme
            {
                Name = "Blue Bower", Centroid = new Vector2(133.2f, 159.6f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 2.20f, Clump = 0.07f, Bare = 0.28f },
                    new Cover { Prototypes = D(D_MeadowBlue, D_MeadowBluePurple), PerSqM = 0.24f, Clump = 0.09f, Bare = 0.32f },
                    new Cover { Prototypes = D(D_FlowerBlue1, D_FlowerBlue2), PerSqM = 0.30f, Clump = 0.13f, Bare = 0.38f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("BroadleafTree_02_Blue", "BroadleafTree_04_Blue", "BroadleafTree_05_Blue"),
                               PerHundredSqM = 1.6f, Spacing = 5f, RoadClearance = 3f,
                               ScaleMin = 0.85f, ScaleMax = 1.25f },
                    new Prop { Prefabs = P("WillowTree_04_Blue", "WillowTree_05_Blue"), PerHundredSqM = 0.6f,
                               Spacing = 5.5f, RoadClearance = 3f, ScaleMin = 0.9f, ScaleMax = 1.2f },
                    new Prop { Prefabs = P("Stones_03", "Rock_Small_01"), PerHundredSqM = 0.4f,
                               Spacing = 2.5f, RoadClearance = 1.5f, MaxSlope = 90f, Tilt = 10f, SinkIntoGround = true },
                },
            },

            // Low and scrubby, so the northern end of the network does not close on another canopy.
            new Theme
            {
                Name = "Glade of Stones", Centroid = new Vector2(96.6f, 172.9f),
                Covers = new[]
                {
                    new Cover { Prototypes = AllGrass, PerSqM = 2.40f, Clump = 0.07f, Bare = 0.30f },
                    new Cover { Prototypes = AllPlants, PerSqM = 0.30f, Clump = 0.11f, Bare = 0.38f },
                    new Cover { Prototypes = D(D_MeadowWhite, D_FlowerWhite), PerSqM = 0.20f, Clump = 0.15f, Bare = 0.45f },
                },
                Props = new[]
                {
                    new Prop { Prefabs = P("Bush_03_01", "Bush_03_02"), PerHundredSqM = 1.6f,
                               Spacing = 3f, RoadClearance = 2f, ScaleMin = 0.8f, ScaleMax = 1.3f },
                    new Prop { Prefabs = P("Stone_Big_02", "Stone_Big_03", "Stone_Medium_01", "Stones_02"),
                               PerHundredSqM = 1.0f, Spacing = 2.5f, RoadClearance = 1.5f,
                               MaxSlope = 90f, Tilt = 11f, SinkIntoGround = true },
                    new Prop { Prefabs = P("BroadleafTree_03_Green"), PerHundredSqM = 0.3f,
                               Spacing = 7f, RoadClearance = 3.5f, ScaleMin = 0.85f, ScaleMax = 1.1f },
                },
            },
        };
    }

    // ------------------------------------------------------------------
    // Menu entries
    // ------------------------------------------------------------------

    [MenuItem("Tools/Jannah Garden/Foliage/Scatter By Region")]
    public static void Scatter()
    {
        var terrain = FindTerrain();
        if (terrain == null) return;

        Clear();
        ResolveLakeFootprint();

        var map = new RegionMap(terrain);
        var themes = BuildThemes();
        map.AssignThemes(themes);

        var root = new GameObject(FoliageRootName);
        Undo.RegisterCreatedObjectUndo(root, "Scatter Foliage");
        var env = GameObject.Find("Environment");
        if (env != null) root.transform.SetParent(env.transform, true);

        PaintCover(terrain, map, themes);
        int placed = PlaceProps(terrain, map, themes, root.transform);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[GardenRegionScatter] " + map.Report() + "Placed " + placed +
                  " prop instances under Environment/" + FoliageRootName + ".");
    }

    [MenuItem("Tools/Jannah Garden/Foliage/Clear")]
    public static void Clear()
    {
        var terrain = FindTerrain();
        if (terrain == null) return;

        var td = terrain.terrainData;
        int dres = td.detailResolution;
        var empty = new int[dres, dres];
        Undo.RegisterCompleteObjectUndo(td, "Clear Foliage");
        for (int i = 0; i < td.detailPrototypes.Length; i++) td.SetDetailLayer(0, 0, i, empty);

        var env = GameObject.Find("Environment");
        Transform existing = env != null ? env.transform.Find(FoliageRootName) : null;
        if (existing == null)
        {
            var loose = GameObject.Find(FoliageRootName);
            if (loose != null) existing = loose.transform;
        }
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
    }

    /// <summary>Writes a colour-coded map of the pockets, for eyeballing the split.</summary>
    [MenuItem("Tools/Jannah Garden/Foliage/Export Region Map PNG")]
    public static void ExportRegionMap()
    {
        var terrain = FindTerrain();
        if (terrain == null) return;
        var map = new RegionMap(terrain);
        map.AssignThemes(BuildThemes());
        string path = EditorUtility.SaveFilePanel("Region map", "", "garden_regions.png", "png");
        if (string.IsNullOrEmpty(path)) return;
        System.IO.File.WriteAllBytes(path, map.ToPng());
        Debug.Log("[GardenRegionScatter] Wrote " + path + "\n" + map.Report());
    }

    private static Terrain FindTerrain()
    {
        var go = GameObject.Find(TerrainName);
        var t = go != null ? go.GetComponent<Terrain>() : null;
        if (t == null) Debug.LogError("[GardenRegionScatter] No Terrain named '" + TerrainName + "' in the open scene.");
        return t;
    }

    // ------------------------------------------------------------------
    // Region analysis
    // ------------------------------------------------------------------

    /// <summary>
    /// Flood-fills the terrain into the pockets the painted roads carve out, and records how far every
    /// cell sits from the nearest road so props can be kept clear of the paths.
    /// </summary>
    private class RegionMap
    {
        /// <summary>
        /// The painted-road fill itself. Shared with the placement area bake so both run off one
        /// definition of where an area ends (see <see cref="GardenTerrainRegions"/>).
        /// </summary>
        private readonly GardenTerrainRegions _regions;

        public int Res => _regions.Res;
        public float CellSize => _regions.CellSize;
        public Vector3 Origin => _regions.Origin;
        public Vector3 Size => _regions.Size;

        /// <summary>-1 for road/sand, otherwise a 1-based pocket label.</summary>
        public int[] Label => _regions.Label;
        /// <summary>Metres from each cell to the nearest road or sand cell.</summary>
        public float[] RoadDistance => _regions.RoadDistance;
        /// <summary>Metres from each cell to the terrain edge.</summary>
        public float[] EdgeDistance => _regions.EdgeDistance;

        public Dictionary<int, float> AreaByLabel => _regions.AreaByLabel;
        public Dictionary<int, Vector2> CentroidByLabel => _regions.CentroidByLabel;

        /// <summary>Pocket label for each theme index, or -1 when no pocket matched.</summary>
        public int[] LabelForTheme;
        private Theme[] _themes;

        // No lake exclusion here on purpose: the scatter's themes are matched to pockets by centroid,
        // and carving the lake out would renumber and reshape those pockets. The water rules in
        // PassesWaterRule already keep props off the lake.
        public RegionMap(Terrain terrain)
        {
            _regions = new GardenTerrainRegions(terrain);
        }

        /// <summary>
        /// Ties each authored theme to the pocket it was written for by centroid rather than by label
        /// index, so a small edit to the painted roads either still matches — or reports that it did
        /// not, instead of silently shuffling every theme to a different part of the map.
        /// </summary>
        public void AssignThemes(Theme[] themes)
        {
            _themes = themes;
            LabelForTheme = new int[themes.Length];
            var taken = new HashSet<int>();
            for (int i = 0; i < themes.Length; i++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                foreach (var kv in CentroidByLabel)
                {
                    if (taken.Contains(kv.Key)) continue;
                    if (AreaByLabel[kv.Key] < 100f) continue;
                    float d = Vector2.Distance(kv.Value, themes[i].Centroid);
                    if (d < bestD) { bestD = d; best = kv.Key; }
                }
                // A pocket more than 12 m from where the theme expects it is not that pocket.
                if (best >= 0 && bestD <= 12f) { LabelForTheme[i] = best; taken.Add(best); }
                else LabelForTheme[i] = -1;
            }
        }

        public int ThemeIndexAt(int cell)
        {
            int lab = Label[cell];
            if (lab <= 0) return -1;
            for (int i = 0; i < LabelForTheme.Length; i++) if (LabelForTheme[i] == lab) return i;
            return -1;
        }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Roads split the terrain into " +
                          AreaByLabel.Count(kv => kv.Value >= 100f) + " pockets:");
            for (int i = 0; i < _themes.Length; i++)
            {
                int lab = LabelForTheme[i];
                if (lab < 0)
                {
                    sb.AppendLine("  " + _themes[i].Name + ": NO MATCHING POCKET (roads changed?) - skipped");
                    continue;
                }
                var c = CentroidByLabel[lab];
                sb.AppendLine("  " + _themes[i].Name.PadRight(20) +
                              AreaByLabel[lab].ToString("F0").PadLeft(6) + " m2  centre (" +
                              c.x.ToString("F0") + ", " + c.y.ToString("F0") + ")");
            }
            return sb.ToString();
        }

        public byte[] ToPng()
        {
            var tex = new Texture2D(Res, Res, TextureFormat.RGB24, false);
            var px = new Color32[Res * Res];
            var palette = new[]
            {
                new Color32(120, 170, 110, 255), new Color32(244, 164, 196, 255), new Color32(250, 224, 120, 255),
                new Color32(46, 102, 78, 255),   new Color32(96, 178, 214, 255),  new Color32(168, 160, 150, 255),
                new Color32(160, 110, 210, 255), new Color32(226, 118, 62, 255),  new Color32(240, 150, 170, 255),
                new Color32(74, 120, 220, 255),  new Color32(140, 168, 120, 255),
            };
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    int c = y * Res + x;
                    Color32 col;
                    if (Label[c] < 0) col = new Color32(214, 160, 120, 255);   // road / sand
                    else
                    {
                        int ti = ThemeIndexAt(c);
                        col = ti < 0 ? new Color32(40, 40, 40, 255) : palette[ti % palette.Length];
                    }
                    px[c] = col;   // row 0 is low Z, which is the bottom row of the image
                }
            tex.SetPixels32(px);
            tex.Apply();
            byte[] bytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            return bytes;
        }
    }

    // ------------------------------------------------------------------
    // Ground cover
    // ------------------------------------------------------------------

    private static void PaintCover(Terrain terrain, RegionMap map, Theme[] themes)
    {
        var td = terrain.terrainData;
        int dres = td.detailResolution;
        int protoCount = td.detailPrototypes.Length;
        float detailCell = td.size.x / dres;
        float detailCellArea = detailCell * detailCell;

        Undo.RegisterCompleteObjectUndo(td, "Paint Ground Cover");

        var layers = new int[protoCount][,];
        for (int i = 0; i < protoCount; i++) layers[i] = new int[dres, dres];

        var rnd = new System.Random(Seed);
        // Per-cover noise offsets, so the grass drifts and the flower drifts are not the same shape.
        var offsets = new Dictionary<Cover, Vector2>();
        foreach (var th in themes)
            foreach (var cv in th.Covers)
                offsets[cv] = new Vector2((float)rnd.NextDouble() * 1000f, (float)rnd.NextDouble() * 1000f);

        for (int dy = 0; dy < dres; dy++)
        {
            for (int dx = 0; dx < dres; dx++)
            {
                // Detail maps are indexed [z, x] like alphamaps; sample the region map at the matching
                // normalised position.
                int ax = Mathf.Min(map.Res - 1, dx * map.Res / dres);
                int az = Mathf.Min(map.Res - 1, dy * map.Res / dres);
                int acell = az * map.Res + ax;

                int ti = map.ThemeIndexAt(acell);
                if (ti < 0) continue;

                float wx = map.Origin.x + (dx + 0.5f) * detailCell;
                float wz = map.Origin.z + (dy + 0.5f) * detailCell;
                float roadDist = map.RoadDistance[acell];
                float height = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + map.Origin.y;

                foreach (var cv in themes[ti].Covers)
                {
                    if (roadDist < cv.RoadClearance) continue;
                    if (!WaterOk(cv.Water, wx, wz, height)) continue;

                    Vector2 off = offsets[cv];
                    float noise = Mathf.PerlinNoise(off.x + wx * cv.Clump, off.y + wz * cv.Clump);
                    if (noise < cv.Bare) continue;

                    // Renormalise above the bare threshold so drifts fade in rather than switch on,
                    // and let cover thin out as it approaches a path edge.
                    float strength = (noise - cv.Bare) / Mathf.Max(0.001f, 1f - cv.Bare);
                    float edgeFade = Mathf.Clamp01((roadDist - cv.RoadClearance) / 2f);

                    // Both the bare cut and the fade-in throw instances away, so without compensation
                    // PerSqM would be an upper bound nowhere reached rather than the average it reads
                    // as. Treating the noise as uniform, the surviving mean is (1 - Bare) / 2.
                    float maskMean = Mathf.Max(0.05f, (1f - cv.Bare) * 0.5f);
                    float expected = cv.PerSqM * detailCellArea * strength * edgeFade / maskMean;

                    int whole = Mathf.FloorToInt(expected);
                    if (rnd.NextDouble() < expected - whole) whole++;
                    if (whole <= 0) continue;

                    int proto = cv.Prototypes[rnd.Next(cv.Prototypes.Length)];
                    layers[proto][dy, dx] += whole;
                }
            }
        }

        for (int i = 0; i < protoCount; i++) td.SetDetailLayer(0, 0, i, layers[i]);
    }

    private static bool WaterOk(WaterRule rule, float wx, float wz, float height)
    {
        // Away from the lake plane there is no water to be in, on or beside.
        if (!_lakeFootprint.Contains(new Vector2(wx, wz)))
            return rule == WaterRule.LandOnly;

        switch (rule)
        {
            case WaterRule.LandOnly: return height > WaterLevel + 0.45f;
            case WaterRule.ShoreOnly: return height > WaterLevel - 0.55f && height < WaterLevel + 1.4f;
            case WaterRule.SubmergedOnly: return height < WaterLevel - 0.3f;
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Props
    // ------------------------------------------------------------------

    private static int PlaceProps(Terrain terrain, RegionMap map, Theme[] themes, Transform root)
    {
        var rnd = new System.Random(Seed ^ 0x5f3a);
        var occupied = new SpatialHash(4f);
        var reserved = BuildExclusions();

        int placed = 0;
        var cache = new Dictionary<string, GameObject>();

        for (int ti = 0; ti < themes.Length; ti++)
        {
            int lab = map.LabelForTheme[ti];
            if (lab < 0) continue;

            var cells = CellsOfLabel(map, lab);
            if (cells.Count == 0) continue;

            var themeRoot = new GameObject(themes[ti].Name);
            themeRoot.transform.SetParent(root, false);
            float area = map.AreaByLabel[lab];

            foreach (var prop in themes[ti].Props)
            {
                int want = Mathf.RoundToInt(area / 100f * prop.PerHundredSqM);
                if (want <= 0) continue;

                var familyRoot = new GameObject(prop.Prefabs[0].Split('_')[0]);
                familyRoot.transform.SetParent(themeRoot.transform, false);

                int made = 0;
                // Dart throwing with a generous budget, because rejection is the point: the survivors
                // are what is left after slope, water, road clearance and spacing have had their say.
                int attempts = want * 200;
                for (int a = 0; a < attempts && made < want; a++)
                {
                    int cell = cells[rnd.Next(cells.Count)];
                    int cz = cell / map.Res, cx = cell % map.Res;
                    float wx = map.Origin.x + (cx + (float)rnd.NextDouble()) * map.CellSize;
                    float wz = map.Origin.z + (cz + (float)rnd.NextDouble()) * map.CellSize;

                    if (map.RoadDistance[cell] < prop.RoadClearance) continue;
                    if (prop.EdgeBandOnly > 0f && map.EdgeDistance[cell] > prop.EdgeBandOnly) continue;

                    float h = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + map.Origin.y;
                    if (!WaterOk(prop.Water, wx, wz, h)) continue;

                    Vector3 normal = terrain.terrainData.GetInterpolatedNormal(
                        (wx - map.Origin.x) / map.Size.x, (wz - map.Origin.z) / map.Size.z);
                    if (Vector3.Angle(normal, Vector3.up) > prop.MaxSlope) continue;

                    var pos = new Vector3(wx, h, wz);
                    if (InReserved(reserved, pos)) continue;
                    if (occupied.AnyWithin(pos, prop.Spacing)) continue;

                    string name = prop.Prefabs[rnd.Next(prop.Prefabs.Length)];
                    GameObject asset;
                    if (!cache.TryGetValue(name, out asset))
                    {
                        asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + name + ".prefab");
                        cache[name] = asset;
                        if (asset == null) Debug.LogWarning("[GardenRegionScatter] Missing prefab: " + name);
                    }
                    if (asset == null) continue;

                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, familyRoot.transform);
                    float s = Mathf.Lerp(prop.ScaleMin, prop.ScaleMax, (float)rnd.NextDouble());
                    if (prop.SinkIntoGround) pos.y -= s * 0.25f;
                    inst.transform.position = pos;
                    inst.transform.rotation = Quaternion.Euler(
                        prop.Tilt * ((float)rnd.NextDouble() * 2f - 1f),
                        (float)rnd.NextDouble() * 360f,
                        prop.Tilt * ((float)rnd.NextDouble() * 2f - 1f));
                    inst.transform.localScale = Vector3.one * s;
                    // Occludee only: occlusion culling can drop these without the build having to bake
                    // static batches or lightmap UVs for several hundred plants.
                    GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.OccludeeStatic);

                    occupied.Add(pos);
                    made++;
                    placed++;
                }

                familyRoot.name = familyRoot.name + " x" + made;
                if (made == 0) UnityEngine.Object.DestroyImmediate(familyRoot);
            }
        }
        return placed;
    }

    /// <summary>
    /// Circles the scatter must stay out of — the gazebo floors and the player spawn — so it never
    /// drops a fir tree through a piece of built content. Returned as (centre XZ, radius).
    /// </summary>
    private static List<Vector3> BuildExclusions()
    {
        var list = new List<Vector3>();
        var env = GameObject.Find("Environment");
        var water = env != null ? env.transform.Find("Water") : null;
        if (water != null)
            foreach (var r in water.GetComponentsInChildren<Renderer>(true))
            {
                // The Lake plane spans most of the basin; it is handled by the water height rules,
                // not by an exclusion circle that would blank the whole lakeside.
                if (r.bounds.size.x > 40f) continue;
                float radius = Mathf.Max(r.bounds.extents.x, r.bounds.extents.z) + 3f;
                list.Add(new Vector3(r.bounds.center.x, radius, r.bounds.center.z));
            }

        var player = GameObject.FindWithTag("Player");
        if (player != null) list.Add(new Vector3(player.transform.position.x, 6f, player.transform.position.z));
        return list;
    }

    private static bool InReserved(List<Vector3> reserved, Vector3 p)
    {
        for (int i = 0; i < reserved.Count; i++)
        {
            float dx = reserved[i].x - p.x, dz = reserved[i].z - p.z;
            float r = reserved[i].y;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    private static List<int> CellsOfLabel(RegionMap map, int label)
    {
        var list = new List<int>();
        for (int i = 0; i < map.Label.Length; i++) if (map.Label[i] == label) list.Add(i);
        return list;
    }

    /// <summary>Uniform-grid neighbour lookup, so spacing checks stay O(1) instead of O(placed).</summary>
    private class SpatialHash
    {
        private readonly float _cell;
        private readonly Dictionary<long, List<Vector3>> _buckets = new Dictionary<long, List<Vector3>>();

        public SpatialHash(float cell) { _cell = cell; }

        private long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

        public void Add(Vector3 p)
        {
            long k = Key(Mathf.FloorToInt(p.x / _cell), Mathf.FloorToInt(p.z / _cell));
            List<Vector3> bucket;
            if (!_buckets.TryGetValue(k, out bucket)) { bucket = new List<Vector3>(); _buckets[k] = bucket; }
            bucket.Add(p);
        }

        public bool AnyWithin(Vector3 p, float distance)
        {
            int reach = Mathf.CeilToInt(distance / _cell);
            int bx = Mathf.FloorToInt(p.x / _cell), bz = Mathf.FloorToInt(p.z / _cell);
            for (int x = bx - reach; x <= bx + reach; x++)
                for (int z = bz - reach; z <= bz + reach; z++)
                {
                    List<Vector3> bucket;
                    if (!_buckets.TryGetValue(Key(x, z), out bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        float dx = bucket[i].x - p.x, dz = bucket[i].z - p.z;
                        if (dx * dx + dz * dz < distance * distance) return true;
                    }
                }
            return false;
        }
    }
}
