// Unit.cs — one unit, structure or ore node. Movement is a NavMeshAgent; the
// order logic (combat, harvesting, construction, production) is the original
// game's, ticked by GameWorld so every entity updates in a known order.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    [DisallowMultipleComponent]
    public sealed class Unit : MonoBehaviour
    {
        public const int OrePerTrip = 8;
        public const float HarvestTime = 1.6f;
        public const int NodeCapacity = 1500;
        const float NavSlack = 0.6f;

        /// <summary>Editor A/B only (BugTrials.Targeting): keep a target picked up on its own
        /// until it dies, as units did before they let go of one out of their reach.</summary>
        public static bool StickyTargets;

        [Header("Identity")]
        public UnitDef def;
        [Tooltip("0 player, 1 AI, 2 neutral")] public int team = 2;
        [Tooltip("Ore remaining (ore nodes only).")] public int oreLeft = NodeCapacity;

        [System.NonSerialized] public int id;
        [System.NonSerialized] public Vector2 pos;
        [System.NonSerialized] public float yaw, turretYaw;
        /// <summary>A tracked Mech's rate of turn (rad/s): it builds up and eases off.</summary>
        [System.NonSerialized] float yawVel;
        [System.NonSerialized] public float hp, buildProgress = 1f, cooldown, damageFlash, deathTimer, bob;
        [System.NonSerialized] public float harvestTimer, queueTimer, repathTimer;
        [System.NonSerialized] public float lastDamagedT = -99f;
        /// <summary>A structure a wildfire set alight (GameWorld.StructureFires): when it
        /// caught, when it goes out, the damage still to come, and until when it cannot
        /// catch again. burnUntil is 0 when it is not burning.</summary>
        [System.NonSerialized] public float burnStart, burnUntil, burnDamageLeft, burnSafeUntil;
        [System.NonSerialized] public bool dying, working, rallySet, visibleToPlayer, everSeenByPlayer;
        [System.NonSerialized] public Order order;
        [System.NonSerialized] public Vector2 orderPos, rally;
        [System.NonSerialized] public Unit target, buildTarget, harvestNode;
        /// <summary>The target is the one it was sent after (GameWorld.CmdAttack), not one it
        /// picked up itself: only that one is chased whatever else comes into reach.</summary>
        [System.NonSerialized] public bool targetOrdered;
        /// <summary>A Digger sent to ore with its load already full while no Foundry stands: it
        /// waits at that ore, not where it is. Dropped once there is a Foundry to bring it to.</summary>
        [System.NonSerialized] public bool waitAtOre;
        [System.NonSerialized] public int carrying, kills, rank;
        /// <summary>Splash shots in a row, fired from about the same place, that burst on
        /// the ground without catching what they were aimed at -- into a rise between
        /// the two, or where a moving target used to be. The shooter knows this about
        /// its own shots, which is what lets a commander move it rather than fire the
        /// same shot again. <see cref="missFrom"/> is where the streak started: moving
        /// more than a few metres starts a new one. (Resetting on every move order did
        /// not work: units re-path every second or so, and the streak never grew.)</summary>
        [System.NonSerialized] public int shotsMissed;
        [System.NonSerialized] public Vector2 missFrom;
        /// <summary>Which way the killing blow pushed it -- away from a shell's burst, or
        /// from whoever shot it -- and how hard, 0 a rifle round .. 1 a shell landing
        /// beside it. Set as it dies; its wreck (View/UnitWreck) falls that way.</summary>
        [System.NonSerialized] public Vector2 killDir;
        [System.NonSerialized] public float killForce;
        /// <summary>How hard a Mauler is shouldering a tree over right now, 0..1
        /// (Vegetation): it slows it, and its hull rides up against the trunk.</summary>
        [System.NonSerialized] public float pushLoad;
        [System.NonSerialized] public readonly List<UnitType> queue = new List<UnitType>(5);
        [System.NonSerialized] public NavMeshAgent agent;
        [System.NonSerialized] public UnitView view;
        /// <summary>The Mech's parts and guns (Mech only).</summary>
        [System.NonSerialized] public MechCore mech;
        /// <summary>Broke and ran (GameWorld.Morale): it takes no orders, fights only when its
        /// camp is set upon, and <see cref="fleeing"/> while still on its way there.</summary>
        [System.NonSerialized] public bool deserted, fleeing;
        /// <summary>An Armory's research (GameWorld.Roster): the ArmyUpgrade it works on (-1 none)
        /// and the seconds left of its total.</summary>
        [System.NonSerialized] public int researchLine = -1;
        [System.NonSerialized] public float researchLeft, researchTotal;
        /// <summary>An aircraft's flight (no NavMesh agent): where it is going, whether it is,
        /// and how fast it is moving over the ground.</summary>
        [System.NonSerialized] public Vector2 flyGoal, flyVel;
        [System.NonSerialized] public bool flyHasGoal;
        /// <summary>A plane's (fixedWing) run at its target: seconds it still holds its heading past the
        /// target before coming round, where it circles with nowhere to go, and how far below its
        /// cruise height it has dived on the way in (metres; AirPoint, its muzzle and the view take it).</summary>
        [System.NonSerialized] public float runOut, dip;
        [System.NonSerialized] public Vector2 loiterAt;
        const float RunOut = 1.0f, Dive = 1.6f;
        /// <summary>How wide a plane circles: a little over its turning circle.</summary>
        float PlaneCircle => Speed / Mathf.Max(0.3f, TurnRate) * 1.25f;
        /// <summary>Shots fired by a gun with more than one barrel (the Ironclad's shoulders take turns).</summary>
        [System.NonSerialized] public int volleys;

        GameWorld world;
        NavMeshObstacle obstacle;
        int pathRetries;
        float fleeStall;   // a fleeing deserter's seconds on a path without getting nearer the corner it steers for (held by the crowd)
        float fleeBest;    // the nearest it has been to the corner it is steering for
        Vector2 fleeCorner;  // that corner (a new one, or a new route, starts the count afresh)
        Vector2 fleeCorner2; float fleeBest2;   // the corner before it and its best: flickering back is no fresh start

        public UnitType Type => def.type;
        public bool Complete => buildProgress >= 1f;
        public bool OnFire => burnUntil > 0f;
        /// <summary>How hard a burning structure is going, 0..1: it takes hold over a
        /// couple of seconds and dies down over the last few.</summary>
        public float FireHeat => burnUntil <= 0f ? 0f
            : Mathf.Clamp01(Mathf.Min((world.time - burnStart) * 0.5f, (burnUntil - world.time) * 0.3f));
        public float MaxHp => mech != null ? mech.MaxHp : def.hp * (1f + 0.1f * rank);
        /// <summary>Not to be shot at: a Mech still coming down from orbit.</summary>
        public bool Untargetable => mech != null && !mech.Landed;
        public float Speed => mech != null ? mech.Speed : def.speed;
        public float TurnRate => mech != null ? mech.TurnRate : def.turnRate;
        public Vector3 Ground => world.Map.Ground(pos);
        public bool HasTurret => def.type == UnitType.Mauler || def.type == UnitType.Sentinel ||
                                 def.type == UnitType.Mech || def.type == UnitType.MechBay ||
                                 def.type == UnitType.Lancer || def.type == UnitType.Aegis || def.type == UnitType.Ironclad ||
                                 def.type == UnitType.Prospector || def.type == UnitType.MechHunter;
        public bool Moving => def.flying ? flyHasGoal || flyVel.sqrMagnitude > 0.25f
                            : agent != null && agent.enabled && agent.isOnNavMesh && (agent.pathPending || agent.hasPath);
        /// <summary>How fast it is going over the ground (an aircraft's own, else the agent's).</summary>
        public Vector3 Velocity => def.flying ? new Vector3(flyVel.x, 0f, flyVel.y)
                                 : agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        public GameWorld World => world;

        public static bool Live(Unit u) => u != null && !u.dying;

        public void Init(GameWorld w, UnitDef d, int t, int uid, bool complete, float initialYaw)
        {
            world = w;
            def = d;
            team = t;
            id = uid;
            CachePosition();
            yaw = turretYaw = initialYaw;
            buildProgress = complete ? 1f : 0f;
            hp = complete ? d.hp : d.hp * 0.08f;
            rally = pos + new Vector2(0f, d.radius + 4f);
            researchLine = -1;
            flyHasGoal = false;
            flyVel = Vector2.zero;
            runOut = dip = 0f;
            loiterAt = pos;
            if (d.type != UnitType.Ore) oreLeft = 0;
            else if (oreLeft <= 0) oreLeft = NodeCapacity;
            view = GetComponent<UnitView>();
            if (d.type == UnitType.Mech)
            {
                mech = GetComponent<MechCore>();
                if (mech == null) mech = gameObject.AddComponent<MechCore>();
                mech.Bind(this, w);
            }

            // An aircraft flies over everything: no agent (Unit.TickFlight moves it).
            if (d.flying)
            {
                var stray = GetComponent<NavMeshAgent>();
                if (stray != null) { stray.enabled = false; Destroy(stray); }
            }
            else if (d.IsMobile)
            {
                agent = GetComponent<NavMeshAgent>();
                if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
                agent.speed = d.speed;
                agent.angularSpeed = 0f;
                agent.acceleration = d.speed * 4.5f;
                agent.radius = d.radius;
                agent.height = 2f;
                agent.stoppingDistance = 0.25f;
                agent.autoBraking = true;
                agent.updateRotation = false;
                // Diggers ignore each other, the way StarCraft workers mineral-walk:
                // a mining line of avoiding agents turns into a traffic jam.
                agent.obstacleAvoidanceType = d.harvester
                    ? ObstacleAvoidanceType.NoObstacleAvoidance
                    : ObstacleAvoidanceType.MedQualityObstacleAvoidance;
                agent.avoidancePriority = d.type == UnitType.Mech ? 10 : d.type == UnitType.Mauler || d.heavyArmour ? 30
                                        : d.type == UnitType.Skimmer || d.type == UnitType.Skitter ? 60 : 50;
                // Boulders stand on Rubble ground: only a Mauler's path runs
                // through them, and a Mech's (they crush them, GameWorld.CrushBoulders).
                agent.areaMask = d.type == UnitType.Mauler || d.type == UnitType.Mech ? NavMesh.AllAreas : GameWorld.GroundAreas;
                if (NavMesh.SamplePosition(transform.position, out var hit, 10f, NavMesh.AllAreas))
                {
                    agent.enabled = true;
                    agent.Warp(hit.position);
                    CachePosition();
                }
            }
            else if (d.building || d.type == UnitType.Ore)
            {
                obstacle = GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Capsule;
                obstacle.radius = d.radius * 0.92f;
                obstacle.height = 4f;
                obstacle.center = new Vector3(0f, 2f, 0f);
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                obstacle.enabled = true;
            }
            transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            if (view != null) view.Bind(this);
        }

        public void CachePosition()
        {
            var p = transform.position;
            pos = new Vector2(p.x, p.z);
        }

        public float Dist(Unit o) => (o.pos - pos).magnitude;
        public float Dist(Vector2 p) => (p - pos).magnitude;

        public void MoveTo(Vector2 dest)
        {
            if (def.flying) { flyGoal = world.ClampToMap(dest); flyHasGoal = true; return; }
            if (agent == null || !agent.enabled) return;
            if (!agent.isOnNavMesh)
            {
                if (!NavMesh.SamplePosition(transform.position, out var h, 6f, NavMesh.AllAreas)) return;
                agent.Warp(h.position);
            }
            agent.isStopped = false;
            agent.SetDestination(world.Map.Ground(dest));
        }

        public void Halt()
        {
            if (def.flying)
            {
                // A plane already circling keeps its spot (taken from where it is on the circle, or on a
                // run, the spot walked off with every Hold or Stop); one stopped on its way circles there.
                if (!def.fixedWing || flyHasGoal || (pos - loiterAt).magnitude > PlaneCircle * 2f) loiterAt = pos;
                flyHasGoal = false;
                return;
            }
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
        }

        bool Arrived() => def.flying ? !flyHasGoal :
            agent == null || !agent.enabled || !agent.isOnNavMesh ||
            (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.5f));

        public void BeginDeath()
        {
            dying = true;
            deathTimer = 0f;
            order = Order.Idle;
            target = null;
            if (agent != null) agent.enabled = false;
            if (obstacle != null) obstacle.enabled = false;
            if (view != null) view.OnDeath();
        }

        public void CreditKill()
        {
            kills++;
            if (mech != null) return;   // a Mech is not promoted: it is already what it is
            int newRank = kills >= 10 ? 3 : (kills >= 5 ? 2 : (kills >= 2 ? 1 : 0));
            if (newRank <= rank) return;
            hp += def.hp * 0.1f * (newRank - rank);
            rank = newRank;
            world.Raise(new GameEvent { kind = GameEventKind.Promoted, unit = this, type = Type, team = team, pos = Ground, scale = rank });
        }

        // ------------------------------------------------------------ tick
        public void Tick(float dt)
        {
            damageFlash = Mathf.Max(0f, damageFlash - dt * 6.5f);
            cooldown = Mathf.Max(0f, cooldown - dt);
            working = false;

            if (def.building)
            {
                if (!Complete) return;
                if (def.Armed) UpdateCombat(dt);
                if (researchLine >= 0) world.TickArmory(this, dt);
                UpdateProduction(dt);
                return;
            }
            if (def.neutral) return;
            if (mech != null) { TickMech(dt); return; }
            if (deserted) { TickDeserter(dt); return; }

            // Veterans patch themselves up once out of the fight.
            if (rank > 0 && world.time - lastDamagedT > 6f && hp < MaxHp)
                hp = Mathf.Min(MaxHp, hp + 0.8f * rank * dt);

            if (order == Order.Build) UpdateBuild(dt);
            if (order == Order.Harvest || order == Order.Return) UpdateHarvest(dt);
            UpdateCombat(dt);

            // Close on the target for attack orders and for attack-move once a
            // target is acquired, or a unit that spots a distant enemy stands still.
            if ((order == Order.Attack || order == Order.AttackMove) && Live(target) && !def.fixedWing)
            {
                float d = Dist(target) - target.def.radius;
                if (d > def.range * 0.92f && (repathTimer -= dt) <= 0f)
                {
                    // Under an aircraft that may be over water or a plateau: as near as it can get.
                    MoveTo(target.def.flying && !def.flying ? world.NearestReachable(this, target.pos) : target.pos);
                    repathTimer = world.Rng.Range(0.45f, 0.75f);
                }
            }
            if (order == Order.AttackMove && target == null && !Moving && Dist(orderPos) > 2.5f && (repathTimer -= dt) <= 0f)
            {
                MoveTo(orderPos);
                repathTimer = 1.2f;
            }
            if ((order == Order.Move || order == Order.AttackMove) && target == null && Arrived() && Dist(orderPos) < (def.fixedWing ? PlaneCircle + 3f : 4f))
                order = Order.Idle;
            if (order == Order.Move && !Moving) KeepGoing(dt);
            else if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath) pathRetries = 0;

            if (def.fixedWing) TickPlane(dt);
            else if (def.flying) TickFlight(dt);
            else UpdateFacing(dt);
        }

        /// <summary>An aircraft: no NavMesh, it flies straight at its goal over whatever is
        /// under it, eases in at the end, keeps clear of other aircraft, and stays over the map.
        /// It turns to face where it goes; standing (hovering) and firing it faces its target.</summary>
        void TickFlight(float dt)
        {
            Vector2 want = Vector2.zero;
            if (flyHasGoal)
            {
                Vector2 to = flyGoal - pos;
                float d = to.magnitude;
                if (d < 0.6f) flyHasGoal = false;
                else want = to / d * Mathf.Min(Speed, d * 1.8f + 0.5f);
            }
            want += world.AirSeparation(this) * Speed * 0.6f;
            flyVel = Vector2.MoveTowards(flyVel, want, Speed * 2.4f * dt);
            Vector2 np = world.ClampToMap(pos + flyVel * dt);
            transform.position = world.Map.Ground(np);
            pos = np;
            float speed = flyVel.magnitude;
            bool aiming = Live(target) && Dist(target) - target.def.radius <= def.range;
            if (speed > 0.6f && !aiming)
            {
                yaw = ApproachAngle(yaw, Mathf.Atan2(flyVel.x, flyVel.y), TurnRate * dt);
                bob += speed * dt * 1.2f;
            }
            turretYaw = yaw;
            transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
        }

        /// <summary>A plane (fixedWing): it never stops. It flies its heading at full speed, turning no
        /// faster than its turn rate; sent somewhere, it circles there; with a target it makes runs at
        /// it -- in, firing along the nose while the target bears (AimAndFire), over it and on for a
        /// moment, then round for the next -- diving a little on the way in.</summary>
        void TickPlane(float dt)
        {
            float speed = Speed, circle = PlaneCircle;
            bool attacking = Live(target) && !target.Untargetable && GameWorld.CanHit(this, target);
            // Idle or holding it stays over where it was put: runs only at what comes within its reach
            // of the circle (free, it followed a scout across the map, and circled wherever its last
            // target died).
            bool leashed = order == Order.Idle || order == Order.Hold;
            if (attacking && leashed && (target.pos - loiterAt).magnitude > def.range + circle) attacking = false;
            if (!attacking && order == Order.AttackMove && Dist(orderPos) > circle + 3f)
            {
                flyGoal = world.ClampToMap(orderPos);
                flyHasGoal = true;
            }
            Vector2 heading = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            Vector2 steer;
            bool inbound = false;
            if (attacking)
            {
                Vector2 to = target.pos - pos;
                float d = to.magnitude;
                if (runOut > 0f) { runOut -= dt; steer = pos + heading * 10f; }
                else
                {
                    steer = target.pos;
                    inbound = d < def.range * 2f;
                    // Over it, or gone past it: on out for a moment, then round for the next run.
                    if (d < 3f + target.def.radius || (d < def.range && Vector2.Dot(heading, to) < 0f)) runOut = RunOut;
                }
                if (!leashed) loiterAt = target.pos;
            }
            else if (flyHasGoal)
            {
                steer = flyGoal;
                if ((flyGoal - pos).magnitude < circle + 2f) { flyHasGoal = false; loiterAt = flyGoal; }
            }
            else
            {
                // Circling: aim a little further round the ring about where it was sent.
                Vector2 r = pos - loiterAt;
                float a = (r.sqrMagnitude < 0.01f ? yaw : Mathf.Atan2(r.x, r.y)) + 0.7f;
                steer = loiterAt + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * circle;
            }
            // Never out over the map's edge: near it, head for the middle.
            float m = world.MapSize, edge = 12f;
            if (pos.x < edge || pos.y < edge || pos.x > m - edge || pos.y > m - edge)
                steer = Vector2.Lerp(steer, new Vector2(m, m) * 0.5f, 0.6f);
            yaw = ApproachAngle(yaw, Mathf.Atan2(steer.x - pos.x, steer.y - pos.y), TurnRate * dt);
            heading = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            flyVel = heading * speed + world.AirSeparation(this) * speed * 0.3f;
            Vector2 np = world.ClampToMap(pos + flyVel * dt);
            transform.position = world.Map.Ground(np);
            pos = np;
            dip = Mathf.MoveTowards(dip, inbound ? Dive : 0f, dt * (inbound ? 1.2f : 0.8f));
            bob += speed * dt * 1.2f;
            turretYaw = yaw;
            transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
        }

        /// <summary>The Mech: its guns look after themselves (MechCore); its AI gives it
        /// only move orders, which run as anyone's do.</summary>
        void TickMech(float dt)
        {
            mech.Tick(dt);
            if (!mech.Landed || dying) return;
            if (order == Order.Move && Arrived() && Dist(orderPos) < 4f) order = Order.Idle;
            if (order == Order.Move && !Moving) KeepGoing(dt);
            else if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath) pathRetries = 0;
            UpdateFacing(dt);
        }

        /// <summary>A move order with no path and none coming. That is arrival only at the
        /// goal or at the end of a partial path (as near as the ground allows). The
        /// other way to lose a path is a NavMesh rebuild under it -- a tree felled or a
        /// rock crushed anywhere along the way, and a Mauler fells its own -- and taking
        /// that for arrival dropped the order: the unit stood where it was until ordered
        /// again. So it asks for the path again, and gives up only if it keeps getting
        /// none (the goal cannot be reached at all).</summary>
        void KeepGoing(float dt)
        {
            bool atEnd = Dist(orderPos) < 4f;
            if (!atEnd && agent != null && agent.enabled && agent.isOnNavMesh)
            {
                Vector3 d = agent.destination - transform.position;
                atEnd = agent.pathStatus == NavMeshPathStatus.PathPartial || new Vector2(d.x, d.z).sqrMagnitude < 1.5f * 1.5f;
            }
            if (atEnd || pathRetries >= 4)
            {
                order = Order.Idle;
                pathRetries = 0;
            }
            else if ((repathTimer -= dt) <= 0f)
            {
                MoveTo(orderPos);
                repathTimer = 0.4f;
                pathRetries++;
            }
        }

        void UpdateFacing(float dt)
        {
            if (agent == null || !agent.enabled) return;
            Vector3 v = agent.velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            float pace = 1f;
            bool trackedMech = mech != null && mech.design != null && mech.design.locomotion == MechLocomotion.Tracks;
            if (speed > 0.25f)
            {
                float want = Mathf.Atan2(v.x, v.z);
                bool aiming = Live(target) && Dist(target) - target.def.radius <= def.range && !HasTurret;
                if (trackedMech)
                {
                    // A tracked Mech steers for the next corner of its path, not the agent's
                    // velocity of the moment (avoidance and the pace coupling below made that
                    // wander, and the hull fishtailed 3-4 degrees about once a second), and
                    // its turning builds up and eases off: a heavy machine on tracks.
                    Vector3 st = agent.steeringTarget - transform.position;
                    if (new Vector2(st.x, st.z).sqrMagnitude > 0.25f) want = Mathf.Atan2(st.x, st.z);
                    float wantRate = Mathf.Clamp(WrapAngle(want - yaw) * 2.5f, -TurnRate, TurnRate);
                    yawVel = Mathf.MoveTowards(yawVel, wantRate, 4f * dt);
                    yaw = WrapAngle(yaw + yawVel * dt);
                }
                else if (!aiming) yaw = ApproachAngle(yaw, want, TurnRate * dt);
                bob += speed * dt * 3.2f;
                // Tracks cannot strafe: a Mauler slows until it has turned to face
                // where it is going, instead of sliding sideways like a hovercraft.
                if (Type == UnitType.Mauler || Type == UnitType.Lancer || Type == UnitType.Aegis || Type == UnitType.Hauler || Type == UnitType.Ironclad ||
                    Type == UnitType.Prospector || Type == UnitType.MechHunter)
                    pace = Lerp(0.3f, 1f, Saturate(Mathf.Cos(WrapAngle(want - yaw))));
                // Nor can legs, much: a walking Mech turns before it goes; a grav
                // skirt slides whichever way it likes.
                else if (mech != null && mech.design != null && mech.design.locomotion != MechLocomotion.Hover)
                    pace = Lerp(mech.design.locomotion == MechLocomotion.Tracks ? 0.3f : 0.45f, 1f, Saturate(Mathf.Cos(WrapAngle(want - yaw))));
            }
            else
            {
                yawVel = Mathf.MoveTowards(yawVel, 0f, 4f * dt);
                if (HasTurret && !Live(target))
                    turretYaw = ApproachAngle(turretYaw, yaw, 1.2f * dt);
            }
            // Wading through the shallows is slow going; a Skimmer hovers over them, and
            // so does a grav skirt. A Mech's legs are long: the shallows barely slow it.
            if (mech != null)
            {
                if (mech.design != null && !mech.design.Loco.skims)
                    pace *= Lerp(1f, 0.8f, Saturate((world.Map.WaterDepth(pos) - 0.3f) / 1.2f));
            }
            else if (Type != UnitType.Skimmer)
                pace *= Lerp(1f, 0.55f, Saturate((world.Map.WaterDepth(pos) - 0.15f) / 0.7f));
            // Shouldering a tree over takes the way out of a Mauler: down to a crawl
            // against a thick trunk as it meets it, picking up as the roots give.
            pace *= 1f - 0.75f * pushLoad;
            agent.speed = Speed * pace;
            if (!HasTurret) turretYaw = yaw;
            transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
        }

        void UpdateCombat(float dt)
        {
            if (!def.Armed || !Complete) return;
            // Auto-acquire unless explicitly told to move without engaging.
            bool acquires = order != Order.Move && order != Order.Harvest && order != Order.Return && order != Order.Build;
            // An armed worker (the Prospector) at its work shoots back at what comes into its
            // reach, and keeps working: it neither chases nor drops the load.
            if (!acquires && def.harvester && def.weapon != WeaponKind.Auto && (order == Order.Harvest || order == Order.Return))
            {
                if (!Live(target) || target.team == team || Dist(target) - target.def.radius > def.range || !GameWorld.CanHit(this, target))
                    target = world.NearestEnemyInReach(this, def.range, false);
                targetOrdered = false;
                // Turned onto the target first: the Prospector's roof gun is a turret.
                if (target != null && Dist(target) - target.def.radius >= def.minRange) AimAndFire(dt);
                return;
            }
            if (!Live(target) || target.team == team)
            {
                target = null;
                targetOrdered = false;
                if (acquires) target = world.NearestEnemy(this, def.sight);
                if (order == Order.Attack && target == null) order = Order.Idle;
            }
            else if (!StickyTargets && acquires && !(order == Order.Attack && targetOrdered) && Dist(target) - target.def.radius > def.range)
            {
                // A target it picked up itself (not one it was sent after: once that dies, or
                // when it struck back at whoever was nearest, what it picks is its own) gives
                // way to an enemy in reach, and is let go once it is out of sight. Kept until it
                // died, a Sentinel or a bay's gun that had locked onto a scout at the edge of its
                // sight stood silent, whatever came up to its walls, until that scout died
                // somewhere else; an idle or holding squad the same; and a squad whose attack
                // target died chased the next thing it saw off across the map. Closing on
                // something that fights back, it gives way only to another that does: going
                // for a Sentinel, the Foundry beside it came into reach first, and they stood
                // shooting the wall while the tower shot them.
                bool closing = order == Order.Attack || order == Order.AttackMove;
                // Whatever it is doing, what fights back comes first: by edge alone a tower took
                // a Garrison's wall over the rifleman shooting it from nearer its middle.
                var inReach = world.NearestEnemyInReach(this, def.range, true);
                if (inReach == null && !(closing && GameWorld.FightsBack(target))) inReach = world.NearestEnemyInReach(this, def.range, false);
                if (inReach != null) target = inReach;
                else if (Dist(target) - target.def.radius > def.sight) target = world.NearestEnemy(this, def.sight);
                targetOrdered = false;
            }
            if (target == null) return;
            // A mortar too close to fire: something else in its band, or it waits for the
            // others to deal with what is on top of it.
            if (def.minRange > 0f && Dist(target) - target.def.radius < def.minRange)
            {
                var band = world.NearestEnemyInBand(this, def.minRange, def.range);
                if (band != null) { target = band; targetOrdered = false; }
                else return;
            }
            if (!AimAndFire(dt)) return;
            // A plane does not stop to shoot: it makes its runs (TickPlane).
            if ((order == Order.Attack || order == Order.AttackMove) && !def.fixedWing) Halt();
        }

        /// <summary>Turn onto the target and fire when it bears: false if it is out of reach.</summary>
        bool AimAndFire(float dt)
        {
            float d = Dist(target) - target.def.radius;
            if (d > def.range) return false;
            float want = Mathf.Atan2(target.pos.x - pos.x, target.pos.y - pos.y);
            if (HasTurret)
            {
                float turn = Type == UnitType.Mauler || Type == UnitType.Lancer ? 2.6f : Type == UnitType.Aegis ? 4f : def.turnRate;
                turretYaw = ApproachAngle(turretYaw, want, turn * dt);
                if (Mathf.Abs(WrapAngle(want - turretYaw)) < 0.10f && cooldown <= 0f) Fire();
            }
            else if (def.fixedWing)
            {
                // A plane fires along its nose as it passes: it does not turn on the spot to aim.
                if (Mathf.Abs(WrapAngle(want - yaw)) < 0.35f && cooldown <= 0f) Fire();
            }
            else
            {
                yaw = ApproachAngle(yaw, want, def.turnRate * dt);
                if (Mathf.Abs(WrapAngle(want - yaw)) < 0.35f && cooldown <= 0f) Fire();
            }
            return true;
        }

        /// <summary>It throws down its oath (GameWorld.Morale): off to <paramref name="slot"/>,
        /// its place in the camp, with a clean count of path retries (one carried over from a
        /// move order would have it settle mid-field after a single lost path).</summary>
        public void BeginDesertion(Vector2 slot)
        {
            deserted = true;
            fleeing = true;
            order = Order.Move;
            target = null;
            harvestNode = null;
            buildTarget = null;
            orderPos = slot;
            pathRetries = 0;
            repathTimer = 0f;
            fleeStall = 0f;
            fleeBest = fleeBest2 = float.MaxValue;
            fleeCorner = fleeCorner2 = new Vector2(float.MaxValue, float.MaxValue);
            MoveTo(slot);
        }

        /// <summary>A deserter: it runs for its side's camp, then sits at its spot there (or
        /// wherever it had to settle). It shoots only while an enemy has hit one of them near
        /// that spot in the last few seconds, only at enemies near it, and never goes further
        /// than CampLeash from it to do so. It never fires on its own side's Mech, even when
        /// that comes for it.</summary>
        void TickDeserter(float dt)
        {
            var M = world.MoraleOf(team);
            Vector2 camp = M.camp;
            if (fleeing)
            {
                target = null;
                // Re-asks for its way after a NavMesh rebuild takes it: it does not stop short.
                // Four asks that get it nowhere (the way there is gone): it settles where it is,
                // at once if it is in the camp. The end of a partial path counts as nowhere (it
                // may keep the path and never read as stopped), and so does standing on a path
                // for 2 s (the others crowding the same end held one short of it, still fleeing).
                // Waiting for its path is not standing on one, and a jam far from the end (boxed in
                // by the knot it broke from) is given longer than one at the end: counted alike, a
                // deserter at the back of a knot could settle where it broke, still in the fight.
                // A stall carries over while it waits for the path it asked for after one: zeroed
                // there, the next tick read "walking" and cleared its retries, and one boxed in far
                // from the end of its path fled for ever.
                bool onPath = Moving && agent.hasPath && !agent.pathPending;
                // By progress, not speed: penned in with a Mauler in the only way out it jostled
                // at 0.2 m/s and more without getting anywhere, its stall stayed at 0 and it fled
                // for ever (BugTrials.PenJam: still fleeing after 90 s). Progress toward the corner
                // it steers for, not along its way left: that reads Infinity on half a long flight
                // (FleeTrace), straight-line distance does not shrink on a leg bending away, and a
                // longer new route read as no progress; a new corner or route starts afresh.
                if (onPath)
                {
                    Vector2 corner = new Vector2(agent.steeringTarget.x, agent.steeringTarget.z);
                    float togo = (corner - pos).magnitude;
                    // A new corner restarts the best, not the count; back to the corner before, its
                    // own best stands. Jammed, the corner flickered between the gap's two sides (5-27
                    // changes while it stood still, BugTrials.PenJam), and each fresh best let a sway
                    // read as progress.
                    if ((corner - fleeCorner).sqrMagnitude > 1f)
                    {
                        if ((corner - fleeCorner2).sqrMagnitude <= 1f)
                        {
                            (fleeCorner, fleeCorner2) = (fleeCorner2, fleeCorner);
                            (fleeBest, fleeBest2) = (fleeBest2, fleeBest);
                            if (togo < fleeBest - 0.5f) { fleeBest = togo; fleeStall = 0f; } else fleeStall += dt;
                        }
                        else
                        {
                            fleeCorner2 = fleeCorner; fleeBest2 = fleeBest;
                            fleeCorner = corner; fleeBest = togo;
                            fleeStall += dt;
                        }
                    }
                    else if (togo < fleeBest - 0.5f) { fleeBest = togo; fleeStall = 0f; }
                    else fleeStall += dt;
                }
                else if (!(Moving && agent.pathPending)) fleeStall = 0f;
                float stallLimit = onPath && agent.remainingDistance > 8f ? 6f : 2f;
                bool deadEnd = fleeStall > stallLimit || agent != null && agent.enabled && agent.isOnNavMesh && !agent.pathPending &&
                               agent.pathStatus == NavMeshPathStatus.PathPartial && agent.remainingDistance <= agent.stoppingDistance + 0.5f;
                bool stopped = !Moving || deadEnd;
                if (Dist(orderPos) < 3f || (stopped && (Dist(camp) < 10f || pathRetries >= 4)))
                {
                    fleeing = false;
                    order = Order.Hold;
                    pathRetries = 0;
                    if (Dist(orderPos) >= 3f) orderPos = pos;
                    Halt();
                }
                else if (stopped) { if ((repathTimer -= dt) <= 0f) { MoveTo(orderPos); repathTimer = 0.8f; pathRetries++; } }   // a dead end counts as a retry
                else if (agent.hasPath && agent.remainingDistance > 2f) pathRetries = 0;   // walking, not at a dead end
                UpdateFacing(dt);
                return;
            }
            order = Order.Hold;
            // Its own spot (in the camp, or wherever it had to settle) is what it defends, and
            // only when an enemy hit one of them near it: a deserter stranded far from the camp
            // fights back too, and one in the camp does not answer a hit somewhere else.
            Vector2 spot = orderPos;
            bool alarmed = def.Armed && world.time - M.alarmT < GameWorld.CampAlarm &&
                           (M.alarmAt - spot).sqrMagnitude < GameWorld.AlarmReach * GameWorld.AlarmReach;
            float reach = GameWorld.CampLeash + def.range;
            if (!alarmed || !Live(target) || target.team == team || (target.pos - spot).sqrMagnitude > reach * reach)
            {
                target = null;
                if (alarmed)
                {
                    var t = world.NearestEnemy(this, def.sight);
                    if (t != null && (t.pos - spot).sqrMagnitude <= reach * reach) target = t;
                }
            }
            if (target != null)
            {
                pathRetries = 0;   // a chase is not a walk back that failed (counted, raids moved its spot)
                if (AimAndFire(dt)) { if (Moving) Halt(); }
                else if ((repathTimer -= dt) <= 0f)
                {
                    // Up to the edge of its leash, no further.
                    Vector2 to = target.pos - spot;
                    Vector2 go = to.sqrMagnitude > GameWorld.CampLeash * GameWorld.CampLeash ? spot + to.normalized * GameWorld.CampLeash : target.pos;
                    MoveTo(go);
                    repathTimer = 0.6f;
                }
            }
            else if (Dist(orderPos) > 2.5f && !Moving && (repathTimer -= dt) <= 0f)
            {
                // Four walks back that end short of it (built over, or a blast put it on ground
                // that does not join): this is its spot now (it re-asked every 1.5 s all match).
                if (++pathRetries > 4) { orderPos = pos; pathRetries = 0; }
                else MoveTo(orderPos);
                repathTimer = 1.5f;
            }
            else if (Dist(orderPos) <= 2.5f) pathRetries = 0;
            UpdateFacing(dt);
        }

        void Fire()
        {
            cooldown = def.cooldown;
            world.Fire(this, target);
        }

        void UpdateHarvest(float dt)
        {
            if (order == Order.Harvest)
            {
                // Full already (sent back to the ore with a load, or off a build it carried
                // one to): bring it in first rather than mine over it.
                if (carrying >= def.carry) { order = Order.Return; waitAtOre = world.NearestDropoff(this) == null; return; }
                var node = harvestNode;
                if (!Live(node) || node.oreLeft <= 0)
                {
                    harvestNode = world.NearestFreeNode(pos, team);
                    // No ore left to mine: a part load is still brought in (idle, it kept it for good).
                    if (harvestNode == null) { order = carrying > 0 ? Order.Return : Order.Idle; return; }
                    MoveTo(harvestNode.pos);
                    return;
                }
                if (Dist(node) <= node.def.radius + def.radius + 0.9f + NavSlack)
                {
                    Halt();
                    yaw = ApproachAngle(yaw, Mathf.Atan2(node.pos.x - pos.x, node.pos.y - pos.y), 8f * dt);
                    working = true;
                    harvestTimer += dt;
                    if (harvestTimer >= def.harvestTime)
                    {
                        harvestTimer = 0f;
                        // Topped up, not replaced: a part load (the last of a seam) is kept.
                        int take = Mathf.Min(def.carry - carrying, node.oreLeft);
                        node.oreLeft -= take;
                        carrying += take;
                        if (node.oreLeft <= 0) world.Deplete(node);
                        order = Order.Return;
                        var dp = world.NearestDropoff(this);
                        if (dp != null) MoveTo(dp.pos);
                    }
                }
                else if (!Moving || (repathTimer -= dt) <= 0f)
                {
                    MoveTo(node.pos);
                    repathTimer = 1.8f;
                }
            }
            else
            {
                var dp = world.NearestDropoff(this);
                // Bringing it in now: if this Foundry falls on the way it stops like any other
                // (kept, the flag from an order given while one stood -- or from a build it
                // finished -- sent it back to the ore after all).
                if (dp != null) waitAtOre = false;
                // Nowhere to bring it: wait with the load until a Foundry stands again, at the
                // ore it was sent to with it (stopped on the spot, one sent to safe ore to get
                // it out from under a gun stood where it was), else where it is (turned back to
                // their ore, the loads on the road walked into whatever had taken the Foundry).
                // Sent back to mine, a Digger dropped each load for the next and stripped the
                // fields for nothing once its Foundry had fallen.
                if (dp == null)
                {
                    var waitAt = waitAtOre ? harvestNode : null;
                    if (Live(waitAt) && Dist(waitAt) > waitAt.def.radius + def.radius + 0.9f + NavSlack)
                    {
                        // On a timer only: several waiting at one seam can hold one another short of
                        // it, and asking every tick while held cost a path request a frame each.
                        if ((repathTimer -= dt) <= 0f) { MoveTo(waitAt.pos); repathTimer = 1.8f; }
                    }
                    else if (Moving) Halt();
                    return;
                }
                if (Dist(dp) <= dp.def.radius + def.radius + 1.2f + NavSlack)
                {
                    world.Deposit(team, carrying);
                    carrying = 0;
                    order = Order.Harvest;
                    if (!Live(harvestNode) || harvestNode.oreLeft <= 0) harvestNode = world.NearestFreeNode(pos, team);
                    if (harvestNode != null) MoveTo(harvestNode.pos);
                }
                else if (!Moving || (repathTimer -= dt) <= 0f)
                {
                    MoveTo(dp.pos);
                    repathTimer = 1.8f;
                }
            }
        }

        void UpdateBuild(float dt)
        {
            var b = buildTarget;
            if (!Live(b) || b.Complete)
            {
                order = Order.Idle;
                buildTarget = null;
                return;
            }
            if (Dist(b) <= b.def.radius + def.radius + 1.0f + NavSlack)
            {
                Halt();
                yaw = ApproachAngle(yaw, Mathf.Atan2(b.pos.x - pos.x, b.pos.y - pos.y), 8f * dt);
                working = true;
                float rate = dt / Mathf.Max(0.5f, b.def.buildTime);
                b.buildProgress = Mathf.Min(1f, b.buildProgress + rate);
                b.hp = Mathf.Min(b.MaxHp, b.hp + b.def.hp * 0.92f * rate);
                if (b.buildProgress >= 1f)
                {
                    world.CompleteConstruction(b);
                    order = Order.Harvest;
                    buildTarget = null;
                    harvestNode = world.NearestFreeNode(pos, team);
                    if (harvestNode != null) MoveTo(harvestNode.pos);
                }
            }
            else if (!Moving || (repathTimer -= dt) <= 0f)
            {
                MoveTo(b.pos);
                repathTimer = 1.8f;
            }
        }

        void UpdateProduction(float dt)
        {
            if (queue.Count == 0) return;
            queueTimer -= dt;
            if (queueTimer > 0f) return;

            var what = queue[0];
            var d = Defs.Get(what);
            Vector2 rallyPt = rallySet ? rally : pos + Norm(world.Map.StartPos(1 - team) - pos) * 8f;

            // Exit on the side facing the rally point, sweeping outward from there -- onto the
            // ground the bases share where there is any: the first walkable spot could be a
            // pocket the building closes off against a cliff or trees, and every unit trained
            // there was cut off for the match. Failing that, any free walkable spot, as before.
            float baseAng = Mathf.Atan2((rallyPt - pos).y, (rallyPt - pos).x);
            Vector2 spot = pos;
            bool ok = false, movedOff = false;
            for (int pass = 0; pass < 2 && !ok; pass++)
                for (int i = 0; i < 24 && !ok; i++)
                {
                    int k = (i + 1) / 2 * ((i & 1) == 0 ? 1 : -1);
                    float a = baseAng + k * (TAU / 24f);
                    Vector2 p = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (def.radius + d.radius + 1.4f);
                    if (!world.Walkable(p, 0.8f) || world.Pick(p, d.radius * 0.5f) != null) continue;
                    // Judged, and put down, where the ground NavMesh has it: a ring point just off the
                    // mesh between a pocket and the main ground could read as the one and land in the other.
                    if (pass == 0 && !world.OnMainGround(p, out p)) { movedOff = true; continue; }
                    if (pass == 1) world.GroundPoint(p, out p);   // on the ground, not a trunk's or boulder's Rubble
                    spot = p;
                    ok = true;
                }
            if (!ok) { queueTimer = 0.5f; return; }
            if (movedOff) world.ExitsMovedOffPocket++;

            queue.RemoveAt(0);
            if (queue.Count > 0) queueTimer = Defs.Get(queue[0]).buildTime;

            bool wantHarvest = d.harvester && !rallySet;
            var n = world.Spawn(what, team, spot, true, Mathf.Atan2(spot.x - pos.x, spot.y - pos.y));
            world.factions[team].unitsProduced++;
            if (wantHarvest)
            {
                n.order = Order.Harvest;
                n.harvestNode = world.NearestFreeNode(n.pos, team);
                if (n.harvestNode != null) n.MoveTo(n.harvestNode.pos);
            }
            else
            {
                n.order = Order.Move;
                n.orderPos = rallyPt;
                n.MoveTo(rallyPt);
            }
            world.Raise(new GameEvent { kind = GameEventKind.UnitReady, unit = n, type = what, team = team, pos = n.Ground });
        }
    }
}
