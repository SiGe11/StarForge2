// MechParts.cs — the fixed list of parts a Mech is built from, and the design
// generator that spends a fixed budget of points on them.
//
// A Mech is a locomotion (legs, tracks or a grav skirt), a frame (the torso: its
// armour and how many hardpoints it has), two to five weapons on those hardpoints,
// and at most one utility module. Every part costs points; every design spends the
// same Budget, and whatever a design cannot spend on a part is turned into armour
// (SparePointHp), so no draw is simply weaker than another -- only different. The
// numbers are the balance sheet: a design is meant to stand up to twenty-odd Maulers
// or thirty-odd Troopers in a straight fight (AgentPlay.MechDuel measures it).
//
// Two more weapons are drawn for each design and held in reserve: the Mech Bay's
// Extra Hardpoint upgrade mounts them, in order, on the frame's auxiliary mounts.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static StarForge.SFMath;

namespace StarForge.World
{
    // Appended, never reordered: designs are logged and compared by number.
    public enum MechLocomotion { Biped = 0, Quad, Tracks, Hover, Count }
    public enum MechFrame { Light = 0, Medium, Heavy, Count }
    public enum MechWeapon { Autocannon = 0, Gatling, Missiles, Mortar, Laser, Flamer, Railgun, FlameTower, Count }
    public enum MechUtility { None = 0, Shield, Nanites, Reactive, Sensors, Count }

    /// <summary>Where a weapon may go: an arm (or chest) mount carries a gun that aims
    /// with the torso; a top mount (shoulder, back, crown) carries a launcher that
    /// fires upward and so can shoot in any direction.</summary>
    public enum MountKind { Arm = 0, Top }

    public enum MechUpgrade { Servos = 0, Armour, Weapons, Hardpoint, Targeting, Count }

    public sealed class LocomotionPart
    {
        public MechLocomotion id;
        public string name, model, blurb;
        public int points;
        public float speed, turnRate, hpMul, armour;
        /// <summary>Range multiplier: a four-legged platform braces and shoots further.</summary>
        public float rangeMul = 1f;
        /// <summary>Metres from the ground to the waist, where the frame sits.</summary>
        public float waist;
        /// <summary>Crosses shallows at full speed (the grav skirt skims over them).</summary>
        public bool skims;
    }

    public sealed class FramePart
    {
        public MechFrame id;
        public string name, model, blurb;
        public int points;
        public float hp, armour, radius;
        public int hardpoints;
        /// <summary>The frame's mounts in unlock order: the first <see cref="hardpoints"/>
        /// are armed by the design, the next two by the Extra Hardpoint upgrade.</summary>
        public string[] mounts;
        public MountKind[] kinds;
        public float height;
    }

    public sealed class WeaponPart
    {
        public MechWeapon id;
        public string name, model, blurb;
        public int points;
        public float range, minRange, damage, cooldown, splash;
        /// <summary>Rounds a trigger pull fires, and the gap between them.</summary>
        public int burst = 1;
        public float burstGap;
        /// <summary>Damage multipliers against light targets (infantry, Diggers,
        /// Skimmers) and heavy ones (tanks, Mechs, structures).</summary>
        public float vsLight = 1f, vsHeavy = 1f;
        /// <summary>Against structures: a Mech's guns are for killing what fights back, so a
        /// base does not fall in the seconds it takes to kill a tank -- except to the
        /// mortar, which is a siege gun.</summary>
        public float vsStructure = 0.45f;

        public float Mul(StarForge.World.Unit e) => e.def.building ? vsStructure : e.def.Heavy ? vsHeavy : vsLight;
        /// <summary>Projectile kind for GameWorld (4 autocannon, 5 missile, 6 mortar), or
        /// -1 for a hitscan weapon whose shot is a GameEventKind.Beam.</summary>
        public int projectile = -1;
        public float projectileSpeed;
        public bool topOnly, armOnly;
        /// <summary>Burns: sets grass and plants alight where it plays.</summary>
        public bool ignites;
        /// <summary>Pierces: a railgun slug goes through everything in its line.</summary>
        public bool pierces;
        /// <summary>A cone, not a line: the flamer.</summary>
        public float coneDeg;
        /// <summary>On its own turning ring (the Flame Tower): it swings onto its target at
        /// <see cref="turnRate"/> rad/s whichever way the torso faces.</summary>
        public bool turret;
        public float turnRate;
        /// <summary>A flame weapon: the flamer and the Flame Tower.</summary>
        public bool Flame => id == MechWeapon.Flamer || id == MechWeapon.FlameTower;
        /// <summary>What it is for, as the AI and the tooltip describe it.</summary>
        public bool antiLight, antiHeavy, antiGroup;
        /// <summary>How hard it shoves the torso and the ground when it fires, 0..1.</summary>
        public float kick;

