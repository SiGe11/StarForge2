// AIEvalMenu.cs — starts the play-mode AI evaluation (see AIEvalRunner).
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarForge.Game;

namespace StarForge.EditorTools
{
    public static class AIEvalMenu
    {
        // Matches run longer since the Mechs (they landed at four or five minutes, and hold
        // a base for a long while), so the caps did too: 420 and 600 s ended every
        // match undecided. Since 2026-11-04 a Mech lands about nine minutes in (seven after
        // its bay stands), so the 720 and 900 s caps leave it 3 and 6 minutes.
        [MenuItem("StarForge/Evaluate AI/Quick (1 game per opponent, 720 s)", priority = 50)]
        public static void Quick() { ClearReplay(); Started(Run(1, 720f)); }

        [MenuItem("StarForge/Evaluate AI/Full (5 games per opponent, 900 s)", priority = 51)]
        public static void Full() { ClearReplay(); Started(Run(5, 900f)); }

        // A replay's opponent and first game: a plain Quick or Full plays them all (left over, a Full
        // played one opponent from game N under a normal header).
        static void ClearReplay()
        {
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefOnly);
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefFirst);
        }

        // The menu items and editor.sh's eval: a refused run is an error answer, not a silent "called".
        static void Started(string refused)
        {
            if (refused != null) throw new System.InvalidOperationException(refused);
        }

        /// <summary>Replay one game of the full run (its opponent and its seed), traced:
        /// set <see cref="ReplayKind"/> and <see cref="ReplayGame"/> first.</summary>
        public static int ReplayKind = 3, ReplayGame = 2;
        public static void Replay()
        {
            PlayerPrefs.SetInt(AIEvalRunner.PrefOnly, ReplayKind);
            PlayerPrefs.SetInt(AIEvalRunner.PrefFirst, ReplayGame);
            Started(Run(1, 900f));
        }

        /// <summary>Starts a run; null when started, else why not (the caller's own state is its to put back:
        /// a run that cannot start takes the eval keys its caller set with it, a replay's or the audit's mask).</summary>
        public static string Run(int gamesPerOpponent, float secondsCap)
        {
            if (EditorApplication.isPlaying)
            {
                AIEvalRunner.ClearRequest();
                return "already playing: stop first";
            }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != MapBuilder.ScenePath)
            {
                if (scene.isDirty)
                {
                    AIEvalRunner.ClearRequest();
                    return $"the open scene '{scene.name}' has unsaved changes: save or close it first";
                }
                EditorSceneManager.OpenScene(MapBuilder.ScenePath);
            }
            PlayerPrefs.SetInt(AIEvalRunner.PrefGames, gamesPerOpponent);
            PlayerPrefs.SetFloat(AIEvalRunner.PrefSeconds, secondsCap);
            PlayerPrefs.SetString(AIEvalRunner.PrefGamesAt, System.DateTime.UtcNow.Ticks.ToString());   // expires in 3 min
            PlayerPrefs.Save();
            EditorApplication.isPlaying = true;
            return null;
        }
    }
}
