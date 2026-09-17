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
        public float height = 7f;
        public Vector3 crownCenter = new Vector3(0f, 5f, 0f);
        public Vector3 crownRadii = new Vector3(2.5f, 2f, 2.5f);
        public Color leaf = new Color(0.13f, 0.22f, 0.07f);
        public Color leaf2 = new Color(0.24f, 0.29f, 0.08f);
        public Color bark = new Color(0.16f, 0.12f, 0.09f);

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
            public float fallAngle, fallSpeed;
            public float flatten;        // a crushed bush, 0..1
            public float sink;           // metres into the ground as it rots away
            public float groundShift;    // craters opened under it since the match began
            public byte generation;      // how many trees the fire has passed through to get here
            public float nextSpread;
        }

        [NonSerialized] public Live[] live = Array.Empty<Live>();
        public static Vegetation Instance { get; private set; }

        /// <summary>Plants on fire right now (indices), for effects and the ground mask.</summary>
        public readonly List<int> burning = new List<int>(64);

        const float Cell = 8f;
        int fires;       // plants burning, kept outside the per-frame list so ignitions mid-tick count
        bool ticking;
        const int MaxBurning = 40;
        const int MaxGeneration = 3;
        int gridSide;
        int[] cellStart, cellItems;
        readonly Rng rng = new Rng(0x7EE5u);

        void Awake()
        {
            Instance = this;
            live = new Live[plants.Length];
            BuildGrid();
        }

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

        /// <summary>Every plant whose cell lies within <paramref name="radius"/> of
        /// <paramref name="c"/>, into a shared list (callers do not nest).</summary>
        List<int> Near(Vector2 c, float radius)
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
            CrushUnderMaulers(world);

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
                    s.fire = Mathf.MoveTowards(s.fire, fed ? 1f : 0f, dt * (fed ? 0.45f : 0.3f));
                    s.charred = Mathf.Min(1f, s.charred + dt * (k.bush ? 0.14f : 0.075f));
                    s.foliageLost = Mathf.Min(1f, s.foliageLost + dt * s.fire * (k.bush ? 0.16f : 0.085f));
                    if (s.fire > 0.55f && world.time >= s.nextSpread)
                    {
                        s.nextSpread = world.time + rng.Range(0.7f, 1.3f);
                        Spread(world, i);
                    }
                    if (!fed && s.fire <= 0.01f)
                    {
                        s.burning = false;
                        s.fire = 0f;
                        fires--;
                        // A burned-out tree may give way.
                        if (!k.bush && s.state == PlantState.Standing && rng.F01() < 0.4f)
                        {
                            float a = rng.Range(0f, TAU);
                            Fell(world, i, new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
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
                        else
                        {
                            s.fallSpeed += dt * 2.6f;
                            s.fallAngle += s.fallSpeed * dt;
                            if (s.fallAngle >= 1.48f)
                            {
                                s.fallAngle = 1.48f;
                                s.state = PlantState.Down;
                                s.t = 0f;
                                world.Raise(new GameEvent
                                {
                                    kind = GameEventKind.PlantLanded, team = 2, index = i,
                                    pos = plants[i].pos + Vector3.up * s.groundShift,
                                    dir = new Vector3(s.fallDir.x, 0f, s.fallDir.y), scale = plants[i].scale * plants[i].stretch
                                });
                            }
                        }
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
                    // Pushed over ahead of the hull, leaning a little off the side it was struck on.
                    Fell(world, i, Norm(heading * 1.6f + Norm(d) * 0.6f));
                }
            }
        }

        public void Fell(GameWorld world, int i, Vector2 dir)
        {
            ref var s = ref live[i];
            if (s.state != PlantState.Standing) return;
            s.state = PlantState.Falling;
            s.t = 0f;
            s.fallDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up;
            s.fallSpeed = 0.35f;
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

        public void Ignite(GameWorld world, int i, int generation)
        {
            ref var s = ref live[i];
            if (s.state == PlantState.Gone || s.burning || s.charred > 0.85f) return;
            if (fires >= MaxBurning || generation > MaxGeneration) return;
            var k = KindOf(i);
            s.burning = true;
            s.fuel = k.bush ? rng.Range(5f, 8f) : rng.Range(9f, 15f);
            s.fire = Mathf.Max(s.fire, 0.1f);
            s.generation = (byte)generation;
            s.nextSpread = world.time + rng.Range(1.5f, 2.5f);
            fires++;
            // Mid-tick, the loop lists it when it reaches it (or next frame).
            if (!ticking) burning.Add(i);
            world.Raise(new GameEvent { kind = GameEventKind.PlantIgnited, team = 2, index = i, pos = plants[i].pos });
        }

        void Spread(GameWorld world, int i)
        {
            int gen = live[i].generation + 1;
            if (gen > MaxGeneration) return;
            float reach = 2.2f + KindOf(i).crownRadii.x * plants[i].scale;
            Vector2 c = Pos2(i);
            // Each generation passes the fire on less readily, so a blaze works through
            // a grove and dies out instead of burning down the map.
            float chance = 0.2f / (1f + live[i].generation * 0.9f);
            foreach (int j in Near(c, reach + 3f))
            {
                if (j == i || live[j].burning || live[j].state == PlantState.Gone) continue;
                float d = (Pos2(j) - c).magnitude;
                if (d > reach + KindOf(j).crownRadii.x * plants[j].scale * 0.5f) continue;
                if (rng.F01() < chance) Ignite(world, j, gen);
            }
        }

        /// <summary>A blast: trees close in are thrown over away from it, bushes flattened,
        /// and anything it reaches may catch fire.</summary>
        public void Blast(GameWorld world, Vector3 at, float radius, float igniteChance)
        {
            if (plants.Length == 0) return;
            Vector2 c = new Vector2(at.x, at.z);
            foreach (int i in Near(c, radius * 1.3f + 3f))
            {
                if (live[i].state == PlantState.Gone) continue;
                Vector2 d = Pos2(i) - c;
                float dist = d.magnitude - TrunkRadius(i);
                var k = KindOf(i);
                if (dist < radius * 0.6f)
                {
                    if (live[i].state == PlantState.Standing) Fell(world, i, d.sqrMagnitude > 1e-4f ? d / d.magnitude : Vector2.up);
                    if (rng.F01() < igniteChance) Ignite(world, i, 0);
                }
                else if (dist < radius * 1.2f + (k.bush ? 0f : k.crownRadii.x * plants[i].scale * 0.5f))
                {
                    if (rng.F01() < igniteChance * 0.5f) Ignite(world, i, 0);
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
