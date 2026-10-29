// GameWorld.Roster.cs — the wider roster's rules: who can shoot at what (aircraft),
// the new units' guns (mortar, flamer, sniper rifle, tank cannon, flak, rockets, the
// interceptor's needle gun, claws, acid, the glaive that leaps), and the Armory's
// upgrades (weapons and armour for infantry, vehicles and creatures).
//
// The original units keep their own path in Fire (WeaponKind.Auto); everything here is
// keyed on the definition's WeaponKind and flags, so a new unit is data, not a new
// branch somewhere else.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    /// <summary>The Armory's research lines: weapons and armour for each class.</summary>
    public enum ArmyUpgrade { InfantryWeapons = 0, InfantryArmour, VehicleWeapons, VehiclePlating, CreatureWeapons, CreatureCarapace,
                              MechHunterProgramme, Count }

    public struct ArmyUpgradeInfo
    {
        public string name, blurb;
        public char hotkey;
        public int[] cost;
        public float[] time;
        public UnitClass forClass;
        public bool armour;
        public int Levels => cost.Length;
    }

    public sealed partial class GameWorld
    {
        // Projectile kinds of the wider roster (0-6 are the original units' and the Mech's;
        // 7 is the railgun's ground strike, an Impact only).
        public const int ProjSniper = 8, ProjAP = 9, ProjAcid = 10, ProjClaw = 11, ProjGlaive = 12, ProjLob = 13, ProjRocket = 14;

        /// <summary>Damage a weapons level adds, and the share of damage an armour level takes off.</summary>
        public const float WeaponsPerLevel = 0.10f, ArmourPerLevel = 0.10f;

        /// <summary>The wider roster's own random stream (the Scorcher's grass), from the match seed:
        /// a match with one draws nothing from the structure-fire stream.</summary>
        Rng rosterRng = new Rng(0x5C0B7u);

        /// <summary>How near <paramref name="p"/> comes to the segment a-b.</summary>
        static float DistToSegment3(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return (a + ab * t - p).magnitude;
        }

        // ------------------------------------------------------------ who can shoot at what
        /// <summary>Can <paramref name="s"/>'s gun reach <paramref name="t"/>: an aircraft only
        /// with a gun that hits air, anything else only with one that hits the ground.</summary>
        public static bool CanHit(Unit s, Unit t) => t.def.flying ? s.def.hitsAir : s.def.hitsGround;

        /// <summary>The enemy nearest by its edge between <paramref name="min"/> and
        /// <paramref name="max"/> of <paramref name="u"/> that its gun can reach (a mortar's band).</summary>
        public Unit NearestEnemyInBand(Unit u, float min, float max)
        {
            Unit best = null;
            float bestD = max + 1e-3f;
            CellRange(u.pos, max + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team == u.team || o.team == 2 || o.Untargetable || !CanHit(u, o)) continue;
                        float d = (o.pos - u.pos).magnitude - o.def.radius;
                        if (d < min || d >= bestD) continue;
                        bestD = d; best = o;
                    }
            return best;
        }

        public Vector2 ClampToMap(Vector2 p) =>
            new Vector2(Mathf.Clamp(p.x, 2f, MapSize - 2f), Mathf.Clamp(p.y, 2f, MapSize - 2f));

        /// <summary>Which way an aircraft should drift to keep clear of the others round it.</summary>
        public Vector2 AirSeparation(Unit u)
        {
            Vector2 push = Vector2.zero;
            float reach = u.def.radius * 2.6f + 0.8f;
            CellRange(u.pos, reach, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o == u || o.dying || !o.def.flying) continue;
                        Vector2 d = u.pos - o.pos;
                        float m = d.magnitude;
                        if (m >= reach) continue;
                        // Exactly on top of each other: part by id, not by the match's random stream.
                        if (m < 1e-3f) d = new Vector2(Mathf.Cos(u.id * 2.4f), Mathf.Sin(u.id * 2.4f));
                        else d /= m;
                        push += d * (1f - m / reach);
                    }
            return push;
        }

        // ------------------------------------------------------------ the Armory
        static readonly ArmyUpgradeInfo[] armyUpgradeInfo =
        {
            new ArmyUpgradeInfo { name = "Infantry Weapons", hotkey = 'T', forClass = UnitClass.Infantry, cost = new[] { 125, 200 }, time = new[] { 45f, 60f },
                blurb = "+10% damage a level for Troopers, Thumpers, Scorchers, Marksmen and Prospectors." },
            new ArmyUpgradeInfo { name = "Infantry Armour", hotkey = 'G', forClass = UnitClass.Infantry, armour = true, cost = new[] { 125, 200 }, time = new[] { 45f, 60f },
                blurb = "Infantry take 10% less damage a level." },
            new ArmyUpgradeInfo { name = "Vehicle Weapons", hotkey = 'V', forClass = UnitClass.Vehicle, cost = new[] { 150, 250 }, time = new[] { 50f, 65f },
                blurb = "+10% damage a level for tanks, Skimmers and aircraft." },
            new ArmyUpgradeInfo { name = "Vehicle Plating", hotkey = 'B', forClass = UnitClass.Vehicle, armour = true, cost = new[] { 150, 250 }, time = new[] { 50f, 65f },
                blurb = "Tanks, Skimmers, aircraft, Diggers and Haulers take 10% less damage a level." },
            new ArmyUpgradeInfo { name = "Creature Venom", hotkey = 'N', forClass = UnitClass.Creature, cost = new[] { 125, 200 }, time = new[] { 45f, 60f },
                blurb = "+10% damage a level for the Biolab's creatures." },
            new ArmyUpgradeInfo { name = "Creature Carapace", hotkey = 'M', forClass = UnitClass.Creature, armour = true, cost = new[] { 125, 200 }, time = new[] { 45f, 60f },
                blurb = "The Biolab's creatures take 10% less damage a level." },
            // A one-off unlock, not a level of anything: the Workshop's Mech-Hunter.
            new ArmyUpgradeInfo { name = "Mech-Hunter Programme", hotkey = 'K', forClass = UnitClass.None, cost = new[] { 200 }, time = new[] { 50f },
                blurb = "Lets the Workshop build the Mech-Hunter, a light vehicle whose coil lance tears through Mechs and Ironclads. " +
                        "One in the field at a time, and a wait after it falls." },
        };

        /// <summary>Seconds a side waits to train another Mech-Hunter after one falls.</summary>
        public const float HunterWait = 75f;

        /// <summary>Why this side cannot train a Mech-Hunter now (null: it can): the Armory's programme
        /// not done, one already in the field or on order at any Workshop, or the wait after one fell.</summary>
        public string HunterBarred(int team)
        {
            if (team < 0 || team > 1) return "No";
            var F = factions[team];
            if (F.armyUpgrades[(int)ArmyUpgrade.MechHunterProgramme] < 1) return "Needs the Armory's Mech-Hunter Programme";
            if (time < F.hunterReadyAt) return $"Next Mech-Hunter ready in {Mathf.CeilToInt(F.hunterReadyAt - time)}s";
            foreach (var u in units)
            {
                if (u == null || u.dying || u.team != team) continue;
                if (u.Type == UnitType.MechHunter && !u.deserted) return "Only one Mech-Hunter in the field at a time";
                if (u.def.building)
                    foreach (var q in u.queue) if (q == UnitType.MechHunter) return "A Mech-Hunter is already on order";
            }
            return null;
        }

        public static ArmyUpgradeInfo ArmyUpgradeOf(ArmyUpgrade up) => armyUpgradeInfo[(int)up];

        static ArmyUpgrade WeaponsLine(UnitClass c) => c == UnitClass.Infantry ? ArmyUpgrade.InfantryWeapons
            : c == UnitClass.Vehicle ? ArmyUpgrade.VehicleWeapons : ArmyUpgrade.CreatureWeapons;
        static ArmyUpgrade ArmourLine(UnitClass c) => c == UnitClass.Infantry ? ArmyUpgrade.InfantryArmour
            : c == UnitClass.Vehicle ? ArmyUpgrade.VehiclePlating : ArmyUpgrade.CreatureCarapace;

        /// <summary>What its side's weapons research adds to a unit's damage.</summary>
        public float WeaponMul(Unit u)
        {
            if (u.team > 1 || u.def.upgradeClass == UnitClass.None) return 1f;
            return 1f + WeaponsPerLevel * factions[u.team].armyUpgrades[(int)WeaponsLine(u.def.upgradeClass)];
        }

        /// <summary>The share of damage its side's armour research lets through.</summary>
        public float ArmourMul(Unit u)
        {
            if (u.team > 1 || u.def.upgradeClass == UnitClass.None) return 1f;
            return 1f - ArmourPerLevel * factions[u.team].armyUpgrades[(int)ArmourLine(u.def.upgradeClass)];
        }

        /// <summary>The ore the next level costs, or -1 when done (or being researched elsewhere).</summary>
        public int ArmyUpgradeCost(int team, ArmyUpgrade up)
        {
            var info = ArmyUpgradeOf(up);
            int lvl = factions[team].armyUpgrades[(int)up];
            return lvl >= info.Levels ? -1 : info.cost[lvl];
        }

        /// <summary>An Armory of this side already working on this line.</summary>
        public Unit ResearchingAt(int team, ArmyUpgrade up)
        {
            foreach (var u in units)
                if (u != null && !u.dying && u.team == team && u.Type == UnitType.Armory && u.researchLine == (int)up) return u;
            return null;
        }

        public bool CmdArmoryResearch(Unit armory, ArmyUpgrade up)
        {
            if (!Unit.Live(armory) || armory.Type != UnitType.Armory || !armory.Complete || armory.team > 1) return false;
            if (up < 0 || up >= ArmyUpgrade.Count) return false;
            int team = armory.team;
            var F = factions[team];
            int cost = ArmyUpgradeCost(team, up);
            if (cost < 0) return Refuse(team, "That upgrade is complete");
            if (armory.researchLine >= 0) return Refuse(team, "The Armory is busy with another upgrade");
            if (ResearchingAt(team, up) != null) return Refuse(team, "Another Armory is already researching that");
            if (F.ore < cost) return Refuse(team, "Not enough ore");
            F.ore -= cost;
            F.armyUpgradeOreSpent += cost;
            armory.researchLine = (int)up;
            armory.researchTotal = armory.researchLeft = ArmyUpgradeOf(up).time[F.armyUpgrades[(int)up]];
            return true;
        }

        public bool CmdCancelArmoryResearch(Unit armory)
        {
            if (!Unit.Live(armory) || armory.researchLine < 0 || armory.team > 1) return false;
            var up = (ArmyUpgrade)armory.researchLine;
            int cost = ArmyUpgradeCost(armory.team, up);
            if (cost > 0) { factions[armory.team].ore += cost; factions[armory.team].armyUpgradeOreSpent -= cost; }
            armory.researchLine = -1;
            armory.researchLeft = armory.researchTotal = 0f;
            return true;
        }

        /// <summary>An Armory's research ticks down (from Unit.Tick); finished, the level is its side's.</summary>
        public void TickArmory(Unit a, float dt)
        {
            if (a.researchLine < 0 || a.team > 1) return;
            a.researchLeft -= dt;
            if (a.researchLeft > 0f) return;
            var up = (ArmyUpgrade)a.researchLine;
            var info = ArmyUpgradeOf(up);
            int lvl = ++factions[a.team].armyUpgrades[a.researchLine];
            a.researchLine = -1;
            a.researchLeft = a.researchTotal = 0f;
            Raise(new GameEvent
            {
                kind = GameEventKind.ArmyUpgradeComplete, unit = a, type = UnitType.Armory, team = a.team, pos = a.Ground,
                index = (int)up, scale = lvl, text = info.Levels == 1 ? $"{info.name} researched" : $"{info.name} {MechParts.Roman(lvl)} researched"
            });
        }

        // ------------------------------------------------------------ the wider roster's guns
        Vector3 Muzzle(Unit s, float up, float forward)
        {
            float ang = s.HasTurret ? s.turretYaw : s.yaw;
            Vector3 fwd = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            return s.Ground + Vector3.up * (up + (s.def.flying ? s.def.altitude - s.dip : 0f)) + fwd * forward;
        }

        /// <summary>The damage multiplier of a definition's gun on what it hits.</summary>
        static float HitMul(UnitDef D, Unit t) =>
            (t.def.building ? D.vsStructure : t.def.flying ? D.vsAir : t.def.Heavy ? D.vsHeavy : D.vsLight) *
            (t.def.harvester && !t.def.Heavy ? D.bonusVsWorkers : 1f) * (t.def.MechLike ? D.vsMech : 1f);

        Projectile Round(Unit s, Unit t, int kind, Vector3 muzzle, float speed)
        {
            var D = s.def;
            var p = projectilePool.Count > 0 ? projectilePool.Pop() : new Projectile();
            p.alive = true;
            p.kind = kind;
            p.pos = p.prevPos = muzzle;
            p.target = t;
            p.shooter = s;
            p.team = s.team;
            p.dmg = D.damage * (1f + 0.15f * s.rank) * WeaponMul(s);
            p.splash = D.splash;
            p.bonusVsWorkers = D.bonusVsWorkers;
            p.vsLight = D.vsLight; p.vsHeavy = D.vsHeavy; p.vsStructure = D.vsStructure; p.vsAir = D.vsAir;
            p.hitsAir = D.hitsAir; p.hitsGround = D.hitsGround;
            p.bounces = 0;
            p.purge = false;
            p.flight = 0f;
            p.age = 0f;
            p.life = 2f;
            p.vel = (AimPoint(t) - muzzle).normalized * speed;
            projectiles.Add(p);
            return p;
        }

        /// <summary>A shot leaving its gun; <paramref name="end"/> is where a blow dealt at once lands
        /// (claws, tusks), for the view to draw it there.</summary>
        void RaiseFire(Unit s, Vector3 muzzle, Vector3 dir, int kind, Vector3 end = default) =>
            Raise(new GameEvent { kind = GameEventKind.Fire, unit = s, type = s.Type, team = s.team, pos = muzzle, dir = dir, projectileKind = kind, end = end });

        void FireRoster(Unit s, Unit t)
        {
            var D = s.def;
            Vector3 aim = AimPoint(t);
            switch (D.weapon)
            {
                case WeaponKind.Rifle:
                {
                    // The Prospector's is a machine gun on its cab roof.
                    bool truck = s.Type == UnitType.Prospector;
                    Vector3 m = Muzzle(s, truck ? 1.76f : 1.2f, truck ? 1.0f : 0.7f);
                    Round(s, t, 0, m, 115f);
                    RaiseFire(s, m, (aim - m).normalized, 0);
                    break;
                }
                case WeaponKind.Mortar:
                {
                    // Lobbed high over whatever is in the way, led a little on a moving target.
                    Vector3 m = Muzzle(s, 1.5f, 0.3f);
                    var p = Round(s, t, ProjLob, m, 1f);
                    Vector3 lead = aim;
                    float dist0 = new Vector2(aim.x - m.x, aim.z - m.z).magnitude;
                    float time = Mathf.Clamp(dist0 / 13f, 1.3f, 2.6f);
                    if (t.agent != null && t.agent.enabled) lead += t.agent.velocity * time * 0.5f;
                    lead.y = Map.HeightAt(new Vector2(lead.x, lead.z)) + 0.3f;
                    Vector3 d = lead - m;
                    p.life = time;
                    p.vel = new Vector3(d.x / time, d.y / time + 0.5f * MortarGravity * time, d.z / time);
                    RaiseFire(s, m, p.vel.normalized, ProjLob);
                    break;
                }
                case WeaponKind.Flamer:
                {
                    // A jet, not a round: what it is aimed at burns, and so does every enemy on
                    // the ground in a narrow cone along it.
                    Vector3 m = Muzzle(s, 1.1f, 0.8f);
                    float dmg = D.damage * (1f + 0.15f * s.rank) * WeaponMul(s);
                    Vector2 from = s.pos, to = t.pos;
                    Vector2 axis = to - from;
                    float len = Mathf.Max(0.5f, axis.magnitude);
                    axis /= len;
                    float reach = D.range + 1.5f;
                    EnemiesNear(s, reach + 2f, flameScratch);
                    foreach (var e in flameScratch)
                    {
                        if (!Unit.Live(e) || e.def.flying || e.Untargetable || e.team == 2) continue;
                        Vector2 r = e.pos - from;
                        float along = Vector2.Dot(r, axis);
                        if (along < 0f || along - e.def.radius > reach) continue;
                        float off = Mathf.Abs(r.x * axis.y - r.y * axis.x) - e.def.radius;
                        float width = D.splash * (0.4f + 0.6f * Mathf.Clamp01(along / reach));
                        if (e != t && off > width) continue;
                        Damage(e, dmg * HitMul(D, e) * (e == t ? 1f : 0.7f), s.pos, s);
                    }
                    Vector3 end = Vector3.Lerp(m, aim, 1f) + new Vector3(axis.x, 0f, axis.y) * 0.8f;
                    Raise(new GameEvent { kind = GameEventKind.Beam, unit = s, type = s.Type, team = s.team, pos = m, end = end,
                                          projectileKind = (int)MechWeapon.Flamer, scale = 0.55f });
                    if (D.ignite > 0f && Plants != null && rosterRng.F01() < 0.35f) Plants.Scorch(this, to, 1.4f, D.ignite);
                    break;
                }
                case WeaponKind.Sniper:
                {
                    Vector3 m = Muzzle(s, 1.25f, 1.1f);
                    Round(s, t, ProjSniper, m, 260f);
                    RaiseFire(s, m, (aim - m).normalized, ProjSniper);
                    break;
                }
                case WeaponKind.Lance:
                {
                    // The Mech-Hunter's coil lance: the slug arrives at once (the Mech railgun's beam
                    // is drawn), its weight on Mechs and Ironclads (vsMech).
                    Vector3 m = Muzzle(s, 1.32f, 3.1f);
                    float dmg = D.damage * (1f + 0.15f * s.rank) * WeaponMul(s);
                    Damage(t, dmg * HitMul(D, t), s.pos, s);
                    Raise(new GameEvent { kind = GameEventKind.Beam, unit = s, type = s.Type, team = s.team, pos = m, end = aim,
                                          projectileKind = (int)MechWeapon.Railgun, scale = 0.7f });
                    break;
                }
                case WeaponKind.Cannon:
                {
                    Vector3 m = Muzzle(s, 1.55f, 2.7f);
                    Round(s, t, ProjAP, m, 170f);
                    RaiseFire(s, m, (aim - m).normalized, ProjAP);
                    break;
                }
                case WeaponKind.Flak:
                {
                    // Bursts in the air round an aircraft; on the ground, a plain round.
                    Vector3 m = Muzzle(s, 1.8f, 1.6f);
                    var p = Round(s, t, 4, m, 150f);
                    p.life = 1.2f;
                    if (!t.def.flying) p.splash = 0f;
                    p.hitsGround = !t.def.flying;
                    RaiseFire(s, m, (aim - m).normalized, 4);
                    break;
                }
                case WeaponKind.Rockets:
                {
                    // A pair of rockets off the pods, each its own curve down onto the target.
                    Vector3 m = Muzzle(s, -0.4f, 0.6f);
                    var p = Round(s, t, 5, m, 1f);
                    float dist = (aim - m).magnitude;
                    int k = s.id + (int)(time * 10f);
                    Vector2 jitter = new Vector2(Mathf.Cos(k * 2.39996f), Mathf.Sin(k * 2.39996f)) * 0.7f;
                    Vector3 side = Vector3.Cross(Vector3.up, (aim - m).normalized);
                    p.from = m;
                    p.aim = aim + new Vector3(jitter.x, 0f, jitter.y) * 0.6f;
                    p.ctrl = Vector3.Lerp(m, aim, 0.4f) + Vector3.up * 1.2f + side * jitter.x * 2f;
                    p.flight = Mathf.Clamp(dist / 42f + 0.2f, 0.35f, 1.4f);
                    p.life = p.flight + 0.5f;
                    RaiseFire(s, m, (aim - m).normalized, 5);
                    break;
                }
                case WeaponKind.Needle:
                {
                    Vector3 m = Muzzle(s, -0.1f, 1.2f);
                    var p = Round(s, t, 4, m, 180f);
                    p.life = 1.0f;
                    RaiseFire(s, m, (aim - m).normalized, 4);
                    break;
                }
                case WeaponKind.Claws:
                {
                    // In the bite's reach the blow lands at once.
                    float dmg = D.damage * (1f + 0.15f * s.rank) * WeaponMul(s) * HitMul(D, t);
                    Vector3 m = Muzzle(s, 0.5f, s.def.radius);
                    RaiseFire(s, m, (aim - m).normalized, ProjClaw, aim);
                    Damage(t, dmg, s.pos, s);
                    break;
                }
                case WeaponKind.Acid:
                {
                    Vector3 m = Muzzle(s, 0.9f, 0.9f);
                    Round(s, t, ProjAcid, m, 55f);
                    RaiseFire(s, m, (aim - m).normalized, ProjAcid);
                    break;
                }
                case WeaponKind.Bazooka:
                {
                    // A rocket off the shoulder: straight, quick, a small burst where it strikes.
                    Vector3 m = Muzzle(s, 1.66f, 1.0f);
                    Round(s, t, ProjRocket, m, 60f);
                    RaiseFire(s, m, (aim - m).normalized, ProjRocket);
                    break;
                }
                case WeaponKind.Battery:
                {
                    // The mini-mech: at an aircraft a pair of missiles off its pods; at anything on the
                    // ground a slug from one shoulder cannon, then the other.
                    float ang = s.HasTurret ? s.turretYaw : s.yaw;
                    Vector3 fwd = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                    Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
                    if (t.def.flying)
                    {
                        for (int k = -1; k <= 1; k += 2)
                        {
                            Vector3 m = s.Ground + Vector3.up * 3.75f + side * (1.25f * k) + fwd * 0.3f;
                            var p = Round(s, t, 5, m, 1f);
                            p.dmg *= 0.6f;
                            float dist = (aim - m).magnitude;
                            p.from = m;
                            p.aim = aim;
                            p.ctrl = Vector3.Lerp(m, aim, 0.4f) + Vector3.up * (2f + dist * 0.2f) + side * k * 1.5f;
                            p.flight = Mathf.Clamp(dist / 45f + 0.25f, 0.4f, 1.5f);
                            p.life = p.flight + 0.5f;
                            RaiseFire(s, m, Vector3.up, 5);
                        }
                    }
                    else
                    {
                        int k = (s.volleys++ & 1) == 0 ? 1 : -1;
                        Vector3 m = s.Ground + Vector3.up * 3.15f + side * (1.2f * k) + fwd * 2.2f;
                        Round(s, t, ProjAP, m, 170f);
                        RaiseFire(s, m, (aim - m).normalized, ProjAP);
                    }
                    break;
                }
                case WeaponKind.Gore:
                {
                    // Tusks: the blow lands at once on what it charged, and the sweep catches what
                    // stands on the ground round it.
                    float dmg = D.damage * (1f + 0.15f * s.rank) * WeaponMul(s);
                    Vector3 m = Muzzle(s, 1.2f, s.def.radius + 0.6f);
                    RaiseFire(s, m, (aim - m).normalized, ProjClaw, aim);
                    Vector2 at = t.pos;
                    EnemiesNear(s, D.range + D.splash + 3f, flameScratch);
                    foreach (var e in flameScratch)
                    {
                        if (!Unit.Live(e) || e.def.flying || e.Untargetable || e.team == 2) continue;
                        if (e == t) { Damage(e, dmg * HitMul(D, e), s.pos, s); continue; }
                        if ((e.pos - at).magnitude - e.def.radius > D.splash) continue;
                        Damage(e, dmg * 0.6f * HitMul(D, e), s.pos, s);
                    }
                    break;
                }
                case WeaponKind.Glaive:
                {
                    Vector3 m = Muzzle(s, -0.3f, 0.8f);
                    var p = Round(s, t, ProjGlaive, m, 70f);
                    p.bounces = D.bounces;
                    RaiseFire(s, m, (aim - m).normalized, ProjGlaive);
                    break;
                }
                default:
                    Round(s, t, 0, Muzzle(s, 1f, 0.7f), 115f);
                    break;
            }
        }

        readonly List<Unit> bounceScratch = new List<Unit>(32);

        /// <summary>The glaive leaps from what it hit to the nearest other enemy it can reach,
        /// each at a third of the blow before, as many times as it has bounces.</summary>
        void Bounce(Projectile p, Unit first, float dmg)
        {
            // From where each blow landed, whether or not it killed (a kill stopped the chain).
            Vector2 at = first.pos;
            Vector3 atAim = AimPoint(first);
            var shooter = p.shooter;
            float blow = p.dmg;
            bounceHit.Clear();
            bounceHit.Add(first);
            for (int b = 0; b < p.bounces; b++)
            {
                blow /= 3f;
                Unit next = null;
                float best = 4.5f * 4.5f;
                bounceScratch.Clear();
                foreach (var o in UnitsNear(at, 4.5f)) bounceScratch.Add(o);
                foreach (var o in bounceScratch)
                {
                    if (bounceHit.Contains(o) || o.dying || o.team == p.team || o.team == 2 || o.Untargetable) continue;
                    if (o.def.flying ? !p.hitsAir : !p.hitsGround) continue;
                    float d = (o.pos - at).sqrMagnitude;
                    if (d < best) { best = d; next = o; }
                }
                if (next == null) break;
                Vector3 nextAim = AimPoint(next);
                Raise(new GameEvent { kind = GameEventKind.Impact, team = p.team, pos = nextAim, end = atAim, scale = 0.4f, projectileKind = ProjGlaive });
                bounceHit.Add(next);
                at = next.pos;
                atAim = nextAim;
                // Each one's own class and worker multipliers, not the first's.
                Damage(next, blow * (next.def.harvester && !next.def.Heavy ? p.bonusVsWorkers : 1f) * p.ClassMul(next), at, Unit.Live(shooter) ? shooter : null);
            }
        }

        readonly List<Unit> bounceHit = new List<Unit>(4);
    }
}