        /// <summary>Damage a second against a target of each class, before upgrades.</summary>
        public float Dps(bool heavy) => damage * burst / Mathf.Max(0.05f, cooldown) * (heavy ? vsHeavy : vsLight);
    }

    public sealed class UtilityPart
    {
        public MechUtility id;
        public string name, blurb;
        public int points;
    }

    /// <summary>One drawn Mech: its parts, who pilots it and what it is called.</summary>
    public sealed class MechDesign
    {
        public MechLocomotion locomotion;
        public MechFrame frame;
        public readonly List<MechWeapon> weapons = new List<MechWeapon>(5);
        /// <summary>Mounted by the Extra Hardpoint upgrade, first then second.</summary>
        public readonly List<MechWeapon> reserve = new List<MechWeapon>(2);
        /// <summary>Which of the frame's mounts each weapon (and each reserve weapon)
        /// sits on. A mount the budget could not arm stays empty, so a weapon's index is
        /// not its mount's.</summary>
        public readonly List<int> weaponMounts = new List<int>(5), reserveMounts = new List<int>(2);

        /// <summary>The mount the i-th weapon sits on (the i-th mount if none was recorded).</summary>
        public int MountOf(int i) => i < weaponMounts.Count ? weaponMounts[i] : i;
        public int ReserveMountOf(int i) => i < reserveMounts.Count ? reserveMounts[i] : Frame.hardpoints + i;
        public MechUtility utility;
        /// <summary>Points the parts left unspent, turned into armour.</summary>
        public int spare;
        public string className, pilot, callsign, order;
        public uint seed;

        public LocomotionPart Loco => MechParts.Locomotion(locomotion);
        public FramePart Frame => MechParts.Frame(frame);

        public int PointsSpent
        {
            get
            {
                int p = Loco.points + Frame.points + MechParts.Utility(utility).points;
                foreach (var w in weapons) p += MechParts.Weapon(w).points;
                return p;
            }
        }

        /// <summary>"Warden-class Strider: Autocannon, Missile Rack, Laser Lance; Shield Projector".</summary>
        public string Loadout(int extraMounted = 0)
        {
            var sb = new StringBuilder();
            sb.Append(Frame.name).Append("-class ").Append(Loco.name).Append(": ");
            for (int i = 0; i < weapons.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(MechParts.Weapon(weapons[i]).name);
            }
            for (int i = 0; i < Mathf.Min(extraMounted, reserve.Count); i++)
                sb.Append(", ").Append(MechParts.Weapon(reserve[i]).name).Append('*');
            if (utility != MechUtility.None) sb.Append("; ").Append(MechParts.Utility(utility).name);
            return sb.ToString();
        }

        public override string ToString() => $"{className} \"{callsign}\" ({Loadout()}, {spare} spare)";
    }

    public static class MechParts
    {
        public const int Budget = 100;
        /// <summary>Hit points a point left unspent buys, as a share of the frame's.</summary>
        public const float SparePointHp = 0.018f;

        static readonly LocomotionPart[] locos =
        {
            new LocomotionPart { id = MechLocomotion.Biped, name = "Strider", model = "MECH_LEGS_BIPED", points = 16,
                speed = 5.0f, turnRate = 2.1f, hpMul = 1.00f, armour = 0.00f, waist = 5.05f,
                blurb = "Reverse-jointed legs: quick on its feet, steps over what a track would climb." },
            new LocomotionPart { id = MechLocomotion.Quad, name = "Arachnid", model = "MECH_LEGS_QUAD", points = 22,
                speed = 4.1f, turnRate = 1.8f, hpMul = 1.12f, armour = 0.03f, rangeMul = 1.08f, waist = 3.9f,
                blurb = "Four braced legs: slow, but the steadiest gun platform there is (+8% range)." },
            new LocomotionPart { id = MechLocomotion.Tracks, name = "Juggernaut", model = "MECH_TRACKS", points = 18,
                speed = 4.3f, turnRate = 1.5f, hpMul = 1.15f, armour = 0.04f, waist = 2.75f,
                blurb = "Twin track units under a heavy skirt: the toughest base, and the slowest to turn." },
            new LocomotionPart { id = MechLocomotion.Hover, name = "Wraith", model = "MECH_HOVER", points = 19,
                speed = 6.8f, turnRate = 3.0f, hpMul = 0.92f, armour = -0.03f, waist = 2.7f, skims = true,
                blurb = "A grav skirt: fast, skims over the shallows, lightly armoured." },
        };

