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
        float eMechAntiLight = 0.5f, eMechHpFrac = 1f;
        bool eMechDesignKnown;
        readonly List<Vector2> guesses = new List<Vector2>(3);
        float aggrT = -1e9f;     // last time we saw them pressuring our base
        float aggrScore;         // cumulative aggression: a rusher stays high between waves
        float armyEst;           // decaying estimate of their total army
        float commitPeak;        // decaying peak of committed force
        // The memory entries this tick's sightings were matched to (Observe): one each, so an
        // entry is one unit, and "in sight now" is exactly these (Forget).
        readonly HashSet<Remembered> claimed = new HashSet<Remembered>();

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
            for (int i = 0; i < guesses.Count; i++) guesses[i] = w.NearestReachable(Home, guesses[i]);
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
            claimed.Clear();
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
                    if (e.deserted) continue;   // gone from the army (GameWorld.Morale)
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
                        case UnitType.MechBay: s.mechBay = true; break;
                        case UnitType.Mech: s.mechAlive = true; s.mechHpFrac = e.hp / Mathf.Max(1f, e.MaxHp); break;
                    }
                    foreach (var q in e.queue) s.queuedType[(int)q]++;
                    continue;
                }
                if (e.team != enemy) continue;
                // Enemy: only if one of our units can currently see that cell.
                if (!w.Visible(team, e.pos)) continue;
                // Their Mech: its guns are on the outside, so seeing it is knowing what it
                // is built to kill.
                if (e.Type == UnitType.Mech && e.mech != null && e.mech.design != null)
                {
                    float lightDps = 0f, heavyDps = 0f;
                    foreach (var g in e.mech.guns)
                    {
                        lightDps += g.part.Dps(false) * (g.part.antiGroup ? 2f : 1f);
                        heavyDps += g.part.Dps(true);
                    }
                    eMechAntiLight = lightDps / Mathf.Max(1f, lightDps + heavyDps);
                    eMechDesignKnown = true;
                    eMechHpFrac = e.hp / Mathf.Max(1f, e.MaxHp);
                }

                // Each sighting takes the nearest entry of its kind that no other sighting has
                // taken this tick: within a few metres, or -- a unit out of sight a while -- as
                // far as it could have walked since, so the same Troopers coming back into view
                // further on are not counted twice. Taking the first entry within reach folded
                // a whole clump into one: five Troopers side by side were remembered as one or
                // two, and a rush read as a raid.
                Remembered hit = null;
                float tol = Tol(e.Type), hitKey = float.MaxValue;
                bool walks = !e.def.building;
                foreach (var r in mem)
                {
                    if (r.type != e.Type) continue;
                    float d = (r.pos - e.pos).magnitude;
                    float reach = walks ? Mathf.Min(60f, tol + 6f * (now - r.lastSeen)) : tol;
                    if (d > reach || claimed.Contains(r)) continue;
                    float key = d <= tol ? d : 1000f + d;   // one close by before one that walked
                    if (key < hitKey) { hitKey = key; hit = r; }
                }
                if (hit == null)
                {
                    hit = new Remembered { type = e.Type, firstSeen = now };
                    mem.Add(hit);
                }
                hit.pos = e.pos;
                hit.lastSeen = now;
                hit.stillVisible = true;
                hit.unfinished = e.def.building && !e.Complete;
                claimed.Add(hit);
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
                    case UnitType.Mech:
                        s.eMech = true; s.eMechPos = r.pos;
                        s.eMechSeenAgo = w.time - r.lastSeen;
                        s.eMechAntiLight = eMechAntiLight; s.eMechDesignKnown = eMechDesignKnown;
                        s.eMechHpFrac = eMechHpFrac;
                        break;
                    case UnitType.MechBay: s.eMechBay = true; s.eMechBayPos = r.pos; break;
                }
                var D = Defs.Get(r.type);
                // The Mech is nobody's style: it is left out of the read of the player.
                if (!D.building && r.type != UnitType.Worker && r.type != UnitType.Mech)
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
                    if (Defs.Get(r.type).building || r.type == UnitType.Worker || r.type == UnitType.Mech) continue;
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
            // In sight now is what Observe matched this tick's sightings to. (Matching again
            // here, first come first served, kept one entry per clump and dropped the rest as
            // "looked and it's gone".)
            foreach (var r in mem) r.stillVisible = claimed.Contains(r);
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
