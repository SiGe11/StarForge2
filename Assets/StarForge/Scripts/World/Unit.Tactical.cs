// Unit.Tactical.cs -- what the game's combat keeps on a unit (GameWorld.Tactical.cs).
using UnityEngine;
using StarForge.Sim;

namespace StarForge.World
{
    public sealed partial class Unit
    {
        /// <summary>How it stands. Owned by the simulation; a soldier's thinking chooses it.</summary>
        [System.NonSerialized] public Posture posture;
        /// <summary>Until when each side knows where it is (GameWorld.TickSpotting), and which of that
        /// side's units saw it last, tried first next time.</summary>
        [System.NonSerialized] public float spotted0, spotted1;
        [System.NonSerialized] public Unit seer0, seer1;
        /// <summary>When it last fired: a gun gives away where it is.</summary>
        [System.NonSerialized] public float firedAt = -99f;
        /// <summary>The wreck it left, for the view to keep while the record stands.</summary>
        [System.NonSerialized] public Hulk hulk;
        /// <summary>The fog cells it sees (GameWorld.UpdateSight), and where and when they were worked out.</summary>
        [System.NonSerialized] public ushort[] viewCells;
        [System.NonSerialized] public int viewCount = -1;
        [System.NonSerialized] public Vector2 viewFrom;
        /// <summary>Where its next look for a target a round would reach starts in the sorted list of candidates
        /// (GameWorld.NearestShootable for what fights back and for anything, TankGunTarget, OpenTarget): past the
        /// four the last look of that kind tried in vain, so men behind cover nearer than the rest cannot use up every
        /// look; back to the top once one is found, the list is used up, or (but the machine gun's) a second has
        /// passed since the last look of that kind (`lineAt*`). The machine gun, keeping the man it is on, goes on
        /// down toward him.</summary>
        [System.NonSerialized] public int lineFromFB, lineFromAll, lineFromGun, lineFromOpen;
        [System.NonSerialized] public float lineAtFB = -99f, lineAtAll = -99f, lineAtOpen = -99f;
        [System.NonSerialized] public float viewAt;
        /// <summary>Whether a round would reach its target from here, looked at a few times a second.</summary>
        [System.NonSerialized] public bool fireBlocked;
        /// <summary>Which way he leans out from cover for his shot at his target, -1, 0 or 1, and where
        /// that takes him on the ground. Set only by the look at his own target.</summary>
        [System.NonSerialized] public int leanSide;
        [System.NonSerialized] public Vector2 leanDir;
        [System.NonSerialized] bool fireLineClear;
        [System.NonSerialized] float fireLookAt = -99f;
        [System.NonSerialized] Unit fireLookedAt;
        /// <summary>When it next looks for another target, and how long it has stood with nowhere left
        /// to walk and no line to this one.</summary>
        [System.NonSerialized] float shootableLookAt, blockedFor, blockedBest;
        [System.NonSerialized] Unit blockedOf;
        const float GiveUpAfter = 3f;
        const float LineOnlyFor = 30f;   // long enough to have walked out of its sight
        /// <summary>It gave up on a target no way led to: until then it takes only what a round would reach.</summary>
        [System.NonSerialized] public float lineOnlyUntil;
        /// <summary>Where its side last knew its target to be. An order goes there, not to where a hidden
        /// unit has got to since.</summary>
        [System.NonSerialized] public Vector2 targetSeenAt;
        [System.NonSerialized] Unit targetSeenOf;

        public float SpottedUntil(int team) => team == 0 ? spotted0 : spotted1;

        /// <summary>In cover he is out from it a moment before and after each round, and back behind it
        /// between them.</summary>
        public bool ShowsForShot => !Reloading && (cooldown <= PeekFor || cooldown >= def.cooldown - PeekFor);
        const float PeekFor = 0.12f;

        public void Spot(int team, float until, Unit by)
        {
            if (team == 0) { spotted0 = until; seer0 = by; }
            else { spotted1 = until; seer1 = by; }
        }

        /// <summary>How tall a target it makes.</summary>
        public float BodyHeight
        {
            get
            {
                if (def.bodyRadius <= 0f) return Mathf.Max(0.5f, def.visualHeight);
                switch (posture)
                {
                    case Posture.Crouch: return Ducked ? DuckedHeight : 1.15f;
                    case Posture.Prone: return 0.5f;
                    default: return 1.8f;
                }
            }
        }

        /// <summary>Crouched in cover with his head down: hiding, or behind a low wall between the rounds he
        /// comes up for. The rig's crouch stands 1.15 m; a low wall stands at least
        /// <see cref="MapGenerator.DuckedClear"/> over a spot the cover search offers, and at 1.15 the top of his
        /// head took 2-9% of rounds there (against 29-75% in front of it).</summary>
        /// His eye is then 0.95 (at 1.05 he saw over what hid him).
        public bool Ducked => mode == SoldierMode.InCover && !risen && (hiding || leanSide == GameWorld.RiseOver);
        const float DuckedHeight = 1.0f;

        public float BodyRadius => def.bodyRadius > 0f ? def.bodyRadius : def.radius * 0.85f;

