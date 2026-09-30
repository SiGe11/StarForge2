// MapInfo.cs — what the game needs to know about the authored map scene.
using UnityEngine;

namespace StarForge.World
{
    public sealed class MapInfo : MonoBehaviour
    {
        public Terrain terrain;
        [Tooltip("Index 0 is the player, 1 the AI.")]
        public Transform[] startLocations = new Transform[2];

        [Header("Built by MapGenerator")]
        public MeshFilter water;
        public GameObject deepWater;
        [Tooltip("The land beyond the rim; outside this object, so the NavMesh bake leaves it out.")]
        public MeshFilter backdrop;
        public Transform oreRoot, boulderRoot, sceneryRoot;
        public Vegetation vegetation;
        public float mapSize = 256f;
        public float waterLevel = 2.4f;
        public uint seed;

        public static MapInfo Instance { get; private set; }

        void Awake() => Instance = this;

        public Vector2 StartPos(int team) =>
            new Vector2(startLocations[team].position.x, startLocations[team].position.z);

        public float HeightAt(Vector2 p) =>
            terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + TerrainTransform.position.y;

        // Heights are asked for thousands of times a frame, so the terrain's Transform
        // is kept rather than fetched from the engine on every call.
        [System.NonSerialized] Terrain transformOf;
        [System.NonSerialized] Transform terrainTransform;

        Transform TerrainTransform
        {
            get
            {
                if (!ReferenceEquals(transformOf, terrain)) { transformOf = terrain; terrainTransform = terrain.transform; }
                return terrainTransform;
            }
        }

        public Vector3 Ground(Vector2 p) => new Vector3(p.x, HeightAt(p), p.y);

        /// <summary>How deep the water stands over the ground here; negative on dry land.</summary>
        public float WaterDepth(Vector2 p) => waterLevel - HeightAt(p);

        public Vector3 NormalAt(Vector2 p)
        {
            var td = terrain.terrainData;
            return td.GetInterpolatedNormal(p.x / td.size.x, p.y / td.size.z);
        }

        public bool InBounds(Vector2 p, float margin = 0f) =>
            p.x >= margin && p.y >= margin && p.x <= mapSize - margin && p.y <= mapSize - margin;
    }
}
