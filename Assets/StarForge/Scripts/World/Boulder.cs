// Boulder.cs — a rock on the battlefield that a Mauler can drive through.
//
// The map builder leaves the rock itself out of the NavMesh bake and marks the
// ground under it as the Rubble area (a NavMeshModifierVolume on this object).
// Every agent but the Mauler excludes Rubble, so infantry, Diggers and Skimmers
// path round the rock while a Mauler's path runs straight through it.
// GameWorld crushes the rock when a Mauler reaches it, then rebuilds the NavMesh
// tiles it covered so the ground is open to everyone.
using Unity.AI.Navigation;
using UnityEngine;

namespace StarForge.World
{
    [DisallowMultipleComponent]
    public sealed class Boulder : MonoBehaviour
    {
        [Tooltip("Footprint radius at scale 1, metres.")]
        public float radius = 1.15f;

        [System.NonSerialized] public bool smashed;

        public Vector2 Pos => new Vector2(transform.position.x, transform.position.z);
        public float Radius => radius * transform.lossyScale.x;

        /// <summary>Removes the rock and its Rubble marking. The caller rebuilds the NavMesh.</summary>
        public void Smash()
        {
            if (smashed) return;
            smashed = true;
            foreach (var v in GetComponents<NavMeshModifierVolume>()) v.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
