// Fauna.cs — the animals that live on the battlefield: wolf packs and foxes on the
// ground, flocks of songbirds feeding in the open, and eagles overhead.
//
// Pure scenery: nothing here is part of the simulation, nothing targets an
// animal and no animal blocks or steers a unit. The models are Quaternius's
// animated low-poly animals (CC0; Tools/pack_fauna.py), built into prefabs by
// SceneAssembler, and play their own walk, idle and flight cycles.
//
// Songbirds live on the ground between flights: a flock feeds on open ground
// (not under a crown), each bird hopping, pecking and looking round, until
// something comes near or it grows restless; then it flushes together and flies
// low to another open spot with a small bird's bounding flight -- a few
// wingbeats, wings shut, a few more -- and lands. Eagles circle high overhead.
//
// Ground animals move over the ground units could use: every step is checked
// against the NavMesh for ground units (GameWorld.GroundAreas), so they go round
// trees, rocks, wrecks and water instead of through them. A pack keeps its
// members a few metres apart and loosely together.
//
// They keep away from trouble. Units coming close, gunfire, explosions, deaths
// and burning plants are dangers: a group near one bolts away from it, then
// settles on new ground as far as it can find from everything that has
// happened lately, and flocks overhead swerve away, climb and do not come back
// while the danger lasts. Flyers hold a steady height over the highest ground
// beneath their whole circle, changing it slowly, so they do not bob up and
// down over every terrace.
//
// Fairness: an animal is shown only where the player can see, and a group takes
// fright at a danger only if the player could see that danger too -- or if the
// player cannot see the group itself -- so a herd bolting at the edge of the
// fog never gives away an unseen enemy. Spectating sees everything.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StarForge.Game;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.View
{
    [Serializable]
    public sealed class FaunaSpecies
    {
        public string name;
        public GameObject prefab;
        public bool flies;
        [Tooltip("Groups on the map, and animals per group.")] public int groups = 1, minPer = 1, maxPer = 1;
        [Tooltip("Metres per second: ambling (or cruising, for flyers) and running.")] public float speed = 1.2f, runSpeed = 6f;
        [Tooltip("The movement cycle, and the speed at which it plays at its own rate.")] public AnimationClip move;
        public float moveClipSpeed = 1.2f;
        [Tooltip("Played standing still; none keeps the movement cycle going slowly.")] public AnimationClip idle;
        [Tooltip("Metres kept between members of a group.")] public float spacing = 2.5f;
        [Tooltip("Flyers: height above the ground below the flock, and the radius they circle at.")]
        public float altitude = 20f, circle = 12f;
        [Tooltip("A group bolts from a unit this close (m).")] public float wariness = 18f;
        [Tooltip("Lives on the ground between flights (songbirds): feeds there, hopping and pecking, and the flock flies off together when disturbed or restless. altitude is then the height it flies at.")]
        public bool perches;
        [Tooltip("Perching flyers: wings shut between wingbeats (bounding flight), and a peck at the ground.")]
        public AnimationClip fold, peck;
        [Tooltip("Perching flyers: the wingbeat's playback rate, and one hop's length (m).")]
        public float flapRate = 2.6f, hop = 0.35f;
    }

    public sealed class Fauna : MonoBehaviour
    {
        public FaunaSpecies[] species = Array.Empty<FaunaSpecies>();

        sealed class Animal
        {
            public Transform t;
            public Animation anim;
            public Renderer[] renderers;
            public bool shown;
            public int group;
            public Vector2 pos, dir, target;
            public float speed, yaw, restUntil, height, angle, radius, bank, phase;
            // Perching flyers.
            public bool air;
            public Vector3 at, landAt;
            public Vector2 slot, hopFrom, hopTo;
            public float launchAt, nextAct, hopT = 1f, beatUntil, foldUntil, lift, pitch;
        }

        sealed class Group
        {
            public FaunaSpecies sp;
            public readonly List<Animal> members = new List<Animal>();
            public Vector2 home, centre, drift, away;
            public float fleeUntil, floor, altitude, nextHome;
            // Perching flyers: a flight from one spot to the next.
            public bool aloft, skipClimb;
            public Vector2 from, to, bend;
            public float t0, dur, restlessAt, pathY;
        }

        readonly List<Group> groups = new List<Group>();
        readonly List<(Vector2 p, float t, float r)> dangers = new List<(Vector2, float, float)>();
        GameWorld world;
        MapInfo map;
        PlayerController player;
        Rng rng;
        float nextScan;

        void Start()
        {
            world = GameWorld.Instance != null ? GameWorld.Instance : FindAnyObjectByType<GameWorld>();
            map = MapInfo.Instance != null ? MapInfo.Instance : FindAnyObjectByType<MapInfo>();
            player = FindAnyObjectByType<PlayerController>();
            // -sfnofauna leaves the animals out (for measuring what they cost).
            if (map == null || species.Length == 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-sfnofauna") >= 0) { enabled = false; return; }
            rng = new Rng(map.seed ^ 0xFA0Au);
            foreach (var sp in species)
                if (sp.prefab != null)
                    for (int g = 0; g < sp.groups; g++) Spawn(sp);
            if (world != null) world.Event += OnEvent;
        }

        void OnDestroy()
        {
            if (world != null) world.Event -= OnEvent;
        }

        /// <summary>Where one animal of a kind is (for the screenshot gallery).</summary>
        public bool TryGetAnimal(bool flying, out Vector3 p)
        {
            foreach (var g in groups)
                if (g.sp.flies == flying && g.members.Count > 0) { p = g.members[0].t.position; return true; }
            p = default;
            return false;
        }

        /// <summary>The middle of a perching flock, and whether it is in the air: one on
        /// the ground for choice, or <paramref name="flying"/> for one on the wing.</summary>
        public bool TryGetSongbirds(out Vector3 p, out bool aloft, bool flying = false)
        {
            Group best = null;
            foreach (var g in groups)
                if (g.sp.perches && g.members.Count > 0 && (best == null || (best.aloft != flying && g.aloft == flying))) best = g;
            if (best == null) { p = default; aloft = false; return false; }
            var c = Centroid(best);
            p = new Vector3(c.x, best.members[0].t.position.y, c.y);
            aloft = best.aloft;
            return true;
        }

        /// <summary>Put a perching flock up, as if something had come near (for the gallery).</summary>
        public void FlushSongbirds()
        {
            Group best = null;
            foreach (var g in groups)
                if (g.sp.perches && g.members.Count > 0 && (best == null || (best.aloft && !g.aloft))) best = g;
            if (best == null) return;
            var c = Centroid(best);
            Scare(best, c + Rotate(Vector2.right, rng.Range(0f, 360f)) * 10f);
        }

        /// <summary>Where every bird of the flock nearest <paramref name="near"/> is, and
        /// whether it is being drawn (for checking a screenshot).</summary>
        public string SongbirdDetail(Vector2 near, Camera cam)
        {
            Group best = null;
            float bestD = float.MaxValue;
            foreach (var g in groups)
                if (g.sp.perches && g.members.Count > 0)
                {
                    float d = (Centroid(g) - near).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = g; }
                }
            if (best == null) return "no flock";
            var sb = new System.Text.StringBuilder($"flock at {Centroid(best)}, {(best.aloft ? "flying" : "feeding")}: ");
            foreach (var a in best.members)
            {
                var v = cam != null ? cam.WorldToViewportPoint(a.t.position) : Vector3.zero;
                sb.Append($"[{a.t.position.x:0},{a.t.position.y:0.0},{a.t.position.z:0} shown {a.shown} on-screen {v.x:0.00},{v.y:0.00},{v.z:0} scale {a.t.localScale.x:0.00}] ");
            }
            return sb.ToString();
        }

        /// <summary>Each flock's height above the ground under it, and how fast it
        /// changed over the last second (for checking they fly level).</summary>
        public string FlightReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var g in groups)
                if (g.sp.flies && g.members.Count > 0)
                {
                    var a = g.members[0];
                    float ground = map.HeightAt(a.pos);
                    if (g.sp.perches)
                        sb.Append($"{g.sp.name}: {(g.aloft ? "flying" : "feeding")}, {g.members.FindAll(m => m.air).Count}/{g.members.Count} in the air, first {a.t.position.y - ground:0.0} m over the ground; ");
                    else
                        sb.Append($"{g.sp.name}: y {a.t.position.y:0.0}, {a.t.position.y - ground:0.0} m over the ground, floor {g.floor:0.0}; ");
                }
            return sb.ToString();
        }

        // ------------------------------------------------------------ ground
        static int Ground => GameWorld.GroundAreas;

        bool OnGround(Vector2 p, out Vector3 at)
        {
            at = default;
            if (!map.InBounds(p, 4f)) return false;
            if (!NavMesh.SamplePosition(map.Ground(p), out var hit, 1.2f, Ground)) return false;
            if (new Vector2(hit.position.x - p.x, hit.position.z - p.y).sqrMagnitude > 0.3f * 0.3f) return false;
            at = hit.position;
            return map.WaterDepth(p) < 0.15f;
        }

        /// <summary>Can an animal walk straight from a to b? (Not into a tree, a rock or the water.)</summary>
        bool Clear(Vector2 a, Vector2 b)
        {
            if (!OnGround(b, out var end)) return false;
            return !NavMesh.Raycast(map.Ground(a), end, out _, Ground);
        }

        float BaseDistance(Vector2 p) => Mathf.Min((p - map.StartPos(0)).magnitude, (p - map.StartPos(1)).magnitude);

        float DangerDistance(Vector2 p)
        {
            float d = 1e9f;
            foreach (var x in dangers) d = Mathf.Min(d, (x.p - p).magnitude);
            if (world != null)
                foreach (var u in world.units)
                    if (u != null && !u.dying && !u.def.neutral) d = Mathf.Min(d, (u.pos - p).magnitude * 1.5f);
            return d;
        }

        /// <summary>Open ground far from the bases and from everything that has happened lately
        /// (<paramref name="open"/>: also clear of the tree crowns, for birds to feed on).</summary>
        Vector2 FindHome(bool flying, Vector2 near, float maxFrom, bool open = false)
        {
            Vector2 best = near;
            float bestScore = -1e9f;
            for (int i = 0; i < 40; i++)
            {
                var p = maxFrom > 0f
                    ? near + new Vector2(rng.Range(-maxFrom, maxFrom), rng.Range(-maxFrom, maxFrom))
                    : new Vector2(rng.Range(20f, map.mapSize - 20f), rng.Range(20f, map.mapSize - 20f));
                if (!map.InBounds(p, flying ? 25f : open ? 30f : 12f)) continue;
                if (!flying && !OnGround(p, out _)) continue;
                if (open && Canopy(p)) continue;
                float score = Mathf.Min(DangerDistance(p), 90f) + Mathf.Min(BaseDistance(p), 70f) * 0.6f + rng.Range(0f, 10f);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        bool Canopy(Vector2 p) => world != null && world.Plants != null && world.Plants.UnderCrown(p, 1f);

        void Spawn(FaunaSpecies sp)
        {
            var g = new Group { sp = sp };
            g.home = g.centre = FindHome(sp.flies && !sp.perches, default, 0f, sp.perches);
            g.restlessAt = Time.time + rng.Range(20f, 70f);
            float a0 = rng.Range(0f, TAU);
            g.drift = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rng.Range(0.6f, 1.4f);
            g.altitude = sp.altitude * rng.Range(0.85f, 1.15f);
            g.floor = FloorUnder(g.centre, sp.circle);
            int n = sp.minPer + (int)(rng.Next() % (uint)Mathf.Max(1, sp.maxPer - sp.minPer + 1));
            for (int i = 0; i < n; i++)
            {
                var go = Instantiate(sp.prefab, transform);
                go.name = sp.name;
                var an = new Animal
                {
                    t = go.transform, anim = go.GetComponentInChildren<Animation>(), renderers = go.GetComponentsInChildren<Renderer>(),
                    group = groups.Count, shown = true, yaw = rng.Range(0f, 360f), phase = rng.Range(0f, 10f),
                    angle = rng.Range(0f, TAU), radius = rng.Range(0.55f, 1.2f), height = rng.Range(-2.5f, 2.5f),
                    restUntil = rng.Range(0f, 5f)
                };
                if (sp.perches)
                {
                    an.slot = Rotate(Vector2.right, rng.Range(0f, 360f)) * Mathf.Sqrt(rng.F01()) * sp.circle;
                    an.height = rng.Range(-1.2f, 1.2f);
                    an.pos = g.home;
                    for (int k = 0; k < 12; k++)
                    {
                        var p = g.home + an.slot * 0.6f + new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                        if (OnGround(p, out _)) { an.pos = p; break; }
                    }
                    an.at = map.Ground(an.pos);
                    an.nextAct = rng.Range(0f, 2f);
                }
                else if (sp.flies) an.pos = g.centre + new Vector2(Mathf.Cos(an.angle), Mathf.Sin(an.angle)) * sp.circle * an.radius;
                else
                {
                    an.pos = g.home;
                    for (int k = 0; k < 12; k++)
                    {
                        var p = g.home + new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)) * (sp.spacing * 1.5f + i);
                        if (OnGround(p, out _)) { an.pos = p; break; }
                    }
                }
                an.target = an.pos;
                an.dir = new Vector2(Mathf.Sin(an.yaw * Mathf.Deg2Rad), Mathf.Cos(an.yaw * Mathf.Deg2Rad));
                if (an.anim != null)
                {
                    foreach (AnimationState st in an.anim) { st.wrapMode = WrapMode.Loop; st.time = rng.Range(0f, st.length); }
                    an.anim.cullingType = AnimationCullingType.BasedOnRenderers;
                    var first = sp.perches ? sp.idle : sp.move;
                    if (first != null) an.anim.Play(first.name);
                }
                g.members.Add(an);
            }
            groups.Add(g);
        }

        // ------------------------------------------------------------ fright
        int ViewerTeam => player != null ? player.team : 0;

        bool PlayerSees(Vector2 p) =>
            world == null || !world.running || MatchSettings.spectate || world.Visible(ViewerTeam, p);

        bool GroupSeen(Group g)
        {
            foreach (var a in g.members) if (PlayerSees(a.pos)) return true;
            return false;
        }

        void OnEvent(GameEvent e)
        {
            float reach;
            float life = 40f;
            if (e.kind == GameEventKind.Impact) reach = 28f + e.scale * 6f;
            else if (e.kind == GameEventKind.Death) reach = 32f;
            else if (e.kind == GameEventKind.Fire) { reach = 22f; life = 15f; }
            else if (e.kind == GameEventKind.PlantIgnited) { reach = 26f; life = 30f; }
            else return;
            var at = new Vector2(e.pos.x, e.pos.z);
            Danger(at, reach, life);
        }

        void Danger(Vector2 at, float reach, float life)
        {
            bool seen = PlayerSees(at);
            // One record per spot: a fire-fight adds hundreds of shots.
            bool merged = false;
            for (int i = 0; i < dangers.Count; i++)
                if ((dangers[i].p - at).sqrMagnitude < 36f)
                {
                    dangers[i] = (dangers[i].p, Mathf.Max(dangers[i].t, Time.time + life), Mathf.Max(dangers[i].r, reach));
                    merged = true;
                    break;
                }
            if (!merged) dangers.Add((at, Time.time + life, reach));
            foreach (var g in groups)
            {
                if (!seen && GroupSeen(g)) continue;   // would give away what the player cannot see
                float d = ((g.sp.flies && !g.sp.perches ? g.centre : Centroid(g)) - at).magnitude;
                if (d < reach + (g.sp.perches ? 8f : g.sp.flies ? 20f : 0f)) Scare(g, at);
            }
        }

        void Scare(Group g, Vector2 from)
        {
            var c = g.sp.flies && !g.sp.perches ? g.centre : Centroid(g);
            var away = c - from;
            g.away = away.sqrMagnitude > 0.01f ? away.normalized : Rotate(Vector2.right, rng.Range(0f, 360f));
            if (g.sp.perches)
            {
                // Up and away -- unless already flying off from this spot.
                if (g.aloft && (Time.time - g.t0 < 4f || (g.to - from).magnitude > 45f)) return;
                g.fleeUntil = Time.time + 3f;
                TakeOff(g, g.away);
                return;
            }
            g.fleeUntil = Time.time + rng.Range(4f, 6f);
            g.nextHome = g.fleeUntil;
        }

        void Scan()
        {
            // Plants on fire are dangers for as long as they burn.
            var veg = world != null ? world.Plants : null;
            if (veg != null)
                foreach (int i in veg.burning)
                {
                    var p = new Vector2(veg.plants[i].pos.x, veg.plants[i].pos.z);
                    Danger(p, 24f, 6f);
                }
            // Units close by: any side, but an unseen enemy only frightens animals
            // the player cannot see either.
            if (world != null && world.running)
                foreach (var u in world.units)
                {
                    if (u == null || u.dying || u.def.neutral) continue;
                    bool seen = MatchSettings.spectate || u.team == ViewerTeam || world.Visible(ViewerTeam, u.pos);
                    foreach (var g in groups)
                    {
                        if (g.fleeUntil > Time.time) continue;
                        if (!seen && GroupSeen(g)) continue;
                        if (g.sp.perches && g.aloft) continue;
                        float reach = g.sp.wariness * (u.def.building ? 1.3f : 1f) + (g.sp.flies && !g.sp.perches ? 5f : 0f);
                        var c = g.sp.flies && !g.sp.perches ? g.centre : Centroid(g);
                        if ((c - u.pos).sqrMagnitude < reach * reach) Scare(g, u.pos);
                    }
                }
            dangers.RemoveAll(x => x.t < Time.time);
        }

        static Vector2 Centroid(Group g)
        {
            var c = Vector2.zero;
            foreach (var a in g.members) c += a.pos;
            return g.members.Count > 0 ? c / g.members.Count : c;
        }

        // ------------------------------------------------------------ update
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (Time.time >= nextScan) { nextScan = Time.time + 0.3f; Scan(); }
            foreach (var g in groups)
            {
                if (g.sp.perches) UpdatePerching(g, dt);
                else if (g.sp.flies) UpdateFlock(g, dt);
                else UpdateGround(g, dt);
            }
        }

        void UpdateGround(Group g, float dt)
        {
            float now = Time.time;
            var sp = g.sp;
            bool fleeing = g.fleeUntil > now;
            // After a scare, the group settles on new ground away from the trouble.
            if (!fleeing && g.nextHome > 0f && now > g.nextHome)
            {
                g.home = FindHome(false, Centroid(g), 60f);
                g.nextHome = 0f;
            }
            var centre = Centroid(g);
            for (int i = 0; i < g.members.Count; i++)
            {
                var a = g.members[i];
                float want;
                Vector2 goal;
                if (fleeing)
                {
                    goal = a.pos + (g.away + new Vector2(Mathf.Sin(i * 1.7f), Mathf.Cos(i * 2.3f)) * 0.3f) * 12f;
                    want = sp.runSpeed * (0.9f + 0.1f * (i % 3));
                    a.restUntil = now + rng.Range(1f, 3f);
                }
                else if (now < a.restUntil) { goal = a.pos; want = 0f; }
                else
                {
                    if ((a.target - a.pos).sqrMagnitude < 0.8f || (a.target - g.home).sqrMagnitude > 16f * 16f)
                    {
                        // Wander to another spot near home, and now and then stop there a while.
                        var t = g.home + new Vector2(rng.Range(-10f, 10f), rng.Range(-10f, 10f));
                        a.target = OnGround(t, out _) ? t : g.home;
                        if (rng.F01() < 0.45f) a.restUntil = now + rng.Range(3f, 9f);
                    }
                    goal = a.target;
                    want = (g.home - a.pos).sqrMagnitude > 22f * 22f ? sp.speed * 2.2f : sp.speed;
                }

                // Steering: toward the goal, apart from packmates, a little toward the pack.
                var to = goal - a.pos;
                var steer = to.sqrMagnitude > 0.04f ? to.normalized : Vector2.zero;
                foreach (var b in g.members)
                {
                    if (b == a) continue;
                    var d = a.pos - b.pos;
                    float m = d.magnitude;
                    if (m < sp.spacing && m > 1e-3f) steer += d / m * (sp.spacing - m) / sp.spacing * 1.6f;
                }
                if (!fleeing && (centre - a.pos).sqrMagnitude > 9f * 9f) steer += (centre - a.pos).normalized * 0.5f;
                if (want > 0f && steer.sqrMagnitude > 1e-4f)
                {
                    var dir = steer.normalized;
                    // Round what is in the way: try turning either side, wider each time.
                    float look = Mathf.Max(1.5f, want * 0.5f);
                    for (int k = 0; k < 8 && !Clear(a.pos, a.pos + dir * look); k++)
                        dir = Rotate(steer.normalized, (k % 2 == 0 ? 1 : -1) * 30f * (k / 2 + 1));
                    if (!Clear(a.pos, a.pos + dir * look)) { want = 0f; a.restUntil = now + 1f; if (!fleeing) a.target = g.home; }
                    a.dir = Vector2.Lerp(a.dir, dir, 1f - Mathf.Exp(-dt * (fleeing ? 8f : 3.5f))).normalized;
                }
                else want = 0f;
                a.speed = Mathf.MoveTowards(a.speed, want, dt * (fleeing ? 14f : 3f));
                var next = a.pos + a.dir * a.speed * dt;
                if (a.speed > 0.01f && OnGround(next, out _)) a.pos = next;
                else a.speed = 0f;
                a.yaw = Mathf.MoveTowardsAngle(a.yaw, Mathf.Atan2(a.dir.x, a.dir.y) * Mathf.Rad2Deg, dt * (fleeing ? 360f : 140f));
                Place(a, map.Ground(a.pos), Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, map.NormalAt(a.pos), 0.5f))
                                           * Quaternion.Euler(0f, a.yaw, 0f));
                Animate(a, sp, a.speed);
            }
        }

        /// <summary>The highest ground under a flock's circle, so it flies level over it.</summary>
        float FloorUnder(Vector2 c, float r)
        {
            float top = map.waterLevel;
            top = Mathf.Max(top, map.HeightAt(c));
            for (int k = 0; k < 12; k++)
            {
                float a = k * TAU / 12f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + 8f);
                if (map.InBounds(p)) top = Mathf.Max(top, map.HeightAt(p));
            }
            return top;
        }

        void UpdateFlock(Group g, float dt)
        {
            float now = Time.time;
            var sp = g.sp;
            bool fleeing = g.fleeUntil > now;
            // The flock's centre wanders the map, pushed away from recent dangers.
            var push = Vector2.zero;
            foreach (var d in dangers)
            {
                var v = g.centre - d.p;
                float m = v.magnitude;
                float r = d.r + 35f;
                if (m < r && m > 1e-3f) push += v / m * (r - m) / r;
            }
            var mid = new Vector2(map.mapSize * 0.5f, map.mapSize * 0.5f);
            if (fleeing) g.drift = Vector2.Lerp(g.drift, g.away * sp.runSpeed, 1f - Mathf.Exp(-dt * 2f));
            else
            {
                g.drift = Rotate(g.drift, Mathf.Sin(now * 0.05f + g.home.x) * 8f * dt);
                g.drift += push * dt * 4f;
                if (!map.InBounds(g.centre, 35f)) g.drift += (mid - g.centre).normalized * dt * 2f;
                float cap = sp.speed * 0.25f + push.magnitude * sp.runSpeed * 0.5f;
                if (g.drift.magnitude > cap) g.drift = g.drift.normalized * Mathf.MoveTowards(g.drift.magnitude, cap, dt * 2f);
            }
            g.centre += g.drift * dt;
            if (!map.InBounds(g.centre, 10f)) g.centre = Vector2.MoveTowards(g.centre, mid, 5f * dt);
            // Height: level over the highest ground under the whole circle, changing
            // slowly; higher while frightened.
            float floor = FloorUnder(g.centre, sp.circle);
            g.floor = Mathf.MoveTowards(g.floor, floor, dt * (floor > g.floor ? 3f : 1.2f));
            float alt = g.floor + g.altitude + (fleeing ? 12f : 0f) + push.magnitude * 8f;

            foreach (var a in g.members)
            {
                float speed = (fleeing ? sp.runSpeed : sp.speed) * (0.9f + 0.2f * a.radius);
                float r = sp.circle * a.radius;
                a.angle += speed / Mathf.Max(3f, r) * dt;
                var c = g.centre + new Vector2(Mathf.Cos(a.angle), Mathf.Sin(a.angle)) * r;
                float y = alt + a.height + Mathf.Sin(now * 0.6f + a.phase) * 0.8f;
                var p = new Vector3(c.x, y, c.y);
                var prev = a.t.position;
                var vel = (p - prev) / Mathf.Max(dt, 1e-3f);
                if (vel.sqrMagnitude > 0.1f) a.yaw = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
                a.bank = Mathf.Lerp(a.bank, Mathf.Clamp(speed * speed / Mathf.Max(3f, r) * 3f, 8f, 35f), dt * 2f);
                a.pos = c;
                Place(a, p, Quaternion.Euler(0f, a.yaw, -a.bank));
                Animate(a, sp, speed);
            }
        }

        // ------------------------------------------------------------ songbirds
        /// <summary>Up from where the flock is feeding, to open ground elsewhere: away
        /// from <paramref name="away"/> when frightened, anywhere near when restless.</summary>
        void TakeOff(Group g, Vector2 away)
        {
            float now = Time.time;
            var sp = g.sp;
            var c = Centroid(g);
            bool fleeing = away.sqrMagnitude > 0.01f;
            Vector2 dest = c;
            for (int k = 0; k < 3 && (dest - c).magnitude < 25f; k++)
                dest = fleeing ? FindHome(false, c + away * rng.Range(55f, 95f), 30f, true)
                               : FindHome(false, c, 80f, true);
            if ((dest - c).magnitude < 12f) dest = c + (fleeing ? away : Rotate(Vector2.right, rng.Range(0f, 360f))) * 40f;
            if (!OnGround(dest, out _)) dest = g.home;      // nowhere better: a turn and back
            float dist = (dest - c).magnitude;
            var side = new Vector2(-(dest - c).y, (dest - c).x) / Mathf.Max(1f, dist);
            if (g.aloft)
            {
                // Already flying: turn off toward the new spot from where the flock is now.
                c = FlockPos(g, Mathf.Clamp01((now - g.t0) / g.dur));
                dist = (dest - c).magnitude;
            }
            else g.pathY = map.HeightAt(c);
            g.from = c;
            g.to = dest;
            g.bend = side * rng.Range(-0.2f, 0.2f) * dist;
            g.t0 = now;
            g.dur = dist / (fleeing ? sp.runSpeed : sp.speed) + 1.5f;
            g.home = dest;
            bool wasAloft = g.aloft;
            g.aloft = true;
            g.skipClimb = wasAloft;    // turning in the air: no need to climb out again
            foreach (var a in g.members)
            {
                if (!wasAloft) a.launchAt = now + rng.Range(0f, fleeing ? 0.3f : 0.9f);
                // Each bird's own landing place, clear of the others.
                a.landAt = map.Ground(dest);
                for (int k = 0; k < 10; k++)
                {
                    var p = dest + a.slot * 0.6f + new Vector2(rng.Range(-0.8f, 0.8f), rng.Range(-0.8f, 0.8f));
                    if (OnGround(p, out _) && !Canopy(p)) { a.landAt = map.Ground(p); break; }
                }
            }
        }

        /// <summary>The flock's centre over the ground, a fraction <paramref name="u"/> of the way along its flight.</summary>
        static Vector2 FlockPos(Group g, float u)
        {
            float e = u * u * (3f - 2f * u);
            return Vector2.Lerp(g.from, g.to, e) + g.bend * (4f * e * (1f - e));
        }

        void UpdatePerching(Group g, float dt)
        {
            float now = Time.time;
            var sp = g.sp;
            g.centre = Centroid(g);
            if (!g.aloft && now > g.restlessAt) TakeOff(g, Vector2.zero);

            float u = g.aloft ? Mathf.Clamp01((now - g.t0) / g.dur) : 1f;
            var c = g.aloft ? FlockPos(g, u) : g.centre;
            var ahead = g.aloft ? FlockPos(g, Mathf.Min(1f, u + 0.02f)) - c : Vector2.zero;
            var heading = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector2.up;
            if (g.aloft)
            {
                // Low over the ground, rising over what is ahead (the next 20 m) in good
                // time and sinking back slowly, so the flock never skims a rise or dives
                // into every hollow; climbing out at the start, gliding down at the end.
                float floor = Mathf.Max(map.waterLevel, map.HeightAt(c));
                for (int k = 1; k <= 4; k++)
                {
                    var p = c + heading * (5f * k);
                    if (map.InBounds(p)) floor = Mathf.Max(floor, map.HeightAt(p));
                }
                g.pathY = Mathf.MoveTowards(g.pathY, floor, dt * (floor > g.pathY ? 6f : 1.5f));
            }
            float climb = g.skipClimb ? 1f : Mathf.SmoothStep(0f, 1f, u / 0.15f), descend = 1f - Mathf.SmoothStep(0f, 1f, (u - 0.7f) / 0.3f);
            float cruise = sp.altitude * Mathf.Min(climb, descend);
            // Turn the flock's formation with its heading, so the birds keep their places.
            float hx = heading.x, hy = heading.y;

            int landed = 0;
            foreach (var a in g.members)
            {
                if (!a.air)
                {
                    if (g.aloft && now >= a.launchAt && u < 0.7f)
                    {
                        a.air = true;
                        a.beatUntil = now + rng.Range(0.8f, 1.2f);   // beating hard all the way up
                        a.foldUntil = 0f;
                        a.lift = 0f;
                        if (a.anim != null && sp.move != null) a.anim.CrossFade(sp.move.name, 0.05f);
                    }
                    else
                    {
                        Feed(g, a, now, dt);
                        landed++;
                        continue;
                    }
                }
                // Where it wants to be: its place in the flock along the flight, then its
                // landing spot as the flock comes down.
                Vector3 want;
                bool landing = u > 0.8f;
                if (landing) want = a.landAt;
                else
                {
                    var off = new Vector2(a.slot.x * hy + a.slot.y * hx, -a.slot.x * hx + a.slot.y * hy);
                    var p = c + off;
                    want = new Vector3(p.x, g.pathY + cruise + a.height * Mathf.Min(climb, descend) + a.lift, p.y);
                }
                // Bounding flight: a burst of wingbeats climbing a little, then the wings
                // shut and a short fall, a few metres at a time. Beating steadily on the
                // way up and in to land.
                bool steady = landing || now - a.launchAt < 1.2f;
                if (now > a.foldUntil && now > a.beatUntil)
                {
                    if (!steady && rng.F01() < 0.75f)
                    {
                        a.foldUntil = now + rng.Range(0.18f, 0.34f);
                        if (a.anim != null && sp.fold != null) a.anim.CrossFade(sp.fold.name, 0.06f);
                    }
                    else
                    {
                        a.beatUntil = now + rng.Range(0.35f, 0.7f);
                        if (a.anim != null && sp.move != null) a.anim.CrossFade(sp.move.name, 0.06f);
                    }
                }
                bool folded = now < a.foldUntil;
                a.lift = Mathf.Clamp(a.lift + dt * (folded ? -1.6f : 0.9f), -0.9f, 0.9f);
                if (a.anim != null && sp.move != null && !folded)
                {
                    var st = a.anim[sp.move.name];
                    if (st != null) st.speed = sp.flapRate * (steady ? 1.15f : 1f);
                }
                var prev = a.at;
                float k = landing ? 3.2f : 2.6f;
                a.at = Vector3.Lerp(a.at, want, 1f - Mathf.Exp(-dt * k));
                // Never under the ground on the way (a rise between two samples).
                float gy = map.HeightAt(new Vector2(a.at.x, a.at.z));
                if (!landing && a.at.y < gy + 1f) a.at.y = gy + 1f;
                var v = (a.at - prev) / Mathf.Max(dt, 1e-3f);
                var flat = new Vector2(v.x, v.z);
                if (flat.sqrMagnitude > 0.05f)
                {
                    float yaw = Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
                    float turn = Mathf.DeltaAngle(a.yaw, yaw);
                    a.yaw = Mathf.MoveTowardsAngle(a.yaw, yaw, dt * 540f);
                    a.bank = Mathf.Lerp(a.bank, Mathf.Clamp(turn * 1.5f, -40f, 40f), 1f - Mathf.Exp(-dt * 5f));
                }
                a.pitch = Mathf.Lerp(a.pitch, Mathf.Clamp(-Mathf.Atan2(v.y, Mathf.Max(1f, flat.magnitude)) * Mathf.Rad2Deg * 0.7f, -35f, 35f), 1f - Mathf.Exp(-dt * 6f));
                a.pos = new Vector2(a.at.x, a.at.z);
                // Down: onto its feet, wings shut.
                if (landing && (a.at - a.landAt).sqrMagnitude < 0.03f)
                {
                    a.air = false;
                    a.at = a.landAt;
                    a.pos = new Vector2(a.at.x, a.at.z);
                    a.pitch = a.bank = 0f;
                    a.hopT = 1f;
                    a.nextAct = now + rng.Range(0.4f, 1.5f);
                    if (a.anim != null && sp.idle != null) a.anim.CrossFade(sp.idle.name, 0.1f);
                }
                Place(a, a.at, Quaternion.Euler(a.pitch, a.yaw, -a.bank));
            }
            // Down when they are all down -- or set down anyway, if one cannot reach
            // its place (a rock in the way) and would hold the flock up for good.
            if (g.aloft && u >= 1f && landed < g.members.Count && now - g.t0 > g.dur + 8f)
                foreach (var a in g.members)
                    if (a.air)
                    {
                        a.air = false;
                        a.at = a.landAt;
                        a.pos = new Vector2(a.at.x, a.at.z);
                        a.pitch = a.bank = 0f;
                        a.hopT = 1f;
                        if (a.anim != null && sp.idle != null) a.anim.CrossFade(sp.idle.name, 0.2f);
                        landed++;
                    }
            if (g.aloft && u >= 1f && landed == g.members.Count)
            {
                g.aloft = false;
                g.restlessAt = now + rng.Range(25f, 80f);
            }
        }

        /// <summary>A bird on the ground: hops about the flock's patch, pecks, looks round.</summary>
        void Feed(Group g, Animal a, float now, float dt)
        {
            var sp = g.sp;
            if (a.hopT < 1f)
            {
                a.hopT = Mathf.Min(1f, a.hopT + dt / 0.16f);
                a.pos = Vector2.Lerp(a.hopFrom, a.hopTo, a.hopT);
                a.at = map.Ground(a.pos) + Vector3.up * (Mathf.Sin(a.hopT * Mathf.PI) * sp.hop * 0.3f);
            }
            else if (now >= a.nextAct)
            {
                float r = rng.F01();
                if (r < 0.5f)
                {
                    // A hop or two, drifting back toward the patch when strayed, and not onto a neighbour.
                    var dir = Rotate(Vector2.right, rng.Range(0f, 360f));
                    var toHome = g.home - a.pos;
                    if (toHome.magnitude > sp.circle * 0.8f) dir = (dir * 0.4f + toHome.normalized).normalized;
                    var to = a.pos + dir * sp.hop * rng.Range(0.6f, 1.4f);
                    bool free = OnGround(to, out _);
                    foreach (var b in g.members)
                        if (free && b != a && !b.air && (b.pos - to).sqrMagnitude < sp.spacing * sp.spacing) free = false;
                    if (free)
                    {
                        a.hopFrom = a.pos;
                        a.hopTo = to;
                        a.hopT = 0f;
                        a.yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                    }
                    a.nextAct = now + (rng.F01() < 0.5f ? rng.Range(0.18f, 0.3f) : rng.Range(0.6f, 2f));
                }
                else if (r < 0.82f && sp.peck != null && a.anim != null)
                {
                    a.anim.CrossFade(sp.peck.name, 0.08f);
                    a.nextAct = now + sp.peck.length * rng.Range(0.5f, 1f);
                }
                else
                {
                    if (a.anim != null && sp.idle != null) a.anim.CrossFade(sp.idle.name, 0.15f);
                    a.yaw += rng.Range(-70f, 70f);
                    a.nextAct = now + rng.Range(0.8f, 2.5f);
                }
                a.at = map.Ground(a.pos);
            }
            else a.at = map.Ground(a.pos);
            // A peck plays once, then back to standing.
            if (a.anim != null && sp.peck != null && sp.idle != null && now >= a.nextAct - 0.05f && a.anim.IsPlaying(sp.peck.name))
                a.anim.CrossFade(sp.idle.name, 0.1f);
            Place(a, a.at, Quaternion.Euler(0f, a.yaw, 0f));
        }

        void Place(Animal a, Vector3 p, Quaternion r)
        {
            a.t.SetPositionAndRotation(p, r);
            bool show = PlayerSees(a.pos);
            if (show != a.shown)
            {
                a.shown = show;
                foreach (var ren in a.renderers) ren.enabled = show;
            }
        }

        static void Animate(Animal a, FaunaSpecies sp, float speed)
        {
            if (a.anim == null) return;
            bool still = speed < 0.15f;
            if (still && sp.idle != null)
            {
                if (!a.anim.IsPlaying(sp.idle.name)) a.anim.CrossFade(sp.idle.name, 0.3f);
                return;
            }
            if (sp.move == null) return;
            if (!a.anim.IsPlaying(sp.move.name)) a.anim.CrossFade(sp.move.name, 0.25f);
            var st = a.anim[sp.move.name];
            if (st != null) st.speed = still ? 0.15f : Mathf.Clamp(speed / Mathf.Max(0.1f, sp.moveClipSpeed), 0.3f, 3.2f);
        }

        static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