        static readonly FramePart[] frames =
        {
            new FramePart { id = MechFrame.Light, name = "Vanguard", model = "MECH_FRAME_LIGHT", points = 14,
                hp = 1400f, armour = 0.08f, radius = 2.2f, hardpoints = 3, height = 2.3f,
                mounts = new[] { "ArmR", "ArmL", "Back", "ShoulderR", "ShoulderL" },
                kinds = new[] { MountKind.Arm, MountKind.Arm, MountKind.Top, MountKind.Top, MountKind.Top },
                blurb = "A light frame: three hardpoints, room left for the rest." },
            new FramePart { id = MechFrame.Medium, name = "Warden", model = "MECH_FRAME_MEDIUM", points = 22,
                hp = 1800f, armour = 0.12f, radius = 2.5f, hardpoints = 4, height = 2.7f,
                mounts = new[] { "ArmR", "ArmL", "ShoulderR", "ShoulderL", "Back", "Chest" },
                kinds = new[] { MountKind.Arm, MountKind.Arm, MountKind.Top, MountKind.Top, MountKind.Top, MountKind.Arm },
                blurb = "The line frame: four hardpoints and a thick hide." },
            new FramePart { id = MechFrame.Heavy, name = "Colossus", model = "MECH_FRAME_HEAVY", points = 30,
                hp = 2250f, armour = 0.16f, radius = 2.8f, hardpoints = 5, height = 3.1f,
                mounts = new[] { "ArmR", "ArmL", "ShoulderR", "ShoulderL", "Back", "Chest", "Crown" },
                kinds = new[] { MountKind.Arm, MountKind.Arm, MountKind.Top, MountKind.Top, MountKind.Top, MountKind.Arm, MountKind.Top },
                blurb = "A siege frame: five hardpoints under the heaviest armour." },
        };

        static readonly WeaponPart[] weapons =
        {
            new WeaponPart { id = MechWeapon.Autocannon, name = "Autocannon", model = "MECH_W_AUTOCANNON", points = 10,
                range = 26f, damage = 14f, cooldown = 1.0f, burst = 3, burstGap = 0.11f, projectile = 4, projectileSpeed = 150f,
                vsLight = 0.4f, vsHeavy = 1.0f, antiLight = true, antiHeavy = true, kick = 0.35f,
                blurb = "Three-round bursts of 40 mm: good against anything." },
            new WeaponPart { id = MechWeapon.Gatling, name = "Rotary Cannon", model = "MECH_W_GATLING", points = 10,
                range = 20f, damage = 4f, cooldown = 0.09f, vsLight = 0.45f, vsHeavy = 0.4f, armOnly = true,
                antiLight = true, kick = 0.08f,
                blurb = "A six-barrelled hose of fire that cuts down infantry." },
            new WeaponPart { id = MechWeapon.Missiles, name = "Missile Rack", model = "MECH_W_MISSILES", points = 12,
                range = 34f, damage = 14f, splash = 2.6f, cooldown = 6.0f, burst = 8, burstGap = 0.13f, projectile = 5,
                projectileSpeed = 36f, vsLight = 0.28f, vsHeavy = 1.0f, topOnly = true, antiGroup = true, antiLight = true, kick = 0.1f,
                blurb = "Salvos of eight homing missiles that come down on a crowd from above." },
            new WeaponPart { id = MechWeapon.Mortar, name = "Siege Mortar", model = "MECH_W_MORTAR", points = 12,
                range = 46f, minRange = 10f, damage = 66f, vsStructure = 1f, splash = 4.5f, cooldown = 5.0f, projectile = 6, projectileSpeed = 30f,
                vsLight = 0.24f, vsHeavy = 1.1f, topOnly = true, antiGroup = true, antiHeavy = true, kick = 0.7f,
                blurb = "Lobs a heavy shell further than anything else in the field. Useless up close." },
            new WeaponPart { id = MechWeapon.Laser, name = "Laser Lance", model = "MECH_W_LASER", points = 13,
                range = 30f, damage = 55f, cooldown = 1.6f, vsLight = 0.28f, vsHeavy = 1.4f, antiHeavy = true, kick = 0.15f,
                blurb = "A searing beam that burns through armour; wasted on infantry." },
            new WeaponPart { id = MechWeapon.Flamer, name = "Inferno Projector", model = "MECH_W_FLAMER", points = 8,
                range = 17f, damage = 4f, cooldown = 0.1f, splash = 3.5f, coneDeg = 16f, vsLight = 0.16f, vsHeavy = 0.35f,
                armOnly = true, ignites = true, antiLight = true, antiGroup = true, kick = 0.02f,
                blurb = "A tongue of burning gel: sweeps infantry away and sets the field alight." },
            new WeaponPart { id = MechWeapon.Railgun, name = "Railgun", model = "MECH_W_RAILGUN", points = 16,
                range = 42f, damage = 150f, cooldown = 6.0f, vsLight = 0.2f, vsHeavy = 1.25f, pierces = true,
                antiHeavy = true, kick = 1f,
                blurb = "A hypersonic slug that punches through up to four targets in its line, weaker with each." },
            new WeaponPart { id = MechWeapon.FlameTower, name = "Flame Tower", model = "MECH_W_FLAMETOWER", points = 10,
                range = 15f, damage = 3.6f, cooldown = 0.1f, splash = 3.5f, coneDeg = 26f, vsLight = 0.22f, vsHeavy = 0.3f,
                topOnly = true, turret = true, turnRate = 3.2f, ignites = true, antiLight = true, antiGroup = true, kick = 0.02f,
                blurb = "A flame turret on its own ring: it turns on its own and burns what comes close, all the way round." },
        };

