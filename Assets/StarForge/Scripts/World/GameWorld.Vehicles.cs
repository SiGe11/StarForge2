// GameWorld.Vehicles.cs -- the new combat's tanks (phase 6): the mounted machine gun's targets and rounds, the
// main gun's armour, smoke launchers. Numbers in TacticalRules; the tank's own state in Unit.Vehicle.cs.
using UnityEngine;
using StarForge.Sim;

namespace StarForge.World
{
    public partial class GameWorld
    {
        /// <summary>Tank machine-gun rounds fired and smoke charges popped (trials).</summary>
        public int TankGunShots, TankGunHits, TankSmokesPopped, TankBackOffs, WorkerFlights, CreatureFlights, RoundsStoppedByCover;

        /// <summary>A direct round at a soldier (no burst: splash has its own shelter) is decided when fired under the new
        /// combat: with cover between the muzzle and the man, at his middle and at his head and shoulders, it flies on and
        /// hurts no one (the user's call: cover protects infantry from vehicles' and the Mech's fire as from rifles').</summary>
        void CoverStops(Projectile p, Vector3 muzzle)
        {
            p.coverStopped = false;
            p.targetSheltered = false;
            if (Sight == null || p.purge) return;
            var t = p.target;
            if (t == null || t.def.bodyRadius <= 0f) return;
            if (p.splash > 0f)
            {
                if (p.kind == 1 && !BodyOpen(muzzle, t, p.shooter)) p.targetSheltered = true;
                return;
            }
            if (BodyOpen(muzzle, t, p.shooter)) return;
            p.coverStopped = true;
            RoundsStoppedByCover++;
            var s = p.shooter;
            if (s == null) return;
            // It still smacks into his cover beside him.
            SuppressAlong(s, muzzle, t.Ground + Vector3.up * (t.BodyHeight * 0.55f));
            // A vehicle gives him up for one a round would reach (it poured rounds into his wall, a man in the open by).
            if (s.def.bodyRadius <= 0f && s.mech == null && s.target == t && !s.targetOrdered)
            {
                var other = OpenTarget(s, t, muzzle);
                if (other != null) s.target = other;
            }
        }

        /// <summary>A round from there gets to some of him: his middle, his head and shoulders, or the side he leans out to.</summary>
        bool BodyOpen(Vector3 muzzle, Unit t, Unit s)
        {
            Vector3 middle = t.Ground + Vector3.up * (t.BodyHeight * 0.55f);
            if (RoundGets(muzzle, middle, s, t) || RoundGets(muzzle, t.Ground + Vector3.up * (t.BodyHeight * 0.88f), s, t)) return true;
            if (t.mode == SoldierMode.InCover && (t.leanSide == 1 || t.leanSide == -1) && t.ShowsForShot)
                return RoundGets(muzzle, middle + new Vector3(t.leanDir.x, 0f, t.leanDir.y) * 0.25f, s, t);
            return false;
        }

        /// <summary>The nearest enemy in a vehicle's reach, known to its side, a round from its gun would get to; not <paramref name="not"/>.</summary>
        Unit OpenTarget(Unit s, Unit not, Vector3 muzzle)
        {
            pickWatch.Restart();
            var r = PickOpen(s, not, muzzle);
            PicksMs += pickWatch.Elapsed.TotalMilliseconds;
            return r;
        }

        Unit PickOpen(Unit s, Unit not, Vector3 muzzle)
        {
            LineCandsClear();
            CellRange(s.pos, s.def.range + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == not || o.dying || o.team == s.team || o.team == 2 || o.Untargetable) continue;
                        if (o.def.flying ? !s.def.hitsAir : !s.def.hitsGround) continue;
                        float d = (o.pos - s.pos).magnitude - o.def.radius;
                        if (d > s.def.range || !Sees(s.team, o)) continue;
                        LineCandAdd(o, d);
                    }
            // Nearest first; anything but a soldier needs no look, and answers past the four. At most four soldiers'
            // bodies looked at: the fresh first (one of the four kept for the oldest due man), then the due, oldest first.
            var mem = s.lineMem ??= new LineMemory(true);
            FillCandAt(mem);
            float since = time;
            int freeAt = lineCandN;
            for (int k = 0; k < lineCandN; k++) if (lineCandU[k].def.bodyRadius <= 0f) { freeAt = k; break; }
            int cap = OldestDue(freeAt, since) >= 0 ? 3 : 4, tried = 0, below = lineCandN;
            Unit free = null;
            for (int k = 0; k < lineCandN; k++)
            {
                var o = lineCandU[k];
                // Anything but a soldier needs no look: it answers at its rank, once the due men nearer are tried.
                if (o.def.bodyRadius <= 0f) { free = o; below = k; break; }
                if (tried >= cap || lineCandAt[k] >= 0f) continue;
                tried++;
                if (BodyOpen(muzzle, o, s)) { mem.Forget(o); return o; }
                NoteCand(mem, k);
            }
            while (tried < 4)
            {
                int k = OldestDue(below, since);
                if (k < 0) break;
                tried++;
                if (BodyOpen(muzzle, lineCandU[k], s)) { mem.Forget(lineCandU[k]); return lineCandU[k]; }
                NoteCand(mem, k);
            }
            return free;
        }

