// AuditTrials.cs — checks for the AI audit's fixes (2026-11-22). What each one measures and
// its target are in Scripts/AI/CLAUDE.md; the waves' own check is the evaluation line
// ("AI retreats", "waves called off", "spent", "reinforced by", "massing").
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class AuditTrials
    {
        /// <summary>Edit mode or play. The opponent model shown one unchanging picture (a narrow lead
        /// for "macro" over "turtling", 0.90 against 0.65) at Commander's, Veteran's and Recruit's
        /// think rate, the evidence counted once a think (before) and by the second (after). Good:
        /// after, the three rates read alike (before, 6 thinks a second ran three times as fast as 2),
        /// and a stale picture leaves the belief soft.</summary>
        public static string Evidence()
        {
            var sb = new StringBuilder();
            bool was = OpponentModel.LegacyEvidence;
            try
            {
                foreach (bool legacy in new[] { true, false })
                {
                    OpponentModel.LegacyEvidence = legacy;
                    sb.AppendLine(legacy ? "once a think (before):" : "by the second (after):");
                    foreach (float conf in new[] { 1f, 0f })
                        foreach (float hz in new[] { 6f, 4f, 2f })
                        {
                            var p = new Perception();
                            var s = p.Snap;
                            s.eWorkers = 9;
                            s.eGarrisons = 1;
                            s.scoutConfidence = conf;
                            s.enemyBaseKnown = true;
                            var om = new OpponentModel();
                            om.Init();
                            float dt = 1f / hz, t = 0f, at85 = -1f, after2 = 0f, after10 = 0f;
                            while (t < 30f)
                            {
                                t += dt;
                                om.Update(p, dt, 200f + t);
                                float top = om.Belief(om.MostLikely());
                                if (at85 < 0f && top > 0.85f) at85 = t;
                                if (Mathf.Abs(t - 2f) < dt * 0.5f) after2 = top;
                                if (Mathf.Abs(t - 10f) < dt * 0.5f) after10 = top;
                            }
                            sb.AppendLine($"  picture {(conf > 0.5f ? "fresh" : "stale")}, {hz:0} thinks a second: {AINames.Of(om.MostLikely())} {after2:0.00} after 2 s, {after10:0.00} after 10 s, " +
                                          $"over 0.85 {(at85 < 0f ? "never in 30 s" : $"after {at85:0.0} s")}, entropy at the end {om.Entropy():0.00} bits");
                        }
                }
            }
            finally { OpponentModel.LegacyEvidence = was; }
            return sb.ToString();
        }

        /// <summary>Edit mode or play. The plan bandit's reward over two 18 s windows with nothing
        /// happening but 2,000 ore arriving in the bank after the first. Good: after, the first
        /// window moves no weight (before, the plan was paid for the Diggers and ore a side starts
        /// with), and the banked ore pays for 300 of it (0.05 x 0.24 = +0.012 on the bias weight;
        /// before 0.05 x 1.0 = +0.05, the same as a 500 lead in army).</summary>
        public static string Reward()
        {
            var sb = new StringBuilder();
            bool was = StrategySelector.LegacyLearning;
            try
            {
                foreach (bool legacy in new[] { true, false })
                {
                    StrategySelector.LegacyLearning = legacy;
                    var sel = new StrategySelector();
                    sel.Init(1000u);
                    var om = new OpponentModel();
                    om.Init();
                    var s = new Snapshot { workers = 8, ore = 100, supplyCap = 10, supplyUsed = 8 };
                    const float dt = 0.25f;
                    float t = 0f;
                    // Let the opening's change of plan pass, so both windows belong to one plan.
                    while (t < 6f) { t += dt; sel.Update(s, om, t, dt); }
                    float[] w0 = sel.ExportWeights();
                    var plan = sel.Current;
                    while (t < 26f) { t += dt; sel.Update(s, om, t, dt); }
                    float[] w1 = sel.ExportWeights();
                    s.ore = 2100;
                    while (t < 46f) { t += dt; sel.Update(s, om, t, dt); }
                    float[] w2 = sel.ExportWeights();
                    int bias = (int)plan * StrategySelector.NFEAT + (StrategySelector.NFEAT - 1);
                    sb.AppendLine($"{(legacy ? "before" : "after")}: plan {AINames.Of(plan)}{(sel.Current != plan ? " (changed during the run: read again)" : "")}, " +
                                  $"its bias weight moved {w1[bias] - w0[bias]:+0.0000;-0.0000;0.0000} over the first window and {w2[bias] - w1[bias]:+0.0000;-0.0000;0.0000} for 2,000 ore banked");
                }
            }
            finally { StrategySelector.LegacyLearning = was; }
            return sb.ToString();
        }

        /// <summary>Edit mode: the varied doctrine's LearnReach. A selector given remembered weights (the doctrine's
        /// plus 0.3) at trust 0.7 runs 600 s on a snapshot whose army grows every window (a reward of ~0.7 each); the
        /// largest move of any weight from where the match began is at most 0.02 with the varied doctrine and
        /// unbounded with the legacy one, and the start is the blend.</summary>
        public static string LearnReachCheck()
        {
            var sb = new StringBuilder();
            bool was = StrategySelector.VariedDoctrine;
            try
            {
                foreach (bool varied in new[] { true, false })
                {
                    StrategySelector.VariedDoctrine = varied;
                    var probe = new StrategySelector(); probe.Init(1000u);
                    float[] doc = probe.ExportWeights();
                    var mem = new float[doc.Length];
                    for (int i = 0; i < mem.Length; i++) mem[i] = doc[i] + 0.3f;
                    var sel = new StrategySelector(); sel.Init(1000u, mem, 0.7f);
                    float[] w0 = sel.ExportWeights();
                    float blendErr = 0f;
                    for (int i = 0; i < w0.Length; i++) blendErr = Mathf.Max(blendErr, Mathf.Abs(w0[i] - Mathf.Lerp(doc[i], mem[i], 0.7f)));
                    var om = new OpponentModel(); om.Init();
                    var s = new Snapshot { workers = 8, ore = 100, supplyCap = 10, supplyUsed = 8 };
                    const float dt = 0.25f;
                    float t = 0f;
                    while (t < 600f) { t += dt; s.armyValue = 20f * t; sel.Update(s, om, t, dt); }
                    float[] w1 = sel.ExportWeights();
                    float move = 0f;
                    for (int i = 0; i < w1.Length; i++) move = Mathf.Max(move, Mathf.Abs(w1[i] - w0[i]));
                    sb.AppendLine($"{(varied ? "varied" : "legacy")}: largest move of a weight in 600 s {move:0.0000}" +
                                  $"{(varied ? $" (bound {StrategySelector.LearnReach:0.00})" : " (no bound)")}, start against the blend {blendErr:0.000000}");
                }
            }
            finally { StrategySelector.VariedDoctrine = was; }
            return sb.ToString();
        }

        /// <summary>Edit mode or play. The memory file's plan weights against a change of doctrine, read from
        /// strings (AIMemory.FromJson; the user's own file is never read or written): a file from before the field
        /// and one stamped with StrategySelector.DoctrineVersion keep them (version 0 is the legacy doctrine);
        /// one stamped with another version loses its plan weights and keeps the rest (profile, histogram, games,
        /// history), restamped with the version in force.</summary>
        public static string MemoryDoctrine()
        {
            var sb = new StringBuilder();
            {
                var old = new AIMemoryData { games = 6, aiWins = 3, aggression = 0.8f, lastRead = "macro" };
                old.weights = new float[(int)Strategy.Count * StrategySelector.NFEAT];
                for (int i = 0; i < old.weights.Length; i++) old.weights[i] = 0.01f * (i % 7);
                old.styleHistogram[(int)PlayerStrat.Macro] = 0.6f;
                string json = JsonUtility.ToJson(old).Replace(",\"doctrine\":0", "");   // as a file from before the field
                var d = AIMemory.FromJson(json);
                sb.AppendLine($"file from before: plan weights {d.weights.Length} (of {old.weights.Length}), " +
                              $"games {d.games}, wins {d.aiWins}, aggression {d.aggression:0.00}, macro in the histogram {d.styleHistogram[(int)PlayerStrat.Macro]:0.00}, " +
                              $"last read {d.lastRead}, doctrine now {d.doctrine}");
                old.doctrine = StrategySelector.DoctrineVersion;
                var e = AIMemory.FromJson(JsonUtility.ToJson(old));
                sb.AppendLine($"file stamped {old.doctrine} (the version in force): plan weights {e.weights.Length}, doctrine {e.doctrine}");
                old.doctrine = StrategySelector.DoctrineVersion + 1;
                var x = AIMemory.FromJson(JsonUtility.ToJson(old));
                sb.AppendLine($"file stamped {old.doctrine} (another version): plan weights {x.weights.Length}, games {x.games}, aggression {x.aggression:0.00}, " +
                              $"macro in the histogram {x.styleHistogram[(int)PlayerStrat.Macro]:0.00}, last read {x.lastRead}, doctrine now {x.doctrine}");
            }
            return sb.ToString();
        }

        /// <summary>Play mode (the definitions under the combat rules). What one of a side's own guns
        /// counts for in its Mech's odds: as damage to a Mech whatever stood there (before), and against
        /// riflemen, tanks and their Mech (after). Good: the Mech-Hunter about 79 only against a Mech
        /// and about 7 against riflemen; a Mauler the same in every column.</summary>
        public static string HunterOdds()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var sb = new StringBuilder();
            sb.AppendLine("damage a second in the Mech's odds: before (any enemy) | after: riflemen | light vehicles | tanks | their Mech | towers || shots a second by the cooldown, and by magazine and reload");
            foreach (var t in new[] { UnitType.MechHunter, UnitType.Mauler, UnitType.Trooper, UnitType.Lancer, UnitType.Scorcher, UnitType.Rocketeer, UnitType.Gunner, UnitType.Marksman, UnitType.Thumper })
            {
                var D = Defs.Get(t);
                if (D == null) continue;
                float old = D.hitsGround && D.Armed ? D.damage * D.vsHeavy * D.vsMech / Mathf.Max(0.1f, D.cooldown) : 0f;
                sb.AppendLine($"  {D.displayName}: {old:0.0} | {MechBrain.FriendDps(D, 0f, 0f, 0f, 0f, 0f, 0f, 1f):0.0} | {MechBrain.FriendDps(D, 0f, 0f, 0f, 0f, 0f, 1f, 0f):0.0} | " +
                              $"{MechBrain.FriendDps(D, 0f, 0f, 0f, 0f, 1f, 0f, 0f):0.0} | {MechBrain.FriendDps(D, 0f, 0f, 0f, 1f, 0f, 0f, 0f):0.0} | {MechBrain.FriendDps(D, 0f, 1f, 0f, 0f, 0f, 0f, 0f):0.0} || " +
                              $"{1f / Mathf.Max(0.1f, D.cooldown):0.00} ({D.cooldown:0.00} s), {MechBrain.Rate(D):0.00} ({D.magazine} rounds, {D.reloadTime:0.0} s)");
            }
            return sb.ToString();
        }

        /// <summary>Play mode, about a minute. A rifleman of ours stands 24 m from one of the AI's on open
        /// ground, is seen, and after 3 s lies down (and never fires): out of its sight on ground it still
        /// sees. Good: before, its memory of him goes the same pass; after, it keeps him about 8 s
        /// (Perception.GraceSoldier). Also how long after first seeing him the AI may react at Recruit's
        /// 0.9 s (Perception.Reactable for a unit: about 0.9 s).</summary>
        public static string Conceal() => Stage<ConcealTrial>("Conceal");
        public static string ConcealReport() => Report<ConcealTrial>();

        /// <summary>The Full evaluation's five games against one scripted opponent (900 s cap) with some of
        /// the audit's switches held on for the whole run (AIEvalRunner.PrefLegacy), for a before/after on
        /// the same seeds. Call out of play mode; `EvalState` reads "running" until the report is written.
        /// Turtle is where the waves show (12 of 27 logged Turtle games ran to the cap before).</summary>
        public static string EvalTurtleNew() => EvalHeld(2, 0);
        /// <summary>With the reserve (Commander.ReserveGroups) on: the rule tried and left off.</summary>
        public static string EvalTurtleReserve() => EvalHeld(2, 1);
        public static string EvalTurtleOldWaveEnd() => EvalHeld(2, 2);
        public static string EvalTurtleOldForget() => EvalHeld(2, 64);
        public static string EvalTurtleOldAll() => EvalHeld(2, 1022);
        /// <summary>A Full with the varied doctrine on (StrategySelector.VariedDoctrine, bit 1024).</summary>
        public static string EvalFullVariedDoctrine() => EvalHeldAll(1024);

        /// <summary>Edit mode or play: which doctrine a fresh selector starts from, by default and with the varied one
        /// switched on (Timing Push's ArmyRatio weight: 2.2 legacy, 1.6 varied).</summary>
        public static string DoctrineCheck()
        {
            bool was = StrategySelector.VariedDoctrine;
            try
            {
                int i = (int)Strategy.TimingPush * StrategySelector.NFEAT + 1;
                StrategySelector.VariedDoctrine = false;   // the default, whatever a held run has set
                var a = new StrategySelector(); a.Init(1000u);
                StrategySelector.VariedDoctrine = true;
                var b = new StrategySelector(); b.Init(1000u);
                return $"default: Timing Push x ArmyRatio {a.ExportWeights()[i]:0.00}; varied on: {b.ExportWeights()[i]:0.00}; " +
                       $"memory doctrine version {StrategySelector.DoctrineVersion}";
            }
            finally { StrategySelector.VariedDoctrine = was; }
        }
        public static string EvalMacroNew() => EvalHeld(1, 0);
        public static string EvalMacroOldAll() => EvalHeld(1, 1022);

        static System.DateTime evalAsked;

        /// <summary>A Full with some of the audit's switches held on (AIEvalRunner.PrefLegacy), all four opponents.</summary>
        static string EvalHeldAll(int mask)
        {
            if (EditorApplication.isPlaying) return "playing: stop first";
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefOnly);
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefFirst);
            PlayerPrefs.SetInt(AIEvalRunner.PrefLegacy, mask);
            PlayerPrefs.SetString(AIEvalRunner.PrefLegacyAt, System.DateTime.UtcNow.Ticks.ToString());
            evalAsked = System.DateTime.UtcNow;
            string refused = AIEvalMenu.Run(5, 900f);
            if (refused != null) { PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacy); PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacyAt); return "not started: " + refused; }
            return $"full evaluation started; audit switches held on: {AIEvalRunner.AuditSwitchNames(mask)}";
        }

        static string EvalHeld(int kind, int mask)
        {
            if (EditorApplication.isPlaying) return "playing: stop first";
            PlayerPrefs.SetInt(AIEvalRunner.PrefLegacy, mask);
            PlayerPrefs.SetString(AIEvalRunner.PrefLegacyAt, System.DateTime.UtcNow.Ticks.ToString());
            int was = AIReview.EvalKind;
            AIReview.EvalKind = kind;
            evalAsked = System.DateTime.UtcNow;
            string r = AIReview.EvalOne();
            AIReview.EvalKind = was;
            // Refused (already playing, an unsaved scene): the menu has cleared the run's keys; say so plainly.
            if (!r.StartsWith("evaluating")) { PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacy); PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacyAt); return "not started: " + r; }
            return $"{r}; audit switches held on: {AIEvalRunner.AuditSwitchNames(mask)}";
        }

        /// <summary>A Full (or a Quick) on the code as it stands, with the plan trace armed (PlanTrace): one CSV
        /// a game under Art/Source/staged/aiaudit/plans/traces/<run>/. Call out of play mode; read
        /// `PlanTraceState` (or `EvalState`) for the end. The trace is disarmed by PlanTraceState once the
        /// report is written.</summary>
        public static string EvalTracedFull() => EvalTraced("full", 5, 900f, 0);
        public static string EvalTracedQuick() => EvalTraced("quick", 1, 720f, 0);
        /// <summary>The same with the varied doctrine on (mask bit 1024); the trace's game lines say which ran.</summary>
        public static string EvalTracedFullVaried() => EvalTraced("full_varied", 5, 900f, 1024);
        /// <summary>Games 6-8 against every opponent (seeds 1185, 1222, 1259: Standard, Standard, EarlyGarrison, the
        /// openings the Full's seeds never draw; AIReview.EvalOtherOpenings), traced, 900 s: the legacy doctrine, then
        /// the varied one.</summary>
        public static string EvalTracedOther() => EvalTraced("other", 3, 900f, 0, 5);
        public static string EvalTracedOtherVaried() => EvalTraced("other_varied", 3, 900f, 1024, 5);

        static string EvalTraced(string what, int games, float cap, int mask, int first = 0)
        {
            if (EditorApplication.isPlaying) return "playing: stop first";
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefOnly);
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefFirst);
            if (first > 0) PlayerPrefs.SetInt(AIEvalRunner.PrefFirst, first);
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacy);
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacyAt);
            if (mask != 0)
            {
                PlayerPrefs.SetInt(AIEvalRunner.PrefLegacy, mask);
                PlayerPrefs.SetString(AIEvalRunner.PrefLegacyAt, System.DateTime.UtcNow.Ticks.ToString());
            }
            string run = $"{what}_{System.DateTime.Now:yyyyMMdd_HHmm}";
            PlanTrace.Arm(run);
            evalAsked = System.DateTime.UtcNow;
            string refused = AIEvalMenu.Run(games, cap);
            if (refused != null) { PlanTrace.Disarm(); PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacy); PlayerPrefs.DeleteKey(AIEvalRunner.PrefLegacyAt); return "not started: " + refused; }
            return $"traced {what} evaluation started; trace in Art/Source/staged/aiaudit/plans/traces/{run}/";
        }

        /// <summary>"running (N games, M thinks traced)" until the traced run's report is written; then the
        /// report, and the trace is disarmed.</summary>
        public static string PlanTraceState()
        {
            string r = EvalState();
            if (r == "running") return $"running ({PlanTrace.Games} games, {PlanTrace.Rows} thinks traced)";
            if (PlanTrace.Armed) PlanTrace.Disarm();
            return $"traced {PlanTrace.Games} games, {PlanTrace.Rows} thinks\n{r}";
        }

        public static string PlanTraceStop() { PlanTrace.Disarm(); return $"trace disarmed after {PlanTrace.Games} games, {PlanTrace.Rows} thinks"; }

        /// <summary>"running" until the evaluation asked for above has written its report, then the report.</summary>
        public static string EvalState()
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "starforge_ai_eval.txt");
            if (AIEvalRunner.Pending || !System.IO.File.Exists(path) || System.IO.File.GetLastWriteTimeUtc(path) < evalAsked) return "running";
            return System.IO.File.ReadAllText(path);
        }

        static string Stage<T>(string name) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(name + "Trial").AddComponent<T>();
            return $"staging; call {name}Report in about a minute";
        }

        static string Report<T>() where T : BugTrial
        {
            var r = Object.FindAnyObjectByType<T>();
            return r == null ? "no trial running" : r.report;
        }
    }

    /// <summary>The plan trace (2026-11-23, the plan retune): every think of every evaluation game, the
    /// plan selector's inputs and choice, written one CSV a game to Art/Source/staged/aiaudit/plans/traces/
    /// <run>/. Read by Tools/plan_replay.py, which replays the selector offline.
    /// Armed by AuditTrials.EvalTracedFull / EvalTracedQuick, disarmed when the run's report is written
    /// (or by PlanTraceStop); hooked on GameWorld.Ticked after the Commander's own handler, so each row is
    /// the think just made. Rows: game, t, think dt, current plan, then per plan score, legal, bias, then
    /// the 20 features, then army, their army value, Diggers, ore, and the legality inputs (Garrisons,
    /// Troopers, Skimmers, their army away from home, their base known).</summary>
    [InitializeOnLoad]
    public static class PlanTrace
    {
        static bool armed;
        static string dir;
        static int game = -1, lastThinks = -1, rows;
        static float lastThinkSeconds;
        static Commander ai;
        static GameWorld world;
        static readonly StringBuilder sb = new StringBuilder(1 << 20);
        static readonly float[] f = new float[StrategySelector.NFEAT];
        // The Commander's wave state for the hand-off question (a forced end of a plan: does the army walk home?).
        // Read by reflection so the trace needs no field in Commander.cs.
        static readonly System.Reflection.FieldInfo WaveOnField = typeof(Commander).GetField("waveOn",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo CommittedField = typeof(Commander).GetField("committed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo MainField = typeof(Commander).GetField("main",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        public static int Games => game + 1;
        public static int Rows => rows;
        public static bool Armed => armed;

        static PlanTrace()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += st => { if (st == PlayModeStateChange.ExitingPlayMode) Flush(); };
        }

        public static void Arm(string run)
        {
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Art/Source/staged/aiaudit/plans/traces", run));
            System.IO.Directory.CreateDirectory(dir);
            game = -1; lastThinks = -1; rows = 0; ai = null; world = null;
            sb.Clear();
            armed = true;
        }

        public static void Disarm()
        {
            Flush();
            Unhook();
            armed = false;
        }

        static void Unhook()
        {
            if (world != null) world.Ticked -= OnTick;
            world = null;
            ai = null;
        }

        // A new match has a new Commander: hook its world's tick (after GameBootstrap's own handler, which
        // subscribed when the match started) and start a new game's file.
        static void Poll()
        {
            if (!armed || !EditorApplication.isPlaying) return;
            var boot = GameBootstrap.Instance;
            var w = GameWorld.Instance;
            if (boot == null || w == null || boot.AI == null || boot.AI == ai) return;
            Flush();
            Unhook();
            ai = boot.AI;
            world = w;
            world.Ticked += OnTick;
            game++;
            lastThinks = -1;
            lastThinkSeconds = 0f;
            sb.Append("# game ").Append(game).Append(" seed ").Append(MatchSettings.seed).Append(" difficulty ").Append(ai.Difficulty)
              .Append(" doctrine ").Append(StrategySelector.VariedDoctrine ? "varied" : "legacy")
              // The bar is the selector's rule, not the game's: legality stays Legal() alone and plan_replay applies the
              // cap from this stamp (the 2026-11-23 22:55 trace wrote a barred Feint as not legal: replay it --legal-rule).
              .Append(" feintcap ").Append(StrategySelector.VariedDoctrine ? $"{StrategySelector.FeintRun:0}/{StrategySelector.FeintRest:0}" : "off")
              .Append(" learnreach ").Append(StrategySelector.VariedDoctrine ? StrategySelector.LearnReach.ToString("0.###", Inv) : "off")
              .Append(" tracefields ").Append(WaveOnField != null && CommittedField != null && MainField != null ? "ok" : "MISSING")
              .Append(" personality ").Append(ai.Dbg.personality.Replace(',', ';')).Append('\n');
            sb.Append("game,t,dt,cur");
            for (int i = 0; i < (int)Strategy.Count; i++) sb.Append(",s").Append(i);
            for (int i = 0; i < (int)Strategy.Count; i++) sb.Append(",l").Append(i);
            for (int i = 0; i < (int)Strategy.Count; i++) sb.Append(",b").Append(i);
            for (int i = 0; i < StrategySelector.NFEAT; i++) sb.Append(",f").Append(i);
            sb.Append(",army,earmy,workers,ore,garrisons,troopers,skimmers,eaway,ebaseknown,wave,committed,armydist\n");
        }

        static void OnTick(float dt)
        {
            if (!armed || ai == null || ai.Dbg.thinks == lastThinks) return;
            lastThinks = ai.Dbg.thinks;
            float told = ai.Dbg.thinkSeconds - lastThinkSeconds;
            lastThinkSeconds = ai.Dbg.thinkSeconds;
            var s = ai.Perception.Snap;
            var sel = ai.Selector;
            StrategySelector.Features(s, ai.Opponent, world.time, f);
            sb.Append(game).Append(',').Append(world.time.ToString("0.000", Inv)).Append(',').Append(told.ToString("0.0000", Inv)).Append(',').Append((int)sel.Current);
            for (int i = 0; i < (int)Strategy.Count; i++) sb.Append(',').Append(sel.ScoreOf((Strategy)i).ToString("0.0000", Inv));
            for (int i = 0; i < (int)Strategy.Count; i++) sb.Append(',').Append(StrategySelector.Legal((Strategy)i, s) ? '1' : '0');
            for (int i = 0; i < (int)Strategy.Count; i++) sb.Append(',').Append(sel.BiasOf((Strategy)i).ToString("0.0000", Inv));
            for (int i = 0; i < StrategySelector.NFEAT; i++) sb.Append(',').Append(f[i].ToString("0.0000", Inv));
            sb.Append(',').Append(s.armyValue.ToString("0", Inv)).Append(',').Append(s.eArmyValue.ToString("0", Inv))
              .Append(',').Append(s.workers).Append(',').Append(s.ore).Append(',').Append(s.garrisons).Append(',').Append(s.troopers)
              .Append(',').Append(s.skimmers).Append(',').Append(s.eArmyAwayFromHome.ToString("0", Inv)).Append(',').Append(s.enemyBaseKnown ? '1' : '0');
            // wave/committed: 1, 0, or -1 where the field could not be read (the header says "tracefields MISSING");
            // armydist: the centre of its main group (the wave, not the raiders, scouts or new men at home) to where it
            // believes their base is (-1 with no group or no base known).
            bool? wave = WaveOnField?.GetValue(ai) as bool?, com = CommittedField?.GetValue(ai) as bool?;
            Vector2 sum = Vector2.zero; int n = 0;
            if (MainField?.GetValue(ai) is List<Unit> main)
                foreach (var u in main)
                    if (u != null && !u.dying) { sum += u.pos; n++; }
            float dist = n > 0 && s.enemyBaseKnown ? (sum / n - s.enemyBase).magnitude : -1f;
            sb.Append(',').Append(wave == null ? "-1" : wave.Value ? "1" : "0").Append(',').Append(com == null ? "-1" : com.Value ? "1" : "0")
              .Append(',').Append(dist.ToString("0", Inv)).Append('\n');
            rows++;
        }

        static void Flush()
        {
            if (sb.Length == 0 || dir == null || game < 0) { sb.Clear(); return; }
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, $"game_{game:00}.csv"), sb.ToString());
            sb.Clear();
        }
    }

    public sealed class ConcealTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var boot = GameBootstrap.Instance;
            var per = boot != null && boot.AI != null ? boot.AI.Perception : null;
            if (per == null) { report = "no opponent AI"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(2211);
            HoldStatics();
            try
            {
                // A field where the two see each other (a fold or a tree between them on some), then both
                // rules on that one field.
                Vector2 b = default, d = default;
                bool found = false;
                for (int tries = 0; tries < 8 && !found; tries++)
                {
                    if (!FindField(rng, out b, out d)) break;
                    var s0 = Put(UnitType.Trooper, 1, b);
                    world.CmdHold(new List<Unit> { s0 });
                    var m0 = Put(UnitType.Trooper, 0, b + d * 24f);
                    m0.noMind = true;
                    world.CmdHold(new List<Unit> { m0 });
                    float t1 = world.time;
                    while (world.time - t1 < 4f && !world.Sees(1, m0))
                    {
                        m0.cooldown = 1e9f;
                        m0.hp = m0.MaxHp;
                        s0.hp = s0.MaxHp;
                        yield return null;
                    }
                    found = world.Sees(1, m0);
                    ClearSpawned();
                    yield return Wait(2f);
                    per.Update(0.02f);
                }
                if (!found) { report = "no field where the two see each other"; yield break; }
                foreach (bool legacy in new[] { true, false })
                {
                    Perception.LegacyForget = legacy;
                    var spotter = Put(UnitType.Trooper, 1, b);
                    world.CmdHold(new List<Unit> { spotter });
                    var man = Put(UnitType.Trooper, 0, b + d * 24f);
                    man.noMind = true;
                    world.CmdHold(new List<Unit> { man });
                    float t0 = world.time, seenAt = -1f, reactAt = -1f, proneAt = -1f, unseenAt = -1f, goneAt = -1f;
                    bool groundSeen = false;
                    int spotterShots = 0;
                    ListenWhileHeld(e => { if (e.kind == GameEventKind.Fire && e.unit == spotter) spotterShots++; });
                    while (world.time - t0 < 30f)
                    {
                        // The silenced Commander does not run its Perception: run it here, as its Update does.
                        per.Update(Time.deltaTime);
                        man.cooldown = 1e9f;   // he never fires: a shot would give him away
                        man.hp = man.MaxHp;
                        spotter.hp = spotter.MaxHp;
                        float t = world.time - t0;
                        bool sees = world.Sees(1, man);
                        if (seenAt < 0f && sees) seenAt = t;
                        if (seenAt >= 0f && reactAt < 0f && per.Reactable(man, 0.9f)) reactAt = t;
                        if (seenAt >= 0f && t - seenAt > 3f)
                        {
                            if (proneAt < 0f) proneAt = t;
                            man.posture = Posture.Prone;
                        }
                        if (proneAt >= 0f && unseenAt < 0f && !sees) { unseenAt = t; groundSeen = world.Visible(1, man.pos); }
                        if (unseenAt >= 0f && !Remembers(per, man.pos)) { goneAt = t; break; }
                        yield return null;
                    }
                    ListenWhileHeld(null);
                    if (seenAt < 0f)
                        sb.AppendLine($"  (never seen: {(Unit.Live(man) ? "alive" : "dead")}, {(Unit.Live(spotter) ? "spotter alive" : "spotter dead")}, {(man.pos - spotter.pos).magnitude:0.0} m apart, posture {man.posture}, " +
                                      $"his ground {(world.Visible(1, man.pos) ? "in" : "out of")} the AI's view, the spotter {(world.Sees(0, spotter) ? "seen" : "not seen")} by our side, spotter's shots {spotterShots})");
                    sb.AppendLine($"{(legacy ? "before" : "after")}: seen at {seenAt:0.0} s, may react (0.9 s) {(reactAt < 0f ? "never" : $"{reactAt - seenAt:0.00} s later")}, " +
                                  $"lay down at {proneAt:0.0} s, out of sight {(unseenAt < 0f ? "never (the staging failed)" : $"{unseenAt - proneAt:0.0} s later, his ground {(groundSeen ? "in view" : "NOT in view")}")}, " +
                                  $"forgotten {(unseenAt < 0f ? "-" : goneAt < 0f ? "not within the 30 s" : $"{goneAt - unseenAt:0.0} s after that")}");
                    ClearSpawned();
                    // The next pass starts only once he is out of its memory (an entry left by a pass
                    // that never forgot him would be read as the next man).
                    float tw = world.time;
                    while (world.time - tw < 60f)
                    {
                        per.Update(Time.deltaTime);
                        if (world.time - tw > 2f && !Remembers(per, b + d * 24f)) break;
                        yield return null;
                    }
                }
            }
            finally
            {
                Perception.LegacyForget = false;
                ReleaseStatics();
            }
            report = sb.ToString();
        }

        // A coroutine's finally does not run when its object is destroyed (play stopped, the trial
        // staged again): the switch goes back to its default here too, or it would stay on for every
        // match after (statics survive play sessions).
        void OnDisable() => Perception.LegacyForget = false;

        static bool Remembers(Perception per, Vector2 at)
        {
            foreach (var r in per.Enemies)
                if (r.type == UnitType.Trooper && (r.pos - at).sqrMagnitude < 9f) return true;
            return false;
        }
    }
}