        /// <summary>How high it looks from, over its own ground.</summary>
        public float EyeHeight
        {
            get
            {
                if (def.bodyRadius <= 0f) return Mathf.Max(1.5f, def.visualHeight * 0.8f);
                switch (posture)
                {
                    case Posture.Crouch: return Ducked ? 0.95f : 1.05f;
                    case Posture.Prone: return 0.35f;
                    default: return 1.7f;
                }
            }
        }

        /// <summary>How high its gun is, over its own ground.</summary>
        public float GunHeight
        {
            get
            {
                switch (posture)
                {
                    case Posture.Crouch: return 0.85f;
                    case Posture.Prone: return 0.3f;
                    default: return 1.25f;
                }
            }
        }

        /// <summary>What it is after, under the new rules: what its side no longer knows of it lets go,
        /// unless it was sent after it, and then it goes to where it was last known, and no further.</summary>
        void ReviewTarget()
        {
            if (!Live(target) || target.team == team) return;
            if (targetSeenOf != target) { targetSeenOf = target; targetSeenAt = target.pos; }
            if (world.Sees(team, target)) { targetSeenAt = target.pos; return; }
            if (!(order == Order.Attack && targetOrdered) || Dist(targetSeenAt) < 3f) { target = null; targetOrdered = false; }
        }

        /// <summary>Where a closing unit walks for its target.</summary>
        Vector2 TargetGoal => world.Sees(team, target) ? target.pos : targetSeenAt;

        /// <summary>The same, for the half of a squad working round it: to one side, on ground he can reach.</summary>
        Vector2 ClosingGoal
        {
            get
            {
                Vector2 way = world.FlankPull(this, target);
                return way == Vector2.zero ? TargetGoal : world.NearestReachable(this, TargetGoal + way * FlankWide);
            }
        }
        const float FlankWide = 12f;

        /// <summary>Its target is out of reach, or no round would get to it. Free to choose, it takes another
        /// a round would reach: what fights back first, and closing on something that fights back only
        /// another that does (as the old give-way rule). If the way there does not get there (a partial
        /// path: it may stand on ground cut off from it) and three seconds gain it nothing, it gives the
        /// target up: an attack-move walks on, an attack order ends, and for a while it takes only what a
        /// round would reach. A longer way round that does get there, it walks.</summary>
        void FireBlocked(float dt)
        {
            bool closing = order == Order.Attack || order == Order.AttackMove;
            if (!(order == Order.Attack && targetOrdered) && def.DirectFire && world.time >= shootableLookAt)
            {
                shootableLookAt = world.time + 0.5f;
                var open = world.NearestShootable(this, true);
                // Anything else only once that list was walked to its end: half-way down it the next look may find one.
                if (open == null && world.LastLookWalkedAll && !(closing && GameWorld.FightsBack(target))) open = world.NearestShootable(this, false);
                if (open != null && open != target) { target = open; targetOrdered = false; fireLookAt = -99f; blockedFor = 0f; return; }
            }
            if (!closing || agent == null || !agent.enabled || !agent.isOnNavMesh) { blockedFor = 0f; return; }
            // Stopped for his turn he gains nothing, and that says nothing about the way.
            if (!SquadLetsMove()) { blockedFor = 0f; return; }
            // Closing asks for its path afresh every half second or so: asking says nothing either way.
            if (agent.pathPending) return;
            if (agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathComplete) { blockedFor = 0f; return; }
            float d = Dist(target);
            if (blockedOf != target || blockedFor <= 0f || d < blockedBest - 0.5f) { blockedOf = target; blockedBest = d; blockedFor = dt; return; }
            blockedFor += dt;
            if (blockedFor < GiveUpAfter) return;
            lineOnlyUntil = world.time + LineOnlyFor;
            target = null;
            targetOrdered = false;
            blockedFor = 0f;
            // Off the partial path it was on, or it walks that to its end first.
            if (order == Order.AttackMove) MoveTo(orderPos);
        }

        /// <summary>Having given a target up, what it takes instead: only what a round would reach,
        /// looked for twice a second.</summary>
        Unit ShootableNow()
        {
            if (world.time < shootableLookAt) return null;
            shootableLookAt = world.time + 0.5f;
            var open = world.NearestShootable(this, true);
            if (open == null && world.LastLookWalkedAll) open = world.NearestShootable(this, false);
            return open;
        }

        /// <summary>Would a round reach what it is aimed at? Asked as it aims, so kept for a quarter of
        /// a second: the ground under a walking soldier does not change faster than that.</summary>
        bool LineOfFireNow()
        {
            if (fireLookedAt != target || world.time >= fireLookAt)
            {
                fireLookedAt = target;
                fireLineClear = world.LineOfFire(this, target, out leanSide);
                fireLookAt = world.time + (fireLineClear ? 0.25f : 0.5f);
                Vector2 to = target.pos - pos;
                float len = to.magnitude;
                // Which way that takes him, on the ground: what shows of him to anyone.
                leanDir = leanSide == 0 || leanSide == GameWorld.RiseOver || len < 1e-3f ? Vector2.zero : new Vector2(-to.y, to.x) / len * leanSide;
            }
            return fireLineClear;
        }
    }
}
