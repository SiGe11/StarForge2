// GameWorld.cs — the authoritative match state and the one command API.
//
// The player's mouse and the AI both drive the game exclusively through the
// Cmd* methods here, so neither has a private path to the simulation. Both
// sides are fogged by the per-team visibility grids kept in this class; the
// AI reads enemy state only through Visible(), exactly as the HUD does.
using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public sealed class Faction
    {
        public int team;
        public int ore = 150;
        public int supplyUsed, supplyCap;
        public int lost, killed;
        public int oreMined, unitsProduced, structuresBuilt;
        /// <summary>Splash shells that caught what they were aimed at, and ones that
        /// burst on the ground short of it or where it used to be.</summary>
        public int shellHits, shellMisses;

        // ---- the Mech (one a side, a match; see GameWorld's Mech Bay section)
        /// <summary>A Mech Bay has been placed: no second one, ever (unless the first was
        /// cancelled before it was finished).</summary>
        public bool bayPlaced;
        public Unit bay, mech;
        /// <summary>When the Mech comes down, once the bay is finished; -1 before.</summary>
        public float mechDropAt = -1f;
        public bool mechInbound, mechDropped, mechLost, bayLost;
        /// <summary>Its Mech: from the start only the pilot (and a placeholder loadout); the
        /// parts are fitted when the drop is called (<see cref="designChosen"/>).</summary>
        public MechDesign design;
        public bool designChosen;
        /// <summary>What the armoury weighed and picked, for the log and the evaluation.</summary>
        public string armouryNote = "";
        public readonly int[] upgrades = new int[(int)MechUpgrade.Count];
        /// <summary>The upgrade the bay is working on (-1 none) and the seconds it still needs.</summary>
        public int researching = -1;
        public float researchLeft, researchTotal;
        /// <summary>Where the Mech comes down (chosen as it is announced).</summary>
        public Vector2 dropSpot;
        public int upgradeOreSpent;
    }

    public enum GameEventKind
    {
        Fire, Impact, Death, Promoted, UnitReady, StructureComplete, StructurePlaced, UnderAttack, Refused, Notice,
        PlantFelled, PlantLanded, PlantIgnited, StructureIgnited, PlantCrushed,
        /// <summary>A hitscan shot from a Mech (laser, railgun, rotary cannon, flamer):
        /// <c>pos</c> the muzzle, <c>end</c> where it struck, <c>projectileKind</c> the MechWeapon.</summary>
        Beam,
        /// <summary>A Mech is on its way down (a few seconds before it lands).</summary>
        MechInbound,
        /// <summary>A Mech has struck the ground beside its bay.</summary>
        MechLanded,
        /// <summary>The Mech's AI speaks to its side: <c>advice</c>, <c>text</c>, <c>pos</c>.</summary>
        MechAdvice,
        /// <summary>A Mech Bay upgrade finished: <c>index</c> the MechUpgrade, <c>scale</c> its level.</summary>
        UpgradeComplete,
        /// <summary>A Mech's heavy footfall (only raised for effects that care about the
        /// ground: the view raises its own for every step).</summary>
        MechStomp
    }

    /// <summary>What a Mech's AI tells its side (MechBrain).</summary>
    public enum MechAdviceKind { None = 0, Attack, Defend, Regroup, Warning, Morale, Status }

    public struct GameEvent
    {
        public GameEventKind kind;
        public Unit unit;
        public UnitType type;
        public int team;
        public Vector3 pos;
        public Vector3 dir;
        public float scale;
        /// <summary>How fast it was going: a falling tree's crown, in metres a second.</summary>
        public float speed;
        public int projectileKind;
        public string text;
        /// <summary>The plant, for the Plant* events (index into Vegetation.plants).</summary>
        public int index;
        /// <summary>Where a Beam ended.</summary>
        public Vector3 end;
        public MechAdviceKind advice;
    }

    public sealed class Projectile
    {
        public bool alive;
        public int kind;            // 0 tracer, 1 shell, 2 pulse, 3 bolt, 4 autocannon, 5 missile, 6 mortar
        public Vector3 pos, prevPos, vel;
        public Unit target, shooter;
        public int team;
        public float dmg, splash, life, age;
        public float bonusVsWorkers = 1f;
        /// <summary>A Mech's rounds hit light and heavy targets, and structures, differently.</summary>
        public float vsLight = 1f, vsHeavy = 1f, vsStructure = 1f;

        public float ClassMul(Unit e) => e.def.building ? vsStructure : e.def.Heavy ? vsHeavy : vsLight;
        /// <summary>A missile's curve (kind 5): from where, over what, to what it last aimed at.</summary>
        public Vector3 from, ctrl, aim;
        public float flight;
    }

    [DefaultExecutionOrder(-50)]
    public sealed partial class GameWorld : MonoBehaviour
    {
        public const int VIS = 64;
        public const int MaxSupply = 200;
        const int GRID = 32;

        public static GameWorld Instance { get; private set; }

        [SerializeField] MapInfo map;
        public MapInfo Map => map;
        public float MapSize => map.mapSize;
        public float VisCell => map.mapSize / VIS;

        public readonly List<Unit> units = new List<Unit>(1024);
        public readonly List<Boulder> boulders = new List<Boulder>(64);
        public readonly Faction[] factions = { new Faction { team = 0 }, new Faction { team = 1 } };
        public readonly List<Projectile> projectiles = new List<Projectile>(256);
        readonly Stack<Projectile> projectilePool = new Stack<Projectile>();

        public struct Ping { public Vector2 pos; public float t; public int team; }
        public readonly List<Ping> pings = new List<Ping>();

        public float time;
        public int winner = -1;
        public bool running;
        public string lastRefusal;
        /// <summary>The player used the testing cheat this match (README, "Testing").</summary>
        public bool cheated;

        /// <summary>Every gameplay event, for effects, audio and the HUD.</summary>
        public event Action<GameEvent> Event;
        /// <summary>Raised after each simulation tick; AI commanders run here.</summary>
        public event Action<float> Ticked;

        readonly byte[][] vis = { new byte[VIS * VIS], new byte[VIS * VIS] };
        readonly byte[][] explored = { new byte[VIS * VIS], new byte[VIS * VIS] };
        readonly float[][] lastSeen = { new float[VIS * VIS], new float[VIS * VIS] };
        float visTimer;
        /// <summary>Goes up each time the sides' sight is worked out, the only time the
        /// visibility grid changes: what reads it every frame can skip the frames between.</summary>
        public int VisVersion { get; private set; }

        // Every Foundry that joined `units`, in the order it joined (the dead are
        // skipped when asked, and swept out with the unit list), so a Digger heading
        // back with ore does not walk every unit on the map each tick.
        readonly List<Unit> foundries = new List<Unit>(8);
        int nextId = 1;
        Rng rng = new Rng(1);
        public Rng Rng => rng;

        /// <summary>NavMesh areas every agent may use; Rubble (ground under a boulder)
        /// is left out, and only Maulers add it back.</summary>
        public static int GroundAreas
        {
            get
            {
                int rubble = NavMesh.GetAreaFromName("Rubble");
                return rubble < 0 ? NavMesh.AllAreas : NavMesh.AllAreas & ~(1 << rubble);
            }
        }

        NavMeshSurface navSurface;
        float navRebuildIn = -1f, navRebuildNotBefore;
        AsyncOperation navRebuild;

        /// <summary>The map's trees and bushes (may be empty on an old map).</summary>
        public Vegetation Plants { get; private set; }
        /// <summary>Craters: the match's own copy of the terrain heights.</summary>
        public GroundDeformer GroundShape { get; private set; }
        /// <summary>Which stretches of the NavMesh join up (see NearestReachable).</summary>
        public NavIslands Islands { get; private set; }

        readonly int[] gridHead = new int[GRID * GRID];
        int[] gridNext = new int[1024];
        float gridCell = 8f;

        void Awake()
        {
            Instance = this;
            if (map == null) map = FindAnyObjectByType<MapInfo>();
            // Path requests share this many A* steps a frame. At Unity's default of 100
            // a long request took many frames, and a NavMesh rebuild landing in the
            // middle started it over: Maulers crossing a wood (felling trees, so
            // rebuilding the tiles every second or so) waited up to nine seconds for a
            // path and stood still meanwhile. A thousand finishes nearly all of them in
            // the frame they are asked for.
            NavMesh.pathfindingIterationsPerFrame = 1000;
            for (int t = 0; t < 2; t++) Array.Fill(lastSeen[t], -1f);

            // Crushed boulders rebuild NavMesh tiles at runtime. Work on a copy of
            // the baked data, before any agent is placed on it: rebuilding the
            // asset itself would carry a match's craters back into the project.
            navSurface = map != null ? map.GetComponent<NavMeshSurface>() : null;
            if (navSurface != null && navSurface.navMeshData != null)
            {
                var copy = Instantiate(navSurface.navMeshData);
                navSurface.RemoveData();
                navSurface.navMeshData = copy;
                navSurface.AddData();
            }
            Islands = new NavIslands(() => navRebuild != null && !navRebuild.isDone, navSurface != null ? navSurface.agentTypeID : 0);

            if (map != null && map.terrain != null)
            {
                GroundShape = map.gameObject.AddComponent<GroundDeformer>();
                GroundShape.Init(map);
                GroundShape.Deformed += OnGroundDeformed;
            }
            Plants = map != null ? map.GetComponentInChildren<Vegetation>() : null;
        }

        /// <summary>A plant or rock no longer blocks its ground: rebuild the NavMesh tiles
        /// there shortly (batched with anything else that changes meanwhile).</summary>
        public void RequestNavRebuild()
        {
            // Start a countdown only if none is running, so a Mauler ploughing through
            // a grove does not keep putting the rebuild off.
            if (navRebuildIn < 0f) navRebuildIn = 0.35f;
        }

        void OnGroundDeformed(Vector2 c, float radius)
        {
            foreach (var b in boulders)
                if (b != null && !b.smashed && (b.Pos - c).magnitude < radius + 0.5f)
                    b.transform.position += Vector3.up * GroundShape.LastChange(b.Pos);
            if (Plants != null) Plants.Resettle(GroundShape, c, radius);
        }

        /// <summary>What an explosion does to the battlefield itself: rocks close in
        /// break, trees are thrown over and may catch fire, and the ground takes a
        /// crater (not under structures or ore, which stand on flattened pads).</summary>
        public void Blast(Vector3 at, float radius, float craterRadius, float craterDepth, float ignite)
        {
            Vector2 c = new Vector2(at.x, at.z);
            for (int i = boulders.Count - 1; i >= 0; i--)
            {
                var b = boulders[i];
                if (b == null || b.smashed) { boulders.RemoveAt(i); continue; }
                Vector2 d = b.Pos - c;
                if (d.magnitude > radius * 0.55f + b.Radius * 0.6f) continue;
                b.Smash();
                boulders.RemoveAt(i);
                RequestNavRebuild();
                Raise(new GameEvent
                {
                    kind = GameEventKind.Death, type = UnitType.Boulder, team = 2,
                    pos = new Vector3(b.Pos.x, map.HeightAt(b.Pos) + 0.5f, b.Pos.y),
                    dir = d.sqrMagnitude > 1e-4f ? new Vector3(d.x, 0f, d.y).normalized : Vector3.forward, scale = b.Radius
                });
            }
            if (Plants != null) Plants.Blast(this, at, radius, ignite);

            if (craterRadius > 0f && GroundShape != null && at.y - map.HeightAt(c) < 1.5f)
            {
                foreach (var u in units)
                {
                    if (u == null || u.dying || !(u.def.building || u.Type == UnitType.Ore)) continue;
                    if ((u.pos - c).magnitude < craterRadius + u.def.radius + 1f) return;
                }
                GroundShape.Crater(at, craterRadius, craterDepth);
            }
        }

        public void BeginMatch(uint seed)
        {
            rng = new Rng(seed);
            fireRng = new Rng(seed ^ 0xB0F1u);
            nextFireCheck = 0f;
            time = 0f;
            winner = -1;
            running = true;
            gridCell = MapSize / GRID;
            foreach (var u in FindObjectsByType<Unit>())
                if (u.def != null && !units.Contains(u))
                {
                    u.Init(this, u.def, u.team, nextId++, true, u.transform.eulerAngles.y * Mathf.Deg2Rad);
                    units.Add(u);
                    if (u.Type == UnitType.Foundry) foundries.Add(u);
                }
            boulders.Clear();
            foreach (var b in FindObjectsByType<Boulder>())
                if (!b.smashed) boulders.Add(b);
            // Each side's pilot is drawn now, from the match's seed; the Mech's parts are
            // picked when it is called down (MechArmoury), against what the enemy fields then.
            for (int t = 0; t < 2; t++)
            {
                factions[t].design = MechParts.Generate(seed * 2654435761u ^ (0x6D656368u + (uint)t * 0x9E3779B9u));
                factions[t].designChosen = false;
                factions[t].armouryNote = "";
            }
            RebuildGrid();
            UpdateVisibility();
        }

        public void Raise(GameEvent e) => Event?.Invoke(e);

        // ------------------------------------------------------------ lifecycle
        public Unit Spawn(UnitType type, int team, Vector2 pos, bool complete = true, float yaw = float.NaN)
        {
            var def = Defs.Get(type);
            var prefab = def.Prefab(team);
            GameObject go = prefab != null
                ? Instantiate(prefab, map.Ground(pos), Quaternion.identity)
                : new GameObject(def.displayName);
            go.name = def.displayName;
            if (prefab == null) go.transform.position = map.Ground(pos);
            var u = go.GetComponent<Unit>();
            if (u == null) u = go.AddComponent<Unit>();
            if (float.IsNaN(yaw)) yaw = rng.Range(-PI, PI);
            u.Init(this, def, team, nextId++, complete, yaw);
            units.Add(u);
            if (type == UnitType.Foundry) foundries.Add(u);
            if (def.building && complete && team < 2) factions[team].supplyCap += def.supplyGive;
            if (Plants != null) Plants.ClearGrassUnder(u);
            if ((def.building || type == UnitType.Ore) && Islands != null) Islands.Invalidate();   // it carves the NavMesh
            return u;
        }

        /// <param name="splash">A shell's burst at <paramref name="from"/> rather than a round
        /// striking home: it throws what it kills away from the burst.</param>
        public void Damage(Unit u, float dmg, Vector2 from, Unit source, bool splash = false)
        {
            if (u == null || u.dying || u.Untargetable) return;
            // A Mech's shield takes it first, then its armour.
            if (u.mech != null) dmg = u.mech.Absorb(dmg, splash);
            if (dmg <= 0f) { u.lastDamagedT = time; return; }
            u.hp -= dmg;
            u.damageFlash = 1f;
            u.lastDamagedT = time;
            if (u.team < 2)
            {
                bool near = false;
                foreach (var p in pings)
                    if (p.team == u.team && (p.pos - u.pos).sqrMagnitude < 30f * 30f) { near = true; break; }
                if (!near)
                {
                    pings.Add(new Ping { pos = u.pos, t = 0f, team = u.team });
                    Raise(new GameEvent { kind = GameEventKind.UnderAttack, unit = u, type = u.Type, team = u.team, pos = u.Ground });
                }
            }
            // Idle defenders retaliate against whatever just hit them.
            if (u.order == Order.Idle && !u.def.building && u.def.Armed && !u.def.Autonomous)
            {
                var t = NearestEnemy(u, u.def.sight);
                if (t != null) { u.order = Order.Attack; u.target = t; }
            }
            if (u.hp <= 0f)
            {
                if (source != null && !source.dying && source.team != u.team && source.team < 2) source.CreditKill();
                Kill(u, from, source, splash);
            }
        }

        // ------------------------------------------------------------ wildfire at the walls
        // A structure a wildfire reaches can catch. While a burning plant's crown or a
        // burning patch of grass is at its walls, each second carries a small chance of
        // it catching, in proportion to how hard that fire is going. A fire that burns
        // up to a building delivers 7-20 heat-seconds at its walls (AgentPlay.
        // BuildingFireTrial), so a little under a third of them set it alight; a tree
        // blazing against it for a long while more often. Once alight it burns for a quarter of a minute
        // and loses 6-12% of its health -- enough to see on its bar, never enough to
        // bring it down (fire alone stops at 1 hp). Then it is safe for a while: what
        // was burning against it has burnt out. A separate stream of random numbers,
        // so a fire does not change what the rest of a seeded match draws.
        const float FireCheck = 0.5f;          // seconds between looks at each building
        public const float CatchRate = 0.025f; // chance per second of catching, at full heat
        const float FireReach = 1.5f;          // how far outside its footprint a fire reaches it
        Rng fireRng = new Rng(0xB0F1u);
        float nextFireCheck;

        void StructureFires(float dt)
        {
            // Burning ones burn down their share, spread over the time they burn.
            foreach (var u in units)
            {
                if (u == null || u.dying || u.burnUntil <= 0f) continue;
                if (time >= u.burnUntil)
                {
                    u.burnUntil = 0f;
                    u.burnDamageLeft = 0f;
                    u.burnSafeUntil = time + 45f;
                    continue;
                }
                float d = u.burnDamageLeft * Mathf.Min(1f, dt / (u.burnUntil - time));
                u.burnDamageLeft -= d;
                u.hp = Mathf.Max(Mathf.Min(u.hp, 1f), u.hp - d);
            }

            var veg = Plants;
            if (veg == null || veg.burning.Count + veg.burningGrass.Count == 0 || time < nextFireCheck) return;
            nextFireCheck = time + FireCheck;
            foreach (var u in units)
            {
                if (u == null || u.dying || !u.def.building || u.team > 1) continue;
                if (u.burnUntil > 0f || time < u.burnSafeUntil) continue;
                float heat = veg.FireNear(u.pos, u.def.radius + FireReach, time);
                if (heat < 0.1f) continue;
                if (fireRng.F01() >= 1f - Mathf.Exp(-CatchRate * heat * FireCheck)) continue;
                Ignite(u);
            }
        }

        /// <summary>Set a structure alight (see StructureFires).</summary>
        public void Ignite(Unit u)
        {
            if (u == null || u.dying || !u.def.building || u.burnUntil > 0f) return;
            u.burnStart = time;
            u.burnUntil = time + fireRng.Range(13f, 19f);
            u.burnDamageLeft = u.MaxHp * fireRng.Range(0.06f, 0.12f);
            Raise(new GameEvent { kind = GameEventKind.StructureIgnited, unit = u, type = u.Type, team = u.team, pos = u.Ground });
        }

        void Kill(Unit u, Vector2 from, Unit source, bool splash)
        {
            var d = u.def;
            // Which way it goes down, and how hard: thrown away from a shell bursting
            // beside it (harder the closer), knocked back from whoever shot it.
            Vector2 away = u.pos - from;
            if (splash && away.sqrMagnitude > 0.04f) { u.killDir = away.normalized; u.killForce = Mathf.Lerp(1f, 0.45f, Saturate(away.magnitude / 4f)); }
            else if (source != null && (u.pos - source.pos).sqrMagnitude > 0.01f)
            {
                u.killDir = (u.pos - source.pos).normalized;
                u.killForce = source.Type == UnitType.Mauler || source.Type == UnitType.Mech ? 0.8f : splash ? 0.9f : 0.25f;
            }
            // Not from the match's random stream: a draw here would shift everything a
            // seeded match draws after it.
            else { u.killDir = new Vector2(Mathf.Cos(u.id * 2.4f), Mathf.Sin(u.id * 2.4f)); u.killForce = 0.5f; }
            if (u.team < 2)
            {
                if (d.building && u.Complete) factions[u.team].supplyCap -= d.supplyGive;
                factions[u.team].lost++;
                factions[1 - u.team].killed++;
            }
            if (u.team < 2) MechBookkeeping(u);
            u.BeginDeath();
            float scale = d.type == UnitType.Mech ? 3.4f : d.building ? 2.6f : (d.type == UnitType.Mauler ? 1.5f : 1f);
            // A wrecked structure or vehicle blasts what stands round it; infantry do not.
            // A Mech's reactor goes up like a small structure's worth of ordnance.
            if (d.type == UnitType.Mech)
            {
                Blast(u.Ground, 9f, 5.5f, 0.75f, 0.6f);
                foreach (var e in UnitsNear(u.pos, 8f).ToArray())
                    if (e != u && e.team < 2 && !e.def.building) Damage(e, 140f * (1f - Saturate((e.pos - u.pos).magnitude / 8f)), u.pos, null, true);
            }
            else if (d.building) Blast(u.Ground, d.radius * 1.4f, 0f, 0f, 0.7f);
            else if (d.type == UnitType.Mauler) Blast(u.Ground, 3.2f, 2.8f, 0.4f, 0.35f);
            else if (d.type == UnitType.Skimmer || d.type == UnitType.Worker) Blast(u.Ground, 2.2f, 2.0f, 0.25f, 0.2f);
            Raise(new GameEvent
            {
                kind = GameEventKind.Death, unit = u, type = d.type, team = u.team,
                pos = u.Ground + Vector3.up * (d.visualHeight * 0.4f), scale = scale,
                dir = new Vector3(u.killDir.x, 0f, u.killDir.y)
            });
        }

        public void Deplete(Unit node)
        {
            if (node == null || node.dying) return;
            node.BeginDeath();
            Raise(new GameEvent { kind = GameEventKind.Death, unit = node, type = node.Type, team = 2, pos = node.Ground + Vector3.up, scale = 0.7f });
        }

        /// <summary>The testing cheat: ore from nowhere. A match it was used in does
        /// not teach the AI's memory about the player.</summary>
        public void CheatOre(int team, int amount)
        {
            factions[team].ore += amount;
            cheated = true;
            Raise(new GameEvent { kind = GameEventKind.Notice, team = team, text = $"+{amount} ore" });
        }

        public void Deposit(int team, int amount)
        {
            factions[team].ore += amount;
            factions[team].oreMined += amount;
        }

        public void CompleteConstruction(Unit b)
        {
            factions[b.team].supplyCap += b.def.supplyGive;
            factions[b.team].structuresBuilt++;
            if (b.Type == UnitType.MechBay && b.team < 2)
            {
                var F = factions[b.team];
                F.bay = b;
                if (!F.mechDropped && F.mechDropAt < 0f) F.mechDropAt = time + MechDropDelay;
            }
            Raise(new GameEvent { kind = GameEventKind.StructureComplete, unit = b, type = b.Type, team = b.team, pos = b.Ground });
        }

        // ------------------------------------------------------------ tick
        /// <summary>For the benchmark: what the last Update cost, and the part of that
        /// spent in <see cref="Ticked"/> (the AIs and the Mech brains).</summary>
        public static float SimMs, TickedMs;
        static readonly System.Diagnostics.Stopwatch simWatch = new System.Diagnostics.Stopwatch();

        void Update()
        {
            if (!running) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            simWatch.Restart();
            time += dt;

            // A unit destroyed from outside (an editor trial, a scene coming down) is taken
            // out of the list below; reading its transform here threw and stopped the whole
            // tick, every frame.
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u != null) u.CachePosition();
            }
            RebuildGrid();

            bool anyRemoved = false;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null) { anyRemoved = true; continue; }
                if (u.dying)
                {
                    u.deathTimer += dt;
                    float ttl = u.def.building ? 2.4f : 1.3f;
                    if (u.deathTimer > ttl)
                    {
                        if (u.def.building || u.Type == UnitType.Ore) Islands.Invalidate();   // its carving goes
                        Destroy(u.gameObject);
                        units[i] = null;
                        anyRemoved = true;
                    }
                    continue;
                }
                u.Tick(dt);
            }
            // Compaction shifts list indices and the spatial hash stores indices, so
            // rebuild it: projectiles and the AI commanders run after this point, and
            // a stale hash sends their queries past the end of the list or to the
            // wrong unit.
            if (anyRemoved)
            {
                units.RemoveAll(x => x == null);
                foundries.RemoveAll(x => x == null);
                RebuildGrid();
            }

            UpdateProjectiles(dt);
            CrushBoulders(dt);
            if (Plants != null) Plants.Tick(this, dt);
            StructureFires(dt);
            TickMechBays(dt);

            visTimer -= dt;
            if (visTimer <= 0f) { UpdateVisibility(); visTimer = 0.12f; }

            // Recount supply from live units plus everything still queued.
            factions[0].supplyUsed = factions[1].supplyUsed = 0;
            foreach (var u in units)
            {
                if (u.dying || u.team > 1) continue;
                factions[u.team].supplyUsed += u.def.supplyCost;
                foreach (var q in u.queue) factions[u.team].supplyUsed += Defs.Get(q).supplyCost;
            }

            for (int i = pings.Count - 1; i >= 0; i--)
            {
                var p = pings[i];
                p.t += dt;
                if (p.t > 4f) pings.RemoveAt(i); else pings[i] = p;
            }

            if (winner < 0)
            {
                int a0 = 0, a1 = 0;
                foreach (var u in units)
                {
                    if (u.dying || u.team > 1) continue;
                    if (u.def.building || u.Type == UnitType.Worker) { if (u.team == 0) a0++; else a1++; }
                }
                // Both sides' last structure and Digger gone in the same moment is a draw
                // (2): left at -1, the match never ended.
                if (a0 == 0 && a1 == 0) winner = 2;
                else if (a0 == 0) winner = 1;
                else if (a1 == 0) winner = 0;
            }

            float beforeTicked = (float)simWatch.Elapsed.TotalMilliseconds;
            Ticked?.Invoke(dt);
            SimMs = (float)simWatch.Elapsed.TotalMilliseconds;
            TickedMs = SimMs - beforeTicked;
        }

        // ------------------------------------------------------------ boulders
        /// <summary>Maulers drive through rocks and break them. Crushed rocks free
        /// their ground, so the NavMesh tiles under them are rebuilt shortly after
        /// (batched, and in the background).</summary>
        void CrushBoulders(float dt)
        {
            if (boulders.Count > 0)
                foreach (var u in units)
                {
                    if (u == null || u.dying || (u.Type != UnitType.Mauler && u.Type != UnitType.Mech) || u.agent == null || !u.agent.enabled) continue;
                    if (u.agent.velocity.sqrMagnitude < 0.25f) continue;
                    for (int i = boulders.Count - 1; i >= 0; i--)
                    {
                        var b = boulders[i];
                        if (b == null || b.smashed) { boulders.RemoveAt(i); continue; }
                        float reach = (u.mech != null ? u.agent.radius : u.def.radius) * 0.8f + b.Radius * 0.75f;
                        if ((b.Pos - u.pos).sqrMagnitude > reach * reach) continue;
                        Vector3 at = new Vector3(b.Pos.x, map.HeightAt(b.Pos), b.Pos.y);
                        b.Smash();
                        boulders.RemoveAt(i);
                        RequestNavRebuild();
                        Raise(new GameEvent
                        {
                            kind = GameEventKind.Death, type = UnitType.Boulder, team = 2, pos = at + Vector3.up * 0.5f,
                            dir = new Vector3(Mathf.Sin(u.yaw), 0f, Mathf.Cos(u.yaw)), scale = b.Radius
                        });
                    }
                }

            if (navRebuildIn >= 0f && (navRebuild == null || navRebuild.isDone))
            {
                navRebuildIn -= dt;
                // Every rebuild takes the path from any unit whose way ran over the tiles
                // it changes; so no more than one every second and a half, however fast a
                // Mauler goes through a wood.
                if (navRebuildIn < 0f && time < navRebuildNotBefore) navRebuildIn = 0f;
                else if (navRebuildIn < 0f && navSurface != null && navSurface.navMeshData != null)
                {
                    navRebuild = navSurface.UpdateNavMesh(navSurface.navMeshData);
                    navRebuildNotBefore = time + 1.5f;
                    Islands.Invalidate();
                }
            }
        }

        // ------------------------------------------------------------ spatial hash
        void RebuildGrid()
        {
            Array.Fill(gridHead, -1);
            if (gridNext.Length < units.Count) gridNext = new int[units.Count * 2];
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u.dying) continue;
                int c = CellIndex(u.pos);
                gridNext[i] = gridHead[c];
                gridHead[c] = i;
            }
        }

        int CellIndex(Vector2 p)
        {
            int x = ClampI((int)(p.x / gridCell), 0, GRID - 1);
            int z = ClampI((int)(p.y / gridCell), 0, GRID - 1);
            return z * GRID + x;
        }

        void CellRange(Vector2 p, float r, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = ClampI((int)((p.x - r) / gridCell), 0, GRID - 1);
            x1 = ClampI((int)((p.x + r) / gridCell), 0, GRID - 1);
            z0 = ClampI((int)((p.y - r) / gridCell), 0, GRID - 1);
            z1 = ClampI((int)((p.y + r) / gridCell), 0, GRID - 1);
        }

        // ------------------------------------------------------------ queries
        public float GroundY(Vector2 p) => map.HeightAt(p);

        public Unit NearestEnemy(Unit u, float radius)
        {
            Unit best = null;
            float bestD = radius * radius;
            CellRange(u.pos, radius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2 || o.Untargetable) continue;
                        float d = (o.pos - u.pos).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = o; }
                    }
            return best;
        }

        /// <summary>Every live enemy of <paramref name="u"/> (not neutral) within
        /// <paramref name="radius"/> of it, into <paramref name="into"/>.</summary>
        public void EnemiesNear(Unit u, float radius, List<Unit> into)
        {
            into.Clear();
            CellRange(u.pos, radius, out int x0, out int x1, out int z0, out int z1);
            float r2 = (radius + 3f) * (radius + 3f);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2) continue;
                        if ((o.pos - u.pos).sqrMagnitude <= r2) into.Add(o);
                    }
        }

        readonly List<Unit> nearScratch = new List<Unit>(64);

        /// <summary>Every live unit or structure within <paramref name="radius"/> of a point
        /// (a shared list: use it before asking again).</summary>
        public List<Unit> UnitsNear(Vector2 p, float radius)
        {
            nearScratch.Clear();
            CellRange(p, radius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying) continue;
                        if ((o.pos - p).magnitude <= radius + o.def.radius) nearScratch.Add(o);
                    }
            return nearScratch;
        }

        /// <summary>The entity under a ground point; the tightest fit wins so a unit
        /// standing on a structure is still pickable.</summary>
        public Unit Pick(Vector2 p, float extra = 0f)
        {
            Unit best = null;
            float bestScore = 1e30f;
            CellRange(p, 6f + extra, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying) continue;
                        float d = (o.pos - p).magnitude;
                        if (d > o.def.radius + extra) continue;
                        float score = d - o.def.radius;
                        if (score < bestScore) { bestScore = score; best = o; }
                    }
            return best;
        }

        public Unit NearestDropoff(Unit u)
        {
            Unit best = null;
            float bestD = 1e30f;
            foreach (var o in foundries)
            {
                if (o == null || o.dying || o.team != u.team || !o.Complete) continue;
                float d = (o.pos - u.pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = o; }
            }
            return best;
        }

        readonly List<(float d, Unit n)> nodeCand = new List<(float, Unit)>(64);

        /// <summary>Nearest ore with a bias toward lightly worked nodes, so a mineral
        /// line spreads out instead of stacking every Digger on one crystal.</summary>
        public Unit NearestFreeNode(Vector2 near, int team)
        {
            nodeCand.Clear();
            foreach (var o in units)
            {
                if (o == null || o.dying || o.Type != UnitType.Ore || o.oreLeft <= 0) continue;
                float d = (o.pos - near).magnitude;
                if (d < 40f) nodeCand.Add((d, o));
            }
            if (nodeCand.Count == 0)
                foreach (var o in units)
                    if (o != null && !o.dying && o.Type == UnitType.Ore && o.oreLeft > 0)
                        nodeCand.Add(((o.pos - near).magnitude, o));
            if (nodeCand.Count == 0) return null;
            nodeCand.Sort((a, b) => a.d.CompareTo(b.d));
            if (nodeCand.Count > 64) nodeCand.RemoveRange(64, nodeCand.Count - 64);

            Unit best = null;
            float bestScore = 1e30f;
            foreach (var (d, n) in nodeCand)
            {
                int load = 0;
                foreach (var w in units)
                    if (w != null && !w.dying && w.team == team && w.Type == UnitType.Worker && w.harvestNode == n) load++;
                float score = d + load * 6f;
                if (score < bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        /// <summary>The nearest NavMesh to <paramref name="p"/> -- anywhere, including a
        /// patch no unit can get onto (a plateau top, a clearing ringed by trees and
        /// boulders). To send a unit somewhere, use <see cref="NearestReachable(Unit, Vector2)"/>.</summary>
        public Vector2 NearestWalkable(Vector2 p, float maxDistance = 48f)
        {
            if (NavMesh.SamplePosition(map.Ground(p), out var hit, maxDistance, GroundAreas))
                return new Vector2(hit.position.x, hit.position.z);
            return p;
        }

        /// <summary>Editor A/B only: send units to the nearest NavMesh anywhere again, as
        /// before NavIslands, to measure what reachable goals are worth.</summary>
        public static bool ReachabilityOff;

        /// <summary>The nearest point to <paramref name="p"/> that a unit standing at
        /// <paramref name="from"/> can walk to: <paramref name="p"/> itself when it is on
        /// the NavMesh and that joins the unit's, else the closest point of the unit's own
        /// stretch of it (NavIslands). Sent to the nearest NavMesh anywhere, a unit could
        /// get a partial path, walk to its end and stand there. <paramref name="rubble"/>:
        /// for a Mauler, which drives through boulders and trunks.</summary>
        public Vector2 NearestReachable(Vector2 from, Vector2 p, bool rubble = false)
        {
            if (!ReachabilityOff && Islands != null &&
                Islands.TryNearest(from, p, rubble ? NavIslands.WithRubble : NavIslands.Ground, out var q)) return q;
            return NearestWalkable(p);
        }

        public Vector2 NearestReachable(Unit u, Vector2 p) => NearestReachable(u.pos, p, DrivesThroughRubble(u));

        /// <summary>Whether a unit standing at <paramref name="from"/> can walk to
        /// <paramref name="p"/> (to the NavMesh under it, or nearest it).</summary>
        public bool Reaches(Vector2 from, Vector2 p, bool rubble = false) =>
            ReachabilityOff || Islands == null || Islands.Connected(from, p, rubble ? NavIslands.WithRubble : NavIslands.Ground);

        public bool Reaches(Unit u, Vector2 p) => Reaches(u.pos, p, DrivesThroughRubble(u));

        static bool DrivesThroughRubble(Unit u) => u.agent != null && u.agent.areaMask == NavMesh.AllAreas;

        public bool Walkable(Vector2 p, float tolerance = 0.5f)
        {
            if (!map.InBounds(p, 1f)) return false;
            if (!NavMesh.SamplePosition(map.Ground(p), out var hit, 1.5f, GroundAreas)) return false;
            return new Vector2(hit.position.x - p.x, hit.position.z - p.y).sqrMagnitude <= tolerance * tolerance;
        }

        public bool HasComplete(int team, UnitType t)
        {
            foreach (var u in units)
                if (u != null && !u.dying && u.team == team && u.Type == t && u.Complete) return true;
            return false;
        }

        public bool CanPlace(UnitType what, Vector2 where)
        {
            var D = Defs.Get(what);
            float r = D.radius;
            if (!map.InBounds(where, r + 2f)) return false;
            float hMin = 1e9f, hMax = -1e9f;
            for (int ring = 0; ring <= 2; ring++)
            {
                float rr = r * ring * 0.5f;
                int n = ring == 0 ? 1 : 10;
                for (int i = 0; i < n; i++)
                {
                    float a = TAU * i / n;
                    var p = where + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (!Walkable(p, 0.6f)) return false;
                    float h = map.HeightAt(p);
                    // Units wade into the shallows; foundations need dry ground.
                    if (h < map.waterLevel + 0.25f) return false;
                    hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
                }
            }
            if (hMax - hMin > 1.3f) return false;

            CellRange(where, r + 7f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying) continue;
                        float need = r + o.def.radius + (o.def.building || o.def.neutral ? 1f : -0.35f);
                        if ((o.pos - where).sqrMagnitude < need * need) return false;
                    }
            return true;
        }

        // ------------------------------------------------------------ visibility
        public bool Visible(int team, Vector2 p)
        {
            int x = (int)(p.x / VisCell), z = (int)(p.y / VisCell);
            if (x < 0 || z < 0 || x >= VIS || z >= VIS || team < 0 || team > 1) return false;
            return vis[team][z * VIS + x] > 0;
        }

        public bool Explored(int team, Vector2 p)
        {
            int x = (int)(p.x / VisCell), z = (int)(p.y / VisCell);
            if (x < 0 || z < 0 || x >= VIS || z >= VIS || team < 0 || team > 1) return false;
            return explored[team][z * VIS + x] != 0;
        }

        public float Staleness(int team, Vector2 p)
        {
            int x = (int)(p.x / VisCell), z = (int)(p.y / VisCell);
            if (x < 0 || z < 0 || x >= VIS || z >= VIS || team < 0 || team > 1) return 1e9f;
            float t = lastSeen[team][z * VIS + x];
            return t < 0f ? 1e9f : time - t;
        }

        public byte VisibleCell(int team, int x, int z) => vis[team][z * VIS + x];
        public byte ExploredCell(int team, int x, int z) => explored[team][z * VIS + x];

        void UpdateVisibility()
        {
            Array.Clear(vis[0], 0, vis[0].Length);
            Array.Clear(vis[1], 0, vis[1].Length);
            float cell = VisCell;
            foreach (var e in units)
            {
                if (e == null || e.dying || e.team > 1) continue;
                float sight = e.mech != null ? e.mech.Sight : e.def.sight;
                if (!e.Complete) sight *= 0.5f;
                if (sight <= 0f) continue;
                int t = e.team;
                var visT = vis[t];
                var exploredT = explored[t];
                var seenT = lastSeen[t];
                float px = e.pos.x, pz = e.pos.y, sight2 = sight * sight;
                int r = (int)(sight / cell) + 1;
                int cx = (int)(px / cell), cz = (int)(pz / cell);
                int x0 = Math.Max(0, cx - r), x1 = Math.Min(VIS - 1, cx + r);
                int z0 = Math.Max(0, cz - r), z1 = Math.Min(VIS - 1, cz + r);
                for (int z = z0; z <= z1; z++)
                {
                    float dz = (z + 0.5f) * cell - pz;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + 0.5f) * cell - px;
                        if (dx * dx + dz * dz > sight2) continue;
                        int i = z * VIS + x;
                        visT[i] = 1;
                        exploredT[i] = 1;
                        seenT[i] = time;
                    }
                }
            }
            VisVersion++;
            foreach (var e in units)
            {
                if (e == null) continue;
                bool seen = e.team == 0 || Visible(0, e.pos);
                e.visibleToPlayer = seen;
                if (seen) e.everSeenByPlayer = true;
            }
        }

        // ------------------------------------------------------------ combat
        public void Fire(Unit shooter, Unit target)
        {
            var D = shooter.def;
            float ang = shooter.HasTurret ? shooter.turretYaw : shooter.yaw;
            Vector3 fwd = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            float muzzleH, muzzleF;
            int kind;
            switch (D.type)
            {
                case UnitType.Mauler: muzzleH = 1.70f; muzzleF = 2.9f; kind = 1; break;
                case UnitType.Skimmer: muzzleH = 0.85f; muzzleF = 1.4f; kind = 2; break;
                case UnitType.Sentinel: muzzleH = 1.78f; muzzleF = 1.75f; kind = 3; break;
                case UnitType.MechBay: muzzleH = MechBayMuzzleHeight; muzzleF = 1.9f; kind = 3; break;
                case UnitType.Trooper: muzzleH = 1.25f; muzzleF = 0.7f; kind = 0; break;
                default: muzzleH = 0.8f; muzzleF = 0.7f; kind = 0; break;
            }
            Vector3 muzzle = shooter.Ground + Vector3.up * muzzleH + fwd * muzzleF;
            Vector3 aim = AimPoint(target);

            var p = projectilePool.Count > 0 ? projectilePool.Pop() : new Projectile();
            p.alive = true;
            p.kind = kind;
            p.pos = p.prevPos = muzzle;
            p.target = target;
            p.shooter = shooter;
            p.team = shooter.team;
            p.dmg = D.damage * (1f + 0.15f * shooter.rank);
            p.splash = D.splash;
            p.bonusVsWorkers = D.bonusVsWorkers;
            // The pool is shared with the Mech's guns: a round reused from one must not
            // keep its multipliers (a rifle round came out at half damage on infantry).
            p.vsLight = p.vsHeavy = p.vsStructure = 1f;
            p.flight = 0f;
            p.age = 0f;
            if (kind == 1)
            {
                // Ballistic arc: solve the vertical velocity for a fixed flight time.
                float dist = (aim - muzzle).magnitude;
                float t = Clamp(dist / 55f, 0.25f, 2f);
                p.life = t;
                Vector3 d = aim - muzzle;
                p.vel = new Vector3(d.x / t, d.y / t + 0.5f * 42f * t, d.z / t);
            }
            else
            {
                p.life = 2f;
                float speed = kind == 3 ? 140f : (kind == 2 ? 90f : 115f);
                p.vel = (aim - muzzle).normalized * speed;
            }
            projectiles.Add(p);
            Raise(new GameEvent { kind = GameEventKind.Fire, unit = shooter, type = D.type, team = shooter.team, pos = muzzle, dir = fwd, projectileKind = kind });
        }

        /// <summary>How far short of <paramref name="target"/> a Mauler standing at
        /// <paramref name="from"/> would burst its shell: 0 if it arrives. The same
        /// muzzle, aim point and flight time as Fire, followed half a metre at a time
        /// against the terrain. A shell's arc is nearly flat -- at full range it climbs
        /// about a metre over the line -- so a low rise between the two stops it, and a
        /// tank on the wrong side of one will shell that rise all day. Only a stretch of
        /// two metres under the ground counts: a real shell moves a metre or two a
        /// frame and tests only where it lands, so it goes through a thinner crest.</summary>
        public float ShellFallsShort(Vector2 from, Unit target, float minUnder = 2f)
        {
            if (!Unit.Live(target) || !map.InBounds(from)) return float.MaxValue;
            Vector2 d2 = target.pos - from;
            float flat = d2.magnitude;
            if (flat < 1e-3f) return 0f;
            Vector2 f2 = d2 / flat;
            Vector3 muzzle = map.Ground(from) + Vector3.up * 1.70f + new Vector3(f2.x, 0f, f2.y) * 2.9f;
            Vector3 aim = target.Ground + Vector3.up * (target.def.radius * 0.6f);
            Vector3 d = aim - muzzle;
            float t = Clamp(d.magnitude / 55f, 0.25f, 2f);
            Vector3 v = new Vector3(d.x / t, d.y / t + 0.5f * 42f * t, d.z / t);
            int steps = Mathf.Max(16, Mathf.CeilToInt(flat / 0.5f));
            float stepLen = flat / steps, under = 0f;
            Vector2 entered = Vector2.zero;
            for (int i = 1; i < steps; i++)
            {
                float s = t * i / steps;
                Vector3 p = muzzle + v * s + Vector3.down * (0.5f * 42f * s * s);
                var p2 = new Vector2(p.x, p.z);
                if (!map.InBounds(p2)) return float.MaxValue;
                if (p.y <= map.HeightAt(p2))
                {
                    if (under <= 0f) entered = p2;
                    under += stepLen;
                    if (under >= minUnder) return (entered - target.pos).magnitude;
                }
                else under = 0f;
            }
            return 0f;
        }

        /// <summary>Does a shell from here do its job? A burst within
        /// <paramref name="splash"/> of the target counts: a big structure behind a low
        /// bank is hit through the bank.</summary>
        public bool ShellClears(Vector2 from, Unit target, float splash) =>
            ShellFallsShort(from, target) <= splash + target.def.radius;

        void UpdateProjectiles(float dt)
        {
            for (int k = projectiles.Count - 1; k >= 0; k--)
            {
                var p = projectiles[k];
                p.prevPos = p.pos;
                p.life -= dt;
                p.age += dt;
                var t = p.target;
                bool targetOk = t != null && !t.dying;

                bool ballistic = p.kind == 1 || p.kind == 6;
                if (p.kind == 5)
                {
                    // A missile flies a curve from the rack, up over the top and down on
                    // what it is after, following it as it moves.
                    if (targetOk) p.aim = AimPoint(t);
                }
                else if (!ballistic)
                {
                    if (targetOk)
                    {
                        Vector3 tp = AimPoint(t);
                        float speed = p.vel.magnitude;
                        Vector3 want = (tp - p.pos).normalized * speed;
                        p.vel = Vector3.Lerp(p.vel, want, Mathf.Min(1f, dt * 14f)).normalized * speed;
                    }
                }
                else p.vel.y -= (p.kind == 6 ? MortarGravity : 42f) * dt;

                Vector3 np;
                if (p.kind == 5)
                {
                    float u = Mathf.Clamp01(p.age / Mathf.Max(0.05f, p.flight));
                    np = (1f - u) * (1f - u) * p.from + 2f * (1f - u) * u * p.ctrl + u * u * p.aim;
                    p.vel = (np - p.pos) / Mathf.Max(dt, 1e-4f);
                }
                else np = p.pos + p.vel * dt;
                bool hit = false;
                Vector3 hitAt = np;
                if (p.kind == 5)
                {
                    if (p.age >= p.flight) { hit = true; hitAt = p.aim; }
                }
                else if (!ballistic)
                {
                    if (targetOk)
                    {
                        Vector3 tp = AimPoint(t);
                        if ((np - tp).magnitude < t.def.radius + 0.7f) { hit = true; hitAt = tp; }
                    }
                    else if (p.life > 0.12f) p.life = 0.12f;
                }
                float gy = map.InBounds(new Vector2(np.x, np.z)) ? map.HeightAt(new Vector2(np.x, np.z)) : 0f;
                if (!hit && np.y <= gy) { hit = true; hitAt = new Vector3(np.x, gy, np.z); }
                // A shell's flight time is solved to land it on its aim point, which
                // sits a little above the target's base, so it bursts there when the
                // time is up. Retired instead, it never reached the ground at 60 fps:
                // every Mauler shot vanished without exploding or doing damage (only
                // the long frames of an accelerated clock carried shells into the
                // ground).
                if (!hit && ballistic && p.life <= 0f) { hit = true; hitAt = new Vector3(np.x, Mathf.Max(np.y, gy), np.z); }

                if (!hit)
                {
                    if (p.life <= 0f) { Recycle(k); continue; }
                    p.pos = np;
                    continue;
                }

                p.pos = hitAt;
                var c = new Vector2(hitAt.x, hitAt.z);
                if (p.splash > 0f)
                {
                    for (int i = units.Count - 1; i >= 0; i--)
                    {
                        var e = units[i];
                        if (e == null || e.dying || e.team == p.team || e.team == 2) continue;
                        float d = (e.pos - c).magnitude;
                        if (d > p.splash + e.def.radius || e.Untargetable) continue;
                        float falloff = 1f - Saturate((d - e.def.radius) / p.splash) * 0.6f;
                        Damage(e, p.dmg * falloff * p.ClassMul(e), c, p.shooter, true);
                        // A heavy burst throws the light ones round it off their feet.
                        if (p.kind >= 5 && !e.dying) Shove(e, e.pos - c, (p.kind == 6 ? 7f : 3.5f) * falloff);
                    }
                    // Did it catch what it was aimed at, or just dig a hole near it?
                    if (Unit.Live(p.shooter) && targetOk)
                    {
                        bool caught = (t.pos - c).magnitude <= p.splash + t.def.radius;
                        var sh = p.shooter;
                        if (caught) { sh.shotsMissed = 0; sh.missFrom = sh.pos; }
                        else if ((sh.pos - sh.missFrom).sqrMagnitude > 25f) { sh.shotsMissed = 1; sh.missFrom = sh.pos; }
                        else sh.shotsMissed++;
                        if (p.team >= 0 && p.team < factions.Length)
                        {
                            if (caught) factions[p.team].shellHits++;
                            else factions[p.team].shellMisses++;
                        }
                    }
                    if (p.kind == 5) Blast(hitAt, p.splash * 0.6f, p.splash * 0.4f, 0.14f, 0.18f);
                    else if (p.kind == 6) Blast(hitAt, p.splash * 0.8f, p.splash * 0.6f, 0.45f, 0.35f);
                    else Blast(hitAt, p.splash * 0.7f, p.splash * 0.55f, 0.35f, 0.3f);
                }
                else if (targetOk)
                {
                    float dmg = p.dmg * (t.Type == UnitType.Worker ? p.bonusVsWorkers : 1f) * p.ClassMul(t);
                    Damage(t, dmg, c, p.shooter);
                }
                float impact = p.kind == 6 ? 2.1f : p.kind == 5 ? 1.02f : p.kind == 4 ? 0.6f : p.splash > 0f ? 1.35f : 0.4f;
                Raise(new GameEvent { kind = GameEventKind.Impact, team = p.team, pos = hitAt, dir = p.vel.normalized, scale = impact, projectileKind = p.kind });
                Recycle(k);
            }
        }

        void Recycle(int index)
        {
            var p = projectiles[index];
            p.alive = false;
            p.target = null;
            p.shooter = null;
            int last = projectiles.Count - 1;
            projectiles[index] = projectiles[last];
            projectiles.RemoveAt(last);
            projectilePool.Push(p);
        }

        // ------------------------------------------------------------ commands
        public void CmdMove(IReadOnlyList<Unit> sel, Vector2 dest, bool attackMove)
        {
            // Spread the destination over a ring so a group does not stack on one point.
            int n = 0;
            foreach (var u in sel) if (Unit.Live(u) && u.def.IsMobile && !u.def.Autonomous) n++;
            int i = 0;
            float spread = Mathf.Sqrt(Mathf.Max(1, n)) * 1.25f;
            foreach (var u in sel)
            {
                if (!Unit.Live(u) || !u.def.IsMobile || u.def.Autonomous) continue;
                Vector2 goal = dest;
                if (n > 1)
                {
                    float a = TAU * i / n + 0.6f;
                    float rr = spread * (0.4f + 0.6f * Mathf.Sqrt((float)(i + 1) / n));
                    goal = dest + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                }
                // Onto ground this unit can walk to. The nearest NavMesh may be a patch it
                // has no way onto (it walked to the end of a partial path and stood, on
                // attack-move asking again every second), and a goal inside a structure's
                // footprint left it standing short of it with the order never done.
                if (ReachabilityOff) { if (n > 1) goal = NearestWalkable(goal, 8f); }
                else goal = NearestReachable(u, goal);
                u.order = attackMove ? Order.AttackMove : Order.Move;
                u.orderPos = goal;
                u.target = null;
                u.harvestNode = null;
                u.buildTarget = null;
                u.MoveTo(goal);
                i++;
            }
        }

        public void CmdAttack(IReadOnlyList<Unit> sel, Unit target)
        {
            if (!Unit.Live(target)) return;
            foreach (var u in sel)
            {
                if (!Unit.Live(u) || u.def.building || !u.def.Armed || u.def.Autonomous) continue;
                u.order = Order.Attack;
                u.target = target;
                u.harvestNode = null;
                u.MoveTo(target.pos);
            }
        }

        public void CmdHarvest(IReadOnlyList<Unit> sel, Unit node)
        {
            foreach (var u in sel)
            {
                if (!Unit.Live(u) || u.Type != UnitType.Worker) continue;
                u.order = Order.Harvest;
                u.harvestNode = node;
                u.target = null;
                u.buildTarget = null;
                if (Unit.Live(node)) u.MoveTo(node.pos);
            }
        }

        public void CmdStop(IReadOnlyList<Unit> sel)
        {
            foreach (var u in sel)
            {
                if (!Unit.Live(u) || u.def.Autonomous) continue;
                u.order = Order.Idle;
                u.target = null;
                u.harvestNode = null;
                u.buildTarget = null;
                u.Halt();
            }
        }

        public void CmdHold(IReadOnlyList<Unit> sel)
        {
            foreach (var u in sel)
            {
                if (!Unit.Live(u) || u.def.building || u.def.Autonomous) continue;
                u.order = Order.Hold;
                u.Halt();
            }
        }

        public void CmdRally(IReadOnlyList<Unit> sel, Vector2 dest)
        {
            foreach (var u in sel)
            {
                if (!Unit.Live(u) || !u.def.building) continue;
                u.rally = dest;
                u.rallySet = true;
            }
        }

        public bool CmdBuild(IReadOnlyList<Unit> sel, UnitType what, Vector2 where)
        {
            var D = Defs.Get(what);
            Unit worker = null;
            foreach (var u in sel)
                if (Unit.Live(u) && u.Type == UnitType.Worker) { worker = u; break; }
            if (worker == null) return Refuse(-1, "Select a Digger to build");
            int team = worker.team;
            if (!D.building) return Refuse(team, "Cannot build that");
            if (what == UnitType.MechBay && factions[team].bayPlaced)
                return Refuse(team, factions[team].bayLost ? "The Mech Bay is lost and cannot be raised again" : "Only one Mech Bay can be raised");
            if (D.requires != UnitType.None && !HasComplete(team, D.requires))
                return Refuse(team, $"Requires a {Defs.Get(D.requires).displayName}");
            if (factions[team].ore < D.cost) return Refuse(team, "Not enough ore");
            if (!CanPlace(what, where)) return Refuse(team, "Cannot build there");

            factions[team].ore -= D.cost;
            // Structures sit square to the map so a base reads as planned.
            float yaw = Mathf.Round(rng.Range(-PI, PI) / (PI * 0.5f)) * (PI * 0.5f);
            var b = Spawn(what, team, where, false, yaw);
            if (what == UnitType.MechBay) { factions[team].bayPlaced = true; factions[team].bay = b; }
            worker.order = Order.Build;
            worker.buildTarget = b;
            worker.harvestNode = null;
            worker.target = null;
            worker.MoveTo(where);
            Raise(new GameEvent { kind = GameEventKind.StructurePlaced, unit = b, type = what, team = team, pos = b.Ground });
            return true;
        }

        public bool CmdTrain(Unit building, UnitType what)
        {
            if (!Unit.Live(building) || !building.def.building || !building.Complete) return false;
            var D = Defs.Get(what);
            int team = building.team;
            if (D.producer != building.Type) return Refuse(team, $"{building.def.displayName} cannot train that");
            if (factions[team].ore < D.cost) return Refuse(team, "Not enough ore");
            int cap = Mathf.Min(factions[team].supplyCap, MaxSupply);
            if (factions[team].supplyUsed + D.supplyCost > cap) return Refuse(team, "Not enough supply — build a Bunkhouse");
            if (building.queue.Count >= 5) return Refuse(team, "Production queue is full");
            factions[team].ore -= D.cost;
            factions[team].supplyUsed += D.supplyCost;
            if (building.queue.Count == 0) building.queueTimer = D.buildTime;
            building.queue.Add(what);
            return true;
        }

        public bool CmdCancelTrain(Unit building)
        {
            if (!Unit.Live(building) || building.queue.Count == 0) return false;
            var last = building.queue[building.queue.Count - 1];
            building.queue.RemoveAt(building.queue.Count - 1);
            factions[building.team].ore += Defs.Get(last).cost;
            if (building.queue.Count > 0 && building.queue.Count == 1 && building.queueTimer <= 0f)
                building.queueTimer = Defs.Get(building.queue[0]).buildTime;
            return true;
        }

        public bool CmdCancelConstruction(Unit building)
        {
            if (!Unit.Live(building) || building.Complete) return false;
            factions[building.team].ore += Mathf.RoundToInt(building.def.cost * 0.75f);
            // A bay called off before it stood was never raised: another may be.
            if (building.Type == UnitType.MechBay && building.team < 2) { factions[building.team].bayPlaced = false; factions[building.team].bay = null; }
            building.BeginDeath();
            return true;
        }

        /// <summary>Contextual right-click: attack enemies, harvest ore, otherwise move.</summary>
        public void CmdSmart(IReadOnlyList<Unit> sel, Vector2 worldPos, Unit hovered)
        {
            if (Unit.Live(hovered))
            {
                if (hovered.Type == UnitType.Ore)
                {
                    CmdHarvest(sel, hovered);
                    var rest = new List<Unit>();
                    foreach (var u in sel) if (Unit.Live(u) && u.Type != UnitType.Worker && u.def.IsMobile) rest.Add(u);
                    if (rest.Count > 0) CmdMove(rest, worldPos, false);
                    return;
                }
                if (hovered.team < 2)
                {
                    foreach (var u in sel)
                        if (Unit.Live(u) && u.team != hovered.team && !u.def.Autonomous) { CmdAttack(sel, hovered); return; }
                    // Right-clicking an own structure under construction sends Diggers to help.
                    if (!hovered.Complete && hovered.def.building)
                    {
                        foreach (var u in sel)
                            if (Unit.Live(u) && u.Type == UnitType.Worker)
                            {
                                u.order = Order.Build; u.buildTarget = hovered; u.harvestNode = null; u.MoveTo(hovered.pos);
                            }
                        return;
                    }
                }
            }
            CmdMove(sel, worldPos, false);
        }

        bool Refuse(int team, string why)
        {
            lastRefusal = why;
            if (team == 0 || team < 0) Raise(new GameEvent { kind = GameEventKind.Refused, team = 0, text = why });
            return false;
        }
    }
}
