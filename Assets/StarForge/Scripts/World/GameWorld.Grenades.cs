// GameWorld.Grenades.cs -- what a soldier carries besides his gun (TacticalRules; his side of it is
// Unit.SideArms.cs). Frag and flash are thrown, land and go off on a fuse; smoke makes a cloud that
// blocks sight for both sides, through SightMap's leaf cells, so everything that looks already knows.
// A grenade is the one thing here that can hurt its own side, so a man looks where his friends will
// be before he throws, and again as he lets go. A same-side hit raises no alarm and credits no kill.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public enum Thrown : byte { None, Frag, Smoke, Flash }

    public sealed class SmokeCloud
    {
        public Vector2 at;
        public float born, until;
        /// <summary>The side that laid it (-1: nobody's, a trial's): no one fires blind through his own side's screen.</summary>
        public int team = -1;
    }

    public sealed partial class GameWorld
    {
        // Appended after the roster's kinds: FX and audio route on these numbers.
        public const int ProjFrag = 15, ProjSmoke = 16, ProjFlash = 17, ProjPistol = 18, ProjKnife = 19, ProjMG = 20;
        const float ThrowGravity = 16f, TossSpeed = 11f, ThrowError = 0.06f;
        /// <summary>Furthest a throw lands from where it was meant: a man allows for it.</summary>
        public const float ThrowErrorMax = 2.5f;
        const int MostSmoke = 12;
        const float DiveChance = 0.75f, NearBurst = 10f, NearBurstWeight = 0.35f, ShelterLeaves = 0.12f;

        public readonly List<SmokeCloud> SmokeClouds = new List<SmokeCloud>(MostSmoke);
        /// <summary>For the trials and the report: thrown by kind; frags landed, and with a friend in
        /// their reach as they landed and as they burst; friends a burst hurt; men stunned; side arms used.</summary>
        public readonly int[] ThrownCount = new int[4];
        public int FragLandings, FragFriendAtLanding, FragFriendAtBurst, FriendHurt, Stunned, PistolShots, Stabs, Reloads;
        /// <summary>For the trials: chances of a mistake by experience, and mistakes made.</summary>
        public readonly int[] MistakeChances = new int[4], MistakesMade = new int[4];
        public int SquadBlunders;

        void BeginArms()
        {
            SmokeClouds.Clear();
            System.Array.Clear(ThrownCount, 0, ThrownCount.Length);
            FragLandings = FragFriendAtLanding = FragFriendAtBurst = FriendHurt = Stunned = PistolShots = Stabs = Reloads = SquadBlunders = 0;
            TankGunShots = TankGunHits = TankSmokesPopped = TankBackOffs = WorkerFlights = CreatureFlights = RoundsStoppedByCover = 0;
            System.Array.Clear(MistakeChances, 0, 4);
            System.Array.Clear(MistakesMade, 0, 4);
        }

        // ------------------------------------------------------------ throwing
        public static float ThrowFlight(float dist) => Mathf.Clamp(dist / TossSpeed, 0.5f, 1.6f);

        /// <summary>It leaves his hand: a little off where he meant, flying for a time worked out to land there.</summary>
        public void Throw(Unit s, Thrown what, Vector2 at)
        {
            if (Truce) return;   // the Quenching's truce
            float meant = (at - s.pos).magnitude;
            Gauss(out float ex, out float ey);
            Vector2 err = new Vector2(ex, ey) * (meant * ThrowError);
            if (err.sqrMagnitude > ThrowErrorMax * ThrowErrorMax) err = err.normalized * ThrowErrorMax;
            Vector2 land = OffWhatStands(ClampToMap(at + err));
            Vector2 flat = land - s.pos;
            float len = flat.magnitude;
            Vector3 fwd = len > 1e-3f ? new Vector3(flat.x / len, 0f, flat.y / len) : Vector3.forward;
            Vector3 hand = s.Ground + Vector3.up * (s.posture == Posture.Prone ? 0.5f : s.posture == Posture.Crouch ? 1.2f : 1.7f) + fwd * 0.3f;
            Vector3 aim = new Vector3(land.x, map.HeightAt(land) + 0.06f, land.y);
            float t = ThrowFlight(len);
            int kind = what == Thrown.Frag ? ProjFrag : what == Thrown.Smoke ? ProjSmoke : ProjFlash;

            var p = projectilePool.Count > 0 ? projectilePool.Pop() : new Projectile();
            p.alive = true;
            p.kind = kind;
            p.pos = p.prevPos = hand;
            p.target = null;
            p.shooter = s;
            p.team = s.team;
            p.dmg = TacticalRules.FragDamage;
            p.splash = TacticalRules.FragReach;
            p.bonusVsWorkers = 1f;
            p.vsLight = 0.35f; p.vsHeavy = 0.05f; p.vsStructure = 0.05f; p.vsAir = 0f;
            p.hitsAir = false; p.hitsGround = true;
            p.bounces = 0;
            p.purge = false;
            p.friendly = true;
            p.landed = false;
            p.aim = aim;
            p.flight = t;
            p.age = 0f;
            p.life = 60f;
            Vector3 d = aim - hand;
            p.vel = new Vector3(d.x / t, d.y / t + 0.5f * ThrowGravity * t, d.z / t);
            projectiles.Add(p);
            ThrownCount[(int)what]++;
            Raise(new GameEvent { kind = GameEventKind.Fire, unit = s, type = s.Type, team = s.team, pos = hand, dir = p.vel.normalized, projectileKind = kind });
        }

        /// <summary>It does not lie on top of a rock or a wall: it rolls off, to the side it came down on.</summary>
        Vector2 OffWhatStands(Vector2 p)
        {
            foreach (var b in boulders)
            {
                if (b == null || b.smashed) continue;
                Vector2 off = p - b.Pos;
                float r = b.Radius + 0.3f;
                if (off.sqrMagnitude < r * r) return b.Pos + (off.sqrMagnitude > 1e-4f ? off.normalized : Vector2.right) * r;
            }
            for (int i = 0; i < Sight.Structures.Count; i++)
            {
                var o = Sight.Structures[i];
                if (o == null || o.dying) continue;
                Vector2 off = p - o.pos;
                float r = o.BodyRadius + 0.3f;
                if (off.sqrMagnitude < r * r) return o.pos + (off.sqrMagnitude > 1e-4f ? off.normalized : Vector2.right) * r;
            }
            return p;
        }

        /// <summary>A thrown thing's flight and fuse: true when it is finished with.</summary>
        bool ThrownStep(Projectile p, float dt)
        {
            if (!p.landed)
            {
                if (p.age < p.flight)
                {
                    p.vel.y -= ThrowGravity * dt;
                    p.pos += p.vel * dt;
                    return false;
                }
                p.pos = p.aim;
                p.vel = Vector3.zero;
                p.landed = true;
                p.landedAt = time;
                Landed(p);
                return p.kind == ProjSmoke;
            }
            float fuse = p.kind == ProjFrag ? TacticalRules.FragFuse : TacticalRules.FlashFuse;
            if (time - p.landedAt < fuse) return false;
            if (p.kind == ProjFrag) FragBurst(p);
            else FlashBurst(p);
            return true;
        }

        void Landed(Projectile p)
        {
            Vector2 c = new Vector2(p.pos.x, p.pos.z);
            if (p.kind == ProjSmoke)
            {
                StartSmoke(c, p.team);
                Raise(new GameEvent { kind = GameEventKind.Impact, team = p.team, pos = p.pos, scale = 0.3f, projectileKind = ProjSmoke });
                return;
            }
            if (p.kind != ProjFrag) return;
            FragLandings++;
            if (FriendWithin(p.team, c, TacticalRules.FragReach)) FragFriendAtLanding++;
            // Who is near it gets away from it, if he can.
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var o = units[i];
                if (o == null || o.dying || o.def.bodyRadius <= 0f || o.team > 1) continue;
                if ((o.pos - c).sqrMagnitude > (TacticalRules.FragReach + 1f) * (TacticalRules.FragReach + 1f)) continue;
                if (combatRng.F01() < DiveChance) o.DiveFrom(c);
            }
        }

        bool FriendWithin(int team, Vector2 c, float reach)
        {
            foreach (var o in units)
                if (o != null && !o.dying && o.team == team && !o.def.flying && !o.def.building && (o.pos - c).magnitude < reach + o.BodyRadius)
                    return true;
            return false;
        }

        void FragBurst(Projectile p)
        {
            Vector3 at = p.pos;
            Vector2 c = new Vector2(at.x, at.z);
            Vector3 from = at + Vector3.up * 0.4f;
            if (FriendWithin(p.team, c, TacticalRules.FragReach)) FragFriendAtBurst++;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var e = units[i];
                if (e == null || e.dying || e.team == 2 || e.def.flying || e.Untargetable) continue;
                bool own = e.team == p.team;
                if (own && e.def.building) continue;
                float d = (e.pos - c).magnitude;
                bool soldier = e.def.bodyRadius > 0f;
                if (soldier && d < NearBurst) e.Suppress(NearBurstWeight * (1f - d / NearBurst));
                float reach = TacticalRules.FragReach + e.BodyRadius;
                if (d > reach) continue;
                float k = 1f - Saturate((d - e.BodyRadius) / TacticalRules.FragReach);
                // A rock between it and him takes it, and flat on the ground the fragments go over.
                if (!RoundGets(from, e.Ground + Vector3.up * (e.BodyHeight * 0.6f), null, e)) k *= ShelterLeaves;
                if (soldier && e.posture == Posture.Prone) k *= 0.6f;
                if (k <= 0.01f) continue;
                if (own) FriendHurt++;
                // A friend's source is himself: the alarm and the kill count know a same-side hit by it,
                // and the man who threw may be gone.
                Unit by = own ? e : Unit.Live(p.shooter) ? p.shooter : null;
                Damage(e, p.dmg * k * (soldier ? 1f : p.ClassMul(e)), c, by, true);
                if (!e.dying && k > 0.2f && e.def.IsMobile) Shove(e, e.pos - c, 3f * k);
            }
            Blast(at, TacticalRules.FragReach * 0.5f, 1.3f, 0.12f, 0.12f);
            Raise(new GameEvent { kind = GameEventKind.Impact, team = p.team, pos = at, dir = Vector3.up, scale = 0.9f, projectileKind = ProjFrag });
        }

        void FlashBurst(Projectile p)
        {
            Vector3 at = p.pos;
            Vector2 c = new Vector2(at.x, at.z);
            Vector3 from = at + Vector3.up * 0.5f;
            foreach (var o in units)
            {
                if (o == null || o.dying || o.def.bodyRadius <= 0f || o.team > 1) continue;
                float d = (o.pos - c).magnitude;
                if (d > TacticalRules.FlashReach) continue;
                if (!RoundGets(from, o.Ground + Vector3.up * o.BodyHeight * 0.85f, null, o)) continue;
                o.Stun(TacticalRules.FlashStun * (1f - 0.5f * d / TacticalRules.FlashReach));
                Stunned++;
            }
            Raise(new GameEvent { kind = GameEventKind.Impact, team = p.team, pos = at, dir = Vector3.up, scale = 0.3f, projectileKind = ProjFlash });
        }

        // ------------------------------------------------------------ smoke
        public void StartSmoke(Vector2 at, int team = -1)
        {
            if (SmokeClouds.Count >= MostSmoke) SmokeClouds.RemoveAt(0);
            SmokeClouds.Add(new SmokeCloud { at = at, born = time, until = time + TacticalRules.SmokeLasts, team = team });
            Sight.AddSmoke(at, TacticalRules.SmokeRadius);
            // What the units round it see of the ground has just changed: they look again.
            foreach (var o in units)
                if (o != null && (o.pos - at).sqrMagnitude < 70f * 70f) o.viewAt = -99f;
        }

        void TickSmoke()
        {
            // The leaf cells forget a cloud at their next refresh, within 2 s.
            for (int i = SmokeClouds.Count - 1; i >= 0; i--)
                if (time >= SmokeClouds[i].until) SmokeClouds.RemoveAt(i);
        }

        public bool SmokeNear(Vector2 at, float reach)
        {
            foreach (var s in SmokeClouds)
                if ((s.at - at).sqrMagnitude < reach * reach) return true;
            return false;
        }

        // ------------------------------------------------------------ what a soldier asks before throwing
        /// <summary>No friend within reach of this spot now, nor where he will be by then. The thrower
        /// himself throws far enough.</summary>
        public bool FriendsClear(Unit s, Vector2 at, float reach, float secs)
        {
            CellRange(at, reach + 6f * secs + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == s || o.dying || o.team != s.team || o.def.flying || o.def.building) continue;
                        float need = reach + o.BodyRadius;
                        if ((o.pos - at).sqrMagnitude < need * need) return false;
                        Vector3 v = o.Velocity;
                        Vector2 then = o.pos + new Vector2(v.x, v.z) * secs;
                        if ((then - at).sqrMagnitude < need * need) return false;
                    }
            return true;
        }

        /// <summary>How many enemies he knows of stand within this of that one, him included.</summary>
        public int EnemiesRound(Unit t, int team, float reach)
        {
            int n = 0;
            CellRange(t.pos, reach + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == team || o.team > 1 || o.def.flying || o.def.building) continue;
                        if ((o.pos - t.pos).sqrMagnitude < reach * reach && Sees(team, o)) n++;
                    }
            return n;
        }

        /// <summary>Near a finished place of his side's that stocks what he carries.</summary>
        public bool NearOwnDepot(Unit u)
        {
            for (int i = 0; i < Sight.Structures.Count; i++)
            {
                var o = Sight.Structures[i];
                if (o == null || o.dying || o.team != u.team || !o.Complete) continue;
                if (o.Type != UnitType.Foundry && o.Type != UnitType.Garrison && o.Type != UnitType.Bunkhouse) continue;
                float r = TacticalRules.RestockReach + o.def.radius;
                if ((o.pos - u.pos).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        /// <summary>Does he get it wrong this time? <paramref name="often"/> scales it for what is asked often.</summary>
        public bool Errs(Unit u, float often = 1f)
        {
            if (TacticalRules.NoMistakes) return false;
            int x = Mathf.Clamp(u.Experience, 0, 3);
            MistakeChances[x]++;
            if (combatRng.F01() >= TacticalRules.MistakeChance(x) * often) return false;
            MistakesMade[x]++;
            return true;
        }

        // ------------------------------------------------------------ pistol and knife
        public void FirePistol(Unit s, Unit t)
        {
            if (Truce) return;   // the Quenching's truce
            Unit struck = DecideShot(s, t, out Vector3 muzzle, out Vector3 end, out bool stopped, TacticalRules.PistolSpread);
            ShotsFired++;
            PistolShots++;
            if (struck != null)
            {
                ShotsHit++;
                float dmg = TacticalRules.PistolDamage * (1f + 0.15f * s.rank) * WeaponMul(s);
                Damage(struck, struck.def.bodyRadius > 0f ? dmg : dmg * 0.4f, s.pos, s);
            }
            Tracer(s, muzzle, end, stopped);
            SuppressAlong(s, muzzle, end);
            RevealShooter(s);
            Vector2 to = t.pos - s.pos;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? new Vector3(to.x, 0f, to.y).normalized : Vector3.forward;
            Raise(new GameEvent { kind = GameEventKind.Fire, unit = s, type = s.Type, team = s.team, pos = muzzle, dir = dir, projectileKind = ProjPistol });
        }

        /// <summary>At arm's length: no noise to give him away.</summary>
        public void Stab(Unit s, Unit t)
        {
            if (Truce) return;   // the Quenching's truce
            Stabs++;
            float dmg = TacticalRules.KnifeDamage * (1f + 0.15f * s.rank);
            Vector3 at = t.Ground + Vector3.up;
            Damage(t, dmg, s.pos, s);
            Vector2 to = t.pos - s.pos;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? new Vector3(to.x, 0f, to.y).normalized : Vector3.forward;
            Raise(new GameEvent { kind = GameEventKind.Fire, unit = s, type = s.Type, team = s.team, pos = at, dir = dir, end = at, projectileKind = ProjKnife });
        }
    }
}
