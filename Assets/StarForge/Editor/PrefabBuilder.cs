// PrefabBuilder.cs — unit definitions (ScriptableObjects), per-team prefabs and
// command-card icons, all generated from the Blender models.
//
// Definitions are only filled in when first created: once an asset exists its
// numbers belong to whoever tunes them in the Inspector, and rebuilding keeps
// them. Prefabs and icons are always regenerated.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class PrefabBuilder
    {
        public const string PrefabDir = MapBuilder.PrefabDir;
        public const string DefsDir = "Assets/StarForge/Data/Units";
        public const string IconDir = "Assets/StarForge/Art/Icons";
        public const string CatalogPath = "Assets/StarForge/Resources/UnitCatalog.asset";

        [MenuItem("StarForge/Build/2 Unit Definitions, Prefabs & Icons", priority = 2)]
        public static void Build()
        {
            SFEditorUtil.EnsureFolder(PrefabDir);
            SFEditorUtil.EnsureFolder(DefsDir);
            SFEditorUtil.EnsureFolder(IconDir);
            SFEditorUtil.EnsureFolder("Assets/StarForge/Resources");
            // The Mech's parts first: the Mech prefab and its icon are made from them.
            MechKitBuilder.Build();
            ModelFactory.ResetMeta();
            created.Clear();

            var defs = new List<UnitDef>
            {
                //   type               name        bld    neu    rad    hp   spd   turn  rng  dmg  cd     spl   sight bt  cost sup give key
                Def(UnitType.Worker,   "Digger",   false, false, 0.70f,  60, 6.6f, 7.0f, 2f,  5,  1.00f, 0f,   22, 12,  50, 1, 0, 'U',
                    "Harvests ore and constructs structures."),
                Def(UnitType.Trooper,  "Trooper",  false, false, 0.58f,  55, 6.0f, 9.0f, 16f, 7,  0.70f, 0f,   30, 14,  50, 1, 0, 'T',
                    "Cheap ranged infantry. Strong in numbers."),
                Def(UnitType.Mauler,   "Mauler",   false, false, 1.50f, 180, 4.1f, 2.6f, 24f, 32, 2.20f, 4.5f, 34, 30, 150, 3, 0, 'M',
                    "Heavy tank. Long-range splash artillery that outranges Sentinels."),
                Def(UnitType.Skimmer,  "Skimmer",  false, false, 0.85f,  75, 11f,  8.0f, 13f, 6,  0.50f, 0f,   38, 16,  75, 2, 0, 'K',
                    "Fast hover raider with wide sensors. Double damage against Diggers."),
                Def(UnitType.Foundry,  "Foundry",  true,  false, 5.0f, 1500, 0, 0, 0, 0, 0, 0, 32, 55, 400, 0, 10, 'F',
                    "Command hub. Trains Diggers, accepts ore, +10 supply."),
                Def(UnitType.Garrison, "Garrison", true,  false, 4.3f, 1000, 0, 0, 0, 0, 0, 0, 26, 30, 150, 0, 0, 'G',
                    "Trains Troopers and Skimmers."),
                Def(UnitType.Workshop, "Workshop", true,  false, 5.0f, 1250, 0, 0, 0, 0, 0, 0, 26, 40, 200, 0, 0, 'V',
                    "Heavy fabricator. Trains Maulers. Requires a Garrison."),
                Def(UnitType.Bunkhouse,"Bunkhouse",true,  false, 2.4f,  400, 0, 0, 0, 0, 0, 0, 20, 20, 100, 0, 8, 'B',
                    "Crew quarters. +8 supply."),
                Def(UnitType.Sentinel, "Sentinel", true,  false, 1.7f,  500, 0, 3.5f, 20f, 13, 0.85f, 0f, 28, 25, 125, 0, 0, 'N',
                    "Defensive gun turret. Requires a Garrison."),
                Def(UnitType.Ore,      "Ore Seam", false, true,  1.6f, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ' ', "Crystal ore. Diggers carry 8 per trip."),
                Def(UnitType.Boulder,  "Boulder",  false, true,  1.1f, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ' ', ""),
                Def(UnitType.MechBay,  "Mech Bay", true,  false, 4.2f, 1800, 0, 3.0f, 24f, 22, 0.60f, 0f, 30, 35, 100, 0, 0, 'Y',
                    "Calls down your Mech two minutes after it stands: a war machine with a mind of its own, allied to you " +
                    "but not under your orders. One a match, never rebuilt. Guards itself with a gun tower, mends itself, " +
                    "repairs the Mech and sells its upgrades."),
                // The Mech's real numbers come from its parts (World/MechParts); these are
                // what the rest of the game reads before one exists.
                Def(UnitType.Mech,     "Mech",     false, false, 2.5f, 3700, 5.0f, 2.1f, 30f, 1, 1.00f, 0f, 36, 0, 0, 0, 0, ' ',
                    "An autonomous war machine from the Mech Bay. Fights on your side, by its own judgement."),

                // ---- the wider roster (README "Units"); numbers are the starting sheet, tuned in the assets
                //   type                 name          bld    neu    rad    hp   spd    turn  rng  dmg  cd     spl   sight bt  cost sup give key
                Def(UnitType.Thumper,   "Thumper",    false, false, 0.60f,  70, 5.2f,  8.0f, 26f, 24, 2.80f, 2.6f, 28, 20, 100, 2, 0, 'M',
                    "Mortar infantry. Lobs shells over cover at long range; cannot fire at anything within 7 m, nor at aircraft. Requires an Armory."),
                Def(UnitType.Scorcher,  "Scorcher",   false, false, 0.62f,  95, 5.6f,  9.0f,  6f,  5, 0.30f, 1.8f, 22, 16,  75, 1, 0, 'F',
                    "Flamethrower infantry. Short reach, burns everything on the ground in its jet; deadly to infantry and Diggers, weak against armour. Sets grass alight."),
                Def(UnitType.Marksman,  "Marksman",   false, false, 0.56f,  55, 5.6f,  9.0f, 30f, 58, 3.20f, 0f,   34, 22, 125, 2, 0, 'N',
                    "Sniper. Outranges everything but the Mech; one shot fells a rifleman. Fragile, slow to fire, poor against armour. Requires an Armory."),
                Def(UnitType.Lancer,    "Lancer",     false, false, 1.40f, 280, 5.2f,  2.8f, 18f, 36, 1.70f, 0f,   30, 28, 175, 3, 0, 'L',
                    "Tank destroyer. A long, fast-firing cannon that punches through armour; wasteful against infantry."),
                Def(UnitType.Aegis,     "Aegis",      false, false, 1.35f, 200, 4.8f,  2.8f, 21f,  9, 0.40f, 1.6f, 32, 24, 150, 3, 0, 'F',
                    "Anti-aircraft tank. Twin flak cannons that shred aircraft; light work against ground targets."),
                Def(UnitType.Kestrel,   "Kestrel",    false, false, 1.20f, 150, 8.5f,  3.5f, 12f, 18, 1.10f, 1.6f, 32, 26, 175, 3, 0, 'G',
                    "Gunship. Flies over cliffs, water and woods and rockets what is on the ground; cannot fire at aircraft."),
                Def(UnitType.Swift,     "Swift",      false, false, 1.00f, 110, 12.5f, 4.5f, 14f, 12, 0.60f, 0f,   34, 22, 125, 2, 0, 'F',
                    "Interceptor. The fastest thing in the sky, made to hunt aircraft; strafes the ground for little."),
                Def(UnitType.Hauler,    "Hauler",     false, false, 1.25f, 280, 4.6f,  3.0f,  0f,  0, 0f,    0f,   20, 24, 125, 1, 0, 'J',
                    "Armoured ore carrier. Slow and costly, but brings in 20 ore a trip and shrugs off raids. Cannot build. Requires a Workshop."),
                Def(UnitType.Prospector,"Prospector", false, false, 0.65f, 100, 6.0f,  8.0f, 15f,  8, 0.75f, 0f,   26, 18, 100, 2, 0, 'K',
                    "Armed miner. Mines (5 ore a trip) and fights back at its work; a fair soldier, a poor miner. Cannot build. Requires a Garrison."),
                Def(UnitType.Skitter,   "Skitter",    false, false, 0.50f,  45, 8.6f, 12.0f, 1.8f, 6, 0.45f, 0f,   22,  9,  35, 1, 0, 'Z',
                    "Bred swarmer. Cheap, very fast, bites what it reaches; strong in a pack, nothing alone. Cannot reach aircraft."),
                Def(UnitType.Spitter,   "Spitter",    false, false, 0.75f, 100, 5.4f,  8.0f, 15f, 12, 0.90f, 0f,   28, 18,  90, 2, 0, 'T',
                    "Bred acid-spitter. Hops into range and spits acid at ground and air alike."),
                Def(UnitType.Razorwing, "Razorwing",  false, false, 0.90f, 130, 10.5f, 5.0f,  8f, 12, 1.00f, 0f,   30, 24, 150, 2, 0, 'V',
                    "Bred flyer. A fast raider that stings what it hunts; hits ground and air."),
                Def(UnitType.Rocketeer, "Rocketeer",  false, false, 0.62f,  90, 5.4f,  8.0f, 19f, 34, 1.90f, 0.8f, 26, 18,  90, 2, 0, 'B',
                    "Anti-armour infantry. A shoulder rocket that wrecks tanks, Mechs and buildings; poor against infantry, cannot fire at aircraft."),
                Def(UnitType.Ironclad,  "Ironclad",   false, false, 1.45f, 480, 3.8f,  2.4f, 20f, 24, 1.30f, 0f,   32, 45, 300, 6, 0, 'T',
                    "Walking mini-mech. Twin shoulder cannons for the ground, missile pods for the air; slow and very hard to stop. Requires an Armory."),
                Def(UnitType.Gorehorn,  "Gorehorn",   false, false, 1.40f, 520, 5.0f,  4.0f, 2.2f, 32, 1.30f, 2.4f, 24, 32, 275, 5, 0, 'G',
                    "Bred armoured beast. Charges in and sweeps its tusks through everything round what it strikes; cannot reach aircraft. Requires an Armory."),
                Def(UnitType.Armory,    "Armory",     true,  false, 3.6f,  900, 0, 0, 0, 0, 0, 0, 24, 30, 150, 0, 0, 'U',
                    "Upgrade works: weapons and armour for infantry, vehicles and creatures, two levels each. Unlocks the Thumper, the Marksman, the Ironclad and the Gorehorn. Requires a Garrison."),
                Def(UnitType.Airfield,  "Airfield",   true,  false, 5.0f, 1100, 0, 0, 0, 0, 0, 0, 28, 40, 200, 0, 0, 'L',
                    "Builds the Kestrel gunship and the Swift interceptor. Requires a Workshop."),
                Def(UnitType.Biolab,    "Biolab",     true,  false, 4.2f, 1000, 0, 0, 0, 0, 0, 0, 26, 35, 175, 0, 0, 'O',
                    "Breeds Skitters, Spitters, Razorwings and Gorehorns. Requires a Garrison."),
            };
            Find(defs, UnitType.Worker).producer = UnitType.Foundry;
            Find(defs, UnitType.Trooper).producer = UnitType.Garrison;
            Find(defs, UnitType.Skimmer).producer = UnitType.Garrison;
            Find(defs, UnitType.Mauler).producer = UnitType.Workshop;
            Find(defs, UnitType.Workshop).requires = UnitType.Garrison;
            Find(defs, UnitType.Sentinel).requires = UnitType.Garrison;
            Find(defs, UnitType.Skimmer).bonusVsWorkers = 2f;
            Roles(defs);
            Tune(defs);

            foreach (var d in defs)
            {
                BuildPrefabs(d);
                EditorUtility.SetDirty(d);
            }

            var catalog = SFEditorUtil.CreateOrLoadAsset<UnitCatalog>(CatalogPath);
            catalog.defs = defs.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Defs.Bind(catalog);

            RenderIcons(defs);
            AssetDatabase.SaveAssets();
            Debug.Log($"[StarForge] {defs.Count} unit definitions, prefabs and icons built");
        }

        static UnitDef Find(List<UnitDef> l, UnitType t) => l.Find(d => d.type == t);

        /// <summary>Definitions created by this run (Def), the only ones Tune writes numbers into.</summary>
        static readonly HashSet<UnitType> created = new HashSet<UnitType>();

        /// <summary>What each unit is -- which building trains it and what it needs, its gun's kind
        /// and what it can shoot at, worker, builder, aircraft, creature, armour and upgrade
        /// class. Set on every build (like producer/requires): these are rules, not tuning, and
        /// the original units' assets predate the fields.</summary>
        static void Roles(List<UnitDef> defs)
        {
            void R(UnitType t, System.Action<UnitDef> set) { var d = Find(defs, t); if (d != null) set(d); }
            R(UnitType.Worker,   d => { d.harvester = true; d.builder = true; d.upgradeClass = UnitClass.Vehicle; d.hitsAir = false; d.weapon = WeaponKind.Auto; });
            R(UnitType.Trooper,  d => { d.upgradeClass = UnitClass.Infantry; d.hitsAir = true; d.weapon = WeaponKind.Auto; });
            R(UnitType.Mauler,   d => { d.upgradeClass = UnitClass.Vehicle; d.heavyArmour = true; d.hitsAir = false; d.weapon = WeaponKind.Auto; });
            R(UnitType.Skimmer,  d => { d.upgradeClass = UnitClass.Vehicle; d.hitsAir = false; d.weapon = WeaponKind.Auto; });
            R(UnitType.Sentinel, d => { d.hitsAir = true; d.weapon = WeaponKind.Auto; });
            R(UnitType.MechBay,  d => { d.hitsAir = true; d.weapon = WeaponKind.Auto; });
            R(UnitType.Mech,     d => { d.hitsAir = true; d.weapon = WeaponKind.Auto; });

            R(UnitType.Thumper,   d => { d.producer = UnitType.Garrison; d.requires = UnitType.Armory; d.weapon = WeaponKind.Mortar; d.hitsAir = false; d.upgradeClass = UnitClass.Infantry; });
            R(UnitType.Scorcher,  d => { d.producer = UnitType.Garrison; d.weapon = WeaponKind.Flamer; d.hitsAir = false; d.upgradeClass = UnitClass.Infantry; });
            R(UnitType.Marksman,  d => { d.producer = UnitType.Garrison; d.requires = UnitType.Armory; d.weapon = WeaponKind.Sniper; d.hitsAir = true; d.upgradeClass = UnitClass.Infantry; });
            R(UnitType.Lancer,    d => { d.producer = UnitType.Workshop; d.weapon = WeaponKind.Cannon; d.hitsAir = false; d.heavyArmour = true; d.upgradeClass = UnitClass.Vehicle; });
            R(UnitType.Aegis,     d => { d.producer = UnitType.Workshop; d.weapon = WeaponKind.Flak; d.hitsAir = true; d.heavyArmour = true; d.upgradeClass = UnitClass.Vehicle; });
            R(UnitType.Kestrel,   d => { d.producer = UnitType.Airfield; d.weapon = WeaponKind.Rockets; d.hitsAir = false; d.flying = true; d.upgradeClass = UnitClass.Vehicle; });
            R(UnitType.Swift,     d => { d.producer = UnitType.Airfield; d.weapon = WeaponKind.Needle; d.hitsAir = true; d.flying = true; d.upgradeClass = UnitClass.Vehicle; });
            R(UnitType.Hauler,    d => { d.producer = UnitType.Foundry; d.requires = UnitType.Workshop; d.harvester = true; d.heavyArmour = true; d.hitsAir = false; d.upgradeClass = UnitClass.Vehicle; });
            R(UnitType.Prospector,d => { d.producer = UnitType.Foundry; d.requires = UnitType.Garrison; d.harvester = true; d.weapon = WeaponKind.Rifle; d.hitsAir = true; d.upgradeClass = UnitClass.Infantry; });
            R(UnitType.Skitter,   d => { d.producer = UnitType.Biolab; d.weapon = WeaponKind.Claws; d.hitsAir = false; d.organic = true; d.upgradeClass = UnitClass.Creature; });
            R(UnitType.Spitter,   d => { d.producer = UnitType.Biolab; d.weapon = WeaponKind.Acid; d.hitsAir = true; d.organic = true; d.upgradeClass = UnitClass.Creature; });
            R(UnitType.Razorwing, d => { d.producer = UnitType.Biolab; d.weapon = WeaponKind.Glaive; d.hitsAir = true; d.flying = true; d.organic = true; d.upgradeClass = UnitClass.Creature; });
            R(UnitType.Rocketeer, d => { d.producer = UnitType.Garrison; d.weapon = WeaponKind.Bazooka; d.hitsAir = false; d.upgradeClass = UnitClass.Infantry; });
            R(UnitType.Ironclad,  d => { d.producer = UnitType.Workshop; d.requires = UnitType.Armory; d.weapon = WeaponKind.Battery; d.hitsAir = true; d.heavyArmour = true; d.upgradeClass = UnitClass.Vehicle; });
            R(UnitType.Gorehorn,  d => { d.producer = UnitType.Biolab; d.requires = UnitType.Armory; d.weapon = WeaponKind.Gore; d.hitsAir = false; d.organic = true; d.heavyArmour = true; d.upgradeClass = UnitClass.Creature; });
            R(UnitType.Armory,    d => { d.requires = UnitType.Garrison; });
            R(UnitType.Airfield,  d => { d.requires = UnitType.Workshop; });
            R(UnitType.Biolab,    d => { d.requires = UnitType.Garrison; });
        }

        /// <summary>The wider roster's numbers beyond Def's columns, written only into an asset this
        /// run created: once it exists they belong to whoever tunes them in the Inspector.</summary>
        static void Tune(List<UnitDef> defs)
        {
            void T(UnitType t, System.Action<UnitDef> set) { var d = Find(defs, t); if (d != null && created.Contains(t)) set(d); }
            T(UnitType.Thumper,   d => { d.minRange = 7f; d.vsHeavy = 0.6f; d.vsStructure = 1.25f; });
            T(UnitType.Scorcher,  d => { d.vsHeavy = 0.5f; d.vsStructure = 0.8f; d.bonusVsWorkers = 1.5f; d.ignite = 0.25f; });
            T(UnitType.Marksman,  d => { d.vsHeavy = 0.5f; d.vsStructure = 0.25f; });
            T(UnitType.Lancer,    d => { d.vsLight = 0.7f; d.vsHeavy = 1.5f; });
            T(UnitType.Aegis,     d => { d.vsAir = 2.2f; d.vsLight = 0.6f; d.vsHeavy = 0.4f; d.vsStructure = 0.3f; });
            T(UnitType.Kestrel,   d => { d.altitude = 7.5f; d.vsHeavy = 1.15f; });
            T(UnitType.Swift,     d => { d.altitude = 9f; d.vsAir = 2.0f; d.vsLight = 0.35f; d.vsHeavy = 0.25f; d.vsStructure = 0.2f; });
            T(UnitType.Hauler,    d => { d.carry = 20; d.harvestTime = 2.6f; });
            T(UnitType.Prospector,d => { d.carry = 5; d.harvestTime = 1.9f; });
            T(UnitType.Spitter,   d => { d.vsAir = 1.25f; });
            T(UnitType.Razorwing, d => { d.altitude = 5.5f; d.bonusVsWorkers = 1.5f; });
            T(UnitType.Rocketeer, d => { d.vsLight = 0.45f; d.vsHeavy = 1.6f; d.vsStructure = 1.1f; });
            T(UnitType.Ironclad,  d => { d.vsAir = 1.6f; d.vsLight = 0.7f; d.vsHeavy = 1.2f; });
            T(UnitType.Gorehorn,  d => { d.vsStructure = 0.8f; });
        }

        /// <summary>Where the bay's gun tower head sits (Tools/blender/build_mechs.py writes
        /// it into models.json as MECH_BAY's _mount).</summary>
        static Vector3 MechBayTowerMount() =>
            MechKitBuilder.TryPoint("MECH_BAY", "GunMount", out var at) ? at : new Vector3(-2.55f, 4.78f, -2.35f);

        static UnitDef Def(UnitType t, string name, bool bld, bool neu, float rad, float hp, float spd, float turn,
                           float rng, float dmg, float cd, float spl, float sight, float bt, int cost, int sup, int give,
                           char key, string blurb)
        {
            string path = $"{DefsDir}/{t}.asset";
            var d = AssetDatabase.LoadAssetAtPath<UnitDef>(path);
            if (d != null) return d;
            d = ScriptableObject.CreateInstance<UnitDef>();
            d.type = t; d.displayName = name; d.building = bld; d.neutral = neu; d.radius = rad; d.hp = hp;
            d.speed = spd; d.turnRate = turn; d.range = rng; d.damage = dmg; d.cooldown = cd; d.splash = spl;
            d.sight = sight; d.buildTime = bt; d.cost = cost; d.supplyCost = sup; d.supplyGive = give;
            d.hotkey = key; d.blurb = blurb;
            AssetDatabase.CreateAsset(d, path);
            created.Add(t);
            return d;
        }

        // ------------------------------------------------------------ prefabs
        static string ModelFor(UnitType t)
        {
            switch (t)
            {
                case UnitType.Worker: return "WORKER";
                case UnitType.Trooper: return "TROOPER";
                case UnitType.Mauler: return "MAULER_HULL";
                case UnitType.Skimmer: return "SKIMMER";
                case UnitType.Foundry: return "FOUNDRY";
                case UnitType.Garrison: return "GARRISON";
                case UnitType.Workshop: return "WORKSHOP";
                case UnitType.Bunkhouse: return "BUNKHOUSE";
                case UnitType.Sentinel: return "SENTINEL_BASE";
                case UnitType.Ore: return "ORE";
                case UnitType.MechBay: return "MECH_BAY";
                case UnitType.Mech: return null;          // assembled from the Mech kit when it drops
                case UnitType.Thumper: return "THUMPER";
                case UnitType.Scorcher: return "SCORCHER";
                case UnitType.Marksman: return "MARKSMAN";
                case UnitType.Prospector: return "PROSPECTOR";
                case UnitType.Lancer: return "LANCER_HULL";
                case UnitType.Aegis: return "AEGIS_HULL";
                case UnitType.Hauler: return "HAULER";
                case UnitType.Kestrel: return "KESTREL";
                case UnitType.Swift: return "SWIFT";
                case UnitType.Armory: return "ARMORY";
                case UnitType.Airfield: return "AIRFIELD";
                case UnitType.Biolab: return "BIOLAB";
                case UnitType.Rocketeer: return "ROCKETEER";
                case UnitType.Ironclad: return "IRONCLAD_LEGS";
                // Animals from the Biolab: Quaternius models (CreatureBody), not a Blender build.
                case UnitType.Skitter: case UnitType.Spitter: case UnitType.Razorwing: case UnitType.Gorehorn: return null;
                default: return "SCAN_BOULDER_A";   // MapBuilder makes the other scanned variants
            }
        }

        static void BuildPrefabs(UnitDef d)
        {
            string model = ModelFor(d.type);
            bool hasModel = model != null && ModelFactory.LoadSource(model) != null;
            var meta = hasModel ? ModelFactory.Meta(model) : new ModelFactory.ModelMeta { radius = d.radius * 1.2f, height = 5f };
            var creature = Creature(d.type);
            if (creature.file != null) meta = new ModelFactory.ModelMeta { radius = creature.length * 0.5f, height = creature.height };
            d.visualRadius = Mathf.Min(meta.radius, d.radius * 1.6f);
            d.visualHeight = meta.height;
            if (d.type == UnitType.Mech) { d.visualRadius = 3.6f; d.visualHeight = 9.0f; }
            if (d.type == UnitType.MechBay) d.visualHeight = Mathf.Max(d.visualHeight, 6.8f);
            if (d.type == UnitType.Mauler) d.visualHeight = 1.40f + ModelFactory.Meta("MAULER_TURRET").height;
            if (d.type == UnitType.Sentinel) d.visualHeight = 1.40f + ModelFactory.Meta("SENTINEL_HEAD").height;
            if (d.type == UnitType.Skimmer) d.visualHeight = meta.height + 0.35f;
            if (d.type == UnitType.Lancer) d.visualHeight = LancerTurretY + ModelFactory.Meta("LANCER_TURRET").height;
            if (d.type == UnitType.Aegis) d.visualHeight = AegisTurretY + ModelFactory.Meta("AEGIS_TURRET").height;
            if (d.type == UnitType.Ironclad) d.visualHeight = IroncladTorsoY + ModelFactory.Meta("IRONCLAD_TORSO").height;
            // Health bars and markers ride with the model, up in the air for an aircraft.
            if (d.flying) d.visualHeight = meta.height + d.altitude;

            int teams = d.neutral ? 1 : 2;
            if (d.prefabs == null || d.prefabs.Length != 2) d.prefabs = new GameObject[2];
            for (int team = 0; team < teams; team++)
            {
                string file = d.neutral ? d.type.ToString() : $"{d.type}_P{team}";
                var root = new GameObject(d.displayName);
                var body = new GameObject("Body").transform;
                body.SetParent(root.transform, false);
                if (hasModel) ModelFactory.Create(model, team, body);
                if (creature.file != null) CreatureBody(creature, team, body);

                Transform turret = null;
                if (d.type == UnitType.Mauler || d.type == UnitType.Sentinel)
                {
                    turret = new GameObject("Turret").transform;
                    turret.SetParent(body, false);
                    turret.localPosition = new Vector3(0f, 1.40f, 0f);
                    ModelFactory.Create(d.type == UnitType.Mauler ? "MAULER_TURRET" : "SENTINEL_HEAD", team, turret);
                }
                string top = d.type == UnitType.Lancer ? "LANCER_TURRET" : d.type == UnitType.Aegis ? "AEGIS_TURRET" : d.type == UnitType.Ironclad ? "IRONCLAD_TORSO" : null;
                if (top != null && ModelFactory.LoadSource(top) != null)
                {
                    turret = new GameObject("Turret").transform;
                    turret.SetParent(body, false);
                    turret.localPosition = new Vector3(0f, d.type == UnitType.Lancer ? LancerTurretY : d.type == UnitType.Aegis ? AegisTurretY : IroncladTorsoY, 0f);
                    ModelFactory.Create(top, team, turret);
                }
                if (d.type == UnitType.MechBay)
                {
                    turret = new GameObject("Turret").transform;
                    turret.SetParent(body, false);
                    turret.localPosition = MechBayTowerMount();
                    if (ModelFactory.LoadSource("MECH_BAY_GUN") != null) ModelFactory.Create("MECH_BAY_GUN", team, turret);
                }

                if (d.type == UnitType.Boulder)
                    root.AddComponent<Boulder>();   // Maulers crush it (GameWorld.CrushBoulders)
                else
                {
                    var unit = root.AddComponent<Unit>();
                    unit.def = d;
                    unit.team = d.neutral ? 2 : team;
                    if (d.type == UnitType.Mech)
                    {
                        // Assembled from its parts when it drops (MechView), and animated,
                        // dressed and wrecked by it; UnitView has no part in it.
                        root.AddComponent<MechCore>();
                        var mv = root.AddComponent<MechView>();
                        mv.body = body;
                    }
                    else
                    {
                        var view = root.AddComponent<UnitView>();
                        view.body = body;
                        view.turret = turret;
                        view.hoverHeight = d.type == UnitType.Skimmer ? 0.35f : d.flying ? d.altitude : 0f;
                        if (d.building && hasModel) view.debris = BuildDebrisPrefab(d, team);
                    }
                }

                if (d.Walks)
                {
                    var agent = root.AddComponent<NavMeshAgent>();
                    agent.radius = d.radius;
                    agent.height = 2f;
                    agent.speed = d.speed;
                    agent.angularSpeed = 0f;
                    agent.updateRotation = false;
                    if (d.type == UnitType.Mech) agent.height = 7f;
                }
                else if (d.building || d.type == UnitType.Ore)
                {
                    var obs = root.AddComponent<NavMeshObstacle>();
                    obs.shape = NavMeshObstacleShape.Capsule;
                    obs.radius = d.radius * 0.92f;
                    obs.height = 4f;
                    obs.center = new Vector3(0f, 2f, 0f);
                    obs.carving = true;
                }

                string path = $"{PrefabDir}/{file}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                d.prefabs[team] = prefab;
            }
            if (teams == 1) d.prefabs[1] = d.prefabs[0];
        }

        // Where the new tanks' turrets sit on their hulls (Tools/blender/build_roster.py).
        const float LancerTurretY = 1.18f, AegisTurretY = 1.30f, IroncladTorsoY = 2.30f;

        /// <summary>A Biolab creature's model: the Quaternius file (Art/Fauna/, imported with its
        /// legacy clips), its length in metres, how tall it stands, which way its head runs in
        /// the file, and its parts' colours largest-first (the team's in its markings).</summary>
        struct CreatureModel { public string file; public float length, height; public Vector3 facing; public bool span; }

        static CreatureModel Creature(UnitType t)
        {
            switch (t)
            {
                // Facings measured from the eyes' (the stinger's) place in each imported mesh.
                case UnitType.Skitter: return new CreatureModel { file = "Creature_Skitter", length = 1.7f, height = 1.1f, facing = Vector3.back };
                case UnitType.Spitter: return new CreatureModel { file = "Creature_Spitter", length = 2.1f, height = 1.6f, facing = Vector3.back };
                case UnitType.Razorwing: return new CreatureModel { file = "Creature_Razorwing", length = 2.4f, height = 1.8f, facing = Vector3.right };
                case UnitType.Gorehorn: return new CreatureModel { file = "Creature_Gorehorn", length = 4.2f, height = 3.0f, facing = Vector3.back };
                default: return default;
            }
        }

        static Material CreatureMat(string name, Color c, float smooth, Color emission = default)
        {
            var m = SFEditorUtil.CreateOrLoadMaterial($"{MapBuilder.MapDir}/Creature_{name}.mat", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            if (emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                // An emissive GI flag, not None: URP re-derives _EMISSION from AnyEmissive whenever it
                // revalidates the material (BaseShaderGUI.SetMaterialKeywords), and None switched it off.
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                // Kept black and flagged so, or a material that stopped glowing keeps its old flag and URP turns it back on.
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The creature's parts in the file's material order, coloured for the team: a dark carapace with the
        /// team's hue in its markings and eyes that glow the team's colour, so a swarm reads as
        /// whose it is at a glance (the animals' own materials are plain grey, as the fauna's).</summary>
        static Dictionary<string, Material> CreatureParts(UnitType t, int team)
        {
            Color glow = team == 0 ? new Color(0.15f, 0.55f, 1f) * 2.4f : new Color(1f, 0.25f, 0.12f) * 2.4f;
            Color mark = team == 0 ? new Color(0.16f, 0.42f, 0.78f) : new Color(0.80f, 0.28f, 0.10f);
            string T = "T" + team;
            // The monster parts every creature carries (Tools/blender/build_creatures.py): bone-pale
            // horn, a carapace darkened toward the side's colour, and the side's colour glowing in its
            // eyes, sacs and glands. Keyed by the file's material names.
            var map = new Dictionary<string, Material>
            {
                ["Horn"] = CreatureMat("Horn", new Color(0.70f, 0.64f, 0.52f), 0.45f),
                ["Plate"] = CreatureMat("Plate_" + T, team == 0 ? new Color(0.08f, 0.10f, 0.20f) : new Color(0.20f, 0.08f, 0.07f), 0.55f),
                ["Glow"] = CreatureMat("Glow_" + T, mark * 0.5f, 0.7f, glow),
            };
            var eye = CreatureMat("Eye_" + T, mark, 0.6f, glow);
            Material Skin(string name, Color c, float smooth = 0.35f) => CreatureMat(name + "_" + T, c, smooth);
            switch (t)
            {
                case UnitType.Skitter:
                    map["Material"] = Skin("Skitter", team == 0 ? new Color(0.05f, 0.06f, 0.10f) : new Color(0.10f, 0.05f, 0.05f));
                    map["Material.001"] = eye;
                    break;
                case UnitType.Spitter:
                    map["Green"] = Skin("Spitter", team == 0 ? new Color(0.10f, 0.16f, 0.17f) : new Color(0.20f, 0.13f, 0.09f), 0.45f);
                    map["Yellow"] = Skin("Belly", Color.Lerp(new Color(0.40f, 0.36f, 0.28f), mark, 0.3f));
                    map["Red"] = eye;
                    map["Black"] = CreatureMat("Pupil", new Color(0.02f, 0.02f, 0.02f), 0.7f);
                    break;
                case UnitType.Razorwing:
                    map["Yellow"] = Skin("Razorwing", mark * 0.8f, 0.4f);
                    map["Black"] = CreatureMat("Chitin", new Color(0.04f, 0.04f, 0.05f), 0.5f);
                    map["LightBlue"] = CreatureMat("Wing", new Color(0.30f, 0.32f, 0.36f), 0.8f);
                    map["Orange"] = CreatureMat("Sting_" + T, mark * 0.6f, 0.5f, glow * 0.4f);
                    break;
                case UnitType.Gorehorn:
                    map["White"] = Skin("Gorehorn", team == 0 ? new Color(0.13f, 0.12f, 0.15f) : new Color(0.17f, 0.12f, 0.11f), 0.3f);
                    map["Black"] = Skin("GorehornDark", new Color(0.05f, 0.05f, 0.06f), 0.3f);
                    map["Pink"] = Skin("GorehornSnout", new Color(0.18f, 0.08f, 0.08f), 0.4f);
                    break;
            }
            return map;
        }

        /// <summary>A creature material by the imported one's name (FBX import may suffix it).</summary>
        static Material PartFor(Dictionary<string, Material> map, Material imported)
        {
            if (imported == null) return null;
            string n = imported.name;
            if (map.TryGetValue(n, out var m)) return m;
            // Longest key the name starts with ("Material.001" before "Material").
            Material best = null; int bestLen = -1;
            foreach (var kv in map)
                if (n.StartsWith(kv.Key) && kv.Key.Length > bestLen) { best = kv.Value; bestLen = kv.Key.Length; }
            return best;
        }

        /// <summary>The creature model under <paramref name="body"/>: turned to face +Z, sized by its
        /// skeleton (the imported renderer bounds are metres off), feet on the ground, coloured,
        /// with its clips on a legacy Animation (UnitView plays them).</summary>
        static void CreatureBody(CreatureModel cm, int team, Transform body)
        {
            string fbx = SFAssetPostprocessor.FaunaDir + cm.file + ".fbx";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (src == null) { Debug.LogWarning($"[StarForge] {fbx} missing: run python3 Tools/pack_fauna.py"); return; }
            var model = (GameObject)Object.Instantiate(src, body, false);
            model.name = cm.file;
            var unitType = cm.file == "Creature_Skitter" ? UnitType.Skitter : cm.file == "Creature_Spitter" ? UnitType.Spitter
                         : cm.file == "Creature_Gorehorn" ? UnitType.Gorehorn : UnitType.Razorwing;
            var parts = CreatureParts(unitType, team);
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    var m = PartFor(parts, mats[k]);
                    if (m == null) Debug.LogWarning($"[StarForge] {cm.file}: no colour for material '{(mats[k] != null ? mats[k].name : "none")}'");
                    mats[k] = m ?? parts["Plate"];
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.updateWhenOffscreen = false;
            }
            foreach (var t in model.GetComponentsInChildren<Transform>())
                if (t != null && t != model.transform && (t.name.StartsWith("Camera") || t.name.StartsWith("Lamp") || t.name.StartsWith("Sun")))
                    Object.DestroyImmediate(t.gameObject);
            Bounds Posed()
            {
                var b = new Bounds();
                bool first = true;
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    foreach (var bone in smr.bones)
                    {
                        if (bone == null || bone.name.Contains("Pole")) continue;
                        if (first) { b = new Bounds(bone.position, Vector3.zero); first = false; } else b.Encapsulate(bone.position);
                    }
                return b;
            }
            model.transform.rotation = Quaternion.FromToRotation(cm.facing, Vector3.forward) * model.transform.rotation;
            var bounds = Posed();
            float have = Mathf.Max(bounds.size.x, bounds.size.z) / 0.85f;
            model.transform.localScale = model.transform.localScale * (cm.length / Mathf.Max(1e-5f, have));
            bounds = Posed();
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) - body.position;
            bounds = Posed();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var inv = smr.transform.worldToLocalMatrix;
                var lb = new Bounds(inv.MultiplyPoint3x4(bounds.center), Vector3.zero);
                for (int c = 0; c < 8; c++)
                    lb.Encapsulate(inv.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents * 1.6f + Vector3.one * 0.3f,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                smr.localBounds = lb;
            }
            var anim = model.GetComponent<Animation>() ?? model.AddComponent<Animation>();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                {
                    c.legacy = true;
                    c.wrapMode = c.name.ToLowerInvariant().Contains("death") ? WrapMode.ClampForever : WrapMode.Loop;
                    if (anim.GetClip(c.name) == null) anim.AddClip(c, c.name);
                }
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.BasedOnRenderers;
        }

        /// <summary>The structure's pre-cut chunks (SF_*_CHUNKS.fbx) as a prefab of
        /// rigid bodies, thrown apart by DebrisBurst when the structure dies.</summary>
        static GameObject BuildDebrisPrefab(UnitDef d, int team)
        {
            string model = ModelFor(d.type) + "_CHUNKS";
            if (ModelFactory.LoadSource(model) == null) return null;
            var root = ModelFactory.Create(model, team);
            root.name = d.displayName + " Debris";
            foreach (Transform chunk in root.transform)
            {
                var r = chunk.GetComponent<MeshRenderer>();
                if (r == null) continue;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                chunk.gameObject.AddComponent<BoxCollider>();   // fits the mesh bounds
                var rb = chunk.gameObject.AddComponent<Rigidbody>();
                Vector3 size = r.bounds.size;
                rb.mass = Mathf.Max(1f, size.x * size.y * size.z * 400f);
                rb.linearDamping = 0.2f;
                rb.angularDamping = 0.6f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            root.AddComponent<DebrisBurst>();
            string path = $"{PrefabDir}/Debris_{d.type}_P{team}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------ icons
        /// <summary>Renders a three-quarter portrait of each player-team model into a PNG,
        /// used by the command card and selection panel.</summary>
        static void RenderIcons(List<UnitDef> defs)
        {
            const int Res = 256;
            var stage = new GameObject("__IconStage");
            stage.transform.position = new Vector3(5000f, 0f, 5000f);
            var camGo = new GameObject("IconCam");
            camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.enabled = false;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;
            var lightGo = new GameObject("IconKey");
            lightGo.transform.SetParent(stage.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.4f;
            light.color = new Color(1f, 0.92f, 0.82f);
            lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            var fillGo = new GameObject("IconFill");
            fillGo.transform.SetParent(stage.transform, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.9f;
            fill.color = new Color(0.55f, 0.7f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            var rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            try
            {
                foreach (var d in defs)
                {
                    if (d.type == UnitType.Boulder) continue;
                    var prefab = d.Prefab(0);
                    if (prefab == null) continue;
                    GameObject inst;
                    if (d.type == UnitType.Mech)
                    {
                        // The Mech has no fixed model: portray a representative one.
                        inst = new GameObject("MechPortrait");
                        var design = new MechDesign { locomotion = MechLocomotion.Biped, frame = MechFrame.Medium };
                        design.weapons.Add(MechWeapon.Autocannon);
                        design.weapons.Add(MechWeapon.Laser);
                        design.weapons.Add(MechWeapon.Missiles);
                        design.weapons.Add(MechWeapon.Missiles);
                        MechView.AssembleStatic(inst.transform, design, 0);
                    }
                    else inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.transform.SetParent(stage.transform, false);
                    inst.transform.localRotation = Quaternion.Euler(0f, 205f, 0f);
                    foreach (var c in inst.GetComponentsInChildren<MonoBehaviour>()) c.enabled = false;

                    var b = new Bounds(inst.transform.position, Vector3.zero);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    // A creature's renderer bounds are padded for its cycles: frame it by its bones.
                    var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>();
                    if (smrs.Length > 0)
                    {
                        bool first = true;
                        foreach (var smr in smrs)
                            foreach (var bone in smr.bones)
                            {
                                // Not the IK pole targets: rig helpers metres out from the body.
                                if (bone == null || bone.name.Contains("Pole")) continue;
                                if (first) { b = new Bounds(bone.position, Vector3.zero); first = false; } else b.Encapsulate(bone.position);
                            }
                        b.Expand(b.size.magnitude * 0.25f);
                    }
                    float size = b.extents.magnitude;
                    float dist = size / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                    camGo.transform.rotation = Quaternion.Euler(24f, 0f, 0f);
                    camGo.transform.position = b.center - camGo.transform.forward * dist;

                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    string path = $"{IconDir}/sf_ui_icon_{d.type}.png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    Object.DestroyImmediate(inst);
                    AssetDatabase.ImportAsset(path);
                    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                    ti.textureType = TextureImporterType.Default;
                    ti.alphaIsTransparency = true;
                    ti.mipmapEnabled = false;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.SaveAndReimport();
                    d.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    EditorUtility.SetDirty(d);
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(stage);
            }
        }
    }
}
