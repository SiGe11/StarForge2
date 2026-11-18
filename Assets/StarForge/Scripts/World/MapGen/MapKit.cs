// MapKit.cs — the assets a map is built from: terrain and water materials,
// the prefabs placed on it (ore, boulders, scenery) and the plant kinds. Made
// by MapBuilder in the editor; MapGenerator reads it every time it builds a map.
using System;
using UnityEngine;

namespace StarForge.World
{
    [Serializable]
    public sealed class SceneryPiece
    {
        public string name;
        public GameObject prefab;
        public int count;
        [Tooltip("Footprint radius at scale 1, metres.")] public float radius;
        public float minScale = 1f, maxScale = 1f;
    }

    /// <summary>A wall or ruin piece the new combat places when a match begins (MapGenerator.PlaceCover),
    /// with its shape for the rules from models.json's `cover` (Tools/blender/build_env.py).</summary>
    [Serializable]
    public sealed class CoverPiece
    {
        public string name;
        public GameObject prefab;
        [Tooltip("Its straight runs in the model's frame: (ax, az, bx, bz).")]
        public Vector4[] segments = Array.Empty<Vector4>();
        public float[] heights = Array.Empty<float>();
        public float thickness = 0.46f;
    }

    public sealed class MapKit : ScriptableObject
    {
        [Tooltip("StarForge/Terrain. A match uses a copy with its own relief-occlusion texture.")]
        public Material terrainMaterial;
        public TerrainLayer[] terrainLayers;
        public Material waterMaterial;
        [Tooltip("StarForge/Terrain with _SF_AUTOSPLAT, for the land beyond the rim.")]
        public Material backdropMaterial;
        public GameObject orePrefab;
        [Tooltip("Boulder prefabs, one per scanned rock; the generator picks one at random for each boulder.")]
        public GameObject[] boulderPrefabs = Array.Empty<GameObject>();
        public SceneryPiece[] scenery = Array.Empty<SceneryPiece>();
        public PlantKind[] plantKinds = Array.Empty<PlantKind>();
        [Tooltip("Low walls and ruins: placed only under the new combat, when a match begins.")]
        public CoverPiece[] cover = Array.Empty<CoverPiece>();
    }
}
