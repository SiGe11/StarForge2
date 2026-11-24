// Unit.Vehicle.cs -- the tanks (tactical phase 6, TacticalRules.IsTank): a mounted machine gun with a target
// and a cooldown of its own (UnitView's recoil reads `cooldown`: the main gun's alone), the main gun turned on armour
// while it takes the men, and two smoke launchers popped as an anti-tank round comes at it.
using UnityEngine;
using StarForge.Sim;

namespace StarForge.World
{
    public partial class Unit
    {
        [System.NonSerialized] public Unit tankGunTarget;
        [System.NonSerialized] float tankGunReadyAt, tankLookAt, tankMainLookAt, tankRestockAt, tankSmokeReadyAt, friendsHeldSince = -1f;
        /// <summary>Smoke charges left (-1 until first ticked).</summary>
        [System.NonSerialized] public int tankSmokes = -1;
        /// <summary>When an anti-tank round was last fired at it, and from where; when it last popped smoke.</summary>
        [System.NonSerialized] public float armourWarnedAt = -99f, smokedAt = -99f;
        [System.NonSerialized] public Vector2 armourWarnedFrom;
        [System.NonSerialized] float tankBackUntil, tankBackLookAt, workerFleeUntil, workerLookAt, creatureFleeUntil, creatureFleeAgainAt, creatureLookAt;
        [System.NonSerialized] float workerFlightsSince;
        [System.NonSerialized] int workerFlightsLately;
        [System.NonSerialized] Vector2 tankBackOrderAt, creatureFleeOrderAt;
        [System.NonSerialized] Order tankBackOrder, creatureFleeOrder;
        [System.NonSerialized] Vector2 tankBackGoal;
        [System.NonSerialized] Unit creatureFleeTarget;

        /// <summary>Running from an enemy (a worker), breaking off (a creature) or backing away (a tank): nothing
        /// re-paths it meanwhile (`SoldierBusy`).</summary>
        bool Fleeing => (world.time < workerFleeUntil || world.time < creatureFleeUntil || world.time < tankBackUntil);
        /// <summary>Backing away: the hull keeps its front on the threat (UpdateFacing).</summary>
        public bool Reversing => world.time < tankBackUntil;

        /// <summary>A worker runs from an armed enemy its side sees within `WorkerFleeReach`, away from it and toward
        /// its dropoff, then goes back to its order (kept: its ore, its load, what it builds). Only while at work or
        /// idle; an armed one (the Prospector) only when badly hurt.</summary>
        bool WorkerFlees()
        {
            float now = world.time;
            if (now < workerFleeUntil) return true;
            if (order != Order.Harvest && order != Order.Return && order != Order.Build && order != Order.Idle) return false;
            if (now < workerLookAt) return false;
            workerLookAt = now + 0.5f;
            // The Prospector's gun answers first; a Digger's cutter is no gun.
            if (def.Armed && Type != UnitType.Worker && hp > MaxHp * 0.4f) return false;
            var th = world.NearestThreat(this, TacticalRules.WorkerFleeReach);
            if (th == null || th.def.building) return false;
            // By its own Foundry it works on: the army defends the base, and the economy is what it fights with.
            var depot = world.NearestDropoff(this);
            if (depot != null && (depot.pos - pos).magnitude < TacticalRules.WorkerStaysNear) return false;
            if (now - workerFlightsSince > 30f) { workerFlightsSince = now; workerFlightsLately = 0; }
            if (workerFlightsLately >= 3) return false;
            Vector2 away = pos - th.pos;
            away = away.sqrMagnitude > 1e-3f ? away.normalized : Vector2.right;
            var home = world.NearestDropoff(this);
            if (home != null && (home.pos - pos).sqrMagnitude > 1f) away = (away + (home.pos - pos).normalized * 0.5f).normalized;
            Vector2 goal = world.NearestReachable(this, pos + away * 14f);
            if ((goal - pos).magnitude < 2f) return false;
            MoveTo(goal);
            workerFlightsLately++;
            workerFleeUntil = now + 5f;
            world.WorkerFlights++;
            return true;
        }

        /// <summary>A creature under `CreatureBreakAt` of its hull breaks off from what it sees for 5 s, once in 25 s,
        /// then goes back to its order: instinct, not tactics.</summary>
        bool CreatureFlees()
        {
            float now = world.time;
            if (now < creatureFleeUntil)
            {
                if (order == creatureFleeOrder && (orderPos - creatureFleeOrderAt).sqrMagnitude <= 4f && (target == null || target == creatureFleeTarget)) return true;
                creatureFleeUntil = 0f;   // a new order: it is obeyed
                return false;
            }
            if (creatureFleeUntil > 0f)
            {
                creatureFleeUntil = 0f;
                if (order == Order.Move || order == Order.AttackMove) MoveTo(orderPos);
            }
            if (hp > MaxHp * TacticalRules.CreatureBreakAt || now < creatureFleeAgainAt || order == Order.Hold || now < creatureLookAt) return false;
            creatureLookAt = now + 0.5f;
            var th = world.NearestThreat(this, def.sight);
            if (th == null) return false;
            Vector2 away = pos - th.pos;
            away = away.sqrMagnitude > 1e-3f ? away.normalized : Vector2.right;
            Vector2 goal = def.flying ? world.ClampToMap(pos + away * 14f) : world.NearestReachable(this, pos + away * 14f);
            if ((goal - pos).magnitude < 2f) return false;
            MoveTo(goal);
            creatureFleeOrder = order;
            creatureFleeOrderAt = orderPos;
            creatureFleeTarget = target;
            creatureFleeUntil = now + 5f;
            creatureFleeAgainAt = now + 25f;
            world.CreatureFlights++;
            return true;
        }

