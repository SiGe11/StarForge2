// Unit.Air.cs -- the new combat's aircraft (phase 7): the Swift's energy and break turns, missiles with a lock,
// flares, rearming by an Airfield; the Kestrel's machine gun at the ground. Numbers in TacticalRules.
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public partial class Unit
    {
        /// <summary>The Swift's speed under the new combat (0 until it first flies).</summary>
        [System.NonSerialized] public float planeSpeed;
        [System.NonSerialized] public int airMissiles = -1, airFlares = -1, airRockets = -1;
        [System.NonSerialized] float rocketReadyAt;
        [System.NonSerialized] public float airWarnedAt = -99f;
        [System.NonSerialized] float airRearmAt, flareReadyAt, lockSince = -1f, breakUntil, breakSide, breakLookAt;
        [System.NonSerialized] Unit lockOn;

        void TickAir(float dt)
        {
            float now = world.time;
            if (airMissiles < 0)
            {
                airMissiles = Type == UnitType.Swift ? TacticalRules.AirMissiles : 0;
                airRockets = Type == UnitType.Swift ? TacticalRules.SwiftRockets : 0;
                airFlares = Type == UnitType.Swift || Type == UnitType.Kestrel ? TacticalRules.AirFlares : 0;
            }
            // Flares as a missile comes at it.
            if (airFlares > 0 && now - airWarnedAt < 0.5f && now >= flareReadyAt)
            {
                airFlares--;
                flareReadyAt = now + 4f;
                world.PopFlares(this);
            }
            if (now >= airRearmAt)
            {
                airRearmAt = now + 10f;
                int fullM = Type == UnitType.Swift ? TacticalRules.AirMissiles : 0;
                int fullF = Type == UnitType.Swift || Type == UnitType.Kestrel ? TacticalRules.AirFlares : 0;
                int fullR = Type == UnitType.Swift ? TacticalRules.SwiftRockets : 0;
                if ((airMissiles < fullM || airFlares < fullF || airRockets < fullR) && world.NearAirfield(this))
                {
                    if (airMissiles < fullM) airMissiles++;
                    if (airFlares < fullF) airFlares++;
                    if (airRockets < fullR) airRockets = Mathf.Min(fullR, airRockets + 2);
                }
            }
            // The Night of the Quenching. TickAir is reached only from UpdateCombat, after its gate, so this is a second
            // guard; a new gun path still asks the truce itself (World notes).
            if (world.Truce) return;
            if (Type == UnitType.Swift) { TickMissileLock(now); if (!TacticalRules.LegacyAir) TickRockets(now); }
            if (Type == UnitType.Kestrel) TickGunship(now);
        }

        /// <summary>A missile once an enemy aircraft has stood in its nose's cone, 15-45 m off, for a second.</summary>
        void TickMissileLock(float now)
        {
            if (airMissiles <= 0 || !Live(target) || !target.def.flying || target.team == team || !world.Sees(team, target)) { lockSince = -1f; return; }
            Vector2 to = target.pos - pos;
            float d = to.magnitude;
            float off = Mathf.Abs(WrapAngle(Mathf.Atan2(to.x, to.y) - yaw));
            if (d < TacticalRules.MissileLockMin || d > TacticalRules.MissileLockMax || off > TacticalRules.MissileCone) { lockSince = -1f; return; }
            if (lockOn != target || lockSince < 0f) { lockOn = target; lockSince = now; return; }
            if (now - lockSince < TacticalRules.MissileLockTime) return;
            airMissiles--;
            lockSince = -1f;
            world.FireAirMissile(this, target);
        }

        /// <summary>The Swift's rockets: a pair on a run at what it is after on the ground, once its nose is on it at
        /// 10-32 m (not on a plain Move).</summary>
        void TickRockets(float now)
        {
            if (airRockets < 2 || now < rocketReadyAt || !Live(target) || target.def.flying || target.team == team || target.team > 1) return;
            if ((order == Order.Move && !hurry) || !world.Sees(team, target)) return;
            Vector2 to = target.pos - pos;
            float d = to.magnitude;
            if (d < TacticalRules.SwiftRocketMin || d > TacticalRules.SwiftRocketMax) return;
            if (Mathf.Abs(WrapAngle(Mathf.Atan2(to.x, to.y) - yaw)) > TacticalRules.SwiftRocketCone) return;
            airRockets -= 2;
            rocketReadyAt = now + TacticalRules.SwiftRocketEvery;
            world.FireSwiftRockets(this, target);
        }

        /// <summary>The Kestrel stays a hovering gunship: its rockets as before, and a machine gun at men and light things
        /// on the ground (the tanks' gun, from its height).</summary>
        void TickGunship(float now)
        {
            if (now >= tankLookAt)
            {
                tankLookAt = now + 0.5f;
                tankGunTarget = world.TankGunTarget(this);
            }
            if (order == Order.Move && !hurry) return;
            if (!Live(tankGunTarget) || now < tankGunReadyAt) return;
            if (!world.Sees(team, tankGunTarget)) { tankGunTarget = null; return; }
            tankGunReadyAt = (now - tankGunReadyAt < 0.1f ? tankGunReadyAt : now) + TacticalRules.TankGunCooldown;
            world.FireTankGun(this, tankGunTarget);
        }

        /// <summary>A break: an enemy aircraft close behind with it in its nose, or a missile after it, and it turns
        /// hard to one side and dives for 2 s.</summary>
        bool PlaneBreaks(ref Vector2 steer, Vector2 heading)
        {
            float now = world.time;
            if (now >= breakUntil)
            {
                if (now < breakUntil + 3f || now < breakLookAt) return false;
                breakLookAt = now + 0.25f;
                var chaser = world.ChaserBehind(this);
                if (chaser == null) return false;
                Vector2 from = chaser.pos;
                Vector2 side = new Vector2(heading.y, -heading.x);
                breakSide = Vector2.Dot(from - pos, side) > 0f ? -1f : 1f;
                breakUntil = now + 2f;
                world.PlaneBreaks++;
            }
            Vector2 s = new Vector2(heading.y, -heading.x) * breakSide;
            steer = pos + (heading * 0.3f + s).normalized * 20f;
            return true;
        }
    }
}
