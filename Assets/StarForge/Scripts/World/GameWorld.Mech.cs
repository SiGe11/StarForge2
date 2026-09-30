// GameWorld.Mech.cs — the Mech Bay and the Mech: the one structure a side may raise
// only once, the Mech it calls down, and the Mech's guns.
//
// A Mech Bay is cheap and needs nothing before it. Two minutes after it stands, a
// Mech drops from orbit beside it: one a side, a match, and never again once it is
// lost. The bay guards itself with a gun tower, mends itself, repairs the Mech when
// it comes home, and sells the upgrades that make the Mech faster, tougher,
// harder-hitting, better armed and longer-sighted -- bought by its side (the player
// or the opponent AI), never by the Mech. The Mech itself takes no orders from its
// side: its own AI (AI/MechBrain) drives it, through CmdMechMove and CmdMechFocus
// below, and advises its side through GameEventKind.MechAdvice.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public sealed partial class GameWorld
    {
        /// <summary>Seconds from the bay standing to the Mech landing.</summary>
        public const float MechDropDelay = 120f;
        /// <summary>How long before the drop its side is warned, and the bay lights up.</summary>
        public const float MechInboundLead = 9f;
        public const float MechBayMuzzleHeight = 6.3f;
        const float BayRepairQuiet = 26f, BayRepairUnderFire = 5f, BayQuietAfter = 8f;
        const float MortarGravity = 30f;
        const float LandingRadius = 7.5f, LandingDamage = 220f;

        /// <summary>Where a shot at <paramref name="t"/> is aimed: a little up its body,
        /// half-way up a Mech.</summary>
        public static Vector3 AimPoint(Unit t) =>
            t.Ground + Vector3.up * (t.mech != null ? t.def.visualHeight * 0.5f : t.def.radius * 0.6f);

        public Unit MechOf(int team) => team >= 0 && team < 2 && Unit.Live(factions[team].mech) ? factions[team].mech : null;
        public Unit BayOf(int team) => team >= 0 && team < 2 && Unit.Live(factions[team].bay) ? factions[team].bay : null;

        /// <summary>Knock a light unit off its line (a blast, a slug, a Mech landing): it
        /// is thrown along <paramref name="dir"/> and walks back to where it was going.</summary>
        public void Shove(Unit e, Vector2 dir, float speed)
        {
            if (e == null || e.dying || e.def.building || e.def.Heavy || e.agent == null || !e.agent.enabled || !e.agent.isOnNavMesh) return;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();
            e.agent.velocity += new Vector3(dir.x, 0f, dir.y) * speed;
        }

        /// <summary>A side's Mech or Mech Bay is gone (from Kill, before the unit dies).</summary>
        void MechBookkeeping(Unit u)
        {
            var F = factions[u.team];
            if (u.Type == UnitType.Mech && F.mech == u)
            {
                F.mech = null;
                F.mechLost = true;
                // An upgrade still in the works has no Mech left to fit: its ore comes back.
                if (F.researching >= 0)
                {
                    int cost = MechParts.Upgrade((MechUpgrade)F.researching).cost[F.upgrades[F.researching]];
                    F.ore += cost;
                    F.upgradeOreSpent -= cost;
                }
                F.researching = -1;
            }
            else if (u.Type == UnitType.MechBay && F.bay == u)
            {
                F.bay = null;
                // An upgrade still in the works has no bay left to finish it: its ore comes back.
                if (F.researching >= 0)
                {
                    int cost = MechParts.Upgrade((MechUpgrade)F.researching).cost[F.upgrades[F.researching]];
                    F.ore += cost;
                    F.upgradeOreSpent -= cost;
                }
                F.researching = -1;
                // A site no Digger had started on was never raised, like one called off: another
                // may be placed (its ore is gone). A bay ordered into the enemy's base went up
                // as a site at once and was shot down before its Digger got there, and the
                // side had lost its Mech for the match on a single order.
                if (u.buildProgress <= 0f) { F.bayPlaced = false; return; }
                F.bayLost = true;
                if (!F.mechDropped)
                {
                    F.mechDropAt = -1f;
                    F.mechLost = true;   // it had nowhere to come down to
                }
            }
        }

        // ------------------------------------------------------------ the bays
        void TickMechBays(float dt)
        {
            for (int t = 0; t < 2; t++)
            {
                var F = factions[t];
                var bay = F.bay;
                if (!Unit.Live(bay) || !bay.Complete) continue;

                // The bay patches itself up, slowly under fire and quickly once left alone.
                if (bay.hp < bay.MaxHp && bay.burnUntil <= 0f)
                {
                    float rate = time - bay.lastDamagedT > BayQuietAfter ? BayRepairQuiet : BayRepairUnderFire;
                    bay.hp = Mathf.Min(bay.MaxHp, bay.hp + rate * dt);
                }

                if (F.researching >= 0 && !F.mechLost)
                {
                    F.researchLeft -= dt;
                    if (F.researchLeft <= 0f)
                    {
                        var up = (MechUpgrade)F.researching;
                        float oldMax = Unit.Live(F.mech) ? F.mech.MaxHp : 0f;
                        F.upgrades[F.researching]++;
                        F.researching = -1;
                        if (Unit.Live(F.mech)) F.mech.mech.OnUpgraded(up, oldMax);
                        Raise(new GameEvent
                        {
                            kind = GameEventKind.UpgradeComplete, unit = bay, type = UnitType.MechBay, team = t, pos = bay.Ground,
                            index = (int)up, scale = F.upgrades[(int)up],
                            text = $"{MechParts.Upgrade(up).name} {MechParts.Roman(F.upgrades[(int)up])} complete"
                        });
                    }
                }

                if (F.mechDropped || F.mechDropAt < 0f) continue;
                if (!F.mechInbound && time >= F.mechDropAt - MechInboundLead)
                {
                    F.mechInbound = true;
                    FitOutMech(t);
                    F.dropSpot = DropSpot(bay);
                    Raise(new GameEvent
                    {
                        kind = GameEventKind.MechInbound, unit = bay, type = UnitType.Mech, team = t, pos = Map.Ground(F.dropSpot),
                        text = F.design != null ? $"{F.design.pilot} is inbound" : "Mech inbound"
                    });
                }
                if (time >= F.mechDropAt) DropMech(t);
            }
        }

        /// <summary>Clear ground beside the bay for the Mech to land on: the side that
        /// faces the enemy first, sweeping round, away from anything standing.</summary>
        Vector2 DropSpot(Unit bay)
        {
            float mr = 2.6f;
            Vector2 toFoe = Norm(Map.StartPos(1 - bay.team) - bay.pos);
            float baseAng = Mathf.Atan2(toFoe.y, toFoe.x);
            for (int ring = 0; ring < 3; ring++)
            {
                float r = bay.def.radius + mr + 2.5f + ring * 3f;
                for (int i = 0; i < 16; i++)
                {
                    int k = (i + 1) / 2 * ((i & 1) == 0 ? 1 : -1);
                    float a = baseAng + k * (TAU / 16f);
                    var p = bay.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!Walkable(p, 0.8f) || Map.WaterDepth(p) > 0.2f) continue;
                    bool clear = true;
                    foreach (var o in UnitsNear(p, mr + 1.5f))
                        if (o.def.building || o.def.neutral) { clear = false; break; }
                    if (clear) return NearestReachable(bay.pos, p);
                }
            }
            return NearestWalkable(bay.pos + toFoe * (bay.def.radius + 6f));
        }

        void DropMech(int team)
        {
            var F = factions[team];
            if (F.mechDropped || F.design == null) return;
            if (!F.designChosen) FitOutMech(team);
            if (!F.mechInbound) F.dropSpot = DropSpot(F.bay);
            F.mechDropped = true;
            Vector2 at = F.dropSpot;
            Vector2 toFoe = Map.StartPos(1 - team) - at;
            var u = Spawn(UnitType.Mech, team, at, true, Mathf.Atan2(toFoe.x, toFoe.y));
            if (u.mech == null) { Debug.LogWarning("[StarForge] Mech prefab has no MechCore"); return; }
            u.mech.Init(F.design, F.upgrades, true);
            F.mech = u;
            factions[team].unitsProduced++;
            Debug.Log($"[StarForge] team {team} Mech: {F.design}");
        }

        /// <summary>The pilot's order fits the Mech out for the field it drops into
        /// (MechArmoury): the parts are picked now, against what the enemy fields.</summary>
        void FitOutMech(int team)
        {
            var F = factions[team];
            if (F.designChosen || F.design == null) return;
            var other = factions[1 - team].designChosen ? factions[1 - team].design : null;
            var note = new System.Text.StringBuilder();
            F.design = MechArmoury.Compose(F.design, MechArmoury.Read(this, team), other, F.upgrades[(int)MechUpgrade.Hardpoint], note);
            F.designChosen = true;
            F.armouryNote = note.ToString();
            Debug.Log($"[StarForge] team {team} armoury: {F.armouryNote}");
        }

        /// <summary>Editor trials only: put a Mech on the field outside its side's
        /// bookkeeping (no bay, no AI drives it unless <paramref name="asSides"/>).</summary>
        public Unit TrialMech(int team, MechDesign design, Vector2 at, float yaw, bool drop, bool asSides = false)
        {
            var u = Spawn(UnitType.Mech, team, at, true, yaw);
            if (u.mech == null) return u;
            u.mech.Init(design, asSides ? factions[team].upgrades : new int[(int)MechUpgrade.Count], drop);
            if (asSides) { factions[team].mech = u; factions[team].mechDropped = true; factions[team].design = design; factions[team].designChosen = true; }
            return u;
        }

        /// <summary>The Mech strikes the ground: what stands round the spot is thrown
        /// down and hurt (its own side only thrown), the ground cratered, the trees
        /// flattened.</summary>
        public void MechTouchdown(Unit m)
        {
            var at = m.Ground;
            foreach (var e in UnitsNear(m.pos, LandingRadius).ToArray())
            {
                if (e == m || e.def.building || e.def.neutral || e.dying) continue;
                float k = 1f - Saturate((e.pos - m.pos).magnitude / LandingRadius);
                if (e.team != m.team && e.team < 2) Damage(e, LandingDamage * k, m.pos, m, true);
                Shove(e, e.pos - m.pos, 9f * k);
            }
            Blast(at, 6.5f, 4.2f, 0.5f, 0.25f);
            Raise(new GameEvent { kind = GameEventKind.MechLanded, unit = m, type = UnitType.Mech, team = m.team, pos = at, scale = 1f });
        }

        /// <summary>Repair a Mech standing at its own bay; false if it is not there, or the
        /// bay is gone, or there is nothing to mend.</summary>
        public bool MechBayRepairs(Unit m, float dt)
        {
            if (m.team > 1) return false;
            var bay = factions[m.team].bay;
            if (!Unit.Live(bay) || !bay.Complete) return false;
            float reach = bay.def.radius + (m.agent != null ? m.agent.radius : 2.5f) + MechCore.RepairReach;
            if (m.Dist(bay) > reach) return false;
            var core = m.mech;
            bool hull = m.hp < m.MaxHp - 0.5f, sh = core.HasShield && core.shield < core.ShieldMax - 0.5f;
            if (!hull && !sh) return false;
            bool quiet = time - m.lastDamagedT > MechCore.RepairQuiet;
            float rate = quiet ? MechCore.BayRepairRate : MechCore.BayRepairUnderFire;
            if (hull) m.hp = Mathf.Min(m.MaxHp, m.hp + rate * dt);
            if (sh) core.shield = Mathf.Min(core.ShieldMax, core.shield + rate * 2f * dt);
            return true;
        }

        // ------------------------------------------------------------ the Mech's guns
        readonly List<Unit> gunScratch = new List<Unit>(64), flameScratch = new List<Unit>(64);
        // Reused every shot: the flamer fires ten times a second and each copy of its
        // target list was garbage.
        readonly List<(float along, Unit u)> railScratch = new List<(float along, Unit u)>(16);
        static readonly System.Comparison<(float along, Unit u)> ByAlong = (x, y) => x.along.CompareTo(y.along);

        public void FireMechGun(Unit shooter, MechCore core, MechGun g, Unit target)
        {
            if (!Unit.Live(target)) return;
            var part = g.part;
            Vector3 muzzle = core.Muzzle(g);
            Vector3 aim = AimPoint(target);
            float dmg = part.damage * core.DamageMul;
            float mul = part.Mul(target);
            Vector3 fwd = new Vector3(Mathf.Sin(shooter.turretYaw), 0f, Mathf.Cos(shooter.turretYaw));

            switch (part.id)
            {
                case MechWeapon.Autocannon:
                {
                    var p = NewProjectile(4, shooter, target, muzzle, dmg, 0f, part);
                    // A burst spreads a little; the rounds still home in (as a Mauler's do).
                    Vector3 d = (aim - muzzle).normalized + Random3(0.012f * (g.shots % 3));
                    p.vel = d.normalized * part.projectileSpeed;
                    p.life = 1.2f;
                    Raise(new GameEvent { kind = GameEventKind.Fire, unit = shooter, type = UnitType.Mech, team = shooter.team, pos = muzzle, dir = d.normalized, projectileKind = 4, index = (int)part.id });
                    break;
                }
                case MechWeapon.Missiles:
                {
                    var p = NewProjectile(5, shooter, target, muzzle, dmg, part.splash, part);
                    // Each missile its own curve: up out of the rack, over, and down on the
                    // target (spread round it so a salvo covers a crowd).
                    float dist = (aim - muzzle).magnitude;
                    int k = g.shots;
                    Vector2 jitter = new Vector2(Mathf.Cos(k * 2.39996f), Mathf.Sin(k * 2.39996f)) * (0.6f + (k % 4) * 0.45f);
                    p.aim = aim;
                    p.from = muzzle;
                    Vector3 mid = Vector3.Lerp(muzzle, aim, 0.45f);
                    Vector3 side = Vector3.Cross(Vector3.up, (aim - muzzle).normalized);
                    p.ctrl = mid + Vector3.up * (4f + dist * 0.38f) + side * jitter.x * 3f + fwd * jitter.y;
                    p.flight = Mathf.Clamp(dist / part.projectileSpeed + 0.45f, 0.8f, 2.4f);
                    p.life = p.flight + 0.5f;
                    p.aim += new Vector3(jitter.x, 0f, jitter.y) * 0.8f;
                    Raise(new GameEvent { kind = GameEventKind.Fire, unit = shooter, type = UnitType.Mech, team = shooter.team, pos = muzzle, dir = Vector3.up, projectileKind = 5, index = (int)part.id });
                    break;
                }
                case MechWeapon.Mortar:
                {
                    var p = NewProjectile(6, shooter, target, muzzle, dmg, part.splash, part);
                    // Lobbed high, led on a moving target by where it is going.
                    Vector3 lead = aim;
                    float dist0 = new Vector2(aim.x - muzzle.x, aim.z - muzzle.z).magnitude;
                    float t = Mathf.Clamp(dist0 / 16f, 1.6f, 3.2f);
                    if (target.agent != null && target.agent.enabled) lead += target.agent.velocity * t * 0.7f;
                    lead.y = Map.HeightAt(new Vector2(lead.x, lead.z)) + 0.3f;
                    Vector3 d = lead - muzzle;
                    p.life = t;
                    p.vel = new Vector3(d.x / t, d.y / t + 0.5f * MortarGravity * t, d.z / t);
                    Raise(new GameEvent { kind = GameEventKind.Fire, unit = shooter, type = UnitType.Mech, team = shooter.team, pos = muzzle, dir = p.vel.normalized, projectileKind = 6, index = (int)part.id });
                    break;
                }
                case MechWeapon.Gatling:
                {
                    // Every round a hit on what it is aimed at; the tracers wander a little.
                    Damage(target, dmg * mul, shooter.pos, shooter);
                    Vector3 end = aim + Random3(target.def.radius * 0.5f);
                    Raise(new GameEvent { kind = GameEventKind.Beam, unit = shooter, type = UnitType.Mech, team = shooter.team, pos = muzzle, end = end, projectileKind = (int)part.id, scale = 1f });
                    if (g.shots % 6 == 0) Raise(new GameEvent { kind = GameEventKind.Impact, team = shooter.team, pos = end, dir = (end - muzzle).normalized, scale = 0.3f, projectileKind = 0 });
                    break;
                }
                case MechWeapon.Laser:
                {
                    Damage(target, dmg * mul, shooter.pos, shooter);
                    Raise(new GameEvent { kind = GameEventKind.Beam, unit = shooter, type = UnitType.Mech, team = shooter.team, pos = muzzle, end = aim, projectileKind = (int)part.id, scale = 1f });
                    break;
                }
                case MechWeapon.Railgun:
                {
                    // Through everything in its line, to the end of its reach or the ground.
                    Vector3 dir = (aim - muzzle).normalized;
                    float reach = core.Range(g) + 6f;
                    Vector3 end = muzzle + dir * reach;
                    for (float s = 2f; s < reach; s += 1f)
                    {
                        Vector3 q = muzzle + dir * s;
                        var q2 = new Vector2(q.x, q.z);
                        if (!Map.InBounds(q2) || q.y < Map.HeightAt(q2) - 0.2f) { end = q; break; }
                    }
                    Vector2 a2 = new Vector2(muzzle.x, muzzle.z), b2 = new Vector2(end.x, end.z);
                    EnemiesNear(shooter, reach + 2f, gunScratch);
                    // Nearest first along the line: each body it passes through takes a third
                    // off it, and after the fourth it is spent.
                    var hitList = railScratch;
                    hitList.Clear();
                    foreach (var e in gunScratch)
                    {
                        if (e.Untargetable || e.dying) continue;
                        if (DistToSegment(e.pos, a2, b2) > e.def.radius + 0.8f) continue;
                        hitList.Add((Vector2.Dot(e.pos - a2, (b2 - a2).normalized), e));
                    }
                    hitList.Sort(ByAlong);
                    float left = 1f;
                    for (int k = 0; k < hitList.Count && k < 4; k++)
                    {
                        var e = hitList[k].u;
                        float m = part.Mul(e);
                        Damage(e, dmg * m * left, shooter.pos, shooter, false);
                        Shove(e, b2 - a2, 8f * left);
                        left *= 0.66f;
                    }
                    Blast(end, 2.2f, 1.6f, 0.18f, 0.08f);
                    Raise(new GameEvent { kind = GameEventKind.Beam, unit = shooter, type = UnitType.Mech, team = shooter.team, pos = muzzle, end = end, projectileKind = (int)part.id, scale = 1f });
                    Raise(new GameEvent { kind = GameEventKind.Impact, team = shooter.team, pos = end, dir = dir, scale = 1.05f, projectileKind = 7 });
                    break;
                }
                case MechWeapon.Flamer:
                case MechWeapon.FlameTower:
                {
                    // The flamer plays along the torso; the tower along its own ring.
                    if (part.turret) fwd = new Vector3(Mathf.Sin(shooter.turretYaw + g.yaw), 0f, Mathf.Cos(shooter.turretYaw + g.yaw));
                    float reach = core.Range(g);
                    float cosHalf = Mathf.Cos(part.coneDeg * Mathf.Deg2Rad);
                    Vector2 f2 = new Vector2(fwd.x, fwd.z);
                    EnemiesNear(shooter, reach + 3f, gunScratch);
                    // A copy: a burning body that dies here must not change the list being walked.
                    flameScratch.Clear();
                    flameScratch.AddRange(gunScratch);
                    foreach (var e in flameScratch)
                    {
                        if (e.Untargetable || e.dying) continue;
                        Vector2 d = e.pos - shooter.pos;
                        float dist = d.magnitude - e.def.radius;
                        if (dist > reach) continue;
                        if (dist > 2.5f && Vector2.Dot(d.normalized, f2) < cosHalf) continue;
                        Damage(e, dmg * part.Mul(e), shooter.pos, shooter, false);
                    }
                    // Where the jet comes down: the grass takes and what stands there may catch.
                    float toTarget = Mathf.Min(reach, shooter.Dist(target));
                    Vector2 land = shooter.pos + f2 * toTarget;
                    if ((g.shots % 3) == 0 && Plants != null) Plants.Scorch(this, land, 2.4f, 0.35f);
                    Vector3 end = Map.Ground(land) + Vector3.up * 0.4f;
                    Raise(new GameEvent { kind = GameEventKind.Beam, unit = shooter, type = UnitType.Mech, team = shooter.team, pos = muzzle, end = end, projectileKind = (int)part.id, scale = 1f });
                    break;
                }
            }
        }

        Projectile NewProjectile(int kind, Unit shooter, Unit target, Vector3 muzzle, float dmg, float splash, WeaponPart part)
        {
            var p = projectilePool.Count > 0 ? projectilePool.Pop() : new Projectile();
            p.alive = true;
            p.kind = kind;
            p.pos = p.prevPos = muzzle;
            p.target = target;
            p.shooter = shooter;
            p.team = shooter.team;
            p.dmg = dmg;
            p.splash = splash;
            p.bonusVsWorkers = 1f;
            p.vsLight = part.vsLight;
            p.vsHeavy = part.vsHeavy;
            p.vsStructure = part.vsStructure;
            p.age = 0f;
            p.flight = 0f;
            projectiles.Add(p);
            return p;
        }

        Vector3 Random3(float r) => new Vector3(rng.Range(-r, r), rng.Range(-r, r) * 0.5f, rng.Range(-r, r));

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Saturate(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        // ------------------------------------------------------------ commands
        // The Mech's own AI drives it through these. They are not the player's commands:
        // those skip the Mech (Autonomous), so neither side can order it about.

        public bool CmdMechMove(Unit m, Vector2 dest)
        {
            if (!Unit.Live(m) || m.mech == null || !m.mech.Landed) return false;
            dest = NearestReachable(m, dest);
            m.order = Order.Move;
            m.orderPos = dest;
            m.target = null;
            m.MoveTo(dest);
            return true;
        }

        public void CmdMechHalt(Unit m)
        {
            if (!Unit.Live(m) || m.mech == null) return;
            m.order = Order.Idle;
            m.Halt();
        }

        public void CmdMechFocus(Unit m, Unit target)
        {
            if (!Unit.Live(m) || m.mech == null) return;
            m.mech.focus = target;
        }

        /// <summary>The Mech's AI speaks to its side.</summary>
        public void MechAdvise(int team, MechAdviceKind kind, Vector2 at, string text, Unit about = null)
        {
            Raise(new GameEvent { kind = GameEventKind.MechAdvice, team = team, advice = kind, pos = Map.Ground(at), text = text, unit = about, type = UnitType.Mech });
        }

        /// <summary>Buy one level of an upgrade at the bay (its side's player or AI).</summary>
        public bool CmdMechUpgrade(Unit bay, MechUpgrade up)
        {
            if (!Unit.Live(bay) || bay.Type != UnitType.MechBay || bay.team > 1) return false;
            int team = bay.team;
            var F = factions[team];
            if (!bay.Complete) return Refuse(team, "The Mech Bay is still being built");
            if (F.mechLost) return Refuse(team, "The Mech is lost: there is nothing left to upgrade");
            if (F.researching >= 0) return Refuse(team, "The bay is already working on an upgrade");
            var info = MechParts.Upgrade(up);
            int lvl = F.upgrades[(int)up];
            if (lvl >= info.levels) return Refuse(team, $"{info.name} is complete");
            if (up == MechUpgrade.Hardpoint && lvl >= ReserveMounts(F)) return Refuse(team, "No hardpoint left to arm");
            int cost = info.cost[lvl];
            if (F.ore < cost) return Refuse(team, "Not enough ore");
            F.ore -= cost;
            F.upgradeOreSpent += cost;
            F.researching = (int)up;
            F.researchLeft = F.researchTotal = info.time[lvl];
            return true;
        }

        public bool CmdCancelMechUpgrade(Unit bay)
        {
            if (!Unit.Live(bay) || bay.Type != UnitType.MechBay || bay.team > 1) return false;
            var F = factions[bay.team];
            if (F.researching < 0) return false;
            var info = MechParts.Upgrade((MechUpgrade)F.researching);
            int cost = info.cost[F.upgrades[F.researching]];
            F.ore += cost;
            F.upgradeOreSpent -= cost;
            F.researching = -1;
            return true;
        }

        /// <summary>How many Extra Hardpoints a side can use: its Mech's spare mounts, or,
        /// before the parts are picked, two -- every frame has two, and the armoury only
        /// fits out a design with as many as have been bought.</summary>
        static int ReserveMounts(Faction F) => F.designChosen && F.design != null ? F.design.reserve.Count : 2;

        /// <summary>What an upgrade's next level costs, or -1 if there is none to buy.</summary>
        public int UpgradeCost(int team, MechUpgrade up)
        {
            var F = factions[team];
            var info = MechParts.Upgrade(up);
            int lvl = F.upgrades[(int)up];
            if (lvl >= info.levels) return -1;
            if (up == MechUpgrade.Hardpoint && lvl >= ReserveMounts(F)) return -1;
            return info.cost[lvl];
        }
    }
}