        static readonly UtilityPart[] utilities =
        {
            new UtilityPart { id = MechUtility.None, name = "none", points = 0, blurb = "" },
            new UtilityPart { id = MechUtility.Shield, name = "Shield Projector", points = 12,
                blurb = "A 600-point energy shield that recharges out of the fight." },
            new UtilityPart { id = MechUtility.Nanites, name = "Repair Nanites", points = 9,
                blurb = "Mends the hull as it fights, faster once it is out of it." },
            new UtilityPart { id = MechUtility.Reactive, name = "Reactive Armour", points = 8,
                blurb = "Plates that blow out against a blast: splash damage cut by 40%." },
            new UtilityPart { id = MechUtility.Sensors, name = "Sensor Mast", points = 6,
                blurb = "+10% range and far wider sight." },
        };

        public static LocomotionPart Locomotion(MechLocomotion id) => locos[(int)id];
        public static FramePart Frame(MechFrame id) => frames[(int)id];
        public static WeaponPart Weapon(MechWeapon id) => weapons[(int)id];
        public static UtilityPart Utility(MechUtility id) => utilities[(int)id];

        // ------------------------------------------------------------ shield and repair
        public const float ShieldCapacity = 600f, ShieldRegen = 45f, ShieldDelay = 5f;
        public const float NaniteRepairCombat = 3f, NaniteRepair = 12f, NaniteDelay = 6f;
        public const float ReactiveSplash = 0.6f, ReactiveDirect = 0.92f;

        // ------------------------------------------------------------ upgrades
        public struct UpgradeInfo
        {
            public string name, blurb;
            public int levels;
            public int[] cost;
            public float[] time;
            public char hotkey;
        }

        static readonly UpgradeInfo[] upgrades =
        {
            new UpgradeInfo { name = "Servo Actuators", levels = 2, cost = new[] { 150, 250 }, time = new[] { 30f, 40f }, hotkey = 'V',
                blurb = "+15% speed and turning a level." },
            new UpgradeInfo { name = "Ablative Armour", levels = 3, cost = new[] { 175, 275, 375 }, time = new[] { 35f, 45f, 55f }, hotkey = 'B',
                blurb = "+10% hull and 4% less damage taken a level." },
            new UpgradeInfo { name = "Weapon Overcharge", levels = 3, cost = new[] { 175, 275, 375 }, time = new[] { 35f, 45f, 55f }, hotkey = 'N',
                blurb = "+10% damage from every weapon a level." },
            new UpgradeInfo { name = "Extra Hardpoint", levels = 2, cost = new[] { 300, 500 }, time = new[] { 45f, 60f }, hotkey = 'M',
                blurb = "Mounts another weapon on an empty or auxiliary hardpoint." },
            new UpgradeInfo { name = "Targeting Uplink", levels = 1, cost = new[] { 200 }, time = new[] { 35f }, hotkey = 'K',
                blurb = "+12% range and +10 m sight for every weapon." },
        };

