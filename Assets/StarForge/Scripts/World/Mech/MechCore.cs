// MechCore.cs — the Mech's rules: what its parts and the Mech Bay's upgrades make
// of it (hull, armour, shield, speed, range), and its guns. Lives on the Mech's
// GameObject beside Unit, which ticks it; the Mech's own AI (AI/MechBrain) only
// moves it and names a target it would like shot first, through GameWorld.CmdMech*.
//
// Every weapon aims and fires on its own: each picks, several times a second, the
// enemy in its reach it is best against -- the rotary cannon and the flamer go for
// infantry, the laser and the railgun for armour, the launchers for a crowd -- and
// the torso turns to bring the arm guns onto the target that matters most. Guns on
// top mounts (missiles, mortar) fire upward and so shoot whichever way it faces.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public sealed class MechGun
    {
        public WeaponPart part;
        public string mount;
        public MountKind kind;
        public bool aux;
        public float cooldown, burstT, retarget;
        public int burstLeft;
        public Unit target;
        /// <summary>Shots fired so far (the view watches it for recoil and spin), and
        /// when the last one went.</summary>
        public int shots;
        public float lastShot = -99f;
        /// <summary>A turret weapon's heading relative to the torso (radians).</summary>
        public float yaw;
        /// <summary>The flamer is playing, the rotary cannon spinning: 0..1.</summary>
        public float firing;
    }

    /// <summary>What the Mech's AI is about, for the HUD and the view.</summary>
    public enum MechIntent { Dropping = 0, Guarding, Defending, Supporting, Hunting, Duelling, Withdrawing, Repairing, Holding }

    [DisallowMultipleComponent]
    public sealed class MechCore : MonoBehaviour
    {
        public const float DropTime = 3.4f;
        /// <summary>The kit is modelled at a working scale; on the field every Mech is drawn
        /// this much bigger, so it towers over the Foundries (MechView scales it; the
        /// muzzles here follow).</summary>
        public const float ModelScale = 1.2f;
        /// <summary>How close to its Mech Bay it stands to be repaired, beyond both radii.</summary>
        public const float RepairReach = 6f;
        /// <summary>Hull a second in the gantry once it is left alone: a Mech back at 38%
        /// is whole in about half a minute. At 75 it was back in under twenty seconds, and
        /// a Mech holding its own base outlasted every wave sent at it.</summary>
        public const float BayRepairRate = 50f;
        /// <summary>The gantry's rate while the Mech is being shot at: its crews work in
        /// the lulls. At the full rate under fire a Mech fighting beside its bay was all
        /// but unkillable, and matches between two of them never ended.</summary>
        public const float BayRepairUnderFire = 15f, RepairQuiet = 4f;

        [System.NonSerialized] public MechDesign design;
        [System.NonSerialized] public readonly List<MechGun> guns = new List<MechGun>(7);
        [System.NonSerialized] public float shield, dropLeft;
        [System.NonSerialized] public MechIntent intent = MechIntent.Dropping;
        [System.NonSerialized] public string intentText = "";
        /// <summary>The target the Mech's AI wants shot first (every gun that can reach it prefers it).</summary>
        [System.NonSerialized] public Unit focus;
        /// <summary>Being repaired at the bay this tick (the view draws the arcs).</summary>
        [System.NonSerialized] public bool repairing;
        [System.NonSerialized] public float lastShieldHit = -99f;
        /// <summary>Recoil the torso takes this frame, 0..1 (the view rocks it).</summary>
        [System.NonSerialized] public float kick;
        [System.NonSerialized] public int kills;

        Unit unit;
        GameWorld world;
        int[] levels;
        int mountedAux;
        readonly List<Unit> near = new List<Unit>(64);

        public Unit Unit => unit;
        public bool Landed => dropLeft <= 0f;
        public int Level(MechUpgrade u) => levels == null ? 0 : levels[(int)u];
        public bool HasShield => design != null && design.utility == MechUtility.Shield;
        public float ShieldMax => HasShield ? MechParts.ShieldCapacity : 0f;

        // ------------------------------------------------------------ stats
        public float MaxHp
        {
            get
            {
                if (design == null) return unit != null ? unit.def.hp : 3000f;
                return design.Frame.hp * design.Loco.hpMul * (1f + design.spare * MechParts.SparePointHp)
                       * (1f + 0.10f * Level(MechUpgrade.Armour));
            }
        }

        /// <summary>Share of each blow the hull shrugs off.</summary>
        public float Armour => design == null ? 0.1f
            : Mathf.Clamp(design.Frame.armour + design.Loco.armour + 0.04f * Level(MechUpgrade.Armour), 0f, 0.5f);

        public float Speed => design == null ? 4.5f : design.Loco.speed * (1f + 0.15f * Level(MechUpgrade.Servos));
        public float TurnRate => design == null ? 2f : design.Loco.turnRate * (1f + 0.15f * Level(MechUpgrade.Servos));
        public float DamageMul => 1f + 0.10f * Level(MechUpgrade.Weapons);
        public float RangeMul => (design == null ? 1f : design.Loco.rangeMul)
                                 * (Level(MechUpgrade.Targeting) > 0 ? 1.12f : 1f)
                                 * (design != null && design.utility == MechUtility.Sensors ? 1.1f : 1f);
        public float Sight => 36f + (Level(MechUpgrade.Targeting) > 0 ? 10f : 0f)
                              + (design != null && design.utility == MechUtility.Sensors ? 14f : 0f);
        public float Range(MechGun g) => g.part.range * RangeMul;

        /// <summary>The longest reach of any gun it carries.</summary>
        public float MaxRange
        {
            get { float r = 0f; foreach (var g in guns) r = Mathf.Max(r, Range(g)); return r; }
        }

        /// <summary>The reach of its direct-fire guns (not the mortar), where it wants to stand.</summary>
        public float FightRange
        {
            get
            {
                float r = 0f;
                foreach (var g in guns) if (g.part.id != MechWeapon.Mortar) r = Mathf.Max(r, Range(g));
                return r > 0f ? r : MaxRange;
            }
        }

        /// <summary>The reach of its shortest direct-fire gun: closer than this, every gun works.</summary>
        public float ShortRange
        {
            get
            {
                float r = float.MaxValue;
                foreach (var g in guns) if (g.part.id != MechWeapon.Mortar) r = Mathf.Min(r, Range(g));
                return r < float.MaxValue ? r : MaxRange;
            }
        }

        /// <summary>Damage a second it puts out against light (or heavy) targets, with its upgrades.</summary>
        public float Dps(bool heavy)
        {
            float s = 0f;
            foreach (var g in guns) s += g.part.Dps(heavy);
            return s * DamageMul;
        }

        /// <summary>Hit points a blow must get through: hull plus shield, grossed up for armour.</summary>
        public float EffectiveHp => (unit != null ? unit.hp : MaxHp) / (1f - Armour) + shield;

        // ------------------------------------------------------------ setup
        public void Bind(Unit u, GameWorld w)
        {
            unit = u;
            world = w;
        }

        /// <summary>Give it its parts and its side's upgrades; called as it is dropped.</summary>
        public void Init(MechDesign d, int[] upgradeLevels, bool drop)
        {
            design = d;
            levels = upgradeLevels;
            guns.Clear();
            mountedAux = 0;
            var frame = d.Frame;
            for (int i = 0; i < d.weapons.Count; i++)
            {
                int m = d.MountOf(i);
                guns.Add(new MechGun { part = MechParts.Weapon(d.weapons[i]), mount = frame.mounts[m], kind = frame.kinds[m] });
            }
            SyncHardpoints();
            unit.hp = MaxHp;
            shield = ShieldMax;
            dropLeft = drop ? DropTime : 0f;
            intent = drop ? MechIntent.Dropping : MechIntent.Guarding;
            ApplyAgent();
        }

        /// <summary>Mount the reserve weapons the Extra Hardpoint upgrade has paid for.</summary>
        void SyncHardpoints()
        {
            int want = Mathf.Min(Level(MechUpgrade.Hardpoint), design.reserve.Count);
            var frame = design.Frame;
            while (mountedAux < want)
            {
                int m = design.ReserveMountOf(mountedAux);
                if (m >= frame.mounts.Length) break;
                guns.Add(new MechGun { part = MechParts.Weapon(design.reserve[mountedAux]), mount = frame.mounts[m], kind = frame.kinds[m], aux = true });
                mountedAux++;
            }
        }

        /// <summary>An upgrade finished at the bay: apply it now (the hull keeps its share of health).</summary>
        public void OnUpgraded(MechUpgrade up, float oldMaxHp)
        {
            if (up == MechUpgrade.Armour && oldMaxHp > 0f) unit.hp *= MaxHp / oldMaxHp;
            if (up == MechUpgrade.Hardpoint) SyncHardpoints();
            ApplyAgent();
        }

        void ApplyAgent()
        {
            var a = unit.agent;
            if (a == null) return;
            a.speed = Speed;
            a.acceleration = Speed * 2.2f;
            a.radius = Mathf.Min(2.6f, design != null ? design.Frame.radius : 2.4f);
            a.height = 7f;
            a.avoidancePriority = 10;   // everything else steps aside
        }

        // ------------------------------------------------------------ damage
        /// <summary>What of a blow gets through the shield and the armour.</summary>
        public float Absorb(float dmg, bool splash)
        {
            if (!Landed) return 0f;
            float now = world.time;
            if (shield > 0f)
            {
                lastShieldHit = now;
                float took = Mathf.Min(shield, dmg);
                shield -= took;
                dmg -= took;
                if (dmg <= 0f) return 0f;
            }
            dmg *= 1f - Armour;
            if (design != null && design.utility == MechUtility.Reactive) dmg *= splash ? MechParts.ReactiveSplash : MechParts.ReactiveDirect;
            return dmg;
        }

        // ------------------------------------------------------------ tick
        public void Tick(float dt)
        {
            if (design == null || unit.dying) return;
            kick = Mathf.MoveTowards(kick, 0f, dt * 4f);
            if (!Landed)
            {
                dropLeft -= dt;
                if (dropLeft <= 0f) { dropLeft = 0f; world.MechTouchdown(unit); intent = MechIntent.Guarding; }
                return;
            }
            float now = world.time;
            float quiet = now - unit.lastDamagedT;

            // The shield comes back once nothing has hit it for a while.
            if (HasShield && shield < ShieldMax && now - lastShieldHit > MechParts.ShieldDelay)
                shield = Mathf.Min(ShieldMax, shield + MechParts.ShieldRegen * dt);
            if (design.utility == MechUtility.Nanites && unit.hp < MaxHp)
                unit.hp = Mathf.Min(MaxHp, unit.hp + (quiet > MechParts.NaniteDelay ? MechParts.NaniteRepair : MechParts.NaniteRepairCombat) * dt);

            // At the bay: the gantry mends it (GameWorld decides whether the bay can).
            repairing = world.MechBayRepairs(unit, dt);

            TickGuns(dt, now);
        }

        void TickGuns(float dt, float now)
        {
            // Arm guns aim with the torso: turn it onto the target that matters most.
            Unit aimAt = PrimaryTarget();
            float turn = 1.9f * (1f + 0.15f * Level(MechUpgrade.Servos));
            if (Unit.Live(aimAt))
                unit.turretYaw = ApproachAngle(unit.turretYaw, Mathf.Atan2(aimAt.pos.x - unit.pos.x, aimAt.pos.y - unit.pos.y), turn * dt);
            else
                unit.turretYaw = ApproachAngle(unit.turretYaw, unit.yaw, turn * 0.5f * dt);

            foreach (var g in guns)
            {
                // A turret swings onto its target on its own ring, and home again when idle.
                if (g.part.turret)
                {
                    float rel = Valid(g, g.target) ? WrapAngle(Mathf.Atan2(g.target.pos.x - unit.pos.x, g.target.pos.y - unit.pos.y) - unit.turretYaw) : 0f;
                    g.yaw = ApproachAngle(g.yaw, rel, g.part.turnRate * (Valid(g, g.target) ? 1f : 0.4f) * dt);
                }
                g.cooldown -= dt;
                g.firing = Mathf.MoveTowards(g.firing, 0f, dt * (g.part.id == MechWeapon.Gatling ? 1.2f : 6f));
                // Re-pick every fifth of a second, or at once when the target is gone; with
                // nothing in reach it looks again on the same clock, not every frame.
                if ((g.retarget -= dt) <= 0f || (g.target != null && !Valid(g, g.target)))
                {
                    g.retarget = 0.2f + (g.shots & 3) * 0.02f;
                    g.target = PickTarget(g);
                }
                if (g.burstLeft > 0)
                {
                    if ((g.burstT -= dt) > 0f) continue;
                    if (!Valid(g, g.target)) { g.burstLeft = 0; continue; }
                    Shoot(g, now);
                    g.burstLeft--;
                    g.burstT = g.part.burstGap;
                    continue;
                }
                if (g.cooldown > 0f || !Valid(g, g.target) || !Aimed(g, g.target)) continue;
                g.cooldown = g.part.cooldown;
                g.burstLeft = g.part.burst - 1;
                g.burstT = g.part.burstGap;
                Shoot(g, now);
            }
        }

        /// <summary>What the torso turns to: the AI's focus if an arm gun can reach it,
        /// else the target of its heaviest arm gun.</summary>
        Unit PrimaryTarget()
        {
            if (Unit.Live(focus) && focus.team != unit.team)
                foreach (var g in guns)
                    if (g.kind == MountKind.Arm && InRange(g, focus)) return focus;
            Unit best = null;
            float bestDps = -1f;
            foreach (var g in guns)
            {
                if (g.kind != MountKind.Arm || !Valid(g, g.target)) continue;
                float d = g.part.Dps(g.target.def.Heavy);
                if (d > bestDps) { bestDps = d; best = g.target; }
            }
            if (best == null)
                foreach (var g in guns) if (Valid(g, g.target)) return g.target;
            return best;
        }

        bool InRange(MechGun g, Unit t)
        {
            float d = unit.Dist(t) - t.def.radius;
            return d <= Range(g) && d >= g.part.minRange;
        }

        bool Valid(MechGun g, Unit t) =>
            Unit.Live(t) && t.team != unit.team && t.team < 2 && !t.Untargetable && InRange(g, t);

        bool Aimed(MechGun g, Unit t)
        {
            if (g.part.turret)
                return Mathf.Abs(WrapAngle(Mathf.Atan2(t.pos.x - unit.pos.x, t.pos.y - unit.pos.y) - (unit.turretYaw + g.yaw))) < 0.35f;
            if (g.kind == MountKind.Top) return true;
            float want = Mathf.Atan2(t.pos.x - unit.pos.x, t.pos.y - unit.pos.y);
            // The arms swing a little either way of the torso.
            return Mathf.Abs(WrapAngle(want - unit.turretYaw)) < (g.part.id == MechWeapon.Flamer ? 0.45f : 0.3f);
        }

        /// <summary>The enemy in reach this gun is best used on: the AI's focus if it is
        /// fair game, else by what the gun is for, how close, how hurt and how crowded.</summary>
        Unit PickTarget(MechGun g)
        {
            float range = Range(g);
            world.EnemiesNear(unit, range + 3f, near);
            var p = g.part;
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var e in near)
            {
                if (e.Untargetable || e.Type == UnitType.Ore) continue;
                float d = unit.Dist(e) - e.def.radius;
                if (d > range || d < p.minRange) continue;
                bool heavy = e.def.Heavy;
                float score = d;
                // What the gun is for: a laser on a rifleman is a waste of a shot.
                float fit = heavy ? p.vsHeavy : p.vsLight;
                score -= fit * 25f;
                if (e.def.building) score += e.def.Armed ? 8f : 30f;     // units first, towers before barracks
                else if (!e.def.Armed) score += 10f;                     // Diggers after soldiers
                if (e.Type == UnitType.Mech) score -= 25f;               // the enemy Mech above all
                // Finish what is nearly dead.
                score -= (1f - Mathf.Clamp01(e.hp / Mathf.Max(1f, e.MaxHp))) * 12f;
                if (p.antiGroup)
                {
                    int crowd = 0;
                    foreach (var o in near)
                        if (o != e && (o.pos - e.pos).sqrMagnitude < p.splash * p.splash * 1.4f) crowd++;
                    score -= crowd * 9f;
                }
                if (e == focus) score -= 40f;
                // An arm gun fires only along the torso: something off to the side it
                // would have to wait for the torso to come round to.
                if (g.kind == MountKind.Arm)
                {
                    float off = Mathf.Abs(WrapAngle(Mathf.Atan2(e.pos.x - unit.pos.x, e.pos.y - unit.pos.y) - unit.turretYaw));
                    score += off * 14f;
                }
                // A turret turns quickly, but not instantly: what it already faces first.
                else if (p.turret)
                    score += Mathf.Abs(WrapAngle(Mathf.Atan2(e.pos.x - unit.pos.x, e.pos.y - unit.pos.y) - (unit.turretYaw + g.yaw))) * 3f;
                if (e == g.target) score -= 4f;                          // do not flit between equals
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        void Shoot(MechGun g, float now)
        {
            g.shots++;
            g.lastShot = now;
            g.firing = 1f;
            kick = Mathf.Max(kick, g.part.kick);
            world.FireMechGun(unit, this, g, g.target);
        }

        /// <summary>Where a gun's shot leaves it, in the world: the waist, the mount on the
        /// frame and the muzzle on the weapon, turned with the torso.</summary>
        public Vector3 Muzzle(MechGun g)
        {
            var frame = design.Frame;
            float waist = MechKit.Point(design.Loco.model, "Waist", new Vector3(0f, design.Loco.waist, 0f)).y;
            Vector3 mount = MechKit.Point(frame.model, g.mount, DefaultMount(design.frame, g.mount));
            Vector3 muzzle = MechKit.Point(g.part.model, "Muzzle", DefaultMuzzle(g.part.id));
            // Left-hand mounts carry the weapon mirrored.
            if (mount.x < 0f) muzzle.x = -muzzle.x;
            // A turret's muzzle turns with its ring.
            if (g.part.turret)
            {
                float tc = Mathf.Cos(g.yaw), ts = Mathf.Sin(g.yaw);
                muzzle = new Vector3(muzzle.x * tc + muzzle.z * ts, muzzle.y, -muzzle.x * ts + muzzle.z * tc);
            }
            Vector3 local = (new Vector3(0f, waist, 0f) + mount + muzzle) * ModelScale;
            float c = Mathf.Cos(unit.turretYaw), s = Mathf.Sin(unit.turretYaw);
            Vector3 world3 = new Vector3(local.x * c + local.z * s, local.y, -local.x * s + local.z * c);
            return unit.Ground + world3 - Vector3.up * (world.GroundShape != null ? world.GroundShape.Drop(unit.pos) : 0f);
        }

        // Where the Blender kit puts things (Tools/blender/build_mechs.py), for a kit
        // that has not been built into the project yet.
        public static Vector3 DefaultMount(MechFrame f, string mount)
        {
            float s = f == MechFrame.Light ? 0.86f : f == MechFrame.Medium ? 1f : 1.14f;
            switch (mount)
            {
                case "ArmR": return new Vector3(1.95f, 0.95f, 0.30f) * s;
                case "ArmL": return new Vector3(-1.95f, 0.95f, 0.30f) * s;
                case "ShoulderR": return new Vector3(1.30f, 2.30f, -0.25f) * s;
                case "ShoulderL": return new Vector3(-1.30f, 2.30f, -0.25f) * s;
                case "Back": return new Vector3(0f, 2.15f, -1.05f) * s;
                case "Chest": return new Vector3(0f, 0.85f, 1.25f) * s;
                case "Crown": return new Vector3(0f, 3.05f, -0.15f) * s;
                default: return new Vector3(0f, 1.5f, 0f);
            }
        }

        public static Vector3 DefaultMuzzle(MechWeapon w)
        {
            switch (w)
            {
                case MechWeapon.Autocannon: return new Vector3(0f, 0f, 2.3f);
                case MechWeapon.Gatling: return new Vector3(0f, -0.05f, 2.1f);
                case MechWeapon.Missiles: return new Vector3(0f, 0.55f, 0.1f);
                case MechWeapon.Mortar: return new Vector3(0f, 1.15f, 0.55f);
                case MechWeapon.Laser: return new Vector3(0f, 0f, 2.7f);
                case MechWeapon.Flamer: return new Vector3(0f, -0.05f, 1.9f);
                case MechWeapon.FlameTower: return new Vector3(0f, 0.46f, 1.08f);
                case MechWeapon.Railgun: return new Vector3(0f, 0.05f, 3.7f);
                default: return new Vector3(0f, 0f, 1.5f);
            }
        }
    }
}
