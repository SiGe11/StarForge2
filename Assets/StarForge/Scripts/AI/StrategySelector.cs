// StrategySelector.cs — linear contextual bandit over the AI's plans.
//
// Each strategy scores a shared feature vector with its own weight row. Weights
// start from a hand-authored doctrine prior so the AI is competent from the
// first second, then move online toward whatever is actually working against
// this player -- and, in this edition, carry over between matches (AIMemory).
//
// So that it does not play the same match twice: each match brings a small
// random taste per plan (Personality), plans the AI leaned on in its last few
// matches start slightly out of favour (more so the ones it lost with), and a
// plan that has run for a long while without paying off grows stale and gives
// way to something else. None of this overrides a clear read: a rush is still
// answered, a player caught out of position is still counter-attacked.
using UnityEngine;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed class StrategySelector
    {
        public const int NFEAT = 20;
        const int S = (int)Strategy.Count;

        enum F
        {
            Time = 0, ArmyRatio, SupplyFill, Ore, Workers, Army,
            EArmy, PRush, PMacro, PTurtle, PHarass, PExpand, PTech,
            Aggression, Expansion, Defensive, ScoutConf, Entropy,
            ArmyAway, Bias
        }

        readonly float[,] w = new float[S, NFEAT];
        readonly float[,] doctrine = new float[S, NFEAT];
        readonly float[] score = new float[S];
        readonly float[] lastFeat = new float[NFEAT];
        readonly float[] feat = new float[NFEAT];
        public readonly float[] timeIn = new float[S];
        Strategy cur = Strategy.Eco, pending = Strategy.Eco;
        float commitT, conf, lastAdv, evalT;
        bool haveAdv;
        /// <summary>A/B: the plan's reward as before -- every ore in the bank counted, the first window
        /// rewarded against nothing, a window credited whole to the plan that ended it (the evaluation
        /// runner resets it).</summary>
        public static bool LegacyLearning;
        /// <summary>Ore in hand counts toward a plan's reward up to this: a working float. Past it,
        /// 1,000 ore unspent paid a plan like a 400 lead in army.</summary>
        const float OreCounted = 400f;

        float Advantage(Snapshot s) =>
            0.0020f * (s.armyValue - s.eArmyValue) + 0.030f * s.workers + 0.0008f * (LegacyLearning ? s.ore : Mathf.Min(s.ore, OreCounted));

        readonly float[] bias = new float[S];        // taste + novelty, fixed for the match
        readonly float[] runPayoff = new float[S];   // reward summed over the current run of a plan
        readonly float[] runTime = new float[S];
        readonly float[] fatigue = new float[S];     // lingering penalty on plans that went stale
        int switches;
        string reason = "opening";
        Rng rng = new Rng(7);

        public Strategy Current => cur;
        public float ScoreOf(Strategy s) => score[(int)s];
        /// <summary>This match's offset for a plan (taste and recent matches): AuditTrials' plan trace.</summary>
        public float BiasOf(Strategy s) => bias[(int)s];
        public float Confidence => conf;
        public string Reason => reason;
        public int Switches => switches;

        /// <summary>Per-plan score offsets for this match: the personality's taste, and a
        /// nudge away from the plans of recent matches (from AIMemory).</summary>
        public void SetBias(Personality p, int[] recentPlans, int[] recentWins)
        {
            for (int i = 0; i < S; i++) bias[i] = p != null ? p.taste[i] : 0f;
            if (recentPlans == null) return;
            for (int k = 0; k < recentPlans.Length; k++)
            {
                int plan = recentPlans[k];
                if (plan < 0 || plan >= S) continue;
                bool won = recentWins != null && k < recentWins.Length && recentWins[k] > 0;
                // Newer matches weigh more; a plan it lost with more than one it won with.
                float age = (k + 1f) / recentPlans.Length;
                bias[plan] -= (won ? 0.05f : 0.12f) * age;
            }
        }

        public void Init(uint seed, float[] rememberedWeights = null, float trust = 0f)
        {
            rng = new Rng(seed);
            System.Array.Clear(doctrine, 0, doctrine.Length);

            if (VariedDoctrine) DoctrineVaried(); else DoctrineLegacy();

            // Blend in what previous matches against this player taught us. The
            // doctrine stays as a regulariser so a few odd games cannot wreck it.
            bool haveMemory = rememberedWeights != null && rememberedWeights.Length == S * NFEAT;
            float k = haveMemory ? Saturate(trust) : 0f;
            for (int s = 0; s < S; s++)
                for (int f = 0; f < NFEAT; f++)
                    w[s, f] = Lerp(doctrine[s, f], haveMemory ? rememberedWeights[s * NFEAT + f] : 0f, k);

            cur = pending = Strategy.Eco;
            conf = commitT = lastAdv = evalT = 0f;
            haveAdv = false;
            switches = 0;
            System.Array.Clear(lastFeat, 0, NFEAT);
            System.Array.Clear(timeIn, 0, S);
            System.Array.Clear(bias, 0, S);
            System.Array.Clear(runPayoff, 0, S);
            System.Array.Clear(runTime, 0, S);
            System.Array.Clear(fatigue, 0, S);
            reason = haveMemory && k > 0f ? "opening (remembers you)" : "opening";
        }

        void W(Strategy s, F f, float v) => doctrine[(int)s, (int)f] = v;

        /// <summary>The varied doctrine, OFF by default (opt-in: AIEvalRunner.SetAuditSwitches bit 1024 turns it on; the
        /// runner, the benchmark and AgentInbox's play stop clear it). Its gate (2026-11-23, 6000.6.10f1): 16/20 won,
        /// 0 lost, every plan used, Timing Push 49% -> 29% -- but against the turtle Feint took 84% of the time after
        /// 480 s (26% before), and the Feint path starts no wave (no massing, Mech-away or beaten bookkeeping):
        /// Turtle 1/5 won against 2/5, massing in no game. Left off until Feint no longer owns the late game.</summary>
        public static bool VariedDoctrine;
        /// <summary>The doctrine the memory file's learned weights belong to: bumped by hand whenever the DEFAULT
        /// doctrine's numbers change, and AIMemory drops saved weights of another version once. 0: the legacy doctrine
        /// (still the default); bump to 2 when <see cref="VariedDoctrine"/> becomes the default (1 was used 13:58-18:30 on
        /// 2026-11-23 for an earlier varied matrix, so a stale file stamped 1 must not match). Not tied to the switch:
        /// that A/B is for evaluations, which run with memory off.</summary>
        public const int DoctrineVersion = 0;

        /// <summary>The doctrine before the retune (2026-11-23): against the scripted players Timing Push and
        /// Turtle/Tech took most of the time, Trooper Rush none and Counter-Attack 0.5%.</summary>
        void DoctrineLegacy()
        {
            // The default doctrine: change any number here and bump DoctrineVersion, or saved weights learned on the
            // old numbers stay blended in.

            // Economy: good early, good when they are not threatening us.
            W(Strategy.Eco, F.Bias, 0.35f); W(Strategy.Eco, F.Time, -0.9f);
            W(Strategy.Eco, F.PRush, -1.4f); W(Strategy.Eco, F.PTurtle, 0.5f);
            W(Strategy.Eco, F.Workers, -0.8f); W(Strategy.Eco, F.EArmy, -0.6f);

            // Trooper rush: only early, best against a greedy or teching opponent.
            W(Strategy.TrooperRush, F.Bias, -0.20f); W(Strategy.TrooperRush, F.Time, -1.8f);
            W(Strategy.TrooperRush, F.PMacro, 1.1f); W(Strategy.TrooperRush, F.PExpand, 1.3f);
            W(Strategy.TrooperRush, F.PTech, 0.9f); W(Strategy.TrooperRush, F.PTurtle, -1.0f);
            W(Strategy.TrooperRush, F.Army, 0.7f); W(Strategy.TrooperRush, F.PRush, -0.6f);

            // Harass: punish economic play; pointless against someone sitting on units.
            W(Strategy.Harass, F.Bias, 0.05f); W(Strategy.Harass, F.PMacro, 1.2f);
            W(Strategy.Harass, F.PExpand, 1.1f); W(Strategy.Harass, F.Defensive, -1.0f);
            W(Strategy.Harass, F.Army, 0.5f); W(Strategy.Harass, F.PRush, -0.7f);
            W(Strategy.Harass, F.ArmyAway, 0.6f);

            // Timing push: commit when we are actually ahead in army.
            W(Strategy.TimingPush, F.Bias, -0.15f); W(Strategy.TimingPush, F.ArmyRatio, 2.2f);
            W(Strategy.TimingPush, F.Army, 0.9f); W(Strategy.TimingPush, F.SupplyFill, 0.7f);
            W(Strategy.TimingPush, F.Time, 0.4f); W(Strategy.TimingPush, F.ScoutConf, 0.5f);

            // Turtle/tech: the answer to aggression, and to being behind.
            W(Strategy.TurtleTech, F.Bias, -0.05f); W(Strategy.TurtleTech, F.PRush, 2.0f);
            W(Strategy.TurtleTech, F.PHarass, 1.0f); W(Strategy.TurtleTech, F.ArmyRatio, -1.5f);
            W(Strategy.TurtleTech, F.Aggression, 1.2f);

            // Expand: only when safe and rich.
            W(Strategy.Expand, F.Bias, -0.35f); W(Strategy.Expand, F.Ore, 1.6f);
            W(Strategy.Expand, F.PRush, -1.8f); W(Strategy.Expand, F.Aggression, -1.3f);
            W(Strategy.Expand, F.ArmyRatio, 0.8f); W(Strategy.Expand, F.PTurtle, 0.7f);

            // Counter-attack: their army is out of position.
            W(Strategy.CounterAttack, F.Bias, -0.5f); W(Strategy.CounterAttack, F.ArmyAway, 2.6f);
            W(Strategy.CounterAttack, F.Army, 0.6f); W(Strategy.CounterAttack, F.ScoutConf, 0.8f);

            // Feint: worth it against a player who reacts hard to what they see.
            W(Strategy.Feint, F.Bias, -0.75f); W(Strategy.Feint, F.Defensive, 1.4f);
            W(Strategy.Feint, F.Army, 0.8f); W(Strategy.Feint, F.ArmyRatio, 0.6f);
            W(Strategy.Feint, F.Time, 0.5f);
        }

        /// <summary>The varied doctrine (the plan retune, 2026-11-23; the user's target: every plan used, Timing
        /// Push's share down, no quota per plan). Eco and Turtle/Tech as before.</summary>
        void DoctrineVaried()
        {
            // The legacy doctrine with Timing Push's "we are ahead" bonus shared out (against the scripted
            // players the AI is nearly always ahead, and ArmyRatio 2.2 / Army 0.9 / SupplyFill 0.7 made Timing
            // Push the plan half the time, Trooper Rush never and Counter-Attack 0.5%): Trooper Rush, Harass and
            // Counter-Attack now take a lead in army too, and their own situation decides among them --
            // Trooper Rush an early window (its bias falls to nothing by 300 s), Harass less put off by a
            // defensive player, Counter-Attack less held back (but not while a rush is read). Expand's ore term
            // 1.6 -> 1.4 (at 1.6 it took most of the time Timing Push gave up against the turtle).
            // Designed against a plan trace (AuditTrials.EvalTracedFull, replayed offline): every plan used.
            // Opt-in (VariedDoctrine): changing a number here needs no DoctrineVersion bump while it is not the default.

            // Economy: good early, good when they are not threatening us.
            W(Strategy.Eco, F.Bias, 0.35f); W(Strategy.Eco, F.Time, -0.9f);
            W(Strategy.Eco, F.PRush, -1.4f); W(Strategy.Eco, F.PTurtle, 0.5f);
            W(Strategy.Eco, F.Workers, -0.8f); W(Strategy.Eco, F.EArmy, -0.6f);

            // Trooper rush: only early, best against a greedy or teching opponent.
            W(Strategy.TrooperRush, F.Bias, 1.2f); W(Strategy.TrooperRush, F.Time, -2.4f);
            W(Strategy.TrooperRush, F.PMacro, 1.1f); W(Strategy.TrooperRush, F.PExpand, 1.3f);
            W(Strategy.TrooperRush, F.PTech, 0.9f); W(Strategy.TrooperRush, F.PTurtle, -0.5f);
            W(Strategy.TrooperRush, F.Army, 0.7f); W(Strategy.TrooperRush, F.PRush, -0.6f); W(Strategy.TrooperRush, F.ArmyRatio, 1.0f);

            // Harass: punish economic play; pointless against someone sitting on units.
            W(Strategy.Harass, F.Bias, 0.15f); W(Strategy.Harass, F.PMacro, 1.2f);
            W(Strategy.Harass, F.PExpand, 1.1f); W(Strategy.Harass, F.Defensive, -0.6f);
            W(Strategy.Harass, F.Army, 0.5f); W(Strategy.Harass, F.PRush, -0.7f);
            W(Strategy.Harass, F.ArmyAway, 0.6f); W(Strategy.Harass, F.ArmyRatio, 0.9f);

            // Timing push: commit when we are actually ahead in army.
            W(Strategy.TimingPush, F.Bias, -0.15f); W(Strategy.TimingPush, F.ArmyRatio, 1.6f);
            W(Strategy.TimingPush, F.Army, 0.6f); W(Strategy.TimingPush, F.SupplyFill, 0.5f);
            W(Strategy.TimingPush, F.Time, 0.4f); W(Strategy.TimingPush, F.ScoutConf, 0.5f);

            // Turtle/tech: the answer to aggression, and to being behind.
            W(Strategy.TurtleTech, F.Bias, -0.05f); W(Strategy.TurtleTech, F.PRush, 2.0f);
            W(Strategy.TurtleTech, F.PHarass, 1.0f); W(Strategy.TurtleTech, F.ArmyRatio, -1.5f);
            W(Strategy.TurtleTech, F.Aggression, 1.2f);

            // Expand: only when safe and rich.
            W(Strategy.Expand, F.Bias, -0.35f); W(Strategy.Expand, F.Ore, 1.4f);
            W(Strategy.Expand, F.PRush, -1.8f); W(Strategy.Expand, F.Aggression, -1.3f);
            W(Strategy.Expand, F.ArmyRatio, 0.8f); W(Strategy.Expand, F.PTurtle, 0.7f);

            // Counter-attack: their army is out of position.
            W(Strategy.CounterAttack, F.Bias, -0.1f); W(Strategy.CounterAttack, F.ArmyAway, 2.6f);
            W(Strategy.CounterAttack, F.Army, 0.6f); W(Strategy.CounterAttack, F.ScoutConf, 0.8f); W(Strategy.CounterAttack, F.ArmyRatio, 0.8f);
            // ...not while it reads a rush coming: a rusher's army out is coming at us (Turtle/Tech's case).
            W(Strategy.CounterAttack, F.PRush, -1.0f);

            // Feint: worth it against a player who reacts hard to what they see.
            // (As before: raised, Feint took the late game from Timing Push against a defensive player, and its
            // path starts no wave -- no staging, no Mech-away or hurt-Mech waves, no beaten or massed count.)
            W(Strategy.Feint, F.Bias, -0.75f); W(Strategy.Feint, F.Defensive, 1.4f);
            W(Strategy.Feint, F.Army, 0.8f); W(Strategy.Feint, F.ArmyRatio, 0.6f);
            W(Strategy.Feint, F.Time, 0.5f);
        }

        public float[] ExportWeights()
        {
            var o = new float[S * NFEAT];
            for (int s = 0; s < S; s++)
                for (int f = 0; f < NFEAT; f++) o[s * NFEAT + f] = w[s, f];
            return o;
        }

        /// <summary>The features a plan is scored by (public for AuditTrials' plan trace; reads nothing of the selector).</summary>
        public static void Features(Snapshot s, OpponentModel om, float t, float[] f)
        {
            System.Array.Clear(f, 0, NFEAT);
            f[(int)F.Time] = Saturate(t / 600f);
            f[(int)F.ArmyRatio] = (s.armyValue - s.eArmyEstimate) / (s.armyValue + s.eArmyEstimate + 200f);
            f[(int)F.SupplyFill] = Saturate(s.supplyUsed / Mathf.Max(1f, s.supplyCap));
            f[(int)F.Ore] = Saturate(s.ore / 900f);
            f[(int)F.Workers] = Saturate(s.workers / 20f);
            f[(int)F.Army] = Saturate(s.armyValue / 900f);
            f[(int)F.EArmy] = Saturate(s.eArmyEstimate / 900f);
            f[(int)F.PRush] = om.Belief(PlayerStrat.Rushing);
            f[(int)F.PMacro] = om.Belief(PlayerStrat.Macro);
            f[(int)F.PTurtle] = om.Belief(PlayerStrat.Turtling);
            f[(int)F.PHarass] = om.Belief(PlayerStrat.Harassing);
            f[(int)F.PExpand] = om.Belief(PlayerStrat.Expanding);
            f[(int)F.PTech] = om.Belief(PlayerStrat.Teching);
            f[(int)F.Aggression] = om.Profile.aggression;
            f[(int)F.Expansion] = om.Profile.expansion;
            f[(int)F.Defensive] = om.Profile.defensive;
            f[(int)F.ScoutConf] = s.scoutConfidence;
            f[(int)F.Entropy] = om.Entropy() / Mathf.Log((float)PlayerStrat.Count, 2f);
            f[(int)F.ArmyAway] = Saturate(s.eArmyAwayFromHome / 400f);
            f[(int)F.Bias] = 1f;
        }

        public static bool Legal(Strategy st, Snapshot s)
        {
            switch (st)
            {
                case Strategy.TrooperRush: return s.garrisons >= 1 && s.troopers >= 4;
                case Strategy.Harass: return s.troopers + s.skimmers >= 3;
                case Strategy.TimingPush: return s.armyValue >= 300f;
                case Strategy.CounterAttack: return s.armyValue >= 200f && s.eArmyAwayFromHome > 60f;
                case Strategy.Feint: return s.armyValue >= 400f && s.enemyBaseKnown;
                case Strategy.Expand: return s.ore >= 350 && s.workers >= 10;
                default: return true;
            }
        }

        void Reward(float r)
        {
            // Bandit-style gradient step on the strategy that was actually running.
            const float lr = 0.05f;
            // A NaN reward or feature would stay in the weight for good (Clamp passes NaN), and be saved.
            if (float.IsNaN(r) || float.IsInfinity(r)) return;
            float g = Clamp(r, -1f, 1f);
            int c = (int)cur;
            for (int i = 0; i < NFEAT; i++)
            {
                float v = w[c, i] + lr * g * lastFeat[i];
                if (!float.IsNaN(v) && !float.IsInfinity(v)) w[c, i] = Clamp(v, -4f, 4f);
            }
            runPayoff[c] += g;
        }

        public void Update(Snapshot s, OpponentModel om, float t, float dt)
        {
            Features(s, om, t, feat);
            timeIn[(int)cur] += dt;
            runTime[(int)cur] += dt;
            for (int i = 0; i < S; i++) if (i != (int)cur) fatigue[i] = Mathf.Max(0f, fatigue[i] - dt * 0.002f);

            // Online credit assignment for the plan we have been running.
            evalT += dt;
            if (evalT >= 18f)
            {
                float adv = Advantage(s);
                // The first window only sets the mark: measured against 0, the opening plan was paid
                // for the Diggers and ore every side starts with.
                if (haveAdv || LegacyLearning) Reward(adv - lastAdv);
                lastAdv = adv;
                haveAdv = true;
                evalT = 0f;
            }
            System.Array.Copy(feat, lastFeat, NFEAT);

            float best = -1e30f, second = -1e30f;
            Strategy bestS = Strategy.Eco;
            for (int si = 0; si < S; si++)
            {
                var st = (Strategy)si;
                if (!Legal(st, s)) { score[si] = -1e30f; continue; }
                float acc = 0f;
                for (int i = 0; i < NFEAT; i++) acc += w[si, i] * feat[i];
                // A little optimism keeps the AI trying things it has not measured
                // yet, and anneals away as the match goes on.
                acc += rng.Range(-1f, 1f) * 0.10f * (1f - Saturate(t / 420f));
                // This match's taste and the nudge away from recent matches' plans,
                // strongest early (the opening is where repetition shows most).
                acc += bias[si] * Mathf.Lerp(1f, 0.5f, Saturate(t / 480f));
                // Staleness: a plan that has run over two minutes without paying off
                // loses its hold, so the AI moves on instead of grinding it out.
                if (si == (int)cur && runTime[si] > 120f && runPayoff[si] <= 0.05f)
                    acc -= Mathf.Min(0.35f, (runTime[si] - 120f) * 0.004f);
                acc -= fatigue[si];
                score[si] = acc;
                if (acc > best) { second = best; best = acc; bestS = st; }
                else if (acc > second) second = acc;
            }
            conf = Saturate((best - second) * 1.5f);

            // Hysteresis: commit for a while so the AI does not thrash.
            commitT -= dt;
            if (bestS != cur)
            {
                if (bestS == pending)
                {
                    if (commitT <= 0f)
                    {
                        // The plan being dropped is paid for its own part of the window first, and the
                        // window starts again with the next: the one that took over a second before the
                        // 18 s were up was credited with all of them.
                        if (!LegacyLearning && haveAdv) Reward(Advantage(s) - lastAdv);
                        // The plan being dropped keeps a little fatigue if it went stale.
                        int was = (int)cur;
                        if (runTime[was] > 120f && runPayoff[was] <= 0.05f) fatigue[was] = Mathf.Min(0.25f, fatigue[was] + 0.12f);
                        runTime[was] = 0f;
                        runPayoff[was] = 0f;
                        cur = bestS;
                        switches++;
                        commitT = 14f;
                        if (!LegacyLearning) { evalT = 0f; lastAdv = Advantage(s); haveAdv = true; }
                        reason = $"{AINames.Of(cur)} — they look {AINames.Of(om.MostLikely())}";
                    }
                }
                else
                {
                    pending = bestS;
                    if (commitT <= 0f) commitT = 2.5f;
                }
            }
            else pending = bestS;
        }
    }
}
