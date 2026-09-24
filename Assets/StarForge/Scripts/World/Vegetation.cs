// Vegetation.cs — the map's trees and bushes, and what a battle does to them.
//
// MapBuilder writes every plant into this component (position, turn, size and
// kind) and, for trees that stand where units walk, a NavMeshModifierVolume
// marking the ground under the trunk as Rubble, the area boulders use: infantry,
// Diggers and Skimmers path round a trunk, a Mauler's path runs straight
// through it and the Mauler knocks the tree down. Explosions throw trees over
// and set them alight, fire spreads through a grove for a while and burns the
// crowns away, and a burned-out tree may come down on its own. A tree that
// falls frees its ground and GameWorld rebuilds the NavMesh tiles it covered.
//
// The wind (World/Wind) is what the fire runs with: downwind spread, embers
// thrown downwind, and the whole thing faster in a gust than in a lull.
//
// Everything green burns -- trees, bushes, ferns, reeds and the grass between
// them -- each at its own rate (dead wood and reeds readily, resinous pines hard,
// green broadleaf slowly), and nothing near the water takes fire easily: damp
// ground and the plants standing on it barely catch at all.
//
// A fire runs downwind: it passes to what stands that way far more readily than
// across or against it, so a blaze works along a grove as a front. The grass
// carries it between the stands: a coarse fuel grid built from the same splat
// weights the ground cover grows on, burning cell by cell, lighting the plants
// it reaches and leaving the ground charred behind it.
//
// How far one fire gets is drawn when it starts (its vigour): most are over in a
// few plants, and a few run for dozens. Each blaze also gets a budget of what it
// may take, so no single fire can burn the whole map -- but a match's fires
// together can, and a map worked over all game ends up black.
//
// Rules only: VegetationRenderer draws the plants (as the player last saw
// them), FXDirector burns and splinters them, GroundMask chars the grass round
// a fire. Nothing here reads enemy state or gives either side an edge.
using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public enum PlantState : byte { Standing, Falling, Down, Gone }

    [Serializable]
    public struct Plant
    {
        public Vector3 pos;
        public float yaw;
        public float scale;
        [Tooltip("Height multiplier on top of scale.")] public float stretch;
        public byte kind;
        [Tooltip("Index into Vegetation.volumes, or -1 when the plant blocks nothing.")] public short volume;
    }

    [Serializable]
    public sealed class PlantKind
    {
        public string name;
        public Mesh mesh;
        [Tooltip("Submesh drawn as bark / as foliage, or -1 if the model has none.")]
        public int barkSubmesh = 0, foliageSubmesh = 1;
        [Tooltip("Decimated copy for plants far from the camera (same submeshes); optional.")]
        public Mesh lodMesh;
        public bool bush;
        [Tooltip("Metres at scale 1.")] public float trunkRadius = 0.35f;
        [Tooltip("Half-width of the ground a standing tree closes to ground units, metres at scale 1 (0: not a tree that blocks). Covers the trunk, the low boughs and a unit's own radius: the Rubble area it marks is not eroded by agent radius the way an obstacle is.")]
        public float blockRadius;
        public float height = 7f;
        public Vector3 crownCenter = new Vector3(0f, 5f, 0f);
        public Vector3 crownRadii = new Vector3(2.5f, 2f, 2.5f);
        public Color leaf = new Color(0.13f, 0.22f, 0.07f);
        public Color leaf2 = new Color(0.24f, 0.29f, 0.08f);
        public Color bark = new Color(0.16f, 0.12f, 0.09f);
        [Tooltip("The leaf-spray cards the crown is clothed in (Tools/blender/make_leaf_cards.py), and their normals.")]
        public Texture2D cardTex, cardNormal;
        [Tooltip("Multiplies the cards' photographed colour.")]
        public Color cardTint = Color.white;
        [Tooltip("Needle tufts on the crown instead of broad leaves (SF_Tree _LeafStyle).")]
        public bool needles;
        [Tooltip("Blossom colour; alpha is the share of the leaves in flower.")]
        public Color bloom = new Color(1f, 1f, 1f, 0f);
        [Tooltip("Pale birch bark with dark marks instead of furrowed bark.")]
        public bool birchBark;
        [Tooltip("Leaf texture repeats per metre: smaller leaves on smaller plants.")]
        public float leafTiling = 0.9f;
        [Tooltip("How readily it catches and passes fire on: 1 is ordinary green growth, dead wood and dry reeds more, sappy green leaves less.")]
        public float burns = 1f;
        [Tooltip("Not drawn farther than this from the camera (m); 0 draws it at any distance. Undergrowth is lost in the ground cover long before then.")]
        public float drawDistance;
        [Tooltip("Casts a shadow. Undergrowth does not: its shadow is a smudge the SSAO already gives.")]
        public bool castShadows = true;

        public bool HasCrown => crownRadii.sqrMagnitude > 0.01f;
    }

    public sealed class Vegetation : MonoBehaviour
    {
        public PlantKind[] kinds = Array.Empty<PlantKind>();
        public Plant[] plants = Array.Empty<Plant>();
        public NavMeshModifierVolume[] volumes = Array.Empty<NavMeshModifierVolume>();

        public struct Live
        {
            public PlantState state;
            public float t;              // seconds in the current state
            public bool burning;
            public float fuel;           // seconds of burning left
            public float fire;           // flame size, 0..1
            public float charred;        // 0..1
            public float foliageLost;    // 0..1
            public Vector2 fallDir;
            public float fallAngle, fallSpeed;   // radians from upright, radians a second
            public float rest;                   // the angle it comes to rest at (a crown holds it off the ground)
            public int lodged;                   // the tree it is hung up on, or -1
            public float slipAt;                 // when a hung-up tree gives way
            public float flatten;        // a crushed bush, 0..1
            public float sink;           // metres into the ground as it rots away
            public float groundShift;    // craters opened under it since the match began
            public byte generation;      // how many plants the fire has passed through to get here
            public byte blaze;           // which fire this is part of (indexes the budgets)
            public float vigour;         // how hard this blaze is running, 0..1 (drawn where it started)
            public float wet;            // 0 dry .. 1 standing in water: damp ground hardly catches
            public float nextSpread;
        }

        [NonSerialized] public Live[] live = Array.Empty<Live>();
        public static Vegetation Instance { get; private set; }

        /// <summary>Plants on fire right now (indices), for effects and the ground mask.</summary>
        public readonly List<int> burning = new List<int>(64);

        /// <summary>Grass cells on fire right now, for effects and the ground mask.</summary>
        public readonly List<int> burningGrass = new List<int>(64);

        const float Cell = 8f;
        int fires;       // plants burning, kept outside the per-frame list so ignitions mid-tick count
        int burned;      // plants the match has set alight in all (for the report)
        bool ticking;
        const int MaxBurning = 45;
        const int MaxGrassFires = 150;
        // Each fire's own budget, so one blaze runs out of reach before it takes the
        // map; a match's blazes together still can. Indexed by Live.blaze.
        const int Blazes = 64;
        readonly int[] blazePlants = new int[Blazes], blazeCells = new int[Blazes],
                       blazeCapPlants = new int[Blazes], blazeCapCells = new int[Blazes];
        readonly float[] blazeVig = new float[Blazes];
        int nextBlaze;
        int gridSide;
        int[] cellStart, cellItems;
        readonly Rng rng = new Rng(0x7EE5u);

        // ---- the grass: a coarse fuel grid, one cell every GrassCell metres.
        // Runtime state, built on the first tick. NonSerialized: entering play mode the
        // editor round-trips private fields through serialization, which turns a null
        // array into an empty one, and an empty grid that looks built indexed -1.
        public const float GrassCell = 3f;
        [NonSerialized] int grassSide;
        [NonSerialized] byte[] grassFuel;      // 0 none .. 255 thick meadow (how much of it grows grass, dried by the water)
        [NonSerialized] ushort[] grassMask;    // which of its 3x3 metre squares grow grass (bit 3*row+col)
        [NonSerialized] byte[] grassState;     // 0 unburnt, 1 burning, 2 burnt out
        [NonSerialized] byte[] grassBlaze;
        [NonSerialized] float[] grassUntil;    // when this cell burns out
        [NonSerialized] float[] grassNext;     // when it next tries its neighbours
        [NonSerialized] float[] grassY;        // the ground height at its middle (the effects ask every frame)

        void Awake()
        {
            Instance = this;
            live = new Live[plants.Length];
            BuildGrid();
        }

        [NonSerialized] bool grassReady;

        /// <summary>The grass fuel and every plant's dampness, worked out on the first
        /// tick: the map (MapInfo, the terrain, the water) is only there once everything
        /// has woken, and this component's Awake can run before it.</summary>
        void EnsureGrass(GameWorld world)
        {
            if (grassReady || MapInfo.Instance == null || MapInfo.Instance.terrain == null) return;
            grassReady = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BuildGrassFuel();
            for (int i = 0; i < live.Length; i++) live[i].wet = Wetness(Pos2(i));
            // The grass is flattened under structures and ore seams, so nothing there burns.
            foreach (var u in world.units)
                if (u != null && !u.dying) ClearGrassUnder(u);
            GrassBuildMs = clock.ElapsedMilliseconds;
        }

        /// <summary>How long the fuel grid took to build on the first tick (for the report).</summary>
        public long GrassBuildMs { get; private set; }

        /// <summary>A structure or an ore seam stands here: the grass under it is flattened
        /// (GroundMask) and must not burn under its walls.</summary>
        public void ClearGrassUnder(Unit u)
        {
            if (u == null || !grassReady) return;
            if (u.def.building) ClearGrass(u.pos, u.def.radius + 0.8f);
            else if (u.Type == UnitType.Ore) ClearGrass(u.pos, u.def.radius + 0.5f);
        }

        /// <summary>Take the grass inside a circle out of the fuel.</summary>
        public void ClearGrass(Vector2 c, float radius)
        {
            if (!grassReady || grassSide <= 0) return;
            int x0 = Mathf.Clamp((int)((c.x - radius) / GrassCell), 0, grassSide - 1), x1 = Mathf.Clamp((int)((c.x + radius) / GrassCell), 0, grassSide - 1);
            int z0 = Mathf.Clamp((int)((c.y - radius) / GrassCell), 0, grassSide - 1), z1 = Mathf.Clamp((int)((c.y + radius) / GrassCell), 0, grassSide - 1);
            float r2 = radius * radius;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int cell = z * grassSide + x;
                    int mask = grassMask[cell];
                    if (mask == 0 || grassState[cell] == 1) continue;
                    int before = Bits(mask);
                    for (int b = 0; b < 9; b++)
                        if ((mask & (1 << b)) != 0 && (SubCentre(cell, b) - c).sqrMagnitude < r2) mask &= ~(1 << b);
                    int after = Bits(mask);
                    if (after == before) continue;
                    grassMask[cell] = (ushort)(after >= MinTufts ? mask : 0);
                    grassFuel[cell] = after >= MinTufts ? (byte)(grassFuel[cell] * after / before) : (byte)0;
                }
        }

        static int Bits(int m)
        {
            int n = 0;
            for (; m != 0; m &= m - 1) n++;
            return n;
        }

        /// <summary>The middle of one of a cell's nine one-metre squares.</summary>
        Vector2 SubCentre(int cell, int bit)
        {
            int x = cell % grassSide, z = cell / grassSide;
            return new Vector2((x + ((bit % 3) + 0.5f) / 3f) * GrassCell, (z + ((bit / 3) + 0.5f) / 3f) * GrassCell);
        }

        /// <summary>How damp the ground is here, 0 dry .. 1 in the water: the water level
        /// against the ground at the spot and a few metres around it. Reeds in a lake and
        /// the grass of a shore hardly take fire; a meadow on a terrace above it does.</summary>
        public float Wetness(Vector2 p)
        {
            var map = MapInfo.Instance;
            if (map == null) return 0f;
            float d = map.WaterDepth(p);                 // >0 under water, <0 above it
            for (int k = 0; k < 4; k++)
            {
                float a = k * TAU / 4f;
                var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5f;
                if (map.InBounds(q)) d = Mathf.Max(d, map.WaterDepth(q));
            }
            return Mathf.Clamp01((d + 3.5f) / 3.5f);
        }

        /// <summary>The grass between the plants, as fuel. Built from the grass itself:
        /// each 3 m cell is looked at in nine one-metre squares, by the same rule
        /// GroundScatter grows its tufts by (MapGenerator.GrassChance, over the terrain's
        /// own splat weights, with the same water, slope and boulder cut-outs). A cell
        /// burns only if at least MinTufts of its squares grow grass, and how much of it
        /// does is its fuel; bare gravel, rock and sand between the meadows carry nothing,
        /// so a grass fire stops where the grass does. Damp ground near water is damped
        /// further (Wetness).</summary>
        void BuildGrassFuel()
        {
            var map = MapInfo.Instance;
            if (map == null || map.terrain == null) return;
            float size = map.mapSize;
            grassSide = Mathf.CeilToInt(size / GrassCell);
            int n = grassSide * grassSide;
            grassFuel = new byte[n];
            grassMask = new ushort[n];
            grassState = new byte[n];
            grassBlaze = new byte[n];
            grassUntil = new float[n];
            grassNext = new float[n];
            grassY = new float[n];
            var td = map.terrain.terrainData;
            float baseY = map.terrain.transform.position.y, tsize = td.size.x;
            int aw = td.alphamapWidth, ah = td.alphamapHeight, layers = td.alphamapLayers;
            float[,,] alpha = td.GetAlphamaps(0, 0, aw, ah);
            Vector4 Weights(float x, float z)
            {
                int ax = Mathf.Clamp((int)(x / tsize * aw), 0, aw - 1), az = Mathf.Clamp((int)(z / tsize * ah), 0, ah - 1);
                return new Vector4(alpha[az, ax, 0], layers > 1 ? alpha[az, ax, 1] : 0f,
                                   layers > 2 ? alpha[az, ax, 2] : 0f, layers > 3 ? alpha[az, ax, 3] : 0f);
            }
            // Boulders keep their ground clear, as GroundScatter leaves it.
            var boulders = new List<Vector3>();
            if (map.boulderRoot != null)
                foreach (Transform b in map.boulderRoot)
                    boulders.Add(new Vector3(b.position.x, 1.5f * b.lossyScale.x, b.position.z));
            float water = map.waterLevel;
            for (int z = 0; z < grassSide; z++)
                for (int x = 0; x < grassSide; x++)
                {
                    int cell = z * grassSide + x;
                    var mid = new Vector2((x + 0.5f) * GrassCell, (z + 0.5f) * GrassCell);
                    grassY[cell] = map.HeightAt(mid);
                    if (!map.InBounds(mid, 1f)) continue;
                    int mask = 0;
                    float cover = 0f;
                    for (int b = 0; b < 9; b++)
                    {
                        var q = SubCentre(cell, b);
                        MapGenerator.GrassChance(Weights(q.x, q.y), q.x, q.y, out float lush, out float dry);
                        float chance = lush + dry;
                        if (chance < 0.02f) continue;
                        float h = td.GetInterpolatedHeight(q.x / tsize, q.y / tsize) + baseY;
                        if (h < water + 0.4f) continue;
                        if (td.GetInterpolatedNormal(q.x / tsize, q.y / tsize).y < MapGenerator.GrassMinUp) continue;
                        bool blocked = false;
                        foreach (var bl in boulders)
                            if ((bl.x - q.x) * (bl.x - q.x) + (bl.z - q.y) * (bl.z - q.y) < bl.y * bl.y) { blocked = true; break; }
                        if (blocked) continue;
                        cover += Mathf.Min(1f, chance);
                        if (chance >= TuftChance) mask |= 1 << b;
                    }
                    if (Bits(mask) < MinTufts || cover * TuftsPerSquare < MinExpectedTufts) continue;
                    float fuel = cover / 9f * (1f - Wetness(mid) * 0.95f);
                    if (fuel < 0.1f) continue;
                    grassMask[cell] = (ushort)mask;
                    grassFuel[cell] = (byte)(Mathf.Clamp01(fuel) * 255f);
                    grassY[cell] = map.HeightAt(GrassMiddle(cell));
                }
        }

        /// <summary>A square counts as grown if a tuft is at least this likely on it.</summary>
        const float TuftChance = 0.3f;
        /// <summary>A cell needs this many grown squares (of nine) to carry a fire.</summary>
        const int MinTufts = 2;
        /// <summary>GroundScatter tries a tuft every 1.15 m at full density: 0.76 a square metre.</summary>
        public const float TuftsPerSquare = 1f / (1.15f * 1.15f);
        /// <summary>A cell burns only if this many tufts are expected on it at full
        /// density. The tufts fall at random, so a thin meadow edge often shows none in
        /// a given cell; below this, a fire there would look like burning bare ground.</summary>
        public const float MinExpectedTufts = 2.5f;

        /// <summary>The middle of the grass in a cell (of its grown squares).</summary>
        Vector2 GrassMiddle(int cell)
        {
            int mask = grassMask[cell];
            if (mask == 0)
            {
                int x = cell % grassSide, z = cell / grassSide;
                return new Vector2((x + 0.5f) * GrassCell, (z + 0.5f) * GrassCell);
            }
            Vector2 sum = Vector2.zero;
            int count = 0;
            for (int b = 0; b < 9; b++)
                if ((mask & (1 << b)) != 0) { sum += SubCentre(cell, b); count++; }
            return sum / count;
        }

        /// <summary>A point on the grass of a burning cell, for a tongue of flame: one of
        /// its grown squares (<paramref name="pick"/> 0..1 chooses which), anywhere in it
        /// (<paramref name="u"/>, <paramref name="v"/> 0..1). Flames never stand on the
        /// bare patches of a cell that is only partly grass.</summary>
        public Vector3 GrassTuft(int cell, float pick, float u, float v)
        {
            int mask = grassMask[cell];
            int count = Bits(mask);
            if (count == 0) return GrassCentre(cell);
            int want = Mathf.Min(count - 1, (int)(pick * count));
            for (int b = 0; b < 9; b++)
            {
                if ((mask & (1 << b)) == 0) continue;
                if (want-- > 0) continue;
                var c = SubCentre(cell, b) + new Vector2(u - 0.5f, v - 0.5f) * (GrassCell / 3f);
                return new Vector3(c.x, grassY[cell], c.y);
            }
            return GrassCentre(cell);
        }

        /// <summary>How hot the fire is within <paramref name="radius"/> of a point,
        /// 0..1: the strongest burning plant whose crown (or trunk, once down) reaches
        /// that far, or burning grass there. What a structure standing there feels.</summary>
        public float FireNear(Vector2 c, float radius, float now)
        {
            float heat = 0f;
            if (fires > 0)
                foreach (int i in Near(c, radius + 8f, nearFire))
                {
                    ref var s = ref live[i];
                    if (!s.burning || s.fire <= heat) continue;
                    var k = KindOf(i);
                    float reach = s.state == PlantState.Standing && k.HasCrown ? k.crownRadii.x * plants[i].scale : TrunkRadius(i) + 0.5f;
                    if ((Pos2(i) - c).magnitude - reach <= radius) heat = s.fire;
                }
            if (grassFuel != null && burningGrass.Count > 0)
            {
                int x0 = Mathf.Clamp((int)((c.x - radius) / GrassCell), 0, grassSide - 1), x1 = Mathf.Clamp((int)((c.x + radius) / GrassCell), 0, grassSide - 1);
                int z0 = Mathf.Clamp((int)((c.y - radius) / GrassCell), 0, grassSide - 1), z1 = Mathf.Clamp((int)((c.y + radius) / GrassCell), 0, grassSide - 1);
                float reach = radius + GrassCell * 0.5f;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int cell = z * grassSide + x;
                        if (grassState[cell] != 1) continue;
                        if ((GrassMiddle(cell) - c).sqrMagnitude > reach * reach) continue;
                        heat = Mathf.Max(heat, GrassFire(cell, now));
                    }
            }
            return heat;
        }

        readonly List<int> nearFire = new List<int>(64);

        /// <summary>A cell's fuel, 0 (bare: cannot burn) .. 255.</summary>
        public int GrassFuel(int cell) => grassFuel != null ? grassFuel[cell] : 0;

        public int GrassIndex(Vector2 p)
        {
            if (grassFuel == null) return -1;
            int x = Mathf.Clamp((int)(p.x / GrassCell), 0, grassSide - 1);
            int z = Mathf.Clamp((int)(p.y / GrassCell), 0, grassSide - 1);
            return z * grassSide + x;
        }

        /// <summary>The grass grid for the view: side, and per cell 0 unburnt, 1 burning, 2 burnt out.</summary>
        public int GrassSide => grassSide;
        public byte[] GrassState => grassState;
        /// <summary>Bumped whenever a cell changes state, so the view can rebuild its texture lazily.</summary>
        public int GrassVersion { get; private set; }

        /// <summary>The middle of a cell's grass (not of the cell: a cell at a meadow's
        /// edge is only partly grown), at ground height.</summary>
        public Vector3 GrassCentre(int cell)
        {
            var m = GrassMiddle(cell);
            return new Vector3(m.x, grassY[cell], m.y);
        }

        /// <summary>How hard a burning cell is going now, 0..1 (it flares and dies quickly).</summary>
        public float GrassFire(int cell, float now)
        {
            float left = grassUntil[cell] - now;
            return Mathf.Clamp01(Mathf.Min(left * 0.7f, (GrassBurnTime - left) * 2.5f));
        }

        const float GrassBurnTime = 7f;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void BuildGrid()
        {
            float size = MapInfo.Instance != null ? MapInfo.Instance.mapSize : 256f;
            gridSide = Mathf.CeilToInt(size / Cell) + 1;
            var counts = new int[gridSide * gridSide + 1];
            foreach (var p in plants) counts[CellOf(p.pos) + 1]++;
            for (int i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
            cellStart = counts;
            cellItems = new int[plants.Length];
            var fill = (int[])counts.Clone();
            for (int i = 0; i < plants.Length; i++) cellItems[fill[CellOf(plants[i].pos)]++] = i;
        }

        int CellOf(Vector3 p)
        {
            int x = Mathf.Clamp((int)(p.x / Cell), 0, gridSide - 1);
            int z = Mathf.Clamp((int)(p.z / Cell), 0, gridSide - 1);
            return z * gridSide + x;
        }

        readonly List<int> near = new List<int>(128);
        readonly List<int> nearCrown = new List<int>(64);

        /// <summary>Every plant whose cell lies within <paramref name="radius"/> of
        /// <paramref name="c"/>, into a shared list (callers do not nest). Lighting a
        /// plant raises an event, and what handles it may ask UnderCrown, so that one
        /// has a list of its own.</summary>
        List<int> Near(Vector2 c, float radius) => Near(c, radius, near);

        List<int> Near(Vector2 c, float radius, List<int> near)
        {
            near.Clear();
            int x0 = Mathf.Clamp((int)((c.x - radius) / Cell), 0, gridSide - 1), x1 = Mathf.Clamp((int)((c.x + radius) / Cell), 0, gridSide - 1);
            int z0 = Mathf.Clamp((int)((c.y - radius) / Cell), 0, gridSide - 1), z1 = Mathf.Clamp((int)((c.y + radius) / Cell), 0, gridSide - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int cell = z * gridSide + x;
                    for (int k = cellStart[cell]; k < cellStart[cell + 1]; k++) near.Add(cellItems[k]);
                }
            return near;
        }

        /// <summary>Every plant whose cell lies within <paramref name="radius"/> of
        /// <paramref name="c"/>, into the caller's own list. For presentation (what a
        /// blast's pressure front tears out of the crowns it crosses), which runs while
        /// the simulation's own list may be in use.</summary>
        public List<int> PlantsNear(Vector2 c, float radius, List<int> into) => Near(c, radius, into);

        /// <summary>Is this spot under a standing tree's crown (within <paramref name="margin"/> m of its edge)?
        /// Birds feed in the open, where they can be seen.</summary>
        public bool UnderCrown(Vector2 p, float margin)
        {
            if (plants.Length == 0) return false;
            foreach (int i in Near(p, 8f, nearCrown))
            {
                var k = KindOf(i);
                if (k.bush || !k.HasCrown || live[i].state != PlantState.Standing) continue;
                float r = k.crownRadii.x * plants[i].scale + margin;
                if ((Pos2(i) - p).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        public PlantKind KindOf(int i) => kinds[plants[i].kind];
        public float TrunkRadius(int i) => KindOf(i).trunkRadius * plants[i].scale;
        public Vector2 Pos2(int i) => new Vector2(plants[i].pos.x, plants[i].pos.z);
        public bool Blocks(int i) => plants[i].volume >= 0 && live[i].state == PlantState.Standing;

        /// <summary>The plant's transform as it stands, falls or lies now.</summary>
        public Matrix4x4 Pose(int i)
        {
            ref var p = ref plants[i];
            ref var s = ref live[i];
            var k = kinds[p.kind];
            Vector3 pos = p.pos + Vector3.up * (s.groundShift - s.sink);
            var rot = Quaternion.Euler(0f, p.yaw, 0f);
            if (s.fallAngle > 0f)
            {
                float sa = Mathf.Sin(s.fallAngle), ca = Mathf.Cos(s.fallAngle);
                rot = Quaternion.FromToRotation(Vector3.up, new Vector3(s.fallDir.x * sa, ca, s.fallDir.y * sa)) * rot;
            }
            float h = p.scale * p.stretch;
            if (k.bush && s.flatten > 0f) h *= Mathf.Lerp(1f, 0.22f, s.flatten);
            return Matrix4x4.TRS(pos, rot, new Vector3(p.scale, h, p.scale));
        }

        // ------------------------------------------------------------ rules
        public void Tick(GameWorld world, float dt)
        {
            if (plants.Length == 0) return;
            EnsureGrass(world);
            CrushUnderMaulers(world);
            TickGrass(world, dt);

            burning.Clear();
            ticking = true;
            for (int i = 0; i < live.Length; i++)
            {
                ref var s = ref live[i];
                if (s.state == PlantState.Gone) continue;
                s.t += dt;
                var k = KindOf(i);

                if (s.burning)
                {
                    bool fed = s.fuel > 0f;
                    s.fuel -= dt;
                    s.fire = Mathf.MoveTowards(s.fire, fed ? 1f : 0f, dt * (fed ? 0.45f * (0.7f + 0.5f * k.burns) : 0.3f));
                    s.charred = Mathf.Min(1f, s.charred + dt * (k.bush ? 0.14f : 0.075f));
                    s.foliageLost = Mathf.Min(1f, s.foliageLost + dt * s.fire * (k.bush ? 0.16f : 0.085f));
                    if (s.fire > 0.55f && world.time >= s.nextSpread)
                    {
                        s.nextSpread = world.time + rng.Range(0.7f, 1.3f);
                        Spread(world, i);
                        LightGrassRound(world, i);
                    }
                    if (!fed && s.fire <= 0.01f)
                    {
                        s.burning = false;
                        s.fire = 0f;
                        fires--;
                        // A burned-out tree may give way.
                        if (!k.bush && s.state == PlantState.Standing && rng.F01() < 0.4f)
                        {
                            // Burned through at the foot: it goes where the wind pushes it.
                            var w = Wind.At(world.time);
                            var a = w.sqrMagnitude > 0.01f ? w.normalized : Vector2.up;
                            float turn = rng.Range(-50f, 50f) * Mathf.Deg2Rad;
                            float ct = Mathf.Cos(turn), st = Mathf.Sin(turn);
                            Fell(world, i, new Vector2(a.x * ct - a.y * st, a.x * st + a.y * ct), 0.05f);
                        }
                    }
                    else burning.Add(i);
                }

                switch (s.state)
                {
                    case PlantState.Falling:
                        if (k.bush)
                        {
                            s.flatten = Mathf.Min(1f, s.flatten + dt * 5f);
                            if (s.flatten >= 1f) { s.state = PlantState.Down; s.t = 0f; }
                        }
                        else Topple(world, i, dt);
                        break;
                    case PlantState.Down:
                        // Lies where it fell for a while, then settles into the ground.
                        float stay = k.bush ? 20f : 30f;
                        if (s.t > stay && !s.burning)
                        {
                            s.sink += dt * 0.35f;
                            if (s.sink > (k.bush ? 1.2f : 1.4f) * plants[i].scale) s.state = PlantState.Gone;
                        }
                        break;
                }
            }
            ticking = false;
        }

        void CrushUnderMaulers(GameWorld world)
        {
            foreach (var u in world.units)
            {
                if (u == null || u.dying || u.Type != UnitType.Mauler || u.agent == null || !u.agent.enabled) continue;
                if (u.agent.velocity.sqrMagnitude < 0.25f) continue;
                Vector2 heading = new Vector2(Mathf.Sin(u.yaw), Mathf.Cos(u.yaw));
                foreach (int i in Near(u.pos, 6f))
                {
                    if (live[i].state != PlantState.Standing) continue;
                    float reach = u.def.radius * 0.85f + TrunkRadius(i) + (KindOf(i).bush ? 0.6f : 0.1f);
                    Vector2 d = Pos2(i) - u.pos;
                    if (d.sqrMagnitude > reach * reach) continue;
                    // Pushed over ahead of the hull, leaning a little off the side it was
                    // struck on. A Mauler leans on it and it goes slowly at first.
                    Fell(world, i, Norm(heading * 1.6f + Norm(d) * 0.6f), 0.3f);
                }
            }
        }

        /// <summary>A tree going over, as a rod pivoting on its stump: gravity's pull grows
        /// as it leans, so it starts slowly and comes down fast, the wind leans it, it
        /// hangs up in a neighbour's crown if one is in the way, and it thumps, bounces
        /// once or twice and settles when it lands.</summary>
        void Topple(GameWorld world, int i, float dt)
        {
            ref var s = ref live[i];
            var k = KindOf(i);
            float length = Mathf.Max(2f, k.height * plants[i].scale * plants[i].stretch);

            if (s.lodged >= 0)
            {
                // Hung up on another tree: it creaks there until it slips off -- or the
                // tree holding it goes, and it falls the rest of the way at once.
                bool held = live[s.lodged].state == PlantState.Standing;
                if (held && world.time < s.slipAt)
                {
                    s.fallSpeed *= 1f - Mathf.Min(1f, dt * 6f);
                    s.fallAngle += s.fallSpeed * dt + Mathf.Sin(world.time * 0.7f + i) * 0.004f * dt;
                    return;
                }
                s.lodged = -1;
            }

            // A uniform rod pivoting at its foot: 3g sin(t) / 2L, in radians a second.
            float acc = 1.5f * 9.81f / length * Mathf.Sin(s.fallAngle + 0.05f);
            // The wind helps it over, or holds it up a moment.
            var wind = Wind.At(world.time);
            acc += Vector2.Dot(wind, s.fallDir) * 0.35f / length;
            s.fallSpeed += acc * dt;
            s.fallSpeed -= s.fallSpeed * Mathf.Abs(s.fallSpeed) * 0.05f * dt;    // air, and the fibres tearing
            s.fallAngle += s.fallSpeed * dt;
            if (s.fallAngle < 0f) { s.fallAngle = 0f; s.fallSpeed = Mathf.Max(0f, s.fallSpeed); }

            if (s.lodged < 0 && s.fallAngle > 0.35f) LodgeCheck(world, i, length);

            if (s.fallAngle >= s.rest)
            {
                float hit = s.fallSpeed;
                s.fallAngle = s.rest;
                if (hit > 0.35f)
                {
                    // Down it comes: the trunk thumps, springs back a little and settles.
                    s.fallSpeed = -hit * 0.22f;
                    s.fallAngle -= 0.02f;
                    world.Raise(new GameEvent
                    {
                        kind = GameEventKind.PlantLanded, team = 2, index = i,
                        pos = plants[i].pos + Vector3.up * s.groundShift,
                        dir = new Vector3(s.fallDir.x, 0f, s.fallDir.y),
                        scale = plants[i].scale * plants[i].stretch,
                        speed = hit * length            // how fast the crown was travelling
                    });
                }
                else
                {
                    s.fallSpeed = 0f;
                    s.state = PlantState.Down;
                    s.t = 0f;
                }
            }
        }

        /// <summary>Is another tree in the way? Then it hangs up in that crown instead of
        /// reaching the ground -- the way a felled tree does in a close wood.</summary>
        void LodgeCheck(GameWorld world, int i, float length)
        {
            ref var s = ref live[i];
            var c = Pos2(i);
            foreach (int j in Near(c, length + 4f))
            {
                if (j == i || live[j].state != PlantState.Standing) continue;
                var kj = KindOf(j);
                if (kj.bush || !kj.HasCrown) continue;
                var d = Pos2(j) - c;
                float dist = d.magnitude;
                if (dist < 1f || dist > length) continue;
                if (Vector2.Dot(d / dist, s.fallDir) < 0.82f) continue;          // not in the way
                float crown = kj.crownRadii.x * plants[j].scale;
                float reach = Mathf.Clamp((dist - crown * 0.75f) / length, 0.25f, 0.98f);
                float angle = Mathf.Asin(reach);
                if (angle <= s.fallAngle + 0.02f) continue;                      // already past it
                // It only holds if the other tree is tall enough to take the weight.
                if (kj.height * plants[j].scale * plants[j].stretch < length * Mathf.Cos(angle) * 0.55f) continue;
                s.rest = angle;
                s.lodged = j;
                s.slipAt = world.time + rng.Range(6f, 40f);
                return;
            }
        }

        /// <summary><paramref name="push"/> is how hard it was started over, in radians a
        /// second: a blast shoves it, a Mauler leans on it, a burned-out snag just gives.</summary>
        public void Fell(GameWorld world, int i, Vector2 dir, float push = 0.25f)
        {
            ref var s = ref live[i];
            if (s.state != PlantState.Standing) return;
            var k = KindOf(i);
            s.state = PlantState.Falling;
            s.t = 0f;
            s.fallDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up;
            s.fallSpeed = push;
            s.lodged = -1;
            // It lies on its own boughs, not flat on the ground, so the bushier the
            // crown the higher the trunk ends up.
            s.rest = k.HasCrown
                ? Mathf.PI * 0.5f - Mathf.Clamp(k.crownRadii.y * plants[i].scale / Mathf.Max(3f, k.height * plants[i].scale), 0.04f, 0.22f)
                : Mathf.PI * 0.5f - 0.03f;
            int v = plants[i].volume;
            if (v >= 0 && v < volumes.Length && volumes[v] != null && volumes[v].enabled)
            {
                volumes[v].enabled = false;
                world.RequestNavRebuild();
            }
            world.Raise(new GameEvent
            {
                kind = GameEventKind.PlantFelled, team = 2, index = i,
                pos = plants[i].pos + Vector3.up * s.groundShift,
                dir = new Vector3(s.fallDir.x, 0f, s.fallDir.y), scale = plants[i].scale * plants[i].stretch
            });
        }

        /// <summary>Set a plant alight. <paramref name="vigour"/> below zero starts a new
        /// blaze; otherwise the fire passing it on hands over its own vigour and blaze.</summary>
        public void Ignite(GameWorld world, int i, int generation, float vigour = -1f, int blaze = -1)
        {
            ref var s = ref live[i];
            if (s.state == PlantState.Gone || s.burning || s.charred > 0.85f) return;
            if (fires >= MaxBurning) return;
            // Damp ground: the plants along the water barely catch.
            if (generation > 0 && rng.F01() < s.wet * 0.97f) return;
            if (blaze < 0) blaze = NewBlaze(vigour);
            else if (blazePlants[blaze] >= blazeCapPlants[blaze]) return;   // this fire has had its run
            blazePlants[blaze]++;
            s.blaze = (byte)blaze;
            var k = KindOf(i);
            s.burning = true;
            float size = 0.7f + 0.4f * plants[i].scale;
            s.fuel = (k.bush ? rng.Range(5f, 8f) : rng.Range(9f, 15f)) * size / Mathf.Max(0.5f, k.burns);
            s.fire = Mathf.Max(s.fire, 0.1f);
            s.generation = (byte)Mathf.Min(255, generation);
            s.vigour = vigour >= 0f ? Mathf.Clamp01(vigour + rng.Range(-0.06f, 0.04f)) : blazeVig[blaze];
            s.nextSpread = world.time + rng.Range(1.5f, 2.5f);
            fires++;
            burned++;
            // Mid-tick, the loop lists it when it reaches it (or next frame).
            if (!ticking) burning.Add(i);
            world.Raise(new GameEvent { kind = GameEventKind.PlantIgnited, team = 2, index = i, pos = plants[i].pos });
        }

        /// <summary>How hard a new blaze runs, 0..1: weighted low, so most fires are
        /// over in a few plants and one in several really takes hold.</summary>
        float Vigour() => Mathf.Pow(rng.F01(), 2.8f);

        /// <summary>Open a new fire: its vigour, and the budget of plants and grass it
        /// may take before it has had its run. A vigorous fire can work through a sixth
        /// of the map's growth, never all of it -- that takes several fires.</summary>
        int NewBlaze(float vigour)
        {
            int id = nextBlaze;
            nextBlaze = (nextBlaze + 1) % Blazes;
            float v = vigour >= 0f ? Mathf.Clamp01(vigour) : Vigour();
            blazeVig[id] = v;
            blazePlants[id] = 0;
            blazeCells[id] = 0;
            blazeCapPlants[id] = 3 + Mathf.RoundToInt(v * 0.16f * plants.Length);
            blazeCapCells[id] = 20 + Mathf.RoundToInt(v * 0.10f * (grassFuel != null ? grassFuel.Length : 0));
            return id;
        }

        void Spread(GameWorld world, int i)
        {
            ref var s = ref live[i];
            int gen = s.generation + 1;
            var k = KindOf(i);
            float v = s.vigour;
            float reach = 2.2f + k.crownRadii.x * plants[i].scale;
            Vector2 c = Pos2(i);
            // What stands downwind catches far more readily, and from further, than what
            // stands across or into the wind, so a fire runs as a front instead of a
            // circle. How readily at all is the blaze's vigour: most go out in a few
            // plants, a few run through a whole stand.
            var wind = Wind.Direction(world.time);
            float blowing = Wind.Speed(world.time);
            // A gust drives a fire on; in a lull it hardly moves.
            float strength = (0.10f + 0.85f * v) * Mathf.Min(1f, s.fire + 0.3f) * (0.55f + 0.7f * blowing);
            foreach (int j in Near(c, reach + 8f))
            {
                if (j == i || live[j].burning || live[j].state == PlantState.Gone) continue;
                var d = Pos2(j) - c;
                float dist = d.magnitude;
                if (dist < 1e-3f) continue;
                float align = Vector2.Dot(d / dist, wind);                    // 1 downwind, -1 into it
                float downwind = 0.3f + 1.5f * Mathf.Pow(Mathf.Clamp01(align * 0.5f + 0.5f), 2f);
                float r = (reach + KindOf(j).crownRadii.x * plants[j].scale * 0.5f) * (1f + 0.7f * Mathf.Max(0f, align));
                if (dist > r) continue;
                float chance = 0.32f * strength * downwind * KindOf(j).burns * (1f - 0.45f * (dist / r));
                if (rng.F01() < chance) Ignite(world, j, gen, v, s.blaze);
            }
            // A fire running hard throws embers into what stands downwind of it, which is
            // how one crosses the gap between two stands and becomes a big fire.
            if (v > 0.55f && s.fire > 0.7f && rng.F01() < 0.12f * v * blowing)
            {
                var at = c + wind * rng.Range(9f, 24f) * (0.5f + blowing) + new Vector2(rng.Range(-4f, 4f), rng.Range(-4f, 4f));
                int best = -1;
                float bestD = 5f;
                foreach (int j in Near(at, 6f))
                {
                    if (live[j].burning || live[j].state == PlantState.Gone) continue;
                    float dj = (Pos2(j) - at).magnitude;
                    if (dj < bestD) { bestD = dj; best = j; }
                }
                if (best >= 0 && rng.F01() < 0.5f * KindOf(best).burns) Ignite(world, best, gen, v * 0.9f, s.blaze);
            }
        }

        /// <summary>A blast: trees close in are thrown over away from it, bushes flattened,
        /// and anything it reaches may catch fire.</summary>
        public void Blast(GameWorld world, Vector3 at, float radius, float igniteChance)
        {
            if (plants.Length == 0) return;
            Vector2 c = new Vector2(at.x, at.z);
            // One draw for the whole blast: what it lights is all the same fire.
            int blaze = NewBlaze(-1f);
            float v = blazeVig[blaze];
            BurnGrass(world, c, radius * 1.1f, igniteChance * 1.4f, blaze);
            foreach (int i in Near(c, radius * 2f + 3f))
            {
                if (live[i].state == PlantState.Gone) continue;
                Vector2 d = Pos2(i) - c;
                float dist = d.magnitude - TrunkRadius(i);
                var k = KindOf(i);
                // A shell landing beside a tree may take it down -- the reach is well
                // outside the fireball, because what fells a tree is the blast, not the
                // flame. (It used to be 0.6 of the radius, under two metres for a
                // Mauler's shell, so a shell could go off beside a trunk and leave it
                // standing.) Whether it actually goes over is a chance: near certain
                // right on top of it, seldom at the edge of the reach, and a thick
                // trunk stands where a slender one snaps -- so shelling a wood thins
                // it rather than flattening it, and the same shot twice is not the
                // same picture. The NavMesh rebuild coalesces, so a whole stand going
                // over at once is still one rebuild.
                float reach = radius * 1.35f;
                if (dist < reach)
                {
                    if (live[i].state == PlantState.Standing)
                    {
                        float near = 1f - Mathf.Clamp01(dist / reach);
                        float thick = Mathf.Clamp01(0.30f / Mathf.Max(0.08f, TrunkRadius(i)));
                        if (rng.F01() < Mathf.Lerp(0.10f, 0.95f, near * near) * thick)
                            // Thrown over by the blast: the closer it stood, the harder it goes.
                            Fell(world, i, d.sqrMagnitude > 1e-4f ? d / d.magnitude : Vector2.up,
                                 Mathf.Lerp(1.5f, 0.35f, Mathf.Clamp01(dist / reach)));
                    }
                    if (rng.F01() < igniteChance * k.burns * (1f - live[i].wet * 0.9f)) Ignite(world, i, 0, v, blaze);
                }
                else if (dist < radius * 1.9f + (k.bush ? 0f : k.crownRadii.x * plants[i].scale * 0.5f))
                {
                    if (rng.F01() < igniteChance * 0.5f * k.burns * (1f - live[i].wet * 0.9f)) Ignite(world, i, 0, v, blaze);
                }
            }
        }

        /// <summary>How much of the map's growth has burned this match (for the report).</summary>
        public string FireReport()
        {
            int lit = 0, spent = 0;
            if (grassFuel != null)
                for (int i = 0; i < grassState.Length; i++)
                {
                    if (grassState[i] == 1) lit++;
                    else if (grassState[i] == 2) spent++;
                }
            int fuelled = 0;
            if (grassFuel != null)
                for (int i = 0; i < grassFuel.Length; i++)
                    if (grassFuel[i] > 20) fuelled++;
            return $"{fires} plants burning, {burned}/{plants.Length} lit this match; grass {lit} cells alight, {spent} of {fuelled} fuelled cells burnt out";
        }

        /// <summary>How much of the map's growth has burned, 0..1 (plants and grass).</summary>
        public float Burnt
        {
            get
            {
                int spent = 0;
                if (grassFuel != null)
                    for (int i = 0; i < grassState.Length; i++)
                        if (grassState[i] == 2) spent++;
                int fuelled = 0;
                if (grassFuel != null)
                    for (int i = 0; i < grassFuel.Length; i++)
                        if (grassFuel[i] > 20) fuelled++;
                return fuelled > 0 ? spent / (float)fuelled : 0f;
            }
        }


        // ------------------------------------------------------------ the grass
        /// <summary>Light the grass in a patch (a blast, or a plant burning over it).</summary>
        public void BurnGrass(GameWorld world, Vector2 centre, float radius, float chance, int blaze = -1)
        {
            if (grassFuel == null) return;
            if (blaze < 0) blaze = NewBlaze(-1f);
            int r = Mathf.Max(0, Mathf.CeilToInt(radius / GrassCell));
            int cx = Mathf.Clamp((int)(centre.x / GrassCell), 0, grassSide - 1);
            int cz = Mathf.Clamp((int)(centre.y / GrassCell), 0, grassSide - 1);
            for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(grassSide - 1, cz + r); z++)
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(grassSide - 1, cx + r); x++)
                {
                    int cell = z * grassSide + x;
                    var d = new Vector2((x + 0.5f) * GrassCell - centre.x, (z + 0.5f) * GrassCell - centre.y);
                    if (d.sqrMagnitude > radius * radius) continue;
                    if (rng.F01() < chance) LightGrass(world, cell, blaze);
                }
        }

        bool LightGrass(GameWorld world, int cell, int blaze)
        {
            if (grassFuel == null || cell < 0 || cell >= grassFuel.Length) return false;
            if (grassState[cell] != 0 || grassFuel[cell] < 25) return false;
            if (burningGrass.Count >= MaxGrassFires) return false;
            if (blazeCells[blaze] >= blazeCapCells[blaze]) return false;
            // Thin, damp grass may simply not take.
            if (rng.F01() > grassFuel[cell] / 255f) return false;
            blazeCells[blaze]++;
            grassState[cell] = 1;
            GrassVersion++;
            grassBlaze[cell] = (byte)blaze;
            grassUntil[cell] = world.time + GrassBurnTime * rng.Range(0.7f, 1.3f);
            grassNext[cell] = world.time + rng.Range(0.4f, 1.1f);
            burningGrass.Add(cell);
            return true;
        }

        /// <summary>A burning plant drops fire into the grass under and downwind of it.</summary>
        void LightGrassRound(GameWorld world, int i)
        {
            if (grassFuel == null) return;
            ref var s = ref live[i];
            var c = Pos2(i) + Wind.Direction(world.time) * rng.Range(0f, 2.5f) * (0.4f + Wind.Speed(world.time));
            float r = 1.5f + KindOf(i).crownRadii.x * plants[i].scale * 0.6f;
            BurnGrass(world, c, r, 0.35f * s.fire * (1f - s.wet), s.blaze);
        }

        /// <summary>The grass fire: every burning cell passes fire to the cells beside it,
        /// downwind for choice, and to the plants standing in it, then burns out. This is
        /// what carries a fire across a meadow from one stand of trees to the next.</summary>
        void TickGrass(GameWorld world, float dt)
        {
            if (grassFuel == null || burningGrass.Count == 0) return;
            float now = world.time;
            for (int n = burningGrass.Count - 1; n >= 0; n--)
            {
                int cell = burningGrass[n];
                if (now >= grassUntil[cell])
                {
                    grassState[cell] = 2;                      // burnt out: it does not burn twice
                    GrassVersion++;
                    burningGrass.RemoveAt(n);
                    continue;
                }
                if (now < grassNext[cell]) continue;
                grassNext[cell] = now + rng.Range(0.5f, 0.9f);
                int blaze = grassBlaze[cell];
                var wind = Wind.Direction(now);
                float blowing = Wind.Speed(now);
                int x = cell % grassSide, z = cell / grassSide;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= grassSide || nz >= grassSide) continue;
                        int to = nz * grassSide + nx;
                        if (grassState[to] != 0) continue;
                        var dir = new Vector2(dx, dz).normalized;
                        float downwind = 0.25f + 1.6f * Mathf.Pow(Mathf.Clamp01(Vector2.Dot(dir, wind) * 0.5f + 0.5f), 2f);
                        if (dx != 0 && dz != 0) downwind *= 0.7f;      // the diagonals are further off
                        if (rng.F01() < 0.30f * downwind * (0.55f + 0.7f * blowing)) LightGrass(world, to, blaze);
                    }
                // And what is growing in it.
                var centre = new Vector2((x + 0.5f) * GrassCell, (z + 0.5f) * GrassCell);
                foreach (int j in Near(centre, GrassCell))
                {
                    if (live[j].burning || live[j].state == PlantState.Gone) continue;
                    if ((Pos2(j) - centre).sqrMagnitude > GrassCell * GrassCell) continue;
                    if (rng.F01() < 0.35f * KindOf(j).burns) Ignite(world, j, 1, -1f, blaze);
                }
            }
        }

        /// <summary>A crater opened: plants near it settle with the ground.</summary>
        public void Resettle(GroundDeformer ground, Vector2 c, float radius)
        {
            foreach (int i in Near(c, radius + 2f))
            {
                if (live[i].state == PlantState.Gone || (Pos2(i) - c).magnitude > radius + 0.5f) continue;
                live[i].groundShift += ground.LastChange(Pos2(i));
            }
        }
    }
}
