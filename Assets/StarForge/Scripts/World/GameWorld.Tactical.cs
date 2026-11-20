// GameWorld.Tactical.cs -- the game's combat: shots decided when fired, and sight (the old combat was deleted
// at phase 8 of the tactical rewrite; git history before it has it).
//
// A small-arms shot is decided when it is fired: an aim error, then the line it flies along, to the
// first thing in its way. The tracer is only what the player sees of it. (At 8x a round moved 11 m a
// step, so a flying bullet would hit differently at different speeds.) Range, posture and cover need
// no numbers of their own: a far or low body is a small target, and cover is what stops the round.
// A round never hurts its own side: it flies on through friends, into cover or an enemy.
//
// Sight is two things. The fog grid says what ground a side can see, by rays from each unit over the
// hills and through the trees. A unit's spotted flag says whether a side knows it is there, since a
// 4 m cell cannot hide a crawling soldier: an enemy within its sight, cut down by posture and foliage,
// with a clear line to it. Commanders, targeting and the player's view read the flag (Sees).
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public sealed partial class GameWorld
    {
        public SightMap Sight { get; private set; }

        Rng combatRng = new Rng(0xC0B47u);

        /// <summary>Shots decided and how many struck a unit (the trials', the benchmark's).</summary>
        public int ShotsFired, ShotsHit;
        /// <summary>Soldiers struck, by what they were doing; looks for cover and how many found it (the trials').</summary>
        public readonly int[] StruckIn = new int[5];
        /// <summary>Rounds at soldiers and how many struck, by the target's side and what it was doing
        /// (team x 5 + mode; the trials').</summary>
        public readonly int[] RoundsAt = new int[10], StruckAt = new int[10];
        public int CoverSearches, CoverFound;
        /// <summary>What the sight rules and the shots' lines cost, milliseconds in all (the benchmark's report).</summary>
        public double SightMs, LinesMs;
        readonly System.Diagnostics.Stopwatch sightWatch = new System.Diagnostics.Stopwatch();
        readonly System.Diagnostics.Stopwatch lineWatch = new System.Diagnostics.Stopwatch();

        void BeginTactical(uint seed)
        {
            Defs.Rebind();
            combatRng = new Rng(seed ^ 0xC0B47u);
            ShotsFired = ShotsHit = 0;
            SightMs = LinesMs = 0;
            spotNext = 0;
            sweepNext = 0;
            BeginSquads();
            BeginArms();
            BeginHulks();
            Sight = new SightMap(map);
        }

        /// <summary>As ReseedMorale: the evaluation's matches share five seeds, and its opponents should
        /// not all see the same rolls.</summary>
        public void ReseedCombat(uint seed) => combatRng = new Rng(seed ^ 0xC0B47u);

        /// <summary>Does this side know where this unit is? Everything that weighs or shoots at an enemy
        /// asks this, the AI as the player's view. Neutrals (the creatures' side) by the fog's cell.</summary>
        public bool Sees(int team, Unit e)
        {
            if (e.team > 1) return Visible(team, e.pos);
            if (e.team == team) return true;
            return e.SpottedUntil(team) > time;
        }

        // ------------------------------------------------------------ shots
        /// <summary>The gun's mouth, as if already turned on what it is aimed at (a soldier turns as he
        /// aims, so which way he faces now says nothing about the line).</summary>
        /// <summary>Where his round leaves from. <paramref name="leanSide"/> -1 or 1 leans out round his
        /// cover, 2 rises over it (a low wall: standing, the gun clears it).</summary>
        static Vector3 ShotMuzzle(Unit s, Unit t, int leanSide = 0)
        {
            Vector2 to = t.pos - s.pos;
            float len = to.magnitude;
            if (len > 1e-3f) to /= len;
            bool rise = leanSide == RiseOver;
            // Up over a wall he is judged from behind it: he is up only for his rounds.
            float gun = rise ? StandingGun : s.risen ? CrouchGun : s.GunHeight;
            int side = rise ? 0 : leanSide;
            // Leaning out round his cover, the gun comes out past his shoulder.
            Vector2 lean = new Vector2(-to.y, to.x) * (LeanOut * side);
            return s.Ground + Vector3.up * gun + new Vector3(to.x + lean.x, 0f, to.y + lean.y) * 0.7f + new Vector3(lean.x, 0f, lean.y) * 0.3f;
        }

        /// <summary>The lean that is no lean: up over a low wall.</summary>
        public const int RiseOver = 2;
        const float StandingGun = 1.25f, CrouchGun = 0.85f;

        const float LeanOut = 0.45f, ShowsAtEdge = 0.25f;

        /// <summary>Where a round at this body is aimed: its middle, or its head and shoulders over a
        /// rock too high to shoot the middle over. A soldier shoots at what shows of him.</summary>
        Vector3 ShotAim(Unit s, Unit t, Vector3 muzzle, out bool open)
        {
            Vector3 middle = t.Ground + Vector3.up * (t.BodyHeight * 0.55f);
            open = RoundGets(muzzle, middle, s, t);
            if (open) return middle;
            Vector3 top = t.Ground + Vector3.up * (t.BodyHeight * 0.88f);
            open = RoundGets(muzzle, top, s, t);
            if (open) return top;
            // A man leaning out from cover shows the side he leans to, wherever the shooter is, and only
            // for his own shot: he ducks back between rounds. Hiding, he shows nothing.
            if (t.mode == SoldierMode.InCover && (t.leanSide == 1 || t.leanSide == -1) && t.ShowsForShot)
            {
                Vector3 edge = middle + new Vector3(t.leanDir.x, 0f, t.leanDir.y) * ShowsAtEdge;
                open = RoundGets(muzzle, edge, s, t);
                if (open) return edge;
            }
            return middle;
        }

        /// <summary>Past the ground, the rocks, the trunks and any structure but the target (a round stops
        /// on a wall, whoever's it is).</summary>
        bool RoundGets(Vector3 a, Vector3 b, Unit s, Unit t)
        {
            if (Sight.FirstStop(a, b, Plants, boulders) < 1f) return false;
            float dx = b.x - a.x, dz = b.z - a.z, dy = b.y - a.y;
            float flat2 = dx * dx + dz * dz;
            // The structures, kept apart: this is asked several times a second by every soldier in a fight.
            for (int i = 0; i < Sight.Structures.Count; i++)
            {
                var o = Sight.Structures[i];
                if (o == null || o == t || o == s || o.dying) continue;
                float hit = SightMap.RayCircle(a.x, a.z, dx, dz, flat2, o.pos.x, o.pos.y, o.BodyRadius);
                if (hit <= 1f && a.y + dy * hit < Sight.GroundAt(o.pos.x, o.pos.y) + o.BodyHeight) return false;
            }
            return true;
        }

        /// <summary>Would a round get there from where it stands? <paramref name="lean"/> is how he must lean
        /// out for it: only a man in cover can (-1, 1; else 0). Asks nothing of the unit but where it stands.</summary>
        public bool LineOfFire(Unit s, Unit t, out int lean)
        {
            lineWatch.Restart();
            lean = 0;
            ShotAim(s, t, ShotMuzzle(s, t), out bool open);
            if (!open && s.mode == SoldierMode.InCover)
            {
                ShotAim(s, t, ShotMuzzle(s, t, 1), out open);
                if (open) lean = 1;
                else
                {
                    ShotAim(s, t, ShotMuzzle(s, t, -1), out open);
                    if (open) lean = -1;
                }
                // Behind a low wall, crouched, he comes up over it for his rounds.
                if (!open && (s.posture != Posture.Stand || s.risen))
                {
                    ShotAim(s, t, ShotMuzzle(s, t, RiseOver), out open);
                    if (open) lean = RiseOver;
                }
            }
            lineWatch.Stop();
            LinesMs += lineWatch.Elapsed.TotalMilliseconds;
            return open;
        }

        public bool LineOfFire(Unit s, Unit t) => LineOfFire(s, t, out _);

        /// <summary>The nearest enemy it knows of in reach that a round would get to; null if none.
        /// With <paramref name="fightsBackOnly"/>, only what can hurt it (<see cref="FightsBack"/>). At
        /// most a few lines are walked: this is for a target that has just been lost.</summary>
        public Unit NearestShootable(Unit u, bool fightsBackOnly)
        {
            Unit best = null;
            float bestD = float.MaxValue;
            int looked = 0;
            float reach = u.def.range;
            CellRange(u.pos, reach + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2 || o.Untargetable || !CanHit(u, o)) continue;
                        if (fightsBackOnly && !FightsBack(o)) continue;
                        float d = (o.pos - u.pos).magnitude - o.def.radius;
                        if (d > reach || d >= bestD || !Sees(u.team, o)) continue;
                        if (looked++ >= 4) return best;
                        if (o.def.flying || LineOfFire(u, o)) { best = o; bestD = d; }
                    }
            return best;
        }

        /// <summary>A round's error: standing still, at a soldier standing still, RifleSpread. A shooter
        /// lying down holds steadier; a moving target is harder; a veteran better.</summary>
        float ShotSpread(Unit s, Unit t, float gun = 0f)
        {
            float k = s.posture == Posture.Prone ? 0.7f : s.posture == Posture.Crouch ? 0.85f : 1f;
            if (s.Velocity.sqrMagnitude > 1f) k *= 1.6f;
            k *= 1f + 1.2f * Mathf.Min(1f, s.suppression);
            float tSpeed = t.Velocity.magnitude;
            k *= 1f + 0.08f * tSpeed;
            k *= 1f - 0.1f * Mathf.Min(s.rank, 3);
            return (gun > 0f ? gun : s.def.shotSpread) * k;
        }

        /// <summary>Two numbers from a normal curve (Box-Muller), from the combat stream.</summary>
        void Gauss(out float a, out float b)
        {
            float u = Mathf.Max(1e-6f, combatRng.F01()), v = combatRng.F01();
            float r = Mathf.Sqrt(-2f * Mathf.Log(u));
            a = r * Mathf.Cos(2f * Mathf.PI * v);
            b = r * Mathf.Sin(2f * Mathf.PI * v);
        }

        /// <summary>Where a round goes: <paramref name="end"/> is where it stops, <paramref name="stopped"/>
        /// whether anything stopped it there, and what it returns is the enemy it struck, or null.</summary>
        Unit DecideShot(Unit s, Unit t, out Vector3 muzzle, out Vector3 end, out bool stopped, float gun = 0f, float height = 0f)
        {
            lineWatch.Restart();
            muzzle = ShotMuzzle(s, t, s.target == t ? s.leanSide : 0);
            // A gun on a hull (a tank's machine gun) fires from its own height.
            if (height > 0f) muzzle.y = s.Ground.y + height;
            Vector3 to = ShotAim(s, t, muzzle, out _) - muzzle;
            float dist = to.magnitude;
            Vector3 dir = dist > 1e-3f ? to / dist : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 up = Vector3.Cross(dir, right);
            Gauss(out float ex, out float ey);
            float sd = ShotSpread(s, t, gun);
            dir = (dir + right * (ex * sd) + up * (ey * sd)).normalized;

            float reach = gun > 0f ? dist + 6f : Mathf.Max(s.def.range * 1.25f, dist + 6f);
            Vector3 far = muzzle + dir * reach;
            // Fired on past him: a wall he stands in still lets the round reach him.
            float stop = Sight.FirstStop(muzzle, far, Plants, boulders, t.pos);
            Unit struck = StruckOnLine(s, muzzle, far, ref stop);
            end = muzzle + (far - muzzle) * stop;
            stopped = stop < 1f;
            lineWatch.Stop();
            LinesMs += lineWatch.Elapsed.TotalMilliseconds;
            return struck;
        }

        /// <summary>How many of this many rounds would strike what they are aimed at, as things stand.
        /// Hurts nothing and shows nothing (the trials').</summary>
        public int ProbeShots(Unit s, Unit t, int rounds) => ProbeShots(s, t, rounds, out _, out _);

        /// <summary>The same, also counting rounds that struck another enemy, and rounds the ground, a
        /// rock or a trunk stopped before they had come as far as the target.</summary>
        public int ProbeShots(Unit s, Unit t, int rounds, out int other, out int blocked)
        {
            int hits = 0;
            other = blocked = 0;
            float to = (t.pos - s.pos).magnitude - t.BodyRadius;
            for (int i = 0; i < rounds; i++)
            {
                var struck = DecideShot(s, t, out Vector3 m, out Vector3 end, out bool stopped);
                if (struck == t) hits++;
                else if (struck != null) other++;
                else if (stopped && new Vector2(end.x - m.x, end.z - m.z).magnitude < to) blocked++;
            }
            return hits;
        }

        /// <summary>What a round of this shooter's does to what it strikes, before the target's armour.</summary>
        public float ShotDamage(Unit s, Unit t)
        {
            var D = s.def;
            return D.damage * (1f + 0.15f * s.rank) * WeaponMul(s) * HitMul(D, t) * (t.def.bodyRadius > 0f ? D.vsSoldier : 1f);
        }

        /// <summary>Fire a small-arms round: decide it now, draw it after.</summary>
        void FireShot(Unit s, Unit t)
        {
            var D = s.def;
            Unit struck = DecideShot(s, t, out Vector3 muzzle, out Vector3 end, out bool stopped);
            ShotsFired++;
            if (t.def.bodyRadius > 0f && t.team < 2)
            {
                RoundsAt[t.team * 5 + (int)t.mode]++;
                if (struck == t) StruckAt[t.team * 5 + (int)t.mode]++;
            }
            if (struck != null)
            {
                ShotsHit++;
                if (struck.def.bodyRadius > 0f) StruckIn[(int)struck.mode]++;
                Damage(struck, ShotDamage(s, struck), s.pos, s);
            }
            // The long rifle's round is drawn as its own line, from the Fire event.
            bool sniper = D.weapon == WeaponKind.Sniper;
            int kind = sniper ? ProjSniper : D.weapon == WeaponKind.MachineGun ? ProjMG : 0;
            if (!sniper) Tracer(s, muzzle, end, stopped);
            else if (stopped) Raise(new GameEvent { kind = GameEventKind.Impact, team = s.team, pos = end, dir = (end - muzzle).normalized, scale = 0.4f, projectileKind = ProjSniper });
            SuppressAlong(s, muzzle, end);
            Vector3 fwd = new Vector3(Mathf.Sin(s.yaw), 0f, Mathf.Cos(s.yaw));
            Raise(new GameEvent { kind = GameEventKind.Fire, unit = s, type = D.type, team = s.team, pos = muzzle, dir = sniper ? (end - muzzle).normalized : fwd, end = sniper ? end : default, projectileKind = kind });
        }

        const float NearMiss = 2.5f, NearMissWeight = 0.06f;

        /// <summary>Every enemy soldier the round passed close to keeps his head down for it: the closer,
        /// the more, a round that struck him included (Damage adds what being hit does).</summary>
        void SuppressAlong(Unit s, Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float flat2 = dx * dx + dz * dz;
            if (flat2 < 1e-3f) return;
            Vector2 mid = new Vector2((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f);
            CellRange(mid, Mathf.Sqrt(flat2) * 0.5f + NearMiss, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == s.team || o.team == 2 || o.def.bodyRadius <= 0f) continue;
                        float t = Mathf.Clamp01(((o.pos.x - a.x) * dx + (o.pos.y - a.z) * dz) / flat2);
                        float ox = a.x + dx * t - o.pos.x, oz = a.z + dz * t - o.pos.y;
                        float d2 = ox * ox + oz * oz;
                        // With a rock in front of him a man takes it better.
                        if (d2 < NearMiss * NearMiss) o.Suppress((s.def.suppressMul > 0f ? s.def.suppressMul : 1f) * NearMissWeight * (1f - Mathf.Sqrt(d2) / NearMiss) * (o.mode == SoldierMode.InCover || o.pinnedInCover ? 0.5f : 1f));
                    }
        }

        /// <summary>The first enemy on the line before <paramref name="stop"/>, which it comes back
        /// shortened to; structures stop a round whichever side they are on, and take it only if they
        /// are the enemy's. Friends on the line it flies past. Aircraft are not hit this way.</summary>
        Unit StruckOnLine(Unit s, Vector3 a, Vector3 b, ref float stop)
        {
            float dx = b.x - a.x, dz = b.z - a.z, dy = b.y - a.y;
            float flat2 = dx * dx + dz * dz;
            Unit struck = null;
            Vector2 mid = new Vector2((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f);
            float half = Mathf.Sqrt(flat2) * 0.5f + LargestRadius;
            CellRange(mid, half, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == s || o.dying || o.team == 2 || o.def.flying || o.Untargetable) continue;
                        bool enemy = o.team != s.team;
                        if (!enemy && !o.def.building) continue;
                        float t = SightMap.RayCircle(a.x, a.z, dx, dz, flat2, o.pos.x, o.pos.y, o.BodyRadius);
                        if (t >= stop) continue;
                        // In from the side it may pass over or under; the round's height where it
                        // crosses the body's middle decides.
                        float across = Mathf.Clamp01(t + o.BodyRadius / Mathf.Max(1e-3f, Mathf.Sqrt(flat2)));
                        float y = a.y + dy * across, g = o.Ground.y;
                        if (y < g - 0.1f || y > g + o.BodyHeight) continue;
                        stop = t;
                        struck = enemy ? o : null;
                    }
            return struck;
        }

        /// <summary>What the player sees of a round: a tracer that flies straight to where it was decided
        /// to end, and sparks there if something stopped it. It does nothing else.</summary>
        void Tracer(Unit s, Vector3 from, Vector3 to, bool strikes)
        {
            const float Speed = 260f;
            var p = projectilePool.Count > 0 ? projectilePool.Pop() : new Projectile();
            Vector3 d = to - from;
            float len = d.magnitude;
            p.alive = true;
            p.cosmetic = true;
            p.strikes = strikes;
            p.kind = 0;
            p.pos = p.prevPos = from;
            p.aim = to;
            p.target = null;
            p.shooter = s;
            p.team = s.team;
            p.dmg = 0f;
            p.splash = 0f;
            p.bounces = 0;
            p.purge = false;
            p.flight = 0f;
            p.age = 0f;
            p.life = len / Speed;
            p.vel = len > 1e-3f ? d / len * Speed : Vector3.zero;
            projectiles.Add(p);
        }

        /// <summary>A tracer's flight: true once it has gone.</summary>
        bool TracerStep(Projectile p, float dt)
        {
            if (p.life > 0f) { p.pos += p.vel * dt; return false; }
            p.pos = p.aim;
            if (p.strikes)
                Raise(new GameEvent { kind = GameEventKind.Impact, team = p.team, pos = p.aim, dir = p.vel.normalized, scale = 0.4f, projectileKind = 0 });
            return true;
        }

        /// <summary>A gun gives away where it is: to any enemy that could see it standing up, at once.
        /// (One already known keeps being known by the usual looks, now that it shows.)</summary>
        public void RevealShooter(Unit s)
        {
            s.firedAt = time;
            int side = 1 - s.team;
            if ((side != 0 && side != 1) || s.SpottedUntil(side) > time) return;
            lineWatch.Restart();
            TrySpot(s, side);
            lineWatch.Stop();
            LinesMs += lineWatch.Elapsed.TotalMilliseconds;
            if (side == 0) s.visibleToPlayer = Sees(0, s);   // its flash is drawn this frame
        }

        // ------------------------------------------------------------ soldiers' thinking
        // Looking for cover walks many lines, so looks are rationed by match time, not by frames: what
        // the soldiers do must not depend on the frame rate or on what the camera sees.
        const float CoverLooksPerSecond = 20f, CoverLooksHeld = 8f;
        float coverLooks = CoverLooksHeld;
        void TickCoverLooks(float dt) => coverLooks = Mathf.Min(CoverLooksHeld, coverLooks + CoverLooksPerSecond * dt);

        public bool TakeCoverLook()
        {
            if (coverLooks < 1f) return false;
            coverLooks -= 1f;
            return true;
        }

        public float CombatRoll() => combatRng.F01();

        /// <summary>The nearest enemy his side knows of that can hurt him; null if none.</summary>
        public Unit NearestThreat(Unit u, float radius)
        {
            Unit best = null;
            float bestD = radius * radius;
            CellRange(u.pos, radius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2 || o.Untargetable || !FightsBack(o)) continue;
                        float d = (o.pos - u.pos).sqrMagnitude;
                        if (d >= bestD || !Sees(u.team, o)) continue;
                        bestD = d;
                        best = o;
                    }
            return best;
        }

        // Where each side lost a soldier lately: seeing a friend go down is what shakes a man.
        const float LossLasts = 8f, LossNear = 20f, FriendsNear = 15f;
        readonly List<(Vector2 at, float t, int team)> soldierLosses = new List<(Vector2, float, int)>(32);

        void NoteLoss(Unit u)
        {
            if (u.def.bodyRadius <= 0f) return;
            SquadLoss(u);
            soldierLosses.Add((u.pos, time, u.team));
            if (soldierLosses.Count > 48) soldierLosses.RemoveRange(0, 16);
        }

        /// <summary>Who is with him: friendly soldiers near, and his side's soldiers down near him lately.</summary>
        public void Company(Unit u, out int friends, out int lost)
        {
            friends = 0;
            lost = 0;
            for (int i = soldierLosses.Count - 1; i >= 0; i--)
            {
                var l = soldierLosses[i];
                if (time - l.t > LossLasts) break;
                if (l.team == u.team && (l.at - u.pos).sqrMagnitude < LossNear * LossNear) lost++;
            }
            CellRange(u.pos, FriendsNear, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == u || o.dying || o.team != u.team || o.def.bodyRadius <= 0f) continue;
                        if ((o.pos - u.pos).sqrMagnitude < FriendsNear * FriendsNear) friends++;
                    }
        }

        static Vector3 ThreatGun(Unit t) => t.Ground + Vector3.up * (t.def.bodyRadius > 0f ? t.GunHeight : Mathf.Max(1.2f, t.def.visualHeight * 0.6f));

        /// <summary>How much of a crouching man at this spot a round from there would be stopped short of:
        /// three lines across his body, a third each.</summary>
        float CoverShare(Vector2 spot, Unit threat) => CoverShare(spot, threat.pos, ThreatGun(threat));

        float CoverShare(Vector2 spot, Vector2 from, Vector3 gun)
        {
            Vector2 to = from - spot;
            float len = to.magnitude;
            if (len < 1e-3f) return 0f;
            Vector2 side = new Vector2(-to.y, to.x) / len * TacticalRules.InfantryBody;
            float g = Sight.GroundAt(spot.x, spot.y) + 0.75f;
            int stopped = 0;
            for (int k = -1; k <= 1; k++)
            {
                Vector2 p = spot + side * k;
                Vector3 body = new Vector3(p.x, g, p.y);
                if (Sight.FirstStop(gun, body, Plants, boulders) < 1f || TankInWay(gun, body, shelterTeam)) stopped++;
            }
            return stopped / 3f;
        }

        /// <summary>Does his cover still stand between him and this enemy, the most of him?</summary>
        public bool CoversFrom(Vector2 spot, Unit threat)
        {
            shelterTeam = threat.team == 0 ? 1 : 0;
            return CoverShare(spot, threat) >= 0.5f;
        }

        /// <summary>Whose tanks count as cover in the look going on.</summary>
        int shelterTeam;

        /// <summary>Could he get a round from there at this enemy, standing up to it if he must?</summary>
        bool CanFireFrom(Vector2 spot, Unit threat)
        {
            Vector2 to = threat.pos - spot;
            float len = to.magnitude;
            if (len < 1e-3f) return true;
            to /= len;
            Vector3 gun = new Vector3(spot.x, Sight.GroundAt(spot.x, spot.y) + 0.85f, spot.y) + new Vector3(to.x, 0f, to.y) * 0.7f;
            Vector3 aim = threat.Ground + Vector3.up * (threat.BodyHeight * 0.55f);
            if (Sight.FirstStop(gun, aim, Plants, boulders) >= 1f) return true;
            // Up over what is in front of him: a low wall or a rock no higher than his gun.
            if (Sight.FirstStop(gun + Vector3.up * (StandingGun - 0.85f), aim, Plants, boulders) >= 1f) return true;
            Vector3 lean = new Vector3(-to.y, 0f, to.x) * LeanOut;
            return Sight.FirstStop(gun + lean, aim, Plants, boulders) >= 1f || Sight.FirstStop(gun - lean, aim, Plants, boulders) >= 1f;
        }

        // Scratch for FindCover: what might shelter a man near him (centre, radius), and the runs of wall near him.
        readonly List<Vector3> shelters = new List<Vector3>(32);
        readonly List<(float d, CoverWall w)> nearRuns = new List<(float d, CoverWall w)>(32);
        static readonly System.Comparison<(float d, CoverWall w)> ByDistanceFn = (x, y) => x.d.CompareTo(y.d);
        static readonly IComparer<(float d, CoverWall w)> ByDistance = Comparer<(float d, CoverWall w)>.Create(ByDistanceFn);
        const int MostShelters = 10;
        UnityEngine.AI.NavMeshPath coverPath;

        /// <summary>The best place within reach to get out of this enemy's fire, if any is better than where
        /// he stands: behind a rock, a trunk or a wall from him, or at its edge if a round must get out from
        /// there, or a fold in the ground. A place he could shoot from counts for more, one a friend
        /// has already taken for nothing, and <paramref name="onward"/> or <paramref name="back"/> pull it
        /// toward the enemy or away. Only somewhere he can walk.</summary>
        public bool FindCover(Unit u, Unit threat, float reach, bool onward, bool back, out Vector2 best, bool poor = false)
        {
            best = default;
            CoverSearches++;
            lineWatch.Restart();
            bool got = FindCoverSpot(u, threat, reach, onward, back, out best, poor);
            lineWatch.Stop();
            LinesMs += lineWatch.Elapsed.TotalMilliseconds;
            if (got) CoverFound++;
            return got;
        }

        bool FindCoverSpot(Unit u, Unit threat, float reach, bool onward, bool back, out Vector2 best, bool poor)
        {
            shelterTeam = u.team;
            best = default;
            Vector2 chosen = default;
            shelters.Clear();
            // A burnt-out tank, and a friend's that is still going, first: there are few, and after the rocks and
            // trunks of a wood had taken all ten places a man 5 m from a wreck never weighed it.
            foreach (var h in Hulks)
                if (shelters.Count < MostShelters && (h.at - u.pos).sqrMagnitude < (reach + h.radius) * (reach + h.radius))
                    shelters.Add(new Vector3(h.at.x, h.at.y, h.radius));
            if (TanksAlive(u.team) > 0)
            {
                CellRange(u.pos, reach + LargestRadius, out int tx0, out int tx1, out int tz0, out int tz1);
                for (int tz = tz0; tz <= tz1; tz++)
                    for (int tx = tx0; tx <= tx1; tx++)
                        for (int i = gridHead[tz * GRID + tx]; i >= 0; i = gridNext[i])
                        {
                            var o = i < units.Count ? units[i] : null;
                            if (o == null || o.dying || o.team != u.team || !IsTankLike(o) || shelters.Count >= MostShelters) continue;
                            if ((o.pos - u.pos).sqrMagnitude < (reach + o.BodyRadius) * (reach + o.BodyRadius))
                                shelters.Add(new Vector3(o.pos.x, o.pos.y, o.BodyRadius));
                        }
            }
            foreach (var b in boulders)
            {
                if (b == null || b.smashed || (b.Pos - u.pos).sqrMagnitude > reach * reach) continue;
                shelters.Add(new Vector3(b.Pos.x, b.Pos.y, b.Radius));
                if (shelters.Count >= MostShelters) break;
            }
            for (int i = 0; i < Sight.Structures.Count && shelters.Count < MostShelters; i++)
            {
                var o = Sight.Structures[i];
                if (o != null && !o.dying && o.Complete && (o.pos - u.pos).sqrMagnitude < (reach + o.def.radius) * (reach + o.def.radius))
                    shelters.Add(new Vector3(o.pos.x, o.pos.y, o.def.radius));
            }
            if (Plants != null) Sight.TrunksNear(u.pos, reach, Plants, shelters, MostShelters);

            float here = (u.pos - threat.pos).magnitude;
            if (here < 1e-3f) return false;
            // Against the enemy as a whole, not only the nearest: where the rest of them stand.
            Vector2 mass = threat.pos;
            int seen = 1;
            CellRange(u.pos, u.def.sight, out int gx0, out int gx1, out int gz0, out int gz1);
            for (int gz = gz0; gz <= gz1; gz++)
                for (int gx = gx0; gx <= gx1; gx++)
                    for (int i = gridHead[gz * GRID + gx]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == threat || o.dying || o.team == u.team || o.team == 2 || !FightsBack(o) || !Sees(u.team, o)) continue;
                        mass += o.pos;
                        seen++;
                    }
            mass /= seen;
            Vector3 massGun = new Vector3(mass.x, Sight.GroundAt(mass.x, mass.y) + 1.25f, mass.y);
            float Sheltered(Vector2 p) => 0.5f * (CoverShare(p, threat) + CoverShare(p, mass, massGun));
            Vector2 pull = onward && u.order != Order.Move ? FlankPull(u, threat) : Vector2.zero;
            // On a plain Move, onward is nearer where he was sent, not nearer the enemy.
            bool toGoal = onward && !back && u.order == Order.Move;
            float goalHere = toGoal ? (u.pos - u.orderPos).magnitude : 0f;
            float bestScore = float.MinValue;
            float herePlain = Sheltered(u.pos) * 4f + (CanFireFrom(u.pos, threat) ? 2f : 0f);
            void Try(Vector2 p, float apart = 1.2f)
            {
                if (!map.InBounds(p, 2f) || !Walkable(p, 0.5f)) return;
                float walk = (p - u.pos).magnitude;
                if (walk > reach) return;
                // A thin trunk shelters a third of him at best: not worth running for under fire.
                float cover = Sheltered(p);
                if (cover < 0.6f) return;
                float d = (p - threat.pos).magnitude;
                // A man who can shoot from behind what shelters him is worth far more than one out of the fight.
                float score = cover * 4f - walk / 6f;
                if (CanFireFrom(p, threat)) score += 2f;
                if (d > u.def.range && !toGoal) score -= 1.5f;
                if (toGoal)
                {
                    float g = (p - u.orderPos).magnitude;
                    if (g > goalHere - 1f) return;
                    score += (goalHere - g) * 0.15f;
                }
                else if (onward) score += (here - d) * 0.15f + Vector2.Dot(p - u.pos, pull) * 0.25f;
                if (back) score += (d - here) * 0.15f;
                if (score <= herePlain + 0.3f) return;
                // Misjudging it, he takes the worst place that would do, not the best.
                float ranked = poor ? -score : score;
                if (ranked <= bestScore) return;
                if (TakenByFriend(u, p, apart)) return;
                bestScore = ranked;
                chosen = p;
            }
            foreach (var s in shelters)
            {
                Vector2 c = new Vector2(s.x, s.y);
                float r = s.z + 0.6f;
                Vector2 toBehind = (c - mass).normalized;
                Vector2 side = new Vector2(-toBehind.y, toBehind.x);
                Try(c + toBehind * r);
                // At its edge: his middle just behind it, his gun able to come out past it.
                float edge = Mathf.Max(0.05f, s.z - 0.15f);
                Try(c + toBehind * (r * 0.8f) + side * edge);
                Try(c + toBehind * (r * 0.8f) - side * edge);
            }
            // Low walls and ruins: behind the eight nearest, a place every metre along each, on the side away
            // from the enemy, just off its band, wherever its top stands over a ducked man's head; men a metre
            // apart (1.2 m let a 3 m wall take two). Runs too low to duck behind (fallen trunks, broken ends) are
            // weighed as any cover, the four nearest, and do not take the walls' places.
            if (Sight.Runs.Count > 0)
            {
                nearRuns.Clear();
                foreach (var w in Sight.Runs)
                {
                    if (w.smashed) continue;
                    float dw = w.Distance(u.pos);
                    if (dw <= reach) nearRuns.Add((dw, w));
                }
                nearRuns.Sort(ByDistance);
                int walls = 0, lows = 0;
                for (int i = 0; i < nearRuns.Count && (walls < 8 || lows < 4); i++)
                {
                    var w = nearRuns[i].w;
                    bool tall = w.height - 0.06f >= MapGenerator.DuckedClear;
                    if (tall ? walls++ >= 8 : lows++ >= 4) continue;
                    Vector2 ab = w.b - w.a;
                    float len = ab.magnitude;
                    if (len < 0.5f) continue;
                    Vector2 n = new Vector2(-ab.y, ab.x) / len;
                    if (Vector2.Dot(mass - w.Mid, n) > 0f) n = -n;
                    float off = w.half + MapGenerator.WallPad + 0.08f;
                    int places = tall ? Mathf.Clamp(Mathf.RoundToInt(len), 1, 4) : 3;
                    for (int k = 0; k < places; k++)
                    {
                        Vector2 p = w.a + ab * (tall ? (k + 0.5f) / places : 0.2f + 0.3f * k) + n * off;
                        if (!tall) Try(p);
                        else if (w.top - Sight.GroundAt(p.x, p.y) >= MapGenerator.DuckedClear) Try(p, 0.95f);
                    }
                }
            }
            // Down in a ditch, near where he stands.
            foreach (var dt in Ditches)
            {
                if (MapGenerator.SegmentDistance(u.pos, dt.a, dt.b) > reach) continue;
                Vector2 ab = dt.b - dt.a;
                float t = Mathf.Clamp01(Vector2.Dot(u.pos - dt.a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                Try(dt.a + ab * t);
                Try(dt.a + ab * Mathf.Clamp01(t + 0.25f));
                Try(dt.a + ab * Mathf.Clamp01(t - 0.25f));
            }
            // Folds in the ground, a few steps away from where he stands.
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                Try(u.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5f);
            }
            if (bestScore <= float.MinValue) return false;
            coverPath ??= new UnityEngine.AI.NavMeshPath();
            // By his own areas: a soldier's way may go over a low wall.
            int areas = u.agent != null ? u.agent.areaMask : GroundAreas;
            if (!UnityEngine.AI.NavMesh.CalculatePath(u.Ground, map.Ground(chosen), areas, coverPath) ||
                coverPath.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) return false;
            best = chosen;
            return true;
        }

        bool TakenByFriend(Unit u, Vector2 p, float apart = 1.2f)
        {
            CellRange(p, 1.5f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == u || o.team != u.team || o.dying) continue;
                        if ((o.pos - p).sqrMagnitude < apart * apart) return true;
                        if ((o.mode == SoldierMode.ToCover || o.mode == SoldierMode.InCover) && (o.coverAt - p).sqrMagnitude < apart * apart) return true;
                    }
            return false;
        }

        // ------------------------------------------------------------ sight
        const float SpotHold = 1.2f;          // a side keeps knowing for this long after it last saw it: four passes at 8x, and some over
        const float SpotAlways = 4f;          // anything this near with a clear line is seen, however it lies
        const float FiredShows = 3f;          // a gun gives its owner away for this long
        const float ViewOverGround = 1f;      // a fog cell shows if this much over its ground can be seen
        const float WalkedForNewView = 2.5f;
        const float ViewLasts = 1.5f, StructureViewLasts = 6f;
        const int SweepsPerPass = 32;
        // Lines walked to notice someone in one pass, at most: a soldier behind a ridge from a hundred
        // enemies is that many lines each time he comes up, and the rest can wait for the next pass.
        const int SpotLinesPerPass = 600;

        int spotNext;
        int sweepNext;
        int spotLines;

        /// <summary>How far this unit can be noticed from, as a share of the looker's sight. A gun firing
        /// undoes the rest.</summary>
        public float Conceal(Unit e)
        {
            if (time - e.firedAt < FiredShows) return 1f;
            float k = 1f;
            bool soldier = e.def.bodyRadius > 0f;
            if (soldier)
            {
                k = e.posture == Posture.Prone ? 0.4f : e.posture == Posture.Crouch ? 0.7f : 1f;
                if (e.Velocity.sqrMagnitude < 0.25f) k *= 0.85f;
            }
            if (!e.def.flying) k *= 1f - (soldier ? 0.5f : 0.25f) * Sight.WoodsAt(e.pos);
            return k;
        }

        static float LookFrom(Unit o)
        {
            float sight = o.mech != null ? o.mech.Sight : o.def.sight;
            return o.Complete ? sight : sight * 0.5f;
        }

        static Vector3 EyeOf(Unit o) => o.def.flying ? AirPoint(o) : o.Ground + Vector3.up * o.EyeHeight;

        static Vector3 TopOf(Unit e) => e.def.flying ? AirPoint(e) : e.Ground + Vector3.up * (e.BodyHeight * 0.9f);

        bool Notices(Unit o, Unit e, float k)
        {
            if (o != null && time < o.blindUntil) return false;
            if (o == null || o.dying || o.team != 1 - e.team) return false;
            float reach = LookFrom(o);
            if (reach <= 0f) return false;
            reach = reach * k + e.def.radius;
            float d2 = (o.pos - e.pos).sqrMagnitude;
            float near = Mathf.Max(reach, SpotAlways);
            if (d2 > near * near) return false;
            spotLines++;
            return Sight.Clear(EyeOf(o), TopOf(e), o.def.building ? o.def.radius : 0f);
        }

        /// <summary>Does this side now see it? Whoever saw it last is asked first: the usual answer. Only
        /// with <paramref name="scan"/> is everyone else in reach asked.</summary>
        void TrySpot(Unit e, int side, bool scan = true)
        {
            if (!e.def.IsMobile)
            {
                // A structure is a big thing that stays put: where its ground can be seen, it is.
                if (Visible(side, e.pos) || NearVisible(side, e)) e.Spot(side, time + SpotHold, null);
                return;
            }
            float k = Conceal(e);
            var last = side == 0 ? e.seer0 : e.seer1;
            if (Unit.Live(last) && Notices(last, e, k)) { e.Spot(side, time + SpotHold, last); return; }
            if (!scan) return;
            CellRange(e.pos, 62f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == last || !Notices(o, e, k)) continue;
                        e.Spot(side, time + SpotHold, o);
                        return;
                    }
        }

        bool NearVisible(int side, Unit e)
        {
            float r = e.def.radius;
            return Visible(side, e.pos + new Vector2(r, 0f)) || Visible(side, e.pos - new Vector2(r, 0f))
                || Visible(side, e.pos + new Vector2(0f, r)) || Visible(side, e.pos - new Vector2(0f, r));
        }

        /// <summary>Structures every pass, as their ground comes into view (the AI forgets a remembered
        /// structure whose ground it sees and which it does not see there). Of the rest, a share each
        /// pass, each about three times a second: once the lines run out only by who saw it last, so
        /// what is in plain sight stays seen while a hidden army is asked about.</summary>
        void TickSpotting()
        {
            int count = units.Count;
            if (count == 0) return;
            spotLines = 0;
            for (int i = 0; i < count; i++)
            {
                var e = units[i];
                if (e != null && !e.dying && e.team <= 1 && !e.def.IsMobile) TrySpot(e, 1 - e.team);
            }
            int slice = Mathf.Max(8, count / 3);
            for (int n = 0; n < slice; n++)
            {
                if (spotNext >= units.Count) spotNext = 0;
                var e = units[spotNext++];
                if (e == null || e.dying || e.team > 1 || !e.def.IsMobile) continue;
                TrySpot(e, 1 - e.team, spotLines < SpotLinesPerPass);
            }
        }

        void UpdateSight()
        {
            sightWatch.Restart();
            Sight.Refresh(this);
            System.Array.Clear(vis[0], 0, vis[0].Length);
            System.Array.Clear(vis[1], 0, vis[1].Length);

            // New eyes at once; the rest in turn, carrying on next pass from where the budget ran out.
            int count = units.Count;
            for (int n = 0; n < count; n++)
            {
                var e = units[n];
                if (e != null && !e.dying && e.team <= 1 && e.viewCount < 0) SweepView(e);
            }
            int budget = SweepsPerPass;
            for (int n = 0; n < count && budget > 0; n++)
            {
                if (sweepNext >= count) sweepNext = 0;
                var e = units[sweepNext++];
                if (e == null || e.dying || e.team > 1) continue;
                float lasts = e.def.IsMobile ? ViewLasts : StructureViewLasts;
                if (time - e.viewAt < lasts && (e.pos - e.viewFrom).sqrMagnitude < WalkedForNewView * WalkedForNewView) continue;
                SweepView(e);
                budget--;
            }

            foreach (var e in units)
            {
                if (e == null || e.dying || e.team > 1 || e.viewCount <= 0) continue;
                var visT = vis[e.team];
                var exploredT = explored[e.team];
                var seenT = lastSeen[e.team];
                var cells = e.viewCells;
                for (int i = 0; i < e.viewCount; i++)
                {
                    int c = cells[i];
                    visT[c] = 1;
                    exploredT[c] = 1;
                    seenT[c] = time;
                }
            }
            VisVersion++;

            TickSpotting();
            foreach (var e in units)
            {
                if (e == null) continue;
                bool seen = e.team == 0 || Sees(0, e);
                e.visibleToPlayer = seen;
                if (seen) e.everSeenByPlayer = true;
            }
            sightWatch.Stop();
            SightMs += sightWatch.Elapsed.TotalMilliseconds;
        }

        void SweepView(Unit e)
        {
            float sight = LookFrom(e);
            e.viewAt = time;
            e.viewFrom = e.pos;
            if (sight <= 0f) { e.viewCount = 0; return; }
            float eye = EyeOf(e).y;
            e.viewCount = Sight.Sweep(e.pos, eye, sight, ViewOverGround, e.def.building ? e.def.radius : 0f, ref e.viewCells);
        }
    }
}
