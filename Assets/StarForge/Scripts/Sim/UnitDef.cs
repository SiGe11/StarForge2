// UnitDef.cs — unit and structure definitions as ScriptableObjects, so the
// balance sheet is editable in the Inspector. UnitCatalog holds one of each
// and is loaded from Resources.
using UnityEngine;

namespace StarForge.Sim
{
    public enum UnitType
    {
        None = -1,
        Worker = 0, Trooper, Mauler, Skimmer,
        Foundry, Garrison, Workshop, Bunkhouse, Sentinel,
        Ore, Boulder,
        // Appended, never reordered: the unit assets store these as numbers.
        MechBay, Mech,
        // The wider roster: three more rifles (Garrison), two tanks (Workshop), two aircraft
        // (Airfield), two more workers (Foundry), three creatures (Biolab) and their buildings.
        Thumper, Scorcher, Marksman, Lancer, Aegis, Kestrel, Swift, Hauler, Prospector,
        Skitter, Spitter, Razorwing, Airfield, Armory, Biolab,
        // Anti-armour rocket infantry (Garrison), a walking mini-mech (Workshop), the Biolab's armoured beast.
        Rocketeer, Ironclad, Gorehorn,
        Count
    }

    /// <summary>Which of the Armory's upgrade lines a unit takes.</summary>
    public enum UnitClass { None = 0, Infantry, Vehicle, Creature }

    /// <summary>How a unit's gun works (GameWorld.Fire). Auto is the original units', chosen
    /// by their type as before.</summary>
    public enum WeaponKind { Auto = 0, Rifle, Mortar, Flamer, Sniper, Cannon, Flak, Rockets, Needle, Claws, Acid, Glaive,
                             Bazooka, Battery, Gore }

    public enum Order { Idle = 0, Move, AttackMove, Attack, Harvest, Return, Build, Hold }

    [CreateAssetMenu(menuName = "StarForge/Unit Definition", fileName = "UnitDef")]
    public sealed class UnitDef : ScriptableObject
    {
        public UnitType type;
        public string displayName;
        [TextArea] public string blurb;
        public bool building;
        public bool neutral;

        [Header("Body")]
        public float radius = 0.7f;
        public float hp = 60f;
        public float speed;
        [Tooltip("Radians per second.")] public float turnRate;

        [Header("Weapon")]
        public float range;
        public float damage;
        public float cooldown;
        public float splash;
        public float sight = 20f;
        [Tooltip("Damage multiplier against Diggers.")] public float bonusVsWorkers = 1f;
        public WeaponKind weapon;
        [Tooltip("Can shoot at aircraft.")] public bool hitsAir;
        [Tooltip("Can shoot at what stands on the ground (every unit before aircraft).")] public bool hitsGround = true;
        [Tooltip("Too close to fire at (a mortar).")] public float minRange;
        [Tooltip("Damage multipliers by what is hit (light, heavy, structure, aircraft).")]
        public float vsLight = 1f, vsHeavy = 1f, vsStructure = 1f, vsAir = 1f;
        [Tooltip("Further enemies a hit leaps on to, each at a third of the last (none of the roster uses it now).")] public int bounces;
        [Tooltip("Chance a hit sets the plants there alight (the Scorcher).")] public float ignite;

        [Header("Role")]
        public UnitClass upgradeClass;
        [Tooltip("Mines ore and brings it to a Foundry.")] public bool harvester;
        [Tooltip("Raises structures (the Digger).")] public bool builder;
        [Tooltip("Ore brought in a trip.")] public int carry = 8;
        [Tooltip("Seconds to cut a load.")] public float harvestTime = 1.6f;
        [Tooltip("Armoured: takes a Mech's anti-armour weapons' damage (tanks, the Hauler).")] public bool heavyArmour;
        public bool flying;
        [Tooltip("Height it flies at, metres.")] public float altitude;
        [Tooltip("A creature from the Biolab: it bleeds rather than burns, and never deserts.")] public bool organic;

        [Header("Production")]
        public float buildTime;
        public int cost;
        public int supplyCost;
        public int supplyGive;
        public char hotkey;
        public UnitType producer = UnitType.None;
        public UnitType requires = UnitType.None;

