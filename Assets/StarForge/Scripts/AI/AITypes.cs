// AITypes.cs — shared types of the adaptive opponent.
//
// The AI plays under the same restrictions as the human: it only sees what its
// own units see (GameWorld.Visible), it issues orders exclusively through the
// same Cmd* API the mouse drives, and every order is paid for out of an APM
// budget. Structure follows ai-research.md:
//
//   Perception -> OpponentModel -> StrategySelector -> {Macro, Scouts, Tactics, Micro}
//        ^                                                      |
//        +------------------- observations ---------------------+
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;

namespace StarForge.AI
{
    public enum Strategy
    {
        Eco = 0,        // workers, expand, tech up; minimal army
        TrooperRush,    // early garrison, commit troopers before maulers exist
        Harass,         // small fast squads into the worker line, refuse fights
        TimingPush,     // mass to a threshold, then commit everything
        TurtleTech,     // hold the ramp with Sentinels, tech to maulers
        Expand,         // take a second ore line
        CounterAttack,  // hit the base while their army is away from it
        Feint,          // show force one place, strike another
        Count
    }

    /// <summary>What we believe the player is doing.</summary>
    public enum PlayerStrat { Rushing = 0, Macro, Turtling, Harassing, Expanding, Teching, Count }

    public enum AIDifficulty { Recruit = 0, Veteran = 1, Commander = 2 }

    public static class AINames
    {
        public static string Of(Strategy s)
        {
            switch (s)
            {
                case Strategy.Eco: return "Economy";
                case Strategy.TrooperRush: return "Trooper Rush";
                case Strategy.Harass: return "Harass";
                case Strategy.TimingPush: return "Timing Push";
                case Strategy.TurtleTech: return "Turtle / Tech";
                case Strategy.Expand: return "Expand";
                case Strategy.CounterAttack: return "Counter-Attack";
                case Strategy.Feint: return "Feint";
                default: return "?";
            }
        }

        public static string Of(PlayerStrat s)
        {
            switch (s)
            {
                case PlayerStrat.Rushing: return "rushing";
                case PlayerStrat.Macro: return "macro";
                case PlayerStrat.Turtling: return "turtling";
                case PlayerStrat.Harassing: return "harassing";
                case PlayerStrat.Expanding: return "expanding";
                case PlayerStrat.Teching: return "teching";
                default: return "?";
            }
        }
    }

    /// <summary>Token bucket. `regen` is the sustained rate; `cap` allows bursts, which is
    /// how real APM behaves (a pro idles, then spikes during an engagement).</summary>
    public sealed class ActionBudget
    {
        public float tokens;
        public float regen = 5.5f;   // actions/sec sustained (330 APM, a top professional)
        public float cap = 14f;      // burst depth
        public int lifetime;
        public int denied;
        readonly List<float> stamps = new List<float>(512);

        public void Tick(float dt) => tokens = Mathf.Min(cap, tokens + regen * dt);
        public bool Can(int n = 1) => tokens >= n;

        public bool Spend(int n = 1)
        {
            if (tokens < n) { denied++; return false; }
            tokens -= n;
            lifetime += n;
            return true;
        }

        public void Note(float t)
        {
            stamps.Add(t);
            int drop = 0;
            while (drop < stamps.Count && t - stamps[drop] > 60f) drop++;
            if (drop > 0) stamps.RemoveRange(0, drop);
        }

        /// <summary>Rolling 60 s average.</summary>
        public float MeasuredAPM()
        {
            if (stamps.Count < 2) return 0f;
            float span = stamps[stamps.Count - 1] - stamps[0];
            return span < 1f ? 0f : stamps.Count / span * 60f;
        }

        /// <summary>Densest window anywhere in the last minute, as APM meters report.</summary>
        public float PeakAPM(float window = 5f)
        {
            if (stamps.Count < 2) return 0f;
            int best = 0, lo = 0;
            for (int hi = 0; hi < stamps.Count; hi++)
            {
                while (stamps[hi] - stamps[lo] > window) lo++;
                best = Mathf.Max(best, hi - lo + 1);
            }
            return best * (60f / window);
        }
    }

    /// <summary>One remembered enemy object, kept after it leaves vision.</summary>
    public sealed class Remembered
    {
        public UnitType type;
        public Vector2 pos;
        public float firstSeen, lastSeen;
        public bool stillVisible;
    }

    public sealed class Snapshot
    {
        // Own side (always fully known).
        public int workers, troopers, maulers, skimmers;
        public int foundries, garrisons, workshops, bunkhouses, sentinels, pending;
        public readonly int[] pendingType = new int[(int)UnitType.Count];
        public readonly int[] queuedType = new int[(int)UnitType.Count];
        public int ore, supplyUsed, supplyCap;
        public float armyValue;

        // Believed enemy state, derived only from what has actually been observed.
        public int eWorkers, eTroopers, eMaulers, eSkimmers;
        public int eFoundries, eGarrisons, eWorkshops, eBunkhouses, eSentinels;
        public float eArmyValue;         // believed present right now
        public float eArmyEstimate;      // decaying peak: what we think they have overall
        public float eArmyOurHalf;       // enemy combat units on our side of the map
        public float eCommitPeak;        // decaying peak of force committed at us
        public float eArmyNearOurBase;
        public float eArmyAwayFromHome;
        public float sinceAggression = 1e9f;
        public float pressure;           // cumulative aggression, decays slowly
        public bool enemyBaseKnown;
        public Vector2 enemyBase;
        public float scoutConfidence;    // 0..1, how fresh our picture of them is

        public void Clear()
        {
            workers = troopers = maulers = skimmers = 0;
            foundries = garrisons = workshops = bunkhouses = sentinels = pending = 0;
            System.Array.Clear(pendingType, 0, pendingType.Length);
            System.Array.Clear(queuedType, 0, queuedType.Length);
            ore = supplyUsed = supplyCap = 0;
            armyValue = 0f;
            eWorkers = eTroopers = eMaulers = eSkimmers = 0;
            eFoundries = eGarrisons = eWorkshops = eBunkhouses = eSentinels = 0;
            eArmyValue = eArmyEstimate = eArmyOurHalf = eCommitPeak = eArmyNearOurBase = eArmyAwayFromHome = 0f;
            sinceAggression = 1e9f;
            pressure = 0f;
            enemyBaseKnown = false;
            enemyBase = Vector2.zero;
            scoutConfidence = 0f;
        }
    }

    public sealed class AIDebug
    {
        public Strategy strategy = Strategy.Eco;
        public PlayerStrat believed = PlayerStrat.Macro;
        public readonly float[] beliefs = new float[(int)PlayerStrat.Count];
        public readonly float[] stratScores = new float[(int)Strategy.Count];
        public float entropy, scoutConfidence, apm, apmPeak, confidence;
        public int squads, switches, actions, denied;
        public string reason = "";
        public string backend = "cpu";
        public float influenceMs;
        public Vector2 scoutTarget;
        public bool enemyBaseKnown;
        public Vector2 enemyBase;
        public int question;
        public float aggression, expansion, defensive, harass, teching, volatility;
        public int memoryGames;
    }
}
