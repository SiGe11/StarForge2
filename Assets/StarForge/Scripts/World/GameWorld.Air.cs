// GameWorld.Air.cs -- the new combat's aircraft (phase 7): cannon rounds decided when fired, missiles and flares,
// rearming, who is on whose tail. The plane's own state is in Unit.Air.cs; numbers in TacticalRules.
using UnityEngine;
using StarForge.Sim;

namespace StarForge.World
{
    public partial class GameWorld
    {
        /// <summary>Counters for the dogfight trial.</summary>
        public int CannonShotsAtAir, CannonHitsAtAir, AirMissilesFired, FlaresPopped, MissilesDecoyed, PlaneBreaks;
        /// <summary>Squad reports a Commander acted on (phase 8).</summary>
        public int SquadFallBacksHeard, SquadAirCallsHeard;

        /// <summary>How high a gun on it stands over the ground: a tank's roof, or an aircraft's height.</summary>
        float GunHeightOf(Unit s) => s.def.flying ? s.def.altitude - s.dip : TacticalRules.TankGunHeight;

        /// <summary>A cannon round at an aircraft, decided when fired: the nearer its nose is on the target and the
        /// closer, the likelier a hit; a miss flies on and hurts no one.</summary>
        void DecideCannon(Unit s, Unit t, Projectile p)
        {
            Vector2 to = t.pos - s.pos;
            float d = to.magnitude;
            float off = Mathf.Abs(Mathf.DeltaAngle(s.yaw * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            float cone = Mathf.Clamp01(off / 0.35f);
            float pk = TacticalRules.CannonHitBase * (1f - cone * cone) * (1f - 0.5f * Mathf.Clamp01(d / Mathf.Max(1f, s.def.range)));
            CannonShotsAtAir++;
            // A dogfight is passes, not a snap kill: four hits took a Swift down (vsAir 2.0) in about 0.7 s of fire.
            p.vsAir = TacticalRules.CannonVsAir;
            if (CombatRoll() < pk) { CannonHitsAtAir++; return; }
            p.coverStopped = true;
        }

        /// <summary>A missile at an aircraft: a seeker on its tail, harmless if flares draw it off.</summary>
        public void FireAirMissile(Unit s, Unit t)
        {
            Vector3 m = Muzzle(s, -0.2f, 0.8f);
            var p = Round(s, t, 5, m, TacticalRules.MissileSpeed);
            Vector3 aim = AimPoint(t);
            p.from = m;
            p.aim = aim;
            p.ctrl = Vector3.Lerp(m, aim, 0.4f) + Vector3.up * 1.5f;
            p.dmg = TacticalRules.MissileDamage * WeaponMul(s);
            p.splash = 0f;
            p.hitsAir = true;
            p.hitsGround = false;
            p.vsAir = 1f;
            p.flight = Mathf.Max(0.3f, (t.pos - s.pos).magnitude / TacticalRules.MissileSpeed);
            p.life = p.flight + 1f;
            AirMissilesFired++;
            RaiseFire(s, m, (AimPoint(t) - m).normalized, 5);
        }

        /// <summary>A missile has just been launched at this aircraft.</summary>
        public void WarnAir(Unit shooter, Unit t)
        {
            if (t == null || !t.def.flying) return;
            t.airWarnedAt = time;
        }

        /// <summary>Flares: each missile in the air at it is drawn off on a `FlareDecoy` roll.</summary>
        public void PopFlares(Unit u)
        {
            FlaresPopped++;
            foreach (var p in projectiles)
            {
                if (!p.alive || p.kind != 5 || p.target != u || p.team == u.team || p.coverStopped) continue;
                if (CombatRoll() < TacticalRules.FlareDecoy) { p.coverStopped = true; MissilesDecoyed++; }
            }
        }

        /// <summary>Within reach of its side's finished Airfield, to take on missiles and flares.</summary>
        public bool NearAirfield(Unit u)
        {
            float r = TacticalRules.AirRearmReach;
            CellRange(u.pos, r + 6f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team != u.team || o.Type != UnitType.Airfield || !o.Complete) continue;
                        if ((o.pos - u.pos).magnitude <= r + o.def.radius) return true;
                    }
            return false;
        }

        /// <summary>An enemy plane within 25 m behind with this one in its nose's 0.5 rad, if any.</summary>
        public Unit ChaserBehind(Unit u)
        {
            CellRange(u.pos, 26f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2 || !o.def.fixedWing || !Sees(u.team, o)) continue;
                        Vector2 heading = new Vector2(Mathf.Sin(u.yaw), Mathf.Cos(u.yaw));
                        if (Vector2.Dot(heading, o.pos - u.pos) >= 0f) continue;
                        Vector2 to = u.pos - o.pos;
                        float d = to.magnitude;
                        if (d > 25f || d < 1e-3f) continue;
                        float off = Mathf.Abs(Mathf.DeltaAngle(o.yaw * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg));
                        if (off < 30f) return o;
                    }
            return null;
        }
    }
}
