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
        Count
    }

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
        public bool IsArmy => !building && !neutral && type != UnitType.Worker && type != UnitType.Mech;
        public bool IsMobile => !building && !neutral;
        /// <summary>Armour class for the Mech's weapons: infantry, Diggers and Skimmers
        /// are light; tanks, Mechs and structures heavy.</summary>
        public bool Heavy => building || type == UnitType.Mauler || type == UnitType.Mech;
        public bool Armed => range > 0f;
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

        /// <summary>Worth of a unit as army, used by perception and the strategy layer.</summary>
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
                default: return 0f;
            }
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
                default: return 0.25f;
            }
        }

        public static readonly UnitType[] Buildable =
            { UnitType.Bunkhouse, UnitType.Garrison, UnitType.Workshop, UnitType.Sentinel, UnitType.Foundry, UnitType.MechBay };
    }
}