        [Header("Presentation")]
        [Tooltip("Prefab per team (player, AI). Neutral types use the first.")]
        public GameObject[] prefabs = new GameObject[2];
        public Texture2D icon;
        [Tooltip("Model radius, drives selection markers.")] public float visualRadius = 1f;
        [Tooltip("Model height, drives health bars and construction effects.")] public float visualHeight = 2f;

        public GameObject Prefab(int team) =>
            prefabs == null || prefabs.Length == 0 ? null : prefabs[Mathf.Clamp(team, 0, prefabs.Length - 1)] ?? prefabs[0];

        /// <summary>Takes no orders from its side: the Mech, which its own AI drives
        /// (AI/MechBrain). Selection shows it; commands skip it.</summary>
        public bool Autonomous => type == UnitType.Mech;
        /// <summary>Fighting units: not workers (the armed Prospector included: it mines), not the Mech.</summary>
        public bool IsArmy => !building && !neutral && !harvester && type != UnitType.Mech;
        public bool IsMobile => !building && !neutral;
        /// <summary>Armour class for the Mech's weapons: infantry, Diggers, Skimmers, aircraft
        /// and creatures are light; tanks, Haulers, Mechs and structures heavy.</summary>
        public bool Heavy => building || heavyArmour || type == UnitType.Mech;
        public bool Armed => range > 0f;
        /// <summary>Moves on the ground through the NavMesh (everything mobile but aircraft).</summary>
        public bool Walks => IsMobile && !flying;
        /// <summary>The worker counts (Diggers, Haulers, Prospectors): what keeps a side's economy.</summary>
        public bool Worker => harvester;
    }

    public static class Defs
    {
        static UnitDef[] table;

        public static void Bind(UnitCatalog catalog)
        {
            table = new UnitDef[(int)UnitType.Count];
            foreach (var d in catalog.defs)
                if (d != null && d.type >= 0 && d.type < UnitType.Count) table[(int)d.type] = d;
        }

        public static UnitDef Get(UnitType t)
        {
            if (table == null) Bind(UnitCatalog.Load());
            return table[(int)t];
        }

        /// <summary>Worth of a unit as army, used by perception and the strategy layer: what it
        /// costs, for every fighting unit and worker (the original three as before).</summary>
        public static float ArmyValue(UnitType t)
        {
            switch (t)
            {
                case UnitType.Trooper: return 50f;
                case UnitType.Mauler: return 150f;
                case UnitType.Skimmer: return 75f;
                case UnitType.Worker: return 50f;
                // As much army as twenty-odd Maulers (MechParts' balance), and valued so.
                case UnitType.Mech: return 3200f;
            }
            if (t < 0 || t >= UnitType.Count) return 0f;
            var d = Get(t);
            return d != null && d.IsMobile ? d.cost : 0f;
        }

        /// <summary>Contribution to the spatial influence field.</summary>
        public static float InfluenceStrength(UnitType t)
        {
            switch (t)
            {
                case UnitType.Mauler: return 3.0f;
                case UnitType.Trooper: return 1.0f;
                case UnitType.Skimmer: return 1.2f;
                case UnitType.Sentinel: return 2.5f;
                case UnitType.MechBay: return 4f;
                // About twenty-odd Maulers' worth (MechParts' balance): the influence map
                // should make an army think twice about walking into one.
                case UnitType.Mech: return 60f;
            }
            // The wider roster by its cost against a Trooper's (1 for 50 ore), workers as before.
            if (t < 0 || t >= UnitType.Count) return 0.25f;
            var d = Get(t);
            return d != null && d.IsMobile && d.Armed && !d.harvester ? d.cost / 50f : 0.25f;
        }

        public static readonly UnitType[] Buildable =
            { UnitType.Bunkhouse, UnitType.Garrison, UnitType.Workshop, UnitType.Sentinel, UnitType.Foundry, UnitType.MechBay,
              UnitType.Armory, UnitType.Airfield, UnitType.Biolab };

        /// <summary>A worker type: mines ore (Digger, Hauler, Prospector).</summary>
        public static bool IsWorkerType(UnitType t) => t >= 0 && t < UnitType.Count && Get(t) != null && Get(t).harvester;
    }
}
