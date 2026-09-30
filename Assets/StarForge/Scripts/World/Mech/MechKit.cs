// MechKit.cs — the Mech's modular parts as the game loads them: one prefab per part
// and team, and the points on each part that others attach to (a frame's mounts,
// a weapon's muzzle, the legs' waist). Written by the editor (PrefabBuilder) from
// the Blender kit's models.json, loaded from Resources at runtime.
//
// The simulation reads only the points (where a shot leaves a muzzle); the
// assembler (View/MechAssembler) reads the prefabs. Every lookup has a fallback,
// so a Mech still fights when the kit has not been built.
using System;
using UnityEngine;

namespace StarForge.World
{
    [CreateAssetMenu(menuName = "StarForge/Mech Kit", fileName = "MechKit")]
    public sealed class MechKit : ScriptableObject
    {
        [Serializable]
        public sealed class Part
        {
            public string model;
            [Tooltip("Per team (player, AI).")] public GameObject[] prefabs = new GameObject[2];
            public string[] pointNames = new string[0];
            public Vector3[] points = new Vector3[0];
            public float height, radius;
        }

        public Part[] parts = new Part[0];

        static MechKit loaded;
        static bool tried;

        public static MechKit Instance
        {
            get
            {
                if (!tried || loaded == null) { loaded = Resources.Load<MechKit>("MechKit"); tried = true; }
                return loaded;
            }
        }

        public Part Get(string model)
        {
            if (parts == null) return null;
            foreach (var p in parts) if (p != null && p.model == model) return p;
            return null;
        }

        public GameObject Prefab(string model, int team)
        {
            var p = Get(model);
            if (p == null || p.prefabs == null || p.prefabs.Length == 0) return null;
            return p.prefabs[Mathf.Clamp(team, 0, p.prefabs.Length - 1)] ?? p.prefabs[0];
        }

        public bool TryPoint(string model, string name, out Vector3 at)
        {
            at = default;
            var p = Get(model);
            if (p == null || p.pointNames == null) return false;
            for (int i = 0; i < p.pointNames.Length && i < p.points.Length; i++)
                if (p.pointNames[i] == name) { at = p.points[i]; return true; }
            return false;
        }

        /// <summary>A named point on a part, or <paramref name="fallback"/> if the kit has none.</summary>
        public static Vector3 Point(string model, string name, Vector3 fallback)
        {
            var k = Instance;
            return k != null && k.TryPoint(model, name, out var at) ? at : fallback;
        }
    }
}
