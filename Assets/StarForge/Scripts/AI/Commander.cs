// Commander.cs — orchestration: macro, scouting, tactics and micro.
//
// Every order in this file goes through GameWorld's public Cmd* API (the same
// entry points the mouse drives) and is paid for out of ActionBudget, so the
// AI's click rate is bounded exactly like a human's.
//
// Variety: each match draws a Personality (opening, unit mix, timing, taste for
// flanks and second prongs), steering away from how its last few matches went
// (AIMemory). Each attack wave then picks a target (the base, production, an
// outlying expansion, the worker line), an approach (straight in, or round
// either flank, by where the influence map says the enemy is weakest, and not
// the way the last failed wave went), gathers at a staging point before it goes
// in, and -- when the army is big enough and the style allows -- sends a second,
// smaller prong at something else at the same time.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed class Commander
    {
        struct MacroPlan
        {
            public int workers, garrisons, workshops, skimmers, sentinels;
            public float trooperBias;      // share of garrison production spent on troopers vs waiting for maulers
            public bool wantExpand;
            public float pushThreshold;    // army value before the main squad commits
        }

        /// <summary>A plan shaded by this match's personality and, for the first few
        /// minutes, by its opening.</summary>
        MacroPlan Plan(Strategy st)
        {
            var p = PlanFor(st);
            var pe = Personality;
            p.trooperBias = Mathf.Clamp01(p.trooperBias + pe.trooperShift);
            if (p.skimmers > 0 || st == Strategy.Eco) p.skimmers += pe.extraSkimmers;
            if (p.workshops > 0) p.workshops = Mathf.Max(1, p.workshops + pe.workshopShift);
            p.pushThreshold *= pe.timing;
            if (w.time < 210f)
            {
                switch (pe.opening)
                {
                    case Opening.FastExpand: p.wantExpand = true; p.workers = Mathf.Max(p.workers, 22); break;
                    case Opening.EarlyGarrison: p.garrisons = Mathf.Max(p.garrisons, 2); p.workers = Mathf.Min(p.workers, 14); break;
                    case Opening.SkimmerFirst: p.skimmers = Mathf.Max(p.skimmers, 2); break;
                }
            }
            return p;
        }

        static MacroPlan PlanFor(Strategy s)
        {
            switch (s)
            {
                case Strategy.TrooperRush:   return new MacroPlan { workers = 12, garrisons = 3, workshops = 0, skimmers = 0, sentinels = 0, trooperBias = 1.00f, wantExpand = false, pushThreshold = 200f };
                case Strategy.Harass:        return new MacroPlan { workers = 16, garrisons = 2, workshops = 1, skimmers = 4, sentinels = 0, trooperBias = 0.80f, wantExpand = false, pushThreshold = 500f };
                case Strategy.TimingPush:    return new MacroPlan { workers = 17, garrisons = 2, workshops = 2, skimmers = 1, sentinels = 0, trooperBias = 0.55f, wantExpand = false, pushThreshold = 420f };
                case Strategy.TurtleTech:    return new MacroPlan { workers = 17, garrisons = 2, workshops = 2, skimmers = 1, sentinels = 3, trooperBias = 0.35f, wantExpand = false, pushThreshold = 900f };
                case Strategy.Expand:        return new MacroPlan { workers = 22, garrisons = 2, workshops = 1, skimmers = 1, sentinels = 1, trooperBias = 0.55f, wantExpand = true, pushThreshold = 700f };
                case Strategy.CounterAttack: return new MacroPlan { workers = 16, garrisons = 2, workshops = 2, skimmers = 2, sentinels = 0, trooperBias = 0.60f, wantExpand = false, pushThreshold = 180f };
                case Strategy.Feint:         return new MacroPlan { workers = 18, garrisons = 2, workshops = 2, skimmers = 1, sentinels = 0, trooperBias = 0.55f, wantExpand = false, pushThreshold = 450f };
                default:                     return new MacroPlan { workers = 20, garrisons = 1, workshops = 1, skimmers = 1, sentinels = 1, trooperBias = 0.60f, wantExpand = true, pushThreshold = 800f };
            }
        }

        // Orders are only worth re-issuing when something actually changed;
        // re-clicking the same destination every tick would burn the APM budget.
        static bool WorthReissuing(Vector2 want, Vector2 have, float slack) => (want - have).magnitude > slack;

        GameWorld w;
        int team;
        public readonly Perception Perception = new Perception();
        public readonly OpponentModel Opponent = new OpponentModel();
        public readonly StrategySelector Selector = new StrategySelector();
        public readonly InfluenceMap Influence = new InfluenceMap();
        public readonly ActionBudget Budget = new ActionBudget();
        public readonly AIDebug Dbg = new AIDebug();
        public AIDifficulty Difficulty { get; private set; }
        public readonly Personality Personality = new Personality();
        public AIMemoryData Memory => memory;
        public int Team => team;

        Rng rng = new Rng(11);
        float thinkT, macroT, scoutT, tacticT, microT;
        float thinkPeriod = 1f / 6f;
        float reactionDelay = 0.22f;
        Unit focusTarget;

        readonly List<Unit> main = new List<Unit>(), harass = new List<Unit>(), scouts = new List<Unit>();
        readonly List<Unit> single = new List<Unit>(1);
        Vector2 rally, harassTarget;
        bool committed;
        // The current attack wave.
        bool waveOn, staging, prongOn;
        float waveStart, stageUntil;
        Vector2 stagePoint, waveAt, prongAt;
        Approach approach, lastFailedApproach = (Approach)(-1);
        WaveTarget waveTarget;
        readonly List<Unit> prong = new List<Unit>();
        // Artillery that keeps shelling the ground where a target used to be.
        readonly List<Unit> stuck = new List<Unit>();
        /// <summary>How many times artillery was moved up after missing (for the
        /// benchmark report and the editor's trial).</summary>
        public int Repositions { get; private set; }
        /// <summary>Editor A/B only: switch the move-up off to measure what it is worth.</summary>
        public static bool RepositionOff;
        float commitT, lastSelHash, retreatUntil;
        int scoutGuess;

        AIMemoryData memory;
        bool learn;
        readonly float[] styleAccum = new float[(int)PlayerStrat.Count];

        public void Init(GameWorld world, int t, uint seed, AIDifficulty difficulty, bool useMemory, IInfluenceBackend gpu = null)
        {
            w = world;
            team = t;
            rng = new Rng(seed ^ 0x9E3779B9u);
            Difficulty = difficulty;
            learn = useMemory;
            memory = useMemory ? AIMemory.Load() : new AIMemoryData();
            float trust = useMemory ? AIMemory.Trust(memory) : 0f;

            Perception.Init(world, t);
            Opponent.Init(memory.styleHistogram, trust * 0.5f);
            Selector.Init(seed, memory.weights, trust);
            DrawPersonality(seed);
            // Only matches it learns from count as history: with memory off (the
            // benchmark, evaluations) every match starts from the seed alone.
            Selector.SetBias(Personality, learn ? memory.recentPlans : null, learn ? memory.recentWins : null);
            Influence.SetBackend(gpu);
            ConfigureDifficulty(difficulty);

            main.Clear(); harass.Clear(); scouts.Clear(); prong.Clear();
            rally = Perception.Home;
            committed = false;
            waveOn = staging = prongOn = false;
            lastFailedApproach = (Approach)(-1);
            scoutGuess = 0;
            thinkT = macroT = scoutT = tacticT = microT = 0f;
            System.Array.Clear(styleAccum, 0, styleAccum.Length);
            Dbg.memoryGames = memory.games;
        }

        void DrawPersonality(uint seed)
        {
            var r = new Rng(seed ^ 0x51A7E5u);
            var pe = Personality;
            // The opening: any but the ones of the last two matches it learned from.
            var recent = learn && memory.recentOpenings != null ? memory.recentOpenings : new int[0];
            var options = new List<Opening>();
            for (int i = 0; i < (int)Opening.Count; i++)
            {
                bool used = false;
                for (int k = Mathf.Max(0, recent.Length - 2); k < recent.Length; k++) if (recent[k] == i) used = true;
                if (!used) options.Add((Opening)i);
            }
            if (options.Count == 0) options.Add(Opening.Standard);
            pe.opening = options[r.IRange(0, options.Count)];
            pe.trooperShift = r.Range(-0.2f, 0.2f);
            pe.extraSkimmers = r.IRange(0, 3);
            pe.workshopShift = r.IRange(-1, 2);
            pe.timing = r.Range(0.8f, 1.3f);
            pe.flankTaste = r.F01();
            pe.raidTaste = r.F01();
            for (int i = 0; i < pe.taste.Length; i++) pe.taste[i] = r.Range(-0.12f, 0.12f);
            Dbg.personality = pe.Describe();
        }

        void ConfigureDifficulty(AIDifficulty d)
        {
            Budget.tokens = 0f;
            switch (d)
            {
                case AIDifficulty.Recruit:
                    Budget.regen = 1.5f; Budget.cap = 6f; reactionDelay = 0.9f; thinkPeriod = 0.5f; break;
                case AIDifficulty.Veteran:
                    Budget.regen = 3.0f; Budget.cap = 10f; reactionDelay = 0.45f; thinkPeriod = 0.25f; break;
                default:
                    // Sized against a top StarCraft II professional: ~330 sustained APM.
                    Budget.regen = 5.5f; Budget.cap = 14f; reactionDelay = 0.22f; thinkPeriod = 1f / 6f; break;
            }
        }

        // ------------------------------------------------------------ order paths
        List<Unit> OwnOf(UnitType t, bool idleOnly = false)
        {
            var v = new List<Unit>();
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.team != team || e.Type != t || !e.Complete) continue;
                if (idleOnly && e.order != Order.Idle) continue;
                v.Add(e);
            }
            return v;
        }

        IReadOnlyList<Unit> Single(Unit u)
        {
            single.Clear();
            single.Add(u);
            return single;
        }

        /// <summary>A human pays for the selection and then for the order: re-ordering the same
        /// group costs one action, switching groups costs two.</summary>
        bool Issue(IReadOnlyList<Unit> sel, int kind, Vector2 pos, Unit target)
        {
            if (sel.Count == 0) return false;
            float h = 0f;
            foreach (var u in sel) if (u != null) h += u.id & 0xFFFF;
            h += sel.Count * 7919f;
            int cost = Mathf.Abs(h - lastSelHash) > 0.5f ? 2 : 1;
            if (!Budget.Spend(cost)) return false;
            Budget.Note(w.time);
            lastSelHash = h;
            switch (kind)
            {
                case 0: w.CmdMove(sel, pos, false); break;
                case 1: w.CmdMove(sel, pos, true); break;
                case 2: w.CmdAttack(sel, target); break;
                case 3: w.CmdStop(sel); break;
                case 4: w.CmdHarvest(sel, target); break;
            }
            return true;
        }

        bool Train(Unit building, UnitType what)
        {
            if (!Budget.Spend(2)) return false;            // select structure + hotkey
            Budget.Note(w.time);
            lastSelHash = -1f;
            return w.CmdTrain(building, what);
        }

        bool Build(Unit worker, UnitType what, Vector2 where)
        {
            if (!Budget.Spend(3)) return false;            // select Digger + hotkey + click
            Budget.Note(w.time);
            lastSelHash = -1f;
            return w.CmdBuild(Single(worker), what, where);
        }

        bool Reserved(Unit u) => scouts.Contains(u) || harass.Contains(u) || main.Contains(u) || prong.Contains(u);

        bool PlaceNear(UnitType what, Vector2 around, float rmin, float rmax, out Vector2 spot)
        {
            for (int i = 0; i < 26; i++)
            {
                float a = rng.F01() * TAU;
                float r = rmin + (rmax - rmin) * Mathf.Sqrt(rng.F01());
                var p = around + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (w.CanPlace(what, p)) { spot = p; return true; }
            }
            spot = default;
            return false;
        }

        // ------------------------------------------------------------ tick
        public void Update(float dt)
        {
            if (w == null || w.winner >= 0) return;
            Budget.Tick(dt);
            Perception.Update(dt);

            thinkT -= dt;
            if (thinkT <= 0f) { thinkT = thinkPeriod; Think(thinkPeriod); }
            macroT -= dt;
            if (macroT <= 0f) { macroT = 0.28f; RunMacro(); }
            scoutT -= dt;
            if (scoutT <= 0f) { scoutT = 1.1f; RunScouts(); }
            tacticT -= dt;
            if (tacticT <= 0f) { tacticT = 0.42f; RunTactics(); }
            microT -= dt;
            if (microT <= 0f) { microT = Unit.Live(focusTarget) ? 0.16f : 0.34f; RunMicro(); }

            Dbg.apm = Budget.MeasuredAPM();
            Dbg.apmPeak = Budget.PeakAPM();
        }

        void Think(float dt)
        {
            Opponent.Update(Perception, dt, w.time);
            Influence.Build(Perception, w, team);
            Selector.Update(Perception.Snap, Opponent, w.time, dt);

            // Accumulate the read of the player's style, weighted by how much we
            // actually saw, for the cross-match memory.
            float wgt = dt * (0.3f + 0.7f * Perception.Snap.scoutConfidence);
            for (int i = 0; i < styleAccum.Length; i++) styleAccum[i] += Opponent.Belief((PlayerStrat)i) * wgt;

            var s = Perception.Snap;
            Dbg.strategy = Selector.Current;
            Dbg.believed = Opponent.MostLikely();
            for (int i = 0; i < (int)PlayerStrat.Count; i++) Dbg.beliefs[i] = Opponent.Belief((PlayerStrat)i);
            for (int i = 0; i < (int)Strategy.Count; i++) Dbg.stratScores[i] = Selector.ScoreOf((Strategy)i);
            Dbg.entropy = Opponent.Entropy();
            Dbg.scoutConfidence = s.scoutConfidence;
            Dbg.confidence = Selector.Confidence;
            Dbg.actions = Budget.lifetime;
            Dbg.denied = Budget.denied;
            Dbg.reason = Selector.Reason;
            Dbg.switches = Selector.Switches;
            Dbg.enemyBaseKnown = s.enemyBaseKnown;
            Dbg.enemyBase = s.enemyBase;
            Dbg.squads = main.Count;
            Dbg.question = Opponent.MostValuableQuestion;
            Dbg.backend = Influence.BackendName;
            Dbg.influenceMs = Influence.BackendMs;
            var p = Opponent.Profile;
            Dbg.aggression = p.aggression; Dbg.expansion = p.expansion; Dbg.defensive = p.defensive;
            Dbg.harass = p.harass; Dbg.teching = p.teching; Dbg.volatility = p.volatility;
        }

        // ------------------------------------------------------------ macro
        void RunMacro()
        {
            var s = Perception.Snap;
            var plan = Plan(Selector.Current);
            var F = w.factions[team];
            Vector2 home = Perception.Home;

            var freeWorkers = new List<Unit>();
            foreach (var h in OwnOf(UnitType.Worker))
                if (!Reserved(h) && h.order != Order.Build) freeWorkers.Add(h);
            Unit Builder() => freeWorkers.Count == 0 ? null : freeWorkers[rng.IRange(0, freeWorkers.Count)];

            // A human macros in bursts and is limited by hands, not by a one-action-
            // per-tick rule. The budget is the real constraint, so this is a
            // prioritised pass rather than returning after the first success.
            int issued = 0;
            const int MaxPerPass = 4;

            // 1. Supply, gated on bunkhouses specifically.
            if (issued < MaxPerPass && s.supplyCap - s.supplyUsed < 6 && s.supplyCap < GameWorld.MaxSupply &&
                F.ore >= Defs.Get(UnitType.Bunkhouse).cost && s.pendingType[(int)UnitType.Bunkhouse] < 2)
            {
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Bunkhouse, home, 10f, 26f, out var spot) && Build(b, UnitType.Bunkhouse, spot)) issued++;
            }

            // 2. Production structures, at most two going up at once.
            int extraProd = F.ore > 550 ? 2 : (F.ore > 320 ? 1 : 0);
            int wantRax = Mathf.Min(5, plan.garrisons + extraProd);
            int wantFac = Mathf.Min(3, plan.workshops + (plan.workshops > 0 ? extraProd : 0));
            if (issued < MaxPerPass && s.garrisons + s.pendingType[(int)UnitType.Garrison] < wantRax &&
                F.ore >= Defs.Get(UnitType.Garrison).cost && s.pending < 2)
            {
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Garrison, home, 12f, 28f, out var spot) && Build(b, UnitType.Garrison, spot)) issued++;
            }
            if (issued < MaxPerPass && s.garrisons >= 1 && s.workshops + s.pendingType[(int)UnitType.Workshop] < wantFac &&
                F.ore >= Defs.Get(UnitType.Workshop).cost && s.pending < 2)
            {
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Workshop, home, 13f, 29f, out var spot) && Build(b, UnitType.Workshop, spot)) issued++;
            }

            // 2b. Sentinels on the approach: the turtle's answer, and insurance
            //     whenever the model smells early aggression.
            int wantSent = plan.sentinels + (Opponent.ThreatOfEarlyAggression > 0.55f ? 1 : 0);
            if (issued < MaxPerPass && s.garrisons >= 1 && s.sentinels + s.pendingType[(int)UnitType.Sentinel] < wantSent &&
                F.ore >= Defs.Get(UnitType.Sentinel).cost && s.pending < 2)
            {
                Vector2 face = s.enemyBaseKnown ? s.enemyBase : Perception.BaseGuesses[0];
                Vector2 around = home + Norm(face - home) * 17f;
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Sentinel, around, 0f, 10f, out var spot) && Build(b, UnitType.Sentinel, spot)) issued++;
            }

            // 3. Expansion, only onto ground we have explored and that is quiet.
            if (issued < MaxPerPass && plan.wantExpand && F.ore >= Defs.Get(UnitType.Foundry).cost &&
                s.foundries + s.pendingType[(int)UnitType.Foundry] < 2 && s.pending < 2)
            {
                Vector2 best = default;
                float bestScore = -1e30f;
                foreach (var e in w.units)
                {
                    if (e == null || e.dying || e.Type != UnitType.Ore) continue;
                    if (!w.Explored(team, e.pos)) continue;
                    float dHome = (e.pos - home).magnitude;
                    if (dHome < 26f || dHome > 110f) continue;
                    float score = -dHome * 0.02f - Influence.Sample(2, e.pos) * 3f;
                    if (score > bestScore) { bestScore = score; best = e.pos; }
                }
                if (bestScore > -1e29f && PlaceNear(UnitType.Foundry, best, 8f, 16f, out var spot))
                {
                    var b = Builder();
                    if (b != null && Build(b, UnitType.Foundry, spot)) issued++;
                }
            }

            // 4. Workers -- never allowed to block army production below.
            if (issued < MaxPerPass && s.workers + s.queuedType[(int)UnitType.Worker] < plan.workers &&
                F.ore >= Defs.Get(UnitType.Worker).cost)
            {
                foreach (var e in w.units)
                {
                    if (e == null || e.dying || e.team != team || e.Type != UnitType.Foundry || !e.Complete || e.queue.Count > 0) continue;
                    if (Train(e, UnitType.Worker)) issued++;
                    break;
                }
            }

            // 5. Army from every idle production structure. Floating ore is the
            //    single most common way for an RTS bot to lose a game it should win.
            bool rich = F.ore > 400;
            for (int i = 0; i < w.units.Count && issued < MaxPerPass; i++)
            {
                var e = w.units[i];
                if (e == null || e.dying || e.team != team || !e.Complete || e.queue.Count >= 2) continue;
                if (e.Type == UnitType.Garrison)
                {
                    int skimHave = s.skimmers + s.queuedType[(int)UnitType.Skimmer];
                    if (skimHave < plan.skimmers && F.ore >= Defs.Get(UnitType.Skimmer).cost)
                    {
                        if (Train(e, UnitType.Skimmer)) { issued++; s.queuedType[(int)UnitType.Skimmer]++; }
                    }
                    else if (F.ore >= Defs.Get(UnitType.Trooper).cost &&
                             (rich || s.workshops == 0 || rng.F01() < plan.trooperBias))
                    {
                        if (Train(e, UnitType.Trooper)) issued++;
                    }
                }
                else if (e.Type == UnitType.Workshop && F.ore >= Defs.Get(UnitType.Mauler).cost)
                {
                    if (Train(e, UnitType.Mauler)) issued++;
                }
            }

            // 6. Idle workers back to ore (scouts and squad members excluded).
            var idle = new List<Unit>();
            foreach (var h in OwnOf(UnitType.Worker, true)) if (!Reserved(h)) idle.Add(h);
            if (idle.Count > 0 && issued < MaxPerPass)
            {
                foreach (var n in w.units)
                {
                    if (n == null || n.dying || n.Type != UnitType.Ore || n.oreLeft <= 0) continue;
                    if ((n.pos - home).magnitude > 50f) continue;
                    Issue(idle, 4, n.pos, n);
                    break;
                }
            }
        }

        // ------------------------------------------------------------ scouting
        void RunScouts()
        {
            var s = Perception.Snap;
            scouts.RemoveAll(u => !Unit.Live(u));

            // Keep one scout alive whenever our picture has gone stale. A stale model
            // is worse than none: the strategy layer acts confidently on it.
            bool wantScout = (!s.enemyBaseKnown && w.time > 8f) || (s.scoutConfidence < 0.40f && w.time > 25f);
            if (wantScout && scouts.Count == 0)
            {
                Unit pick = null;
                var sk = OwnOf(UnitType.Skimmer);
                if (sk.Count > 0) pick = sk[sk.Count - 1];            // the Skimmer is built for this
                else
                {
                    var tr = OwnOf(UnitType.Trooper);
                    if (tr.Count >= 6) pick = tr[tr.Count - 1];
                    else
                    {
                        var ws = OwnOf(UnitType.Worker);
                        if (ws.Count >= 6) pick = ws[ws.Count - 1];
                    }
                }
                if (pick != null)
                {
                    main.Remove(pick);
                    harass.Remove(pick);
                    scouts.Add(pick);
                }
            }
            if (!wantScout && scouts.Count > 0 && s.scoutConfidence > 0.8f)
            {
                scouts.Clear();   // release it back to the army or the economy
                return;
            }

            foreach (var h in scouts)
            {
                bool arrived = h.Dist(Dbg.scoutTarget) < 9f;
                bool busy = h.order == Order.Move || h.order == Order.AttackMove;
                if (busy && !arrived) continue;

                if (!s.enemyBaseKnown)
                {
                    scoutGuess++;
                    var g = Perception.BaseGuesses[scoutGuess % Perception.BaseGuesses.Count];
                    if (Issue(Single(h), 0, g, null)) Dbg.scoutTarget = g;
                    continue;
                }

                // Uncertainty-driven: sample around whatever we are least sure about,
                // preferring stale ground and avoiding known threat.
                int q = Opponent.MostValuableQuestion;
                Vector2 anchor = s.enemyBase;
                if (q == 2)
                {
                    float bestD = 1e30f;
                    foreach (var n in w.units)
                    {
                        if (n == null || n.dying || n.Type != UnitType.Ore) continue;
                        float d = (n.pos - s.enemyBase).magnitude;
                        if (d > 25f && d < bestD) { bestD = d; anchor = n.pos; }
                    }
                }
                Vector2 best = anchor;
                float bestScore = -1e30f;
                for (int i = 0; i < 28; i++)
                {
                    float a = rng.F01() * TAU, r = rng.Range(0f, 30f);
                    var c = w.NearestWalkable(anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, 12f);
                    float score = Influence.Sample(3, c) * 2f - Influence.Sample(2, c) * 1.2f;
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                if (Issue(Single(h), 0, best, null)) Dbg.scoutTarget = best;
            }
        }

        // ------------------------------------------------------------ tactics
        void RunTactics()
        {
            var s = Perception.Snap;
            var plan = Plan(Selector.Current);
            var st = Selector.Current;

            main.RemoveAll(u => !Unit.Live(u));
            harass.RemoveAll(u => !Unit.Live(u));
            scouts.RemoveAll(u => !Unit.Live(u));
            prong.RemoveAll(u => !Unit.Live(u));

            // Assign fresh army units to a squad. Skimmers are the natural raiders.
            int harassWant = st == Strategy.Harass ? 4 : 0;
            foreach (var t in new[] { UnitType.Skimmer, UnitType.Trooper, UnitType.Mauler })
                foreach (var h in OwnOf(t))
                {
                    if (Reserved(h)) continue;
                    if (t != UnitType.Mauler && harass.Count < harassWant) harass.Add(h);
                    else main.Add(h);
                }
            if (harassWant == 0 && harass.Count > 0)
            {
                main.AddRange(harass);   // fold raiders back in when the plan changes
                harass.Clear();
            }
            Dbg.squads = main.Count;

            // Home defence. Only fresh sightings count: remembered attackers linger,
            // and counting them pinned the whole army at home in the original.
            float threatHome = 0f;
            Vector2 home = Perception.Home;
            Vector2 threatAt = home;
            foreach (var r in Perception.Enemies)
            {
                if (Defs.Get(r.type).building) continue;
                if (!Perception.Reactable(r, reactionDelay)) continue;   // no instant reactions
                if (w.time - r.lastSeen > 10f) continue;                  // stale: not a live threat
                float d = (r.pos - home).magnitude;
                if (d < 55f)
                {
                    threatHome += r.type == UnitType.Worker ? 15f : Defs.ArmyValue(r.type);
                    if (d < (threatAt - home).magnitude || threatAt == home) threatAt = r.pos;
                }
            }

            Vector2 mainCentre = Vector2.zero;
            int n = 0;
            foreach (var h in main) { mainCentre += h.pos; n++; }
            if (n > 0) mainCentre /= n;

            // Divert only if the raid matters relative to what we field.
            float divertAt = Mathf.Max(45f, s.armyValue * 0.14f);
            if (threatHome > divertAt && main.Count > 0)
            {
                if (WorthReissuing(threatAt, rally, 12f) && Issue(main, 1, threatAt, null)) rally = threatAt;
                committed = false;
                EndWave(false);
                return;
            }

            // Retreat when the local balance is bad.
            if (n > 0 && committed)
            {
                float mine = Influence.Sample(0, mainCentre);
                float theirs = Influence.Sample(1, mainCentre);
                if (theirs > mine * 1.5f + 0.5f && w.time > retreatUntil)
                {
                    var safe = Influence.SafestNear(home, 30f);
                    if (Issue(main, 0, safe, null))
                    {
                        rally = safe;
                        committed = false;
                        retreatUntil = w.time + 8f;   // don't oscillate
                        EndWave(false);
                    }
                    return;
                }
            }

            // Harass squad: go for workers, never trade with the army.
            if (harass.Count > 0)
            {
                Vector2 target = s.enemyBase;
                Unit victim = null;
                float bestD = 1e30f;
                foreach (var e in w.units)
                {
                    if (e == null || e.dying || e.team == team || e.team == 2 || e.Type != UnitType.Worker) continue;
                    if (!w.Visible(team, e.pos)) continue;           // must actually see it
                    float d = (e.pos - s.enemyBase).magnitude;
                    if (d < bestD) { bestD = d; victim = e; target = e.pos; }
                }
                Vector2 hc = Vector2.zero;
                foreach (var h in harass) hc += h.pos;
                hc /= harass.Count;
                float theirs = Influence.Sample(1, hc), mine = Influence.Sample(0, hc);
                if (theirs > mine * 2f + 0.8f)
                {
                    var safe = Influence.SafestNear(home, 40f);
                    if (WorthReissuing(safe, harassTarget, 14f) && Issue(harass, 0, safe, null)) harassTarget = safe;
                }
                else if (victim != null)
                {
                    if (WorthReissuing(target, harassTarget, 9f) && Issue(harass, 2, target, victim)) harassTarget = target;
                }
                else if (WorthReissuing(target, harassTarget, 14f) && Issue(harass, 1, target, null)) harassTarget = target;
            }

            if (main.Count == 0) return;

            bool wantAttack;
            Vector2 attackAt = s.enemyBase;
            switch (st)
            {
                case Strategy.CounterAttack:
                    wantAttack = s.eArmyAwayFromHome > 60f && s.armyValue >= plan.pushThreshold;
                    break;
                case Strategy.Feint:
                {
                    // Show up somewhere obvious, then swing. Against a player who
                    // reacts to what they see, the reposition is the whole point.
                    wantAttack = s.armyValue >= plan.pushThreshold;
                    float phase = (w.time - commitT) % 44f;
                    if (phase < 22f)
                    {
                        Vector2 dir = Norm(s.enemyBase - home);
                        attackAt = w.NearestWalkable(s.enemyBase + Perp(dir) * 42f);
                    }
                    break;
                }
                default:
                    wantAttack = s.armyValue >= plan.pushThreshold;
                    break;
            }

            if (wantAttack)
            {
                if (!committed) { committed = true; commitT = w.time; }
                if (st == Strategy.Feint)
                {
                    // The feint does its own positioning (above).
                    bool idle = false;
                    foreach (var h in main) if (h.order == Order.Idle) { idle = true; break; }
                    if ((idle || WorthReissuing(attackAt, rally, 12f)) && Issue(main, 1, attackAt, null)) rally = attackAt;
                    return;
                }
                if (!waveOn) StartWave(s, st, plan, mainCentre);
                RunWave(s, mainCentre);
            }
            else
            {
                committed = false;
                EndWave(true);
                // Hold a defensible spot between home and the likely approach.
                Vector2 face = s.enemyBaseKnown ? s.enemyBase : Perception.BaseGuesses[0];
                Vector2 guard = home + Norm(face - home) * 16f;
                guard = Influence.SafestNear(w.NearestWalkable(guard), 14f);
                bool anyIdle = false;
                foreach (var h in main)
                    if (h.order == Order.Idle && h.Dist(guard) > 14f) { anyIdle = true; break; }
                if ((anyIdle || WorthReissuing(guard, rally, 16f)) && Issue(main, 1, guard, null)) rally = guard;
            }
        }

        // ------------------------------------------------------------ attack waves
        void StartWave(Snapshot s, Strategy st, MacroPlan plan, Vector2 from)
        {
            waveOn = true;
            waveStart = w.time;
            Dbg.waves++;
            // What to go for. Every choice is something the AI has actually seen.
            float rBase = 1f, rProd = 0.7f, rExp = 0.6f, rWork = 0.5f;
            if (st == Strategy.TimingPush) rProd += 0.8f;
            if (st == Strategy.CounterAttack) { rWork += 0.9f; rBase += 0.4f; }
            if (st == Strategy.Expand || st == Strategy.TurtleTech) rExp += 0.7f;
            if (!Remembers(UnitType.Garrison) && !Remembers(UnitType.Workshop)) rProd = 0f;
            if (OutlyingFoundry(s, out _) == false) rExp = 0f;
            float roll = rng.F01() * (rBase + rProd + rExp + rWork);
            waveTarget = roll < rBase ? WaveTarget.Base : roll < rBase + rProd ? WaveTarget.Production
                       : roll < rBase + rProd + rExp ? WaveTarget.Expansion : WaveTarget.Workers;
            waveAt = TargetOf(waveTarget, s, from);

            // How to go in: score each approach by the enemy influence along it (the
            // lower the better), the personality's taste for flanks, and never the way
            // the last beaten wave went.
            Vector2 home = Perception.Home;
            float bestScore = 1e30f;
            approach = Approach.Direct;
            for (int a = 0; a < 3; a++)
            {
                var ap = (Approach)a;
                var via = ApproachPoint(ap, from, waveAt);
                float danger = 0f;
                for (int k = 1; k <= 4; k++)
                {
                    float t = k / 5f;
                    var q = t < 0.5f ? Vector2.Lerp(from, via, t * 2f) : Vector2.Lerp(via, waveAt, (t - 0.5f) * 2f);
                    danger += Influence.Sample(1, q);
                }
                float score = danger + (ap == Approach.Direct ? 0f : 0.6f - Personality.flankTaste) + rng.Range(0f, 0.5f);
                if (ap == lastFailedApproach) score += 2f;
                if (score < bestScore) { bestScore = score; approach = ap; }
            }
            stagePoint = w.NearestWalkable(ApproachPoint(approach, from, waveAt));
            staging = approach != Approach.Direct || (waveAt - from).magnitude > 70f;
            stageUntil = w.time + 22f;

            // A second prong: a few fast units at something else, when there is army
            // to spare and the style likes it.
            prongOn = false;
            if (s.armyValue > plan.pushThreshold * 1.6f && rng.F01() < 0.25f + Personality.raidTaste * 0.5f)
            {
                var other = waveTarget == WaveTarget.Workers ? WaveTarget.Expansion : WaveTarget.Workers;
                if (other == WaveTarget.Expansion && !OutlyingFoundry(s, out _)) other = WaveTarget.Production;
                prongAt = TargetOf(other, s, from);
                if ((prongAt - waveAt).magnitude > 25f)
                {
                    prong.Clear();
                    int want = Mathf.Max(2, main.Count / 4);
                    for (int i = main.Count - 1; i >= 0 && prong.Count < want; i--)
                        if (main[i].Type != UnitType.Mauler) { prong.Add(main[i]); main.RemoveAt(i); }
                    prongOn = prong.Count >= 2;
                    if (!prongOn) { main.AddRange(prong); prong.Clear(); }
                }
            }
            Dbg.wave = $"{waveTarget} via {approach}{(staging ? " (staging)" : "")}{(prongOn ? $", prong of {prong.Count}" : "")}";
        }

        void RunWave(Snapshot s, Vector2 mainCentre)
        {
            // Gather at the staging point until most of the army is there, or it has
            // waited long enough: going in strung out is how waves die.
            if (staging)
            {
                int near = 0;
                foreach (var h in main) if (h.Dist(stagePoint) < 13f) near++;
                if (near >= main.Count * 0.7f || w.time > stageUntil) staging = false;
                else
                {
                    bool idle = false;
                    foreach (var h in main) if (h.order == Order.Idle && h.Dist(stagePoint) > 13f) { idle = true; break; }
                    if ((idle || WorthReissuing(stagePoint, rally, 10f)) && Issue(main, 1, stagePoint, null)) rally = stagePoint;
                    return;
                }
            }
            // Once the target is gone, go on to the nearest structure it remembers.
            if (!StillStanding(waveAt))
            {
                float bestD = 1e30f;
                foreach (var r in Perception.Enemies)
                {
                    if (!Defs.Get(r.type).building) continue;
                    float d = (r.pos - mainCentre).magnitude;
                    if (d < bestD) { bestD = d; waveAt = r.pos; }
                }
                if (bestD > 1e29f) waveAt = s.enemyBase;
            }
            bool anyIdle = false;
            foreach (var h in main) if (h.order == Order.Idle) { anyIdle = true; break; }
            if ((anyIdle || WorthReissuing(waveAt, rally, 12f)) && Issue(main, 1, waveAt, null)) rally = waveAt;

            if (prongOn && prong.Count > 0)
            {
                bool idle = false;
                foreach (var h in prong) if (h.order == Order.Idle) { idle = true; break; }
                if (idle || WorthReissuing(prongAt, harassTarget, 12f))
                    if (Issue(prong, 1, prongAt, null)) harassTarget = prongAt;
            }
        }

        void EndWave(bool calledOff)
        {
            if (!waveOn) return;
            // A wave beaten back (not merely called off by a change of plan) marks
            // its approach as one to avoid next time.
            if (!calledOff && w.time - waveStart > 8f) lastFailedApproach = approach;
            waveOn = staging = false;
            if (prong.Count > 0) { main.AddRange(prong); prong.Clear(); }
            prongOn = false;
        }

        Vector2 ApproachPoint(Approach a, Vector2 from, Vector2 to)
        {
            if (a == Approach.Direct) return Vector2.Lerp(from, to, 0.55f);
            Vector2 dir = Norm(to - from);
            float side = a == Approach.FlankLeft ? 1f : -1f;
            float len = (to - from).magnitude;
            return Vector2.Lerp(from, to, 0.6f) + Perp(dir) * side * Mathf.Clamp(len * 0.35f, 22f, 45f);
        }

        bool Remembers(UnitType t)
        {
            foreach (var r in Perception.Enemies) if (r.type == t) return true;
            return false;
        }

        /// <summary>A remembered enemy Foundry away from their main base.</summary>
        bool OutlyingFoundry(Snapshot s, out Vector2 at)
        {
            at = default;
            float best = 0f;
            foreach (var r in Perception.Enemies)
            {
                if (r.type != UnitType.Foundry) continue;
                float d = (r.pos - s.enemyBase).magnitude;
                if (d > 30f && d > best) { best = d; at = r.pos; }
            }
            return best > 0f;
        }

        Vector2 TargetOf(WaveTarget t, Snapshot s, Vector2 from)
        {
            switch (t)
            {
                case WaveTarget.Production:
                {
                    float bestD = 1e30f;
                    Vector2 at = s.enemyBase;
                    foreach (var r in Perception.Enemies)
                    {
                        if (r.type != UnitType.Garrison && r.type != UnitType.Workshop) continue;
                        float d = (r.pos - from).magnitude;
                        if (d < bestD) { bestD = d; at = r.pos; }
                    }
                    return at;
                }
                case WaveTarget.Expansion:
                    return OutlyingFoundry(s, out var e) ? e : s.enemyBase;
                case WaveTarget.Workers:
                {
                    // The ore their main Foundry mines: the nearest explored seam to it.
                    Vector2 at = s.enemyBase;
                    float bestD = 1e30f;
                    foreach (var n in w.units)
                    {
                        if (n == null || n.dying || n.Type != UnitType.Ore || !w.Explored(team, n.pos)) continue;
                        float d = (n.pos - s.enemyBase).magnitude;
                        if (d < bestD && d < 30f) { bestD = d; at = n.pos; }
                    }
                    return at;
                }
                default:
                    return s.enemyBase;
            }
        }

        /// <summary>Whether the AI still believes something of the enemy's stands there.</summary>
        bool StillStanding(Vector2 at)
        {
            foreach (var r in Perception.Enemies)
                if ((r.pos - at).sqrMagnitude < 12f * 12f) return true;
            return !w.Visible(team, at);   // not seen emptied yet: keep going
        }

        // ------------------------------------------------------------ micro
        void RunMicro()
        {
            // Focus fire: concentrating damage is what a human spends most combat
            // actions on, which is why the AI's APM spikes in fights like a player's.
            if (main.Count == 0 || !Budget.Can(2)) return;
            Vector2 c = Vector2.zero;
            int n = 0;
            foreach (var h in main) if (Unit.Live(h)) { c += h.pos; n++; }
            if (n == 0) return;
            c /= n;
            RepositionStuck();

            Unit best = null;
            float bestScore = 1e30f;
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.team == team || e.team == 2 || e.def.building) continue;
                if (!w.Visible(team, e.pos)) continue;
                float d = (e.pos - c).magnitude;
                if (d > 28f) continue;
                // Prefer the weakest, nearest thing that can shoot back.
                float score = e.hp + d * 2.5f - (e.def.range > 0f ? 45f : 0f);
                if (score < bestScore) { bestScore = score; best = e; }
            }
            if (best == null) { focusTarget = null; return; }
            if (best == focusTarget && Unit.Live(focusTarget)) return;
            if (Issue(main, 2, Vector2.zero, best)) focusTarget = best;
        }

        /// <summary>Move the artillery that keeps missing to where it can hit.
        ///
        /// A Mauler's shell flies almost flat -- at full range it climbs about a metre
        /// over the line -- so a low rise between it and its target catches every
        /// shell, and a target that keeps walking is shelled where it used to be. After
        /// three fruitless shots in a row from the same place, a tank is moved, as a
        /// player would move it: to the nearest of a ring of spots round its target, at
        /// two thirds to four fifths of its range, from which the shell's actual arc
        /// reaches its target rather than a rise short of it (GameWorld.ShellClears --
        /// the terrain is no secret). If
        /// none does, it closes to half range, which is under most rises. A plain move,
        /// not an attack-move: an attack-move halts the moment its target is in range,
        /// which it already is. Each tank is moved on its own target, two at most per
        /// micro tick, and only against a target the team can see.</summary>
        void RepositionStuck()
        {
            if (RepositionOff) return;
            stuck.Clear();
            foreach (var u in main)
            {
                if (!Unit.Live(u) || u.def.splash <= 0f || u.shotsMissed < 3) continue;
                if (!Unit.Live(u.target) || !w.Visible(team, u.target.pos)) continue;
                // Out of range is a different problem, and the wave logic already owns it.
                if (u.Dist(u.target) > u.def.range + 6f) continue;
                if (u.Moving && u.order == Order.Move) continue;      // already on its way
                stuck.Add(u);
                if (stuck.Count == 2) break;
            }
            foreach (var u in stuck)
            {
                var target = u.target;
                Vector2 back = u.pos - target.pos;
                float dist = back.magnitude;
                if (dist < 1e-3f) continue;
                back /= dist;
                float range = u.def.range;
                // Candidates round the target, nearest the way the tank already is
                // first, so a clear spot close to it wins over one on the far side.
                Vector2 dest = Vector2.zero;
                float bestCost = float.MaxValue;
                for (int ring = 0; ring < 2; ring++)
                {
                    float r = range * (ring == 0 ? 0.68f : 0.82f);
                    for (int k = 0; k < 9; k++)
                    {
                        float ang = (k == 0 ? 0f : ((k + 1) / 2) * 25f * (k % 2 == 0 ? 1f : -1f)) * Mathf.Deg2Rad;
                        float c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                        var dir = new Vector2(back.x * c - back.y * sn, back.x * sn + back.y * c);
                        var spot = target.pos + dir * r;
                        if (!w.Map.InBounds(spot, 4f) || w.Map.WaterDepth(spot) > 0.6f) continue;
                        if ((spot - u.pos).sqrMagnitude < 16f) continue;      // not where it is now
                        if (!w.ShellClears(spot, target, u.def.splash)) continue;
                        float cost = (spot - u.pos).magnitude;
                        if (cost < bestCost) { bestCost = cost; dest = spot; }
                    }
                }
                // Nothing clears from out there: close to half range, under most rises.
                if (bestCost == float.MaxValue) dest = target.pos + back * Mathf.Max(6f, range * 0.5f);
                if (Issue(Single(u), 0, dest, null)) Repositions++;
            }
        }

        // ------------------------------------------------------------ memory
        public PlayerStrat DominantStyle()
        {
            int best = 0;
            for (int i = 1; i < styleAccum.Length; i++) if (styleAccum[i] > styleAccum[best]) best = i;
            return (PlayerStrat)best;
        }

        public float StyleShare(PlayerStrat p)
        {
            float sum = 0f;
            foreach (var v in styleAccum) sum += v;
            return sum > 1e-6f ? styleAccum[(int)p] / sum : 0f;
        }

        /// <summary>Fold this match into the cross-match memory and save it.</summary>
        public void EndMatch(bool aiWon)
        {
            if (!learn) return;
            var m = memory;
            m.games++;
            if (aiWon) m.aiWins++;
            float k = m.games == 1 ? 1f : 0.5f;
            m.weights = Selector.ExportWeights();
            float sum = 0f;
            foreach (var v in styleAccum) sum += v;
            if (sum > 1e-6f)
                for (int i = 0; i < styleAccum.Length; i++)
                    m.styleHistogram[i] = Lerp(m.styleHistogram[i], styleAccum[i] / sum, k);
            var p = Opponent.Profile;
            m.aggression = Lerp(m.aggression, p.aggression, k);
            m.expansion = Lerp(m.expansion, p.expansion, k);
            m.defensive = Lerp(m.defensive, p.defensive, k);
            m.harass = Lerp(m.harass, p.harass, k);
            m.teching = Lerp(m.teching, p.teching, k);
            for (int i = 0; i < (int)Strategy.Count; i++) m.strategySeconds[i] += Selector.timeIn[i];
            m.lastRead = AINames.Of(DominantStyle());
            int longest = 0;
            for (int i = 1; i < (int)Strategy.Count; i++) if (Selector.timeIn[i] > Selector.timeIn[longest]) longest = i;
            m.recentOpenings = AIMemory.Push(m.recentOpenings, (int)Personality.opening);
            m.recentPlans = AIMemory.Push(m.recentPlans, longest);
            m.recentWins = AIMemory.Push(m.recentWins, aiWon ? 1 : 0);
            AIMemory.Save(m);
        }
    }
}