        /// <summary>A Mech's hitscan gun reaches a soldier only with nothing in the way (the new combat).</summary>
        bool MechLineGets(Vector3 muzzle, Unit t, Unit shooter)
        {
            if (Sight == null || t.def.bodyRadius <= 0f || (t.deserted && t.team == shooter.team)) return true;
            if (BodyOpen(muzzle, t, shooter)) return true;
            RoundsStoppedByCover++;
            SuppressAlong(shooter, muzzle, t.Ground + Vector3.up * (t.BodyHeight * 0.55f));
            return false;
        }

        /// <summary>The nearest anti-tank man (or Mech-Hunter) its side sees within his reach and the margin, and that reach.</summary>
        public Unit NearestAntiTank(Unit u, out float reach)
        {
            Unit best = null;
            float bestD = float.MaxValue;
            reach = 0f;
            CellRange(u.pos, 50f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2 || o.def.flying || o.def.Heavy || o.def.building) continue;
                        if (!TacticalRules.AntiArmour(o.def.weapon)) continue;
                        float d = (o.pos - u.pos).magnitude;
                        if (d > o.def.range + TacticalRules.TankBackMargin || d >= bestD || !Sees(u.team, o)) continue;
                        best = o;
                        bestD = d;
                        reach = o.def.range;
                    }
            return best;
        }

        Vector3 TankGunMuzzle(Unit s, Unit t)
        {
            Vector2 to = t.pos - s.pos;
            float len = to.magnitude;
            if (len > 1e-3f) to /= len;
            return s.Ground + Vector3.up * GunHeightOf(s) + new Vector3(to.x, 0f, to.y) * 0.6f;
        }

        /// <summary>What a tank's machine gun takes: a man or a light thing in its reach, known to its side, that a
        /// round from its roof would get to; a soldier first, and not what the main gun is on while another will do.</summary>
        public Unit TankGunTarget(Unit u)
        {
            pickWatch.Restart();
            var r = PickGunTarget(u);
            PicksMs += pickWatch.Elapsed.TotalMilliseconds;
            return r;
        }

        Unit PickGunTarget(Unit u)
        {
            float reach = Mathf.Max(TacticalRules.TankGunRange, u.def.range);
            LineCandsClear();
            CellRange(u.pos, reach + 2f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2 || o.def.flying || o.def.building || o.def.Heavy) continue;
                        if (o.mech != null || o.Untargetable) continue;
                        float d = (o.pos - u.pos).magnitude;
                        if (d - o.def.radius > reach || !Sees(u.team, o)) continue;
                        LineCandAdd(o, d + (o == u.target ? 12f : 0f) + (o.def.bodyRadius > 0f ? 0f : 8f));
                    }
            // At most four lines a look (NearestShootable's limit). The man it is on first: kept while a round still
            // reaches him and no better one is found. Then, of those ranked better, the fresh first (one of the four kept
            // for the oldest due man), then the due, oldest first. The roof's memory ignores leaning (its line does).
            var mem = u.gunMem ??= new LineMemory(false);
            FillCandAt(mem);
            float since = time;
            Unit prev = u.tankGunTarget, found = null;
            int prevAt = -1, tried = 0, foundAt = int.MaxValue;
            if (prev != null)
                for (int k = 0; k < lineCandN; k++)
                    if (lineCandU[k] == prev) { prevAt = k; break; }
            if (prevAt >= 0)
            {
                tried++;
                if (GunLine(u, prev)) { found = prev; foundAt = prevAt; } else NoteCand(mem, prevAt);
            }
            int below = found == null ? lineCandN : foundAt;
            int cap = OldestDue(below, since) >= 0 ? 3 : 4;
            Unit better = null;
            for (int k = 0; k < lineCandN && k < below && tried < cap; k++)
            {
                if (k == prevAt || lineCandAt[k] >= 0f) continue;
                tried++;
                if (GunLine(u, lineCandU[k])) { better = lineCandU[k]; break; }
                NoteCand(mem, k);
            }
            while (better == null && tried < 4)
            {
                int k = OldestDue(below, since);
                if (k < 0) break;
                tried++;
                if (GunLine(u, lineCandU[k])) { better = lineCandU[k]; break; }
                NoteCand(mem, k);
            }
            if (better != null) found = better;
            if (found != null) mem.Forget(found);
            return found;
        }

        bool GunLine(Unit u, Unit o)
        {
            Vector3 m = TankGunMuzzle(u, o);
            return RoundGets(m, o.Ground + Vector3.up * (o.BodyHeight * 0.55f), u, o) ||
                   RoundGets(m, o.Ground + Vector3.up * (o.BodyHeight * 0.88f), u, o);
        }

        /// <summary>Armour or a structure in a tank's main-gun reach, known to its side: what fights back first.</summary>
        public Unit TankMainTarget(Unit u, Unit not)
        {
            Unit best = null;
            float bestD = float.MaxValue;
            CellRange(u.pos, u.def.range + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == not || o.dying || o.team == u.team || o.team == 2 || o.Untargetable) continue;
                        if (!o.def.Heavy && !o.def.building) continue;
                        if (o.def.flying ? !u.def.hitsAir : !u.def.hitsGround) continue;
                        float d = (o.pos - u.pos).magnitude - o.def.radius;
                        if (d > u.def.range || !Sees(u.team, o)) continue;
                        if (!FightsBack(o)) d += 20f;
                        if (d < bestD) { bestD = d; best = o; }
                    }
            return best;
        }

        public void FireTankGun(Unit s, Unit t)
        {
            Unit struck = DecideShot(s, t, out Vector3 muzzle, out Vector3 end, out bool stopped, TacticalRules.TankGunSpread, GunHeightOf(s));
            TankGunShots++;
            if (struck != null)
            {
                TankGunHits++;
                float dmg = TacticalRules.TankGunDamage * TacticalRules.TankGunMul(s.Type) * (1f + 0.15f * s.rank) * WeaponMul(s);
                Damage(struck, struck.def.bodyRadius > 0f ? dmg : struck.def.Heavy || struck.def.building ? dmg * 0.1f : dmg * 0.4f, s.pos, s);
            }
            Tracer(s, muzzle, end, stopped);
            SuppressAlong(s, muzzle, end);
            RevealShooter(s);
            Vector2 to = t.pos - s.pos;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? new Vector3(to.x, 0f, to.y).normalized : Vector3.forward;
            Raise(new GameEvent { kind = GameEventKind.Fire, unit = s, type = s.Type, team = s.team, pos = muzzle, dir = dir, projectileKind = ProjMG });
        }

        /// <summary>An anti-tank round has just been fired at this tank, from there.</summary>
        public void WarnArmour(Unit shooter, Unit t)
        {
            if (t == null || !TacticalRules.IsTank(t.Type)) return;
            t.armourWarnedAt = time;
            t.armourWarnedFrom = shooter.pos;
        }

        /// <summary>A smoke charge: a cloud between the tank and whoever fired at it.</summary>
        public void TankSmoke(Unit u, Vector2 from)
        {
            Vector2 to = from - u.pos;
            float len = to.magnitude;
            StartSmoke(len > 1e-3f ? u.pos + to / len * Mathf.Min(4f, len * 0.5f) : u.pos);
            TankSmokesPopped++;
        }

        /// <summary>Within reach of a depot to take on smoke: a soldier's, or a Workshop.</summary>
        public bool NearTankDepot(Unit u)
        {
            if (NearOwnDepot(u)) return true;
            CellRange(u.pos, 26f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team != u.team || o.Type != UnitType.Workshop || !o.Complete) continue;
                        if ((o.pos - u.pos).magnitude <= 20f + o.def.radius) return true;
                    }
            return false;
        }
    }
}
