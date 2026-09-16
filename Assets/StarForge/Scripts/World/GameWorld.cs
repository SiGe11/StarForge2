// GameWorld.cs — the authoritative match state and the one command API.
//
// The player's mouse and the AI both drive the game exclusively through the
// Cmd* methods here, so neither has a private path to the simulation. Both
// sides are fogged by the per-team visibility grids kept in this class; the
// AI reads enemy state only through Visible(), exactly as the HUD does.
using System;
using System.Collections.Generic;
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
    }

    public enum GameEventKind { Fire, Impact, Death, Promoted, UnitReady, StructureComplete, StructurePlaced, UnderAttack, Refused }

    public struct GameEvent
    {
        public GameEventKind kind;
        public Unit unit;
        public UnitType type;
        public int team;
        public Vector3 pos;
        public Vector3 dir;
        public float scale;
        public int projectileKind;
        public string text;
    }

    public sealed class Projectile
    {
        public bool alive;
        public int kind;            // 0 tracer, 1 shell, 2 pulse, 3 bolt
        public Vector3 pos, prevPos, vel;
        public Unit target, shooter;
        public int team;
        public float dmg, splash, life, age;
        public float bonusVsWorkers = 1f;
    }

    [DefaultExecutionOrder(-50)]
    public sealed class GameWorld : MonoBehaviour
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
        public readonly Faction[] factions = { new Faction { team = 0 }, new Faction { team = 1 } };
        public readonly List<Projectile> projectiles = new List<Projectile>(256);
        readonly Stack<Projectile> projectilePool = new Stack<Projectile>();

        public struct Ping { public Vector2 pos; public float t; public int team; }
        public readonly List<Ping> pings = new List<Ping>();

        public float time;
        public int winner = -1;
        public bool running;
        public string lastRefusal;

        /// <summary>Every gameplay event, for effects, audio and the HUD.</summary>
        public event Action<GameEvent> Event;
        /// <summary>Raised after each simulation tick; AI commanders run here.</summary>
        public event Action<float> Ticked;

        readonly byte[][] vis = { new byte[VIS * VIS], new byte[VIS * VIS] };
        readonly byte[][] explored = { new byte[VIS * VIS], new byte[VIS * VIS] };
        readonly float[][] lastSeen = { new float[VIS * VIS], new float[VIS * VIS] };
        float visTimer;
        int nextId = 1;
        Rng rng = new Rng(1);
        public Rng Rng => rng;

        readonly int[] gridHead = new int[GRID * GRID];
        int[] gridNext = new int[1024];
        float gridCell = 8f;

        void Awake()
        {
            Instance = this;
            if (map == null) map = FindAnyObjectByType<MapInfo>();
            for (int t = 0; t < 2; t++) Array.Fill(lastSeen[t], -1f);
        }

        public void BeginMatch(uint seed)
        {
            rng = new Rng(seed);
            time = 0f;
            winner = -1;
            running = true;
            gridCell = MapSize / GRID;
            foreach (var u in FindObjectsByType<Unit>())
                if (u.def != null && !units.Contains(u))
                {
                    u.Init(this, u.def, u.team, nextId++, true, u.transform.eulerAngles.y * Mathf.Deg2Rad);
                    units.Add(u);
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
            if (def.building && complete && team < 2) factions[team].supplyCap += def.supplyGive;
            return u;
        }

        public void Damage(Unit u, float dmg, Vector2 from, Unit source)
        {
            if (u == null || u.dying) return;
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
            if (u.order == Order.Idle && !u.def.building && u.def.Armed)
            {
                var t = NearestEnemy(u, u.def.sight);
                if (t != null) { u.order = Order.Attack; u.target = t; }
            }
            if (u.hp <= 0f)
            {
                if (source != null && !source.dying && source.team != u.team && source.team < 2) source.CreditKill();
                Kill(u);
            }
        }

        void Kill(Unit u)
        {
            var d = u.def;
            if (u.team < 2)
            {
                if (d.building && u.Complete) factions[u.team].supplyCap -= d.supplyGive;
                factions[u.team].lost++;
                factions[1 - u.team].killed++;
            }
            u.BeginDeath();
            float scale = d.building ? 2.6f : (d.type == UnitType.Mauler ? 1.5f : 1f);
            Raise(new GameEvent
            {
                kind = GameEventKind.Death, unit = u, type = d.type, team = u.team,
                pos = u.Ground + Vector3.up * (d.visualHeight * 0.4f), scale = scale
            });
        }

        public void Deplete(Unit node)
        {
            if (node == null || node.dying) return;
            node.BeginDeath();
            Raise(new GameEvent { kind = GameEventKind.Death, unit = node, type = node.Type, team = 2, pos = node.Ground + Vector3.up, scale = 0.7f });
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
            Raise(new GameEvent { kind = GameEventKind.StructureComplete, unit = b, type = b.Type, team = b.team, pos = b.Ground });
        }

        // ------------------------------------------------------------ tick
        void Update()
        {
            if (!running) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            time += dt;

            for (int i = 0; i < units.Count; i++) units[i].CachePosition();
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
                RebuildGrid();
            }

            UpdateProjectiles(dt);

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
                if (a0 == 0 && a1 > 0) winner = 1;
                else if (a1 == 0 && a0 > 0) winner = 0;
            }

            Ticked?.Invoke(dt);
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
                        if (o == null || o.dying || o.team == u.team || o.team == 2) continue;
                        float d = (o.pos - u.pos).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = o; }
                    }
            return best;
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
            foreach (var o in units)
            {
                if (o == null || o.dying || o.team != u.team || o.Type != UnitType.Foundry || !o.Complete) continue;
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

        public Vector2 NearestWalkable(Vector2 p, float maxDistance = 48f)
        {
            if (NavMesh.SamplePosition(map.Ground(p), out var hit, maxDistance, NavMesh.AllAreas))
                return new Vector2(hit.position.x, hit.position.z);
            return p;
        }

        public bool Walkable(Vector2 p, float tolerance = 0.5f)
        {
            if (!map.InBounds(p, 1f)) return false;
            if (!NavMesh.SamplePosition(map.Ground(p), out var hit, 1.5f, NavMesh.AllAreas)) return false;
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
                float sight = e.def.sight;
                if (!e.Complete) sight *= 0.5f;
                if (sight <= 0f) continue;
                int t = e.team;
                int r = (int)(sight / cell) + 1;
                int cx = (int)(e.pos.x / cell), cz = (int)(e.pos.y / cell);
                for (int z = Math.Max(0, cz - r); z <= Math.Min(VIS - 1, cz + r); z++)
                    for (int x = Math.Max(0, cx - r); x <= Math.Min(VIS - 1, cx + r); x++)
                    {
                        float dx = (x + 0.5f) * cell - e.pos.x;
                        float dz = (z + 0.5f) * cell - e.pos.y;
                        if (dx * dx + dz * dz > sight * sight) continue;
                        int i = z * VIS + x;
                        vis[t][i] = 1;
                        explored[t][i] = 1;
                        lastSeen[t][i] = time;
                    }
            }
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
                case UnitType.Trooper: muzzleH = 1.25f; muzzleF = 0.7f; kind = 0; break;
                default: muzzleH = 0.8f; muzzleF = 0.7f; kind = 0; break;
            }
            Vector3 muzzle = shooter.Ground + Vector3.up * muzzleH + fwd * muzzleF;
            Vector3 aim = target.Ground + Vector3.up * (target.def.radius * 0.6f);

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

                if (p.kind != 1)
                {
                    if (targetOk)
                    {
                        Vector3 tp = t.Ground + Vector3.up * (t.def.radius * 0.6f);
                        float speed = p.vel.magnitude;
                        Vector3 want = (tp - p.pos).normalized * speed;
                        p.vel = Vector3.Lerp(p.vel, want, Mathf.Min(1f, dt * 14f)).normalized * speed;
                    }
                }
                else p.vel.y -= 42f * dt;

                Vector3 np = p.pos + p.vel * dt;
                bool hit = false;
                Vector3 hitAt = np;
                if (p.kind != 1)
                {
                    if (targetOk)
                    {
                        Vector3 tp = t.Ground + Vector3.up * (t.def.radius * 0.6f);
                        if ((np - tp).magnitude < t.def.radius + 0.7f) { hit = true; hitAt = tp; }
                    }
                    else if (p.life > 0.12f) p.life = 0.12f;
                }
                float gy = map.InBounds(new Vector2(np.x, np.z)) ? map.HeightAt(new Vector2(np.x, np.z)) : 0f;
                if (!hit && np.y <= gy) { hit = true; hitAt = new Vector3(np.x, gy, np.z); }

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
                        if (d > p.splash + e.def.radius) continue;
                        float falloff = 1f - Saturate((d - e.def.radius) / p.splash) * 0.6f;
                        Damage(e, p.dmg * falloff, c, p.shooter);
                    }
                }
                else if (targetOk)
                {
                    float dmg = p.dmg * (t.Type == UnitType.Worker ? p.bonusVsWorkers : 1f);
                    Damage(t, dmg, c, p.shooter);
                }
                Raise(new GameEvent { kind = GameEventKind.Impact, team = p.team, pos = hitAt, dir = p.vel.normalized, scale = p.splash > 0f ? 1.35f : 0.4f, projectileKind = p.kind });
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
            foreach (var u in sel) if (Unit.Live(u) && u.def.IsMobile) n++;
            int i = 0;
            float spread = Mathf.Sqrt(Mathf.Max(1, n)) * 1.25f;
            foreach (var u in sel)
            {
                if (!Unit.Live(u) || !u.def.IsMobile) continue;
                Vector2 goal = dest;
                if (n > 1)
                {
                    float a = TAU * i / n + 0.6f;
                    float rr = spread * (0.4f + 0.6f * Mathf.Sqrt((float)(i + 1) / n));
                    goal = NearestWalkable(dest + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, 8f);
                }
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
                if (!Unit.Live(u) || u.def.building || !u.def.Armed) continue;
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
                if (!Unit.Live(u)) continue;
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
                if (!Unit.Live(u) || u.def.building) continue;
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
            if (D.requires != UnitType.None && !HasComplete(team, D.requires))
                return Refuse(team, $"Requires a {Defs.Get(D.requires).displayName}");
            if (factions[team].ore < D.cost) return Refuse(team, "Not enough ore");
            if (!CanPlace(what, where)) return Refuse(team, "Cannot build there");

            factions[team].ore -= D.cost;
            // Structures sit square to the map so a base reads as planned.
            float yaw = Mathf.Round(rng.Range(-PI, PI) / (PI * 0.5f)) * (PI * 0.5f);
            var b = Spawn(what, team, where, false, yaw);
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
                        if (Unit.Live(u) && u.team != hovered.team) { CmdAttack(sel, hovered); return; }
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
