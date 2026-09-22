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
    }
}
