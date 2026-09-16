using UnityEngine;

namespace StarForge.Sim
{
    [CreateAssetMenu(menuName = "StarForge/Unit Catalog", fileName = "UnitCatalog")]
    public sealed class UnitCatalog : ScriptableObject
    {
        public UnitDef[] defs;

        public static UnitCatalog Load() => Resources.Load<UnitCatalog>("UnitCatalog");
    }
}
