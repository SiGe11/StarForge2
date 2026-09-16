// StrategySelector.cs — linear contextual bandit over the AI's plans.
//
// Each strategy scores a shared feature vector with its own weight row. Weights
// start from a hand-authored doctrine prior so the AI is competent from the
// first second, then move online toward whatever is actually working against
// this player -- and, in this edition, carry over between matches (AIMemory).
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
        int switches;
        string reason = "opening";
        Rng rng = new Rng(7);

        public Strategy Current => cur;
        public float ScoreOf(Strategy s) => score[(int)s];
        public float Confidence => conf;
        public string Reason => reason;
        public int Switches => switches;

        public void Init(uint seed, float[] rememberedWeights = null, float trust = 0f)
        {
            rng = new Rng(seed);
            System.Array.Clear(doctrine, 0, doctrine.Length);

            void W(Strategy s, F f, float v) => doctrine[(int)s, (int)f] = v;

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

            // Blend in what previous matches against this player taught us. The
            // doctrine stays as a regulariser so a few odd games cannot wreck it.
            bool haveMemory = rememberedWeights != null && rememberedWeights.Length == S * NFEAT;
            float k = haveMemory ? Saturate(trust) : 0f;
            for (int s = 0; s < S; s++)
                for (int f = 0; f < NFEAT; f++)
                    w[s, f] = Lerp(doctrine[s, f], haveMemory ? rememberedWeights[s * NFEAT + f] : 0f, k);

            cur = pending = Strategy.Eco;
            conf = commitT = lastAdv = evalT = 0f;
            switches = 0;
            System.Array.Clear(lastFeat, 0, NFEAT);
            System.Array.Clear(timeIn, 0, S);
            reason = haveMemory && k > 0f ? "opening (remembers you)" : "opening";
        }

        public float[] ExportWeights()
        {
            var o = new float[S * NFEAT];
            for (int s = 0; s < S; s++)
                for (int f = 0; f < NFEAT; f++) o[s * NFEAT + f] = w[s, f];
            return o;
        }

        void Features(Snapshot s, OpponentModel om, float t, float[] f)
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

        static bool Legal(Strategy st, Snapshot s)
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
            float g = Clamp(r, -1f, 1f);
            int c = (int)cur;
            for (int i = 0; i < NFEAT; i++) w[c, i] = Clamp(w[c, i] + lr * g * lastFeat[i], -4f, 4f);
        }

        public void Update(Snapshot s, OpponentModel om, float t, float dt)
        {
            Features(s, om, t, feat);
            timeIn[(int)cur] += dt;

            // Online credit assignment for the plan we have been running.
            evalT += dt;
            if (evalT >= 18f)
            {
                float adv = 0.0020f * (s.armyValue - s.eArmyValue) + 0.030f * s.workers + 0.0008f * s.ore;
                Reward(adv - lastAdv);
                lastAdv = adv;
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
                        cur = bestS;
                        switches++;
                        commitT = 14f;
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
