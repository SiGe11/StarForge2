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

        GameWorld world;
        NavMeshObstacle obstacle;
        int pathRetries;

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
                                 def.type == UnitType.Mech || def.type == UnitType.MechBay;
        public bool Moving => agent != null && agent.enabled && agent.isOnNavMesh && (agent.pathPending || agent.hasPath);
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
            if (d.type != UnitType.Ore) oreLeft = 0;
            else if (oreLeft <= 0) oreLeft = NodeCapacity;
            view = GetComponent<UnitView>();
            if (d.type == UnitType.Mech)
            {
                mech = GetComponent<MechCore>();
                if (mech == null) mech = gameObject.AddComponent<MechCore>();
                mech.Bind(this, w);
            }

            if (d.IsMobile)
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
                agent.obstacleAvoidanceType = d.type == UnitType.Worker
                    ? ObstacleAvoidanceType.NoObstacleAvoidance
                    : ObstacleAvoidanceType.MedQualityObstacleAvoidance;
                agent.avoidancePriority = d.type == UnitType.Mech ? 10 : d.type == UnitType.Mauler ? 30 : (d.type == UnitType.Skimmer ? 60 : 50);
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
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
        }

        bool Arrived() =>
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
                UpdateProduction(dt);
                return;
            }
            if (def.neutral) return;
            if (mech != null) { TickMech(dt); return; }

            // Veterans patch themselves up once out of the fight.
            if (rank > 0 && world.time - lastDamagedT > 6f && hp < MaxHp)
                hp = Mathf.Min(MaxHp, hp + 0.8f * rank * dt);

            if (order == Order.Build) UpdateBuild(dt);
            if (order == Order.Harvest || order == Order.Return) UpdateHarvest(dt);
            UpdateCombat(dt);

            // Close on the target for attack orders and for attack-move once a
            // target is acquired, or a unit that spots a distant enemy stands still.
            if ((order == Order.Attack || order == Order.AttackMove) && Live(target))
            {
                float d = Dist(target) - target.def.radius;
                if (d > def.range * 0.92f && (repathTimer -= dt) <= 0f)
                {
                    MoveTo(target.pos);
                    repathTimer = world.Rng.Range(0.45f, 0.75f);
                }
            }
            if (order == Order.AttackMove && target == null && !Moving && Dist(orderPos) > 2.5f && (repathTimer -= dt) <= 0f)
            {
                MoveTo(orderPos);
                repathTimer = 1.2f;
            }
            if ((order == Order.Move || order == Order.AttackMove) && target == null && Arrived() && Dist(orderPos) < 4f)
                order = Order.Idle;
            if (order == Order.Move && !Moving) KeepGoing(dt);
            else if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath) pathRetries = 0;

            UpdateFacing(dt);
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
                if (Type == UnitType.Mauler)
                    pace = Lerp(0.3f, 1f, Saturate(Mathf.Cos(WrapAngle(want - yaw))));
                // Nor can legs, much: a walking Mech turns before it goes; a grav
                // skirt slides whichever way it likes.
                else if (mech != null && mech.design != null && mech.design.locomotion != MechLocomotion.Hover)
                    pace = Lerp(mech.design.locomotion == MechLocomotion.Tracks ? 0.3f : 0.45f, 1f, Saturate(Mathf.Cos(WrapAngle(want - yaw))));
            }
            else
            {
                yawVel = Mathf.MoveTowards(yawVel, 0f, 4f * dt);
                if (Type == UnitType.Mauler && !Live(target))
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
            if (!Live(target) || target.team == team)
            {
                target = null;
                if (acquires) target = world.NearestEnemy(this, def.sight);
                if (order == Order.Attack && target == null) order = Order.Idle;
            }
            else if (!StickyTargets && acquires && order != Order.Attack && Dist(target) - target.def.radius > def.range)
            {
                // A target it picked up itself (not one it was sent after) gives way to an
                // enemy in reach, and is let go once it is out of sight. Kept until it died,
                // a Sentinel or a bay's gun that had locked onto a scout at the edge of its
                // sight stood silent, whatever came up to its walls, until that scout died
                // somewhere else; and an idle or holding squad the same.
                var inReach = world.NearestEnemy(this, def.range);
                if (inReach != null) target = inReach;
                else if (Dist(target) - target.def.radius > def.sight) target = world.NearestEnemy(this, def.sight);
            }
            if (target == null) return;

            float d = Dist(target) - target.def.radius;
            if (d > def.range) return;
            float want = Mathf.Atan2(target.pos.x - pos.x, target.pos.y - pos.y);
            if (HasTurret)
            {
                float turn = Type == UnitType.Mauler ? 2.6f : def.turnRate;
                turretYaw = ApproachAngle(turretYaw, want, turn * dt);
                if (Mathf.Abs(WrapAngle(want - turretYaw)) < 0.10f && cooldown <= 0f) Fire();
            }
            else
            {
                yaw = ApproachAngle(yaw, want, def.turnRate * dt);
                if (Mathf.Abs(WrapAngle(want - yaw)) < 0.35f && cooldown <= 0f) Fire();
            }
            if (order == Order.Attack || order == Order.AttackMove) Halt();
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
                if (carrying >= OrePerTrip) { order = Order.Return; return; }
                var node = harvestNode;
                if (!Live(node) || node.oreLeft <= 0)
                {
                    harvestNode = world.NearestFreeNode(pos, team);
                    if (harvestNode == null) { order = Order.Idle; return; }
                    MoveTo(harvestNode.pos);
                    return;
                }
                if (Dist(node) <= node.def.radius + def.radius + 0.9f + NavSlack)
                {
                    Halt();
                    yaw = ApproachAngle(yaw, Mathf.Atan2(node.pos.x - pos.x, node.pos.y - pos.y), 8f * dt);
                    working = true;
                    harvestTimer += dt;
                    if (harvestTimer >= HarvestTime)
                    {
                        harvestTimer = 0f;
                        // Topped up, not replaced: a part load (the last of a seam) is kept.
                        int take = Mathf.Min(OrePerTrip - carrying, node.oreLeft);
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
                // Nowhere to bring it: wait with the load until a Foundry stands again. Sent
                // back to the ore, a Digger mined on and dropped each load for the next,
                // stripping the fields for nothing once its Foundry had fallen.
                if (dp == null) { if (Moving) Halt(); return; }
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

            // Exit on the side facing the rally point, sweeping outward from there.
            float baseAng = Mathf.Atan2((rallyPt - pos).y, (rallyPt - pos).x);
            Vector2 spot = pos;
            bool ok = false;
            for (int i = 0; i < 24 && !ok; i++)
            {
                int k = (i + 1) / 2 * ((i & 1) == 0 ? 1 : -1);
                float a = baseAng + k * (TAU / 24f);
                Vector2 p = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (def.radius + d.radius + 1.4f);
                if (world.Walkable(p, 0.8f) && world.Pick(p, d.radius * 0.5f) == null) { spot = p; ok = true; }
            }
            if (!ok) { queueTimer = 0.5f; return; }

            queue.RemoveAt(0);
            if (queue.Count > 0) queueTimer = Defs.Get(queue[0]).buildTime;

            bool wantHarvest = what == UnitType.Worker && !rallySet;
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
