// OpponentModel.cs — Bayesian posterior over the player's plan, fused with a
// continuously updated behaviour profile. Both are cheap and interpretable,
// which is what ai-research.md recommends over an opaque end-to-end model.
using UnityEngine;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed class PlayerProfile
    {
        public float aggression = 0.45f;   // pushes out / attacks
        public float expansion = 0.30f;    // takes extra bases
        public float defensive = 0.40f;    // keeps army home
        public float harass = 0.25f;       // sends small squads at workers
        public float teching = 0.35f;      // invests in workshops/maulers
        public float armyTrend;            // observed army growth per minute
        public float volatility = 0.3f;    // how often they switch behaviour
    }

    public sealed class OpponentModel
    {
        const int K = (int)PlayerStrat.Count;
        readonly float[] post = new float[K];
        readonly float[] prior = new float[K];
        public readonly PlayerProfile Profile = new PlayerProfile();
        float earlyAggr, lastArmy, lastSwitchT;
        int question;
        PlayerStrat lastTop = PlayerStrat.Macro;

        public float Belief(PlayerStrat s) => post[(int)s];
        public float ThreatOfEarlyAggression => earlyAggr;
        /// <summary>Which enemy fact would most reduce uncertainty: 0 army, 1 tech, 2 expansions.</summary>
        public int MostValuableQuestion => question;

        /// <summary>Starts from uniform, or from what this AI remembers about the player
        /// across previous matches (a mild prior, so a changed player is still seen).</summary>
        public void Init(float[] remembered = null, float trust = 0f)
        {
            float sum = 0f;
            for (int i = 0; i < K; i++)
            {
                float r = remembered != null && remembered.Length == K ? Mathf.Max(0f, remembered[i]) : 0f;
                prior[i] = r;
                sum += r;
            }
            for (int i = 0; i < K; i++)
            {
                float mem = sum > 1e-6f ? prior[i] / sum : 1f / K;
                prior[i] = Lerp(1f / K, mem, Saturate(trust));
                post[i] = prior[i];
            }
            earlyAggr = lastArmy = lastSwitchT = 0f;
            question = 0;
            lastTop = PlayerStrat.Macro;
        }

        public void Update(Perception p, float dt, float t)
        {
            var s = p.Snap;

            // HMM-style predict step: beliefs decay toward the prior so the model
            // can change its mind when the player changes plan.
            float drift = 1f - Mathf.Exp(-dt / 45f);
            for (int i = 0; i < K; i++) post[i] = post[i] * (1f - drift) + drift * prior[i];

            float earlyGame = 1f - SoftIndicator(t, 180f, 420f);
            float armySeen = SoftIndicator(s.eArmyEstimate, 50f, 600f);
            float nearUs = SoftIndicator(Mathf.Max(s.eArmyNearOurBase, s.eCommitPeak), 25f, 300f);
            // A rush commits its whole army; a raid is three units.
            float bigCommit = SoftIndicator(s.eCommitPeak, 150f, 400f);
            float ecoHeavy = SoftIndicator(s.eWorkers, 6f, 18f);
            float techSeen = SoftIndicator(s.eWorkshops, 0f, 2f);
            float expandSeen = SoftIndicator(s.eFoundries, 1f, 2f);
            float depotHeavy = SoftIndicator(s.eBunkhouses, 2f, 6f);
            float turretSeen = SoftIndicator(s.eSentinels, 0f, 3f);
            float raiders = SoftIndicator(s.eSkimmers, 0f, 4f);
            float smallRaid = nearUs * (1f - bigCommit);
            float aggrRecent = s.sinceAggression > 1e8f ? 0f : Mathf.Exp(-s.sinceAggression / 30f);
            float pressure = s.pressure;
            float peace = (1f - pressure) * (1f - pressure);
            float prodSeen = SoftIndicator(s.eGarrisons + s.eWorkshops, 0f, 2f);
            // What fraction of everything we believe they own is thrown at us? Only
            // meaningful if we have recently looked at their base.
            float commitFrac = Saturate(s.eCommitPeak / Mathf.Max(120f, s.eArmyEstimate));
            commitFrac = Lerp(0.5f, commitFrac, s.scoutConfidence);

            var L = new float[K];
            L[(int)PlayerStrat.Rushing] = 0.05f + 2.0f * bigCommit + 1.0f * pressure * earlyGame
                                        + 0.5f * nearUs + 0.5f * earlyGame * armySeen
                                        + 1.6f * pressure * commitFrac;
            L[(int)PlayerStrat.Turtling] = 0.05f + (1.2f * prodSeen + 1.6f * armySeen * (1f - nearUs)
                                        + 0.9f * depotHeavy + 1.4f * turretSeen) * peace;
            L[(int)PlayerStrat.Macro] = 0.05f + (2.2f * ecoHeavy + 0.30f * (1f - armySeen)) * peace;
            L[(int)PlayerStrat.Harassing] = 0.05f + 2.2f * smallRaid * (1f - bigCommit)
                                          + 1.8f * pressure * (1f - commitFrac)
                                          + 0.4f * aggrRecent * (1f - bigCommit)
                                          + 0.8f * raiders * aggrRecent;
            L[(int)PlayerStrat.Expanding] = 0.05f + 2.4f * expandSeen + 0.5f * ecoHeavy * peace;
            L[(int)PlayerStrat.Teching] = 0.05f + 2.2f * techSeen + 0.4f * (1f - earlyGame) * peace;

            // Down-weight evidence when our picture of them is stale: a fractional
            // power flattens the likelihood toward "uninformative".
            float conf = Clamp(0.25f + 0.75f * s.scoutConfidence, 0.05f, 1f);
            float sum = 0f;
            for (int i = 0; i < K; i++)
            {
                post[i] *= Mathf.Pow(Mathf.Max(1e-4f, L[i]), conf);
                sum += post[i];
            }
            if (sum > 1e-9f) for (int i = 0; i < K; i++) post[i] /= sum;
            // Never let a hypothesis reach certainty: a saturated posterior cannot be
            // moved by new evidence, which is exactly when a player switches plans.
            sum = 0f;
            for (int i = 0; i < K; i++) { post[i] = Mathf.Max(post[i], 0.02f); sum += post[i]; }
            for (int i = 0; i < K; i++) post[i] /= sum;

            float a = 1f - Mathf.Exp(-dt / 12f);
            Profile.aggression = Lerp(Profile.aggression, nearUs, a);
            Profile.expansion = Lerp(Profile.expansion, expandSeen, a);
            Profile.defensive = Lerp(Profile.defensive, Mathf.Max(armySeen * (1f - pressure), turretSeen), a);
            Profile.harass = Lerp(Profile.harass, smallRaid, a);
            Profile.teching = Lerp(Profile.teching, techSeen, a);
            float growth = (s.eArmyEstimate - lastArmy) / Mathf.Max(1e-3f, dt) * 60f;
            Profile.armyTrend = Lerp(Profile.armyTrend, Clamp(growth, -600f, 600f), a * 0.5f);
            lastArmy = s.eArmyEstimate;

            var top = MostLikely();
            if (top != lastTop)
            {
                float since = t - lastSwitchT;
                // A volatile opponent is the one worth feinting.
                Profile.volatility = Lerp(Profile.volatility, Saturate(30f / Mathf.Max(4f, since)), 0.35f);
                lastSwitchT = t;
                lastTop = top;
            }

            earlyAggr = Saturate(post[(int)PlayerStrat.Rushing] * 1.4f + post[(int)PlayerStrat.Harassing] * 0.7f) *
                        (0.4f + 0.6f * earlyGame);

            // Which question would sharpen the picture the most?
            float needArmy = 1f - Saturate(s.eArmyEstimate / 300f) + post[(int)PlayerStrat.Rushing] * 0.6f;
            float needTech = 1f - Saturate((s.eWorkshops + s.eGarrisons) / 2f) + post[(int)PlayerStrat.Teching] * 0.6f;
            float needExpand = 1f - Saturate(s.eFoundries / 2f) + post[(int)PlayerStrat.Expanding] * 0.6f;
            question = (needTech > needArmy && needTech > needExpand) ? 1 : (needExpand > needArmy ? 2 : 0);
        }

        public PlayerStrat MostLikely()
        {
            int best = 0;
            for (int i = 1; i < K; i++) if (post[i] > post[best]) best = i;
            return (PlayerStrat)best;
        }

        /// <summary>Uncertainty over the player's plan, in bits.</summary>
        public float Entropy()
        {
            float h = 0f;
            for (int i = 0; i < K; i++) if (post[i] > 1e-6f) h -= post[i] * Mathf.Log(post[i], 2f);
            return h;
        }
    }
}