        public static UpgradeInfo Upgrade(MechUpgrade u) => upgrades[(int)u];

        public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => n.ToString() };

        // ------------------------------------------------------------ the generator
        /// <summary>Draw a design from <paramref name="seed"/>: pick a locomotion and a
        /// frame, arm its hardpoints with what the points will buy, take a utility if
        /// any are left, and turn the rest into armour. Every design spends the same
        /// budget; the draw only changes how it is spent.</summary>
        public static MechDesign Generate(uint seed)
        {
            var r = new Rng(seed == 0 ? 1u : seed);
            var d = new MechDesign { seed = seed };
            d.locomotion = (MechLocomotion)Pick(r, new[] { 1.0f, 0.8f, 0.8f, 0.7f });
            // A heavy frame on the dearest legs leaves too little for guns: weigh it down.
            float heavyW = d.locomotion == MechLocomotion.Quad ? 0.4f : 0.9f;
            d.frame = (MechFrame)Pick(r, new[] { 0.9f, 1.1f, heavyW });
            var frame = d.Frame;
            int left = Budget - d.Loco.points - frame.points;

            // Arm each base mount with a weapon it takes and the points still cover,
            // leaving enough for the mounts still to come (the cheapest weapon each).
            int cheapest = 8;
            for (int m = 0; m < frame.hardpoints; m++)
            {
                var kind = frame.kinds[m];
                int still = frame.hardpoints - m - 1;
                var options = new List<MechWeapon>();
                var weights = new List<float>();
                for (int k = 0; k < (int)MechWeapon.Count; k++)
                {
                    var w = Weapon((MechWeapon)k);
                    if (!Fits(w, kind)) continue;
                    if (w.points > left - still * cheapest) continue;
                    float wt = 1f;
                    // Variety: a second copy of the same gun is less likely than a new one.
                    int copies = 0;
                    foreach (var have in d.weapons) if (have == w.id) copies++;
                    // Two of a kind at most: a tight budget once filled every mount with
                    // the cheapest gun (four autocannons on a Colossus).
                    if (copies >= 2) continue;
                    wt *= copies == 0 ? 1f : 0.3f;
                    // Launchers belong on the shoulders and the back; a gun there is the
                    // exception, not the filler it was (autocannons on 88% of designs).
                    if (kind == MountKind.Top) wt *= w.topOnly ? 2.4f : 0.55f;
                    // A mortar or a flamer alone is a Mech that cannot fight at every range.
                    if (w.id == MechWeapon.Mortar && Has(d, MechWeapon.Mortar)) wt *= 0.1f;
                    // Fire is the exception, not the rule: with the Flame Tower on the top
                    // mounts as well as the flamer on the arms, seven designs in ten burned.
                    if (w.Flame) wt *= 0.55f;
                    // One fire weapon is a Mech that burns; two is a Mech that cannot reach.
                    if (w.Flame && (Has(d, MechWeapon.Flamer) || Has(d, MechWeapon.FlameTower))) wt *= 0.1f;
                    options.Add(w.id);
                    weights.Add(wt);
                }
                // Nothing it can afford that it does not already carry twice: leave the
                // mount empty -- the points go to armour, and the Extra Hardpoint upgrade
                // has a mount to fill.
                if (options.Count == 0) continue;
                var pick = options[Pick(r, weights.ToArray())];
                d.weapons.Add(pick);
                d.weaponMounts.Add(m);
                left -= Weapon(pick).points;
            }
            EnsureAllRange(d, ref left);

            // A utility if the points reach one (and sometimes not even then: a spare
            // point is armour too).
            var uopts = new List<int>();
            var uw = new List<float>();
            for (int k = 1; k < (int)MechUtility.Count; k++)
                if (Utility((MechUtility)k).points <= left) { uopts.Add(k); uw.Add(1f); }
            if (uopts.Count > 0 && r.F01() < 0.85f)
            {
                d.utility = (MechUtility)uopts[Pick(r, uw.ToArray())];
                left -= Utility(d.utility).points;
            }
            d.spare = Mathf.Max(0, left);

            // Reserve weapons for the mounts left empty -- base mounts the budget could not
            // arm first, then the auxiliary ones -- what the design lacks first.
            var free = new List<int>();
            for (int m = 0; m < frame.mounts.Length; m++) if (!d.weaponMounts.Contains(m)) free.Add(m);
            foreach (int m in free)
            {
                if (d.reserve.Count >= 2) break;
                var kind = frame.kinds[m];
                bool needLight = !Covers(d, true), needHeavy = !Covers(d, false);
                var options = new List<MechWeapon>();
                var weights = new List<float>();
                for (int k = 0; k < (int)MechWeapon.Count; k++)
                {
                    var w = Weapon((MechWeapon)k);
                    if (!Fits(w, kind)) continue;
                    float wt = 1f;
                    if (needLight && w.antiLight) wt *= 3f;
                    if (needHeavy && w.antiHeavy) wt *= 3f;
                    if (Has(d, w.id) || d.reserve.Contains(w.id)) wt *= 0.35f;
                    options.Add(w.id);
                    weights.Add(wt);
                }
                if (options.Count > 0) { d.reserve.Add(options[Pick(r, weights.ToArray())]); d.reserveMounts.Add(m); }
            }

            Name(d, r);
            return d;
        }

