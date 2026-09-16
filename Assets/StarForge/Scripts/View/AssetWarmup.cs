// AssetWarmup.cs — touches the assets a match will need but does not show for
// minutes, while the player is still loading the match.
//
// Shader variants are preloaded at startup (StarForge ▸ Build ▸ 5), but the
// meshes and materials of a structure's debris are only referenced by a prefab
// that is first instantiated when something blows up — and a 300-second
// benchmark caught that as a 200 ms hitch on the first building to die.
// Instantiating each one once here, inactive and off the map, pays that cost
// where nothing is moving.
using UnityEngine;
using StarForge.Sim;

namespace StarForge.View
{
    [DefaultExecutionOrder(-50)]
    public sealed class AssetWarmup : MonoBehaviour
    {
        void Start()
        {
            var parent = new GameObject("Warmup").transform;
            parent.position = new Vector3(0f, -500f, 0f);
            parent.gameObject.SetActive(false);

            int n = 0;
            for (int i = 0; i < (int)UnitType.Count; i++)
            {
                var def = Defs.Get((UnitType)i);
                if (def == null || def.prefabs == null) continue;
                for (int team = 0; team < def.prefabs.Length; team++)
                {
                    var prefab = def.prefabs[team];
                    if (prefab == null) continue;
                    var view = prefab.GetComponent<StarForge.World.UnitView>();
                    if (view == null || view.debris == null) continue;
                    Instantiate(view.debris, parent);
                    n++;
                }
            }
            Destroy(parent.gameObject);
            if (n > 0) Debug.Log($"[StarForge] warmed {n} debris prefabs");
        }
    }
}
