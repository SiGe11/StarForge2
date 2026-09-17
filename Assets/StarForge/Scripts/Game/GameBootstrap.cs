// GameBootstrap.cs — sets up a match: starting bases, the AI commander and the
// match lifecycle (menu -> playing -> ended). The Game layer owns no AI logic;
// it only constructs a Commander and ticks it after the world, exactly like the
// original's app and eval harness did.
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using StarForge.AI;
using StarForge.Sim;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.Game
{
    public enum MatchState { Menu, Playing, Paused, Ended }

    /// <summary>Choices that survive a scene reload (restart keeps them).</summary>
    public static class MatchSettings
    {
        public static AIDifficulty difficulty = AIDifficulty.Veteran;
        public static bool aiMemory = true;
        /// <summary>The map to build when the scene starts; 0 makes a new one every time (MapRuntime).</summary>
        public static uint mapSeed;
        public static bool skipMenu;
        public static bool spectate;
        public static uint seed = 1000;

        /// <summary>Developer view of the AI: the inspector (I), its minimap marks, the
        /// memory toggle and reset on the title screen, and the end-of-match dossier.
        /// Players never see any of it. On with the <c>-sfdebug</c> launch argument, or
        /// in the editor with StarForge ▸ Debug ▸ AI Internals.</summary>
        public static bool debugAI =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sfdebug") >= 0
#if UNITY_EDITOR
            || UnityEditor.EditorPrefs.GetBool("sf_debug_ai", false)
#endif
            ;
    }

    [DefaultExecutionOrder(-40)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public GameWorld world;
        [Tooltip("Start a match immediately instead of showing the title screen (editor testing).")]
        public bool autoStart;

        public static GameBootstrap Instance { get; private set; }
        public Commander AI { get; private set; }
        /// <summary>When spectating, a second adaptive AI plays the player's side.</summary>
        public Commander PlayerProxy { get; private set; }
        public MatchState State { get; private set; } = MatchState.Menu;
        public float MatchTime => world != null ? world.time : 0f;

        public event Action MatchStarted;
        public event Action<int> MatchEnded;
        public event Action<MatchState> StateChanged;

        InfluenceComputeBackend gpu;

        void Awake()
        {
            Instance = this;
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            Application.targetFrameRate = 60;
        }

        void Start()
        {
            if (autoStart || MatchSettings.skipMenu)
            {
                MatchSettings.skipMenu = false;
                StartMatch();
            }
            else SetState(MatchState.Menu);
        }

        void SetState(MatchState s)
        {
            State = s;
            Time.timeScale = s == MatchState.Playing ? 1f : (s == MatchState.Menu ? 1f : 0f);
            StateChanged?.Invoke(s);
        }

        public void StartMatch()
        {
            if (State == MatchState.Playing) return;
            uint seed = MatchSettings.seed;
            world.BeginMatch(seed);

            for (int t = 0; t < 2; t++)
            {
                Vector2 basePos = world.Map.StartPos(t);
                world.Spawn(UnitType.Foundry, t, basePos, true, t == 0 ? 0f : PI);
                for (int i = 0; i < 5; i++)
                {
                    float a = TAU * i / 5f + 0.4f;
                    Vector2 p = world.NearestWalkable(basePos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8f, 6f);
                    var wkr = world.Spawn(UnitType.Worker, t, p);
                    wkr.order = Order.Harvest;
                    wkr.harvestNode = world.NearestFreeNode(wkr.pos, t);
                    if (wkr.harvestNode != null) wkr.MoveTo(wkr.harvestNode.pos);
                }
            }

            gpu = InfluenceComputeBackend.TryCreate();
            AI = new Commander();
            AI.Init(world, 1, seed, MatchSettings.difficulty, MatchSettings.aiMemory, gpu);
            if (MatchSettings.spectate)
            {
                PlayerProxy = new Commander();
                PlayerProxy.Init(world, 0, seed ^ 0xABCDu, MatchSettings.difficulty, false, null);
            }
            world.Ticked += OnTick;
            SetState(MatchState.Playing);
            MatchStarted?.Invoke();
        }

        void OnTick(float dt)
        {
            AI?.Update(dt);
            PlayerProxy?.Update(dt);
            if (world.winner >= 0 && State == MatchState.Playing) EndMatch();
        }

        void EndMatch()
        {
            // Spectated games are not the player's style, so they teach nothing;
            // nor does a match played with the ore cheat.
            if (!MatchSettings.spectate && !world.cheated) AI.EndMatch(world.winner == 1);
            SetState(MatchState.Ended);
            Time.timeScale = 1f;
            MatchEnded?.Invoke(world.winner);
        }

        public void Surrender()
        {
            if (State != MatchState.Playing && State != MatchState.Paused) return;
            world.winner = 1;
            SetState(MatchState.Playing);
            EndMatch();
        }

        public void TogglePause()
        {
            if (State == MatchState.Playing) SetState(MatchState.Paused);
            else if (State == MatchState.Paused) SetState(MatchState.Playing);
        }

        public void Restart()
        {
            MatchSettings.skipMenu = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void ReturnToMenu()
        {
            MatchSettings.skipMenu = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void OnDestroy()
        {
            if (world != null) world.Ticked -= OnTick;
            gpu?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