        static bool Fits(WeaponPart w, MountKind kind) =>
            kind == MountKind.Arm ? !w.topOnly : !w.armOnly;

        static bool Has(MechDesign d, MechWeapon w) => d.weapons.Contains(w);

        /// <summary>Whether something it carries is good against light (or heavy) targets.</summary>
        public static bool Covers(MechDesign d, bool light)
        {
            foreach (var w in d.weapons)
            {
                var p = Weapon(w);
                if (light ? p.antiLight : p.antiHeavy) return true;
            }
            return false;
        }

        /// <summary>A Mech that can only shoot far (a mortar) or only near (a flamer)
        /// cannot hold its own: swap its last such weapon for an autocannon.</summary>
        static void EnsureAllRange(MechDesign d, ref int left)
        {
            bool mid = false;
            foreach (var w in d.weapons)
                if (w != MechWeapon.Mortar && !Weapon(w).Flame) { mid = true; break; }
            if (mid || d.weapons.Count == 0) return;
            for (int i = d.weapons.Count - 1; i >= 0; i--)
            {
                var kind = d.Frame.kinds[d.MountOf(i)];
                var ac = Weapon(MechWeapon.Autocannon);
                if (!Fits(ac, kind)) continue;
                left += Weapon(d.weapons[i]).points - ac.points;
                d.weapons[i] = MechWeapon.Autocannon;
                return;
            }
        }

        static int Pick(Rng r, float[] weights)
        {
            float sum = 0f;
            foreach (var w in weights) sum += w;
            float x = r.F01() * sum;
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x <= 0f) return i;
            }
            return weights.Length - 1;
        }

        // ------------------------------------------------------------ names
        // The pilots are a knightly order sworn to whichever side raises a Mech Bay:
        // allies, not soldiers, who fight for the same end in their own way.
        static readonly string[] Titles = { "Castellan", "Knight-Warden", "Paladin", "Knight-Errant", "Seneschal", "Marshal", "Vigilant" };
        static readonly string[] Given = { "Aurek", "Maren", "Tavish", "Ilse", "Corvan", "Brannoc", "Sefa", "Oriel", "Dagny", "Varro", "Hale", "Isolde", "Kestrel", "Anselm", "Rhoswen", "Teodor" };
        static readonly string[] Family = { "Voss", "Ardent", "Kade", "Morrow", "Thane", "Galloway", "Reyne", "Ashcombe", "Stroud", "Vale", "Draycott", "Halloran", "Quill", "Serrin" };
        static readonly string[] Calls = { "IRONCLAD", "BASTION", "HALBERD", "WARBELL", "EMBERFALL", "GRAVEN", "SUNDER", "TEMPEST", "OBELISK", "PALISADE", "HARROW", "CINDER", "ANVIL", "BULWARK", "LODESTAR", "RAMPART" };
        static readonly string[] Orders = { "the Ashen Covenant", "the Order of the Iron Lantern", "the Ninth Vigil", "the Sable Oath", "the Order of the Broken Crown", "the Emberguard", "the Wardens of the Long Night", "the Brazen Host" };

        static void Name(MechDesign d, Rng r)
        {
            d.className = $"{d.Frame.name}-class {d.Loco.name}";
            d.pilot = $"{Titles[r.IRange(0, Titles.Length)]} {Given[r.IRange(0, Given.Length)]} {Family[r.IRange(0, Family.Length)]}";
            d.callsign = Calls[r.IRange(0, Calls.Length)];
            d.order = Orders[r.IRange(0, Orders.Length)];
        }
    }
}
