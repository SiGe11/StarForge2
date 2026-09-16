// Perception.cs — fog-limited observation and a decaying memory of the enemy.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed class Perception
    {
        GameWorld w;
        int team;
        readonly List<Remembered> mem = new List<Remembered>(128);
        readonly List<Vector2> guesses = new List<Vector2>(3);
        float aggrT = -1e9f;     // last time we saw them pressuring our base
        float aggrScore;         // cumulative aggression: a rusher stays high between waves
        float armyEst;           // decaying estimate of their total army
        float commitPeak;        // decaying peak of committed force

        public readonly Snapshot Snap = new Snapshot();
        public Vector2 Home { get; private set; }
        public IReadOnlyList<Remembered> Enemies => mem;
        /// <summary>Candidate enemy main-base sites, best guess first.</summary>
        public IReadOnlyList<Vector2> BaseGuesses => guesses;

        /// <summary>The AI may not react to something it only just laid eyes on.</summary>
        public bool Reactable(Remembered r, float reactionDelay) => w.time - r.firstSeen >= reactionDelay;

        public void Init(GameWorld world, int t)
        {
            w = world;
            team = t;
            Home = w.Map.StartPos(t);
            mem.Clear();
            aggrT = -1e9f;
            aggrScore = armyEst = commitPeak = 0f;

            // Reading a symmetric map is something a human does before scouting
            // too -- but a guess is all it is. Nothing counts as known until a unit
            // of ours actually lays eyes on an enemy structure.
            float S = w.MapSize;
            guesses.Clear();
            guesses.Add(new Vector2(S - Home.x, S - Home.y));   // rotational mirror
            guesses.Add(new Vector2(S - Home.x, Home.y));       // horizontal mirror
            guesses.Add(new Vector2(Home.x, S - Home.y));       // vertical mirror
            for (int i = 0; i < guesses.Count; i++) guesses[i] = w.NearestWalkable(guesses[i]);
        }

        public void Update(float dt)
        {
            Observe();
            Forget();
            var s = Snap;
            // Units we kill vanish from memory immediately, so an instantaneous
            // count badly underestimates them. Track a decaying peak instead.
            if (s.eArmyValue > armyEst) armyEst = s.eArmyValue;
            else armyEst = Lerp(armyEst, s.eArmyValue, 1f - Mathf.Exp(-dt / 60f));
            s.eArmyEstimate = armyEst;
            // A wave we killed still tells us they are the kind of player who sends waves.
            if (s.eArmyOurHalf > commitPeak) commitPeak = s.eArmyOurHalf;
            else commitPeak = Lerp(commitPeak, s.eArmyOurHalf, 1f - Mathf.Exp(-dt / 120f));
            s.eCommitPeak = commitPeak;
            // Cumulative aggression: a rusher rebuilding between waves looks calm
            // for 45 s and would otherwise read as a turtle.
            float target = Saturate(s.eArmyOurHalf / 200f);
            if (target > aggrScore) aggrScore = Lerp(aggrScore, target, 1f - Mathf.Exp(-dt / 3f));
            else aggrScore = Lerp(aggrScore, target, 1f - Mathf.Exp(-dt / 240f));
            s.pressure = aggrScore;
        }

        static float Tol(UnitType t) => Defs.Get(t).building ? 2f : 7f;

        void Observe()
        {
            var s = Snap;
            float keepEst = s.eArmyEstimate, keepCommit = s.eCommitPeak, keepPressure = s.pressure;
            s.Clear();
            s.eArmyEstimate = keepEst; s.eCommitPeak = keepCommit; s.pressure = keepPressure;
            int enemy = 1 - team;
            var F = w.factions[team];
            s.ore = F.ore;
            s.supplyUsed = F.supplyUsed;
            s.supplyCap = F.supplyCap;
            float now = w.time;

            foreach (var e in w.units)
            {
                if (e == null || e.dying) continue;
                if (e.team == team)
                {
                    if (!e.Complete) { s.pending++; s.pendingType[(int)e.Type]++; continue; }
                    switch (e.Type)
                    {
                        case UnitType.Worker: s.workers++; break;
                        case UnitType.Trooper: s.troopers++; s.armyValue += 50f; break;
                        case UnitType.Mauler: s.maulers++; s.armyValue += 150f; break;
                        case UnitType.Skimmer: s.skimmers++; s.armyValue += 75f; break;
                        case UnitType.Foundry: s.foundries++; break;
                        case UnitType.Garrison: s.garrisons++; break;
                        case UnitType.Workshop: s.workshops++; break;
                        case UnitType.Bunkhouse: s.bunkhouses++; break;
                        case UnitType.Sentinel: s.sentinels++; break;
                    }
                    foreach (var q in e.queue) s.queuedType[(int)q]++;
                    continue;
                }
                if (e.team != enemy) continue;
                // Enemy: only if one of our units can currently see that cell.
                if (!w.Visible(team, e.pos)) continue;

                bool merged = false;
                float tol = Tol(e.Type);
                foreach (var r in mem)
                {
                    if (r.type != e.Type) continue;
                    if ((r.pos - e.pos).magnitude <= tol)
                    {
                        r.pos = e.pos;
                        r.lastSeen = now;
                        r.stillVisible = true;
                        merged = true;
                        break;
                    }
                }
                if (!merged)
                    mem.Add(new Remembered { type = e.Type, pos = e.pos, firstSeen = now, lastSeen = now, stillVisible = true });
            }

            Vector2 believedBase = guesses.Count > 0 ? guesses[0] : Home;
            float bestBaseScore = -1f;
            foreach (var r in mem)
            {
                if (!Defs.Get(r.type).building) continue;
                float wgt = r.type == UnitType.Foundry ? 3f : 1f;
                if (wgt > bestBaseScore) { bestBaseScore = wgt; believedBase = r.pos; s.enemyBaseKnown = true; }
            }

            foreach (var r in mem)
            {
                switch (r.type)
                {
                    case UnitType.Worker: s.eWorkers++; break;
                    case UnitType.Trooper: s.eTroopers++; s.eArmyValue += 50f; break;
                    case UnitType.Mauler: s.eMaulers++; s.eArmyValue += 150f; break;
                    case UnitType.Skimmer: s.eSkimmers++; s.eArmyValue += 75f; break;
                    case UnitType.Foundry: s.eFoundries++; break;
                    case UnitType.Garrison: s.eGarrisons++; break;
                    case UnitType.Workshop: s.eWorkshops++; break;
                    case UnitType.Bunkhouse: s.eBunkhouses++; break;
                    case UnitType.Sentinel: s.eSentinels++; break;
                }
                var D = Defs.Get(r.type);
                if (!D.building && r.type != UnitType.Worker)
                {
                    float dHome = (r.pos - Home).magnitude;
                    if (dHome < 70f) s.eArmyNearOurBase += Defs.ArmyValue(r.type);
                    // Anything of theirs on our side of the map is aggression, wherever
                    // the fight actually happens -- engagements rarely occur in the base.
                    float dThem = (r.pos - (s.enemyBaseKnown ? believedBase : guesses[0])).magnitude;
                    if (dHome < dThem)
                    {
                        s.eArmyOurHalf += Defs.ArmyValue(r.type);
                        if (now - r.lastSeen < 6f) aggrT = now;
                    }
                }
            }
            s.enemyBase = s.enemyBaseKnown ? believedBase : guesses[0];

            if (s.enemyBaseKnown)
                foreach (var r in mem)
                {
                    if (Defs.Get(r.type).building || r.type == UnitType.Worker) continue;
                    if ((r.pos - s.enemyBase).magnitude > 45f) s.eArmyAwayFromHome += Defs.ArmyValue(r.type);
                }

            // How current is our picture of them? Purely a function of when we
            // last looked at the place we believe they live.
            s.sinceAggression = aggrT < -1e8f ? 1e9f : now - aggrT;
            float stale = w.Staleness(team, s.enemyBase);
            s.scoutConfidence = s.enemyBaseKnown ? Mathf.Exp(-stale / 40f) : 0f;
        }

        void Forget()
        {
            float now = w.time;
            foreach (var r in mem) r.stillVisible = false;
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.team == team || e.team == 2) continue;
                if (!w.Visible(team, e.pos)) continue;
                float tol = Tol(e.Type);
                foreach (var r in mem)
                    if (r.type == e.Type && (r.pos - e.pos).magnitude <= tol) { r.stillVisible = true; break; }
            }
            // Evidence of absence: if we can see where we last saw something and it
            // is not there, drop it -- "I looked and it's gone" is information.
            mem.RemoveAll(r =>
            {
                if (r.stillVisible) return false;
                if (w.Visible(team, r.pos)) return true;
                if (Defs.Get(r.type).building) return false;  // buildings do not move
                return now - r.lastSeen > 50f;
            });
        }
    }
}