        /// <summary>A tank idle or below half its hull backs away from an anti-tank man it sees within his reach and
        /// `TankBackMargin`, unless, unhurt, its machine gun is already on him (it holds and keeps his head down); any
        /// tank backs out of its own smoke after popping it; front and turret on him. Not on Hold, hurried, or sent
        /// after a target.</summary>
        void TankTactics(float now)
        {
            if (TacticalRules.NoTankTactics) return;
            if (tankBackUntil > 0f)
            {
                bool arrived = order == Order.Idle && (pos - tankBackGoal).sqrMagnitude < 2.5f * 2.5f;
                bool sameOrder = (orderPos - tankBackOrderAt).sqrMagnitude <= 4f && !targetOrdered && (order == tankBackOrder || arrived);
                if (!sameOrder) tankBackUntil = 0f;
                else if (now >= tankBackUntil)
                {
                    tankBackUntil = 0f;
                    if (tankBackOrder == Order.Move || tankBackOrder == Order.AttackMove) { order = tankBackOrder; MoveTo(orderPos); }
                }
            }
            if (now < tankBackLookAt || now < tankBackUntil || order == Order.Hold || hurry) return;
            tankBackLookAt = now + 0.5f;
            Vector2 from = default;
            float want = 0f;
            var at = world.NearestAntiTank(this, out float reachOf);
            // Idle, or below half its hull: sent forward, it goes (backing off fought every order that sent it on).
            bool mayBack = order == Order.Idle || hp < MaxHp * 0.5f;
            // Unhurt, with its machine gun already on him, it holds and keeps his head down: backing out of its own
            // reach let him up to fire (Backoff over 15 fields: hull lost 221 against 186 standing).
            if (mayBack && at != null && hp >= MaxHp * 0.5f && tankGunTarget == at) mayBack = false;
            if (at != null && mayBack && !targetOrdered)
            {
                from = at.pos;
                want = reachOf + TacticalRules.TankBackMargin - (at.pos - pos).magnitude;
            }
            else if (now - smokedAt < 1f && !targetOrdered)
            {
                from = armourWarnedFrom;
                want = 10f;
            }
            if (want <= 1f) return;
            Vector2 away = pos - from;
            away = away.sqrMagnitude > 1e-3f ? away.normalized : Vector2.right;
            Vector2 goal = world.NearestReachable(this, pos + away * Mathf.Clamp(want, 4f, 15f));
            if ((goal - pos).magnitude < 2f) return;
            MoveTo(goal);
            tankBackGoal = goal;
            tankBackUntil = now + 3f;
            tankBackOrderAt = orderPos;
            tankBackOrder = order;
            world.TankBackOffs++;
        }

        void TickTank(float dt)
        {
            float now = world.time;
            int charges = TacticalRules.TankSmokeCharges;
            if (tankSmokes < 0) tankSmokes = charges;
            // A charge as the first anti-tank round comes at it, the second for the next one.
            if (tankSmokes > 0 && now - armourWarnedAt < 1f && now >= tankSmokeReadyAt)
            {
                tankSmokes--;
                tankSmokeReadyAt = now + 8f;
                // Refilled only a while after one is spent (a depot gave it back on the tick it was popped).
                tankRestockAt = now + TacticalRules.TankSmokeRefillNow;
                smokedAt = now;
                world.TankSmoke(this, armourWarnedFrom);
            }
            if (tankSmokes < charges && now >= tankRestockAt)
            {
                tankRestockAt = now + TacticalRules.TankSmokeRefillNow;
                if (world.NearTankDepot(this)) tankSmokes++;
            }
            bool look = now >= tankLookAt;
            if (look)
            {
                tankLookAt = now + 0.5f;
                tankGunTarget = world.TankGunTarget(this);
            }
            TankTactics(now);
            // While the machine gun has the men, the main gun goes for armour or a structure in its reach: asked at each
            // look and, at most four times a second, while it is ready to fire (its first shell went at the men).
            bool gunHasMen = tankGunTarget != null && tankGunTarget.def.bodyRadius > 0f;
            if (gunHasMen && Live(target) && target.def.bodyRadius > 0f && !targetOrdered && (look || (cooldown <= dt && now >= tankMainLookAt)))
            {
                tankMainLookAt = now + 0.25f;
                var heavy = world.TankMainTarget(this, null);
                if (heavy != null) { target = heavy; friendsLookAt = 0f; }
            }
            // A Mauler's shell held 3 s for friends by its target: another in reach, if there is one.
            if (Type == UnitType.Mauler && friendsLookAt > 0f && !friendsClear && Live(target)) { if (friendsHeldSince < 0f) friendsHeldSince = now; }
            else friendsHeldSince = -1f;
            if (friendsHeldSince >= 0f && now - friendsHeldSince > 3f && !targetOrdered && now >= tankMainLookAt)
            {
                tankMainLookAt = now + 0.5f;
                var other = world.TankMainTarget(this, target);
                if (other != null) { target = other; friendsLookAt = 0f; friendsHeldSince = -1f; }
            }
            // On a plain Move a tank engages nothing (the old rule), its machine gun neither.
            if (order == Order.Move && !hurry) return;
            if (!Live(tankGunTarget) || now < tankGunReadyAt) return;
            if (!world.Sees(team, tankGunTarget)) { tankGunTarget = null; return; }
            tankGunReadyAt = (now - tankGunReadyAt < 0.1f ? tankGunReadyAt : now) + TacticalRules.TankGunCooldown;
            world.FireTankGun(this, tankGunTarget);
        }
    }
}
