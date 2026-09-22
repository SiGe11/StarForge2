// MapRuntime.cs — a new battlefield every time the scene starts.
//
// Runs before anything else in the scene wakes up and rebuilds the Map from a
// fresh seed (MapGenerator), so everything that reads the terrain, the ore or
// the NavMesh at startup sees the new map. Restarting a match or going back to
// the title screen reloads the scene, which makes another.
//
// The seed: -sfseed N on the command line, or MatchSettings.mapSeed when a
// caller fixes it; the benchmark, the screenshot gallery and the AI evaluation stay on the default map
// so their numbers compare across builds; otherwise a random one.
using System;
using UnityEngine;
using StarForge.Game;

namespace StarForge.World
{
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(MapInfo))]
    public sealed class MapRuntime : MonoBehaviour
    {
        public MapKit kit;
        [Tooltip("Generate a new map whenever the scene starts playing. Off keeps the map saved in the scene.")]
        public bool newMapEveryMatch = true;

        public static uint LastSeed { get; private set; }

        void Awake()
        {
            var info = GetComponent<MapInfo>();
            LastSeed = info.seed;
            if (!newMapEveryMatch || kit == null) return;
            uint seed = ChooseSeed();
            var r = MapGenerator.Generate(info, kit, seed, bakeNavMesh: true, sharedMaterials: false);
            LastSeed = seed;
            Debug.Log($"[StarForge] new map from seed {seed}: terrain {r.heightsMs} ms, textures {r.splatMs} ms, " +
                      $"objects {r.objectsMs} ms, NavMesh {r.navMs} ms; {r.ore} ore, {r.boulders} boulders, " +
                      $"{r.scenery} scenery, {r.plants} plants ({r.blockingPlants} blocking, {r.grovesDropped} groves dropped to keep paths open)");
        }

        static uint ChooseSeed()
        {
            var args = Environment.GetCommandLineArgs();
            bool fixedMap = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-sfseed" && i + 1 < args.Length && uint.TryParse(args[i + 1], out uint s) && s != 0) return s;
                if (args[i] == "-sfbench" || args[i] == "-sfgallery") fixedMap = true;
            }
            if (MatchSettings.mapSeed != 0) return MatchSettings.mapSeed;
            if (fixedMap || AIEvalRunner.Pending) return MapGenerator.DefaultSeed;
            uint seed;
            do seed = (uint)UnityEngine.Random.Range(1, int.MaxValue) ^ (uint)DateTime.Now.Ticks; while (seed == 0);
            return seed;
        }
    }
}
