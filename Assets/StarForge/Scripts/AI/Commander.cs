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
//
// Every place it sends the army of its own accord (staging point, guard post,
// fall-back, feint, scouting) is put on the stretch of NavMesh its base stands on
// (GameWorld.NearestReachable): the nearest NavMesh to a point may be a plateau top
// or a clearing ringed by trees, and a wave told to gather there never gathered.
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
            // A raiding personality keeps a pair of Skimmers for its side raids (RunTactics).
            if (pe.raidTaste > 0.5f && w.time > 150f) p.skimmers = Mathf.Max(p.skimmers, 2);
            if (p.workshops > 0) p.workshops = Mathf.Max(1, p.workshops + pe.workshopShift);
            p.pushThreshold *= pe.timing;
            // The Mechs weigh on when to go: its own walks with an attack (MechBrain
            // supports one), theirs stands in the way of one.
            var snap = Perception.Snap;
            if (snap.mechAlive) p.pushThreshold *= 0.85f;
            // Their Mech eats a small army whole: while it is about, wait for a big one --
            // half again as big even with its own Mech walking alongside, and more without.
            if (snap.eMech) p.pushThreshold *= (snap.mechAlive ? 1.5f : 1.9f) * (1f + 0.2f * mechRepulses);
            // ...and with their Mech about and not out in the field (MechAway opens its own window),
            // an army that can break it (the old rule above stays as the floor). That rule topped
            // out near 1,400, a third of what breaking a Mech takes, and it fed the Mech wave after
            // wave: in every match that ran to the cap their Mech stood at its bay on 130-150 kills.
            // (Asked only of a Mech last seen within 55 m of its base, a Mech on guard 55-70 m out
            // -- neither "at home" nor "away" -- got the old rule alone.)
            if (snap.eMech && !MechAway(snap)) p.pushThreshold = Mathf.Max(p.pushThreshold, MechNeed(snap));
            // Build against the Mech it has seen: one made to kill infantry (flame, rotary
            // cannon, missiles) is met with armour; one made to kill armour (lasers, the
            // railgun, the mortar) with a swarm of rifles -- the cheapest answer to a Mech
            // there is (35 Troopers bring one down for 1,750 ore; it takes 21 Maulers, 3,150).
            if (snap.eMech && snap.eMechDesignKnown)
            {
                float lean = (snap.eMechAntiLight - 0.5f) * 2f;   // -1 anti-armour .. +1 anti-infantry
                p.trooperBias = Mathf.Clamp01(p.trooperBias - 0.35f * lean);
                if (lean > 0.25f && p.workshops == 0) p.workshops = 1;
            }
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

        /// <summary>The army it takes to break their Mech (with its own Mech's help if that is fit):
        /// by the trials, 35 Troopers (1,750) bring down one built to kill armour and 21 Maulers
        /// (3,150) one built to burn infantry -- what it builds leans that way once it has seen the
        /// guns (Plan) -- scaled by the health it was last seen with (a health bar is there for
        /// anyone to read), and roughly halved when its own Mech, fit, goes in too.</summary>
        float MechNeed(Snapshot s)
        {
            float worth = Mathf.Lerp(1750f, 3150f, s.eMechDesignKnown ? s.eMechAntiLight : 0.5f);
            return worth * Mathf.Lerp(0.3f, 1f, s.eMechHpFrac) * (s.mechAlive && s.mechHpFrac > 0.6f ? 0.55f : 1f);
        }

        /// <summary>The army a maxed-out side fields: 85% of the supply its Diggers leave, at 50 a
        /// supply (a Trooper 50 for 1, a Mauler 150 for 3).</summary>
        static float MaxedArmy(Snapshot s) => 50f * 0.85f * Mathf.Max(60f, GameWorld.MaxSupply - s.workers);

        /// <summary>The army it masses before a plan wave once their Mech has beaten two back: two and
        /// a half times the plan's threshold (what those waves went in at), or maxed out if that is
        /// less. Maxed out alone (about 7,500 at 22 Diggers) it never had: its army peaked at
        /// 1,000-4,800 by 900 s, so after two repulses no plan wave went again.</summary>
        static float MassedArmy(Snapshot s, MacroPlan plan) => Mathf.Min(MaxedArmy(s), 2.5f * plan.pushThreshold);

        /// <summary>Beaten back twice by their Mech, which is about and at home: plan waves wait for
        /// the massed army.</summary>
        bool MaxingOut(Snapshot s) => s.eMech && !MechAway(s) && mechRepulses >= 2 && !LegacyNoMaxOut;

        /// <summary>Editor A/B only: go on pushing at the Mech threshold after repulses, as before
        /// (`MassedArmy` not asked). The evaluation runner resets it.</summary>
        public static bool LegacyNoMaxOut;

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
        /// <summary>The wider roster's choices (which unit a building trains, what it researches):
        /// their own stream, so the rolls the plan made before -- the trooper share, the Mech's
        /// advice -- come out as they did.</summary>
        Rng mixRng = new Rng(29);
        /// <summary>When an enemy last hit one of its workers (its own alert: what a player hears).</summary>
        float lastWorkerHitAt = -1e9f;
        float thinkT, macroT, scoutT, tacticT, microT;
        float thinkPeriod = 1f / 6f;
        float reactionDelay = 0.22f;
        Unit focusTarget;

        readonly List<Unit> main = new List<Unit>(), harass = new List<Unit>(), scouts = new List<Unit>();
        /// <summary>The raiders, for trials (AIReview.RaidTrial): read-only.</summary>
        public IReadOnlyList<Unit> Raiders => harass;
        readonly List<Unit> single = new List<Unit>(1);
        // Where each squad was last sent (a prong had shared the raiders' and, with a side raid out,
        // both were re-ordered every pass).
        Vector2 rally, harassTarget, prongTarget;
        bool committed;
        // The current attack wave.
        bool waveOn, staging, prongOn;
        float waveStart, stageUntil;
        Vector2 stagePoint, waveAt, prongAt;
        Approach approach, lastFailedApproach = (Approach)(-1);
        /// <summary>For the A/B in <c>BayEdgeCases</c>: leave enemy structures going up in its base
        /// to the rule for finished ones.</summary>
        public static bool IgnoreSites;
        /// <summary>For the A/B in <c>BayEdgeCases</c> case 8: hold the army back from their Mech
        /// beside a structure inside its own base too (the rule before the exemption).</summary>
        public static bool MechGateInBase;

        /// <summary>Waves beaten back while their Mech was about (raises the push threshold).</summary>
        int mechRepulses;
        /// <summary>This wave goes while their Mech is away from home; it gathers on its own Mech.</summary>
        bool mechAwayWave, stageWithMech;
        /// <summary>This wave goes at their Mech seen badly hurt, before its bay mends it.</summary>
        bool hurtWave;
        float windowNotBefore;
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
        // The raiders: on a sortie, or resting after being driven off.
        bool raiding;
        float raidRestUntil = -1f;
        // After a retreat no new wave goes before regroupUntil, unless the army has grown to
        // regroupArmy.
        float regroupUntil = -1f, regroupArmy;
        /// <summary>Waves pulled back because the fight turned (for the evaluation).</summary>
        public int Retreats { get; private set; }
        // Focus fire: the units turned on one target, and where the target was then.
        readonly List<Unit> focusGroup = new List<Unit>();
        Vector2 focusAt;
        int scoutGuess;

        AIMemoryData memory;
        bool learn;

        // What its Mech last told it, and until when that holds.
        float adviceAttackUntil = -1f, adviceDefendUntil = -1f, adviceRegroupUntil = -1f;
        Vector2 adviceAttackAt, adviceDefendAt;
        bool adviceWave;
        readonly float[] styleAccum = new float[(int)PlayerStrat.Count];

        public void Init(GameWorld world, int t, uint seed, AIDifficulty difficulty, bool useMemory, IInfluenceBackend gpu = null)
        {
            w = world;
            team = t;
            rng = new Rng(seed ^ 0x9E3779B9u);
            mixRng = new Rng(seed ^ 0x3F1B7Du);
            lastWorkerHitAt = -1e9f;
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
            barredSites.Clear(); fieldsTried.Clear(); explorer = null; missSince = -1f; clearedAt.Clear(); stuckUntil.Clear(); movedForMech.Clear(); mechGunAt = -1e9f; minedOut = false; minedOutCheckAt = 0f;
            rally = Perception.Home;
            committed = false;
            waveOn = staging = prongOn = false;
            lastFailedApproach = (Approach)(-1);
            mechRepulses = 0;
            scoutGuess = 0;
            thinkT = macroT = scoutT = tacticT = microT = 0f;
            System.Array.Clear(styleAccum, 0, styleAccum.Length);
            Dbg.memoryGames = memory.games;
            w.Event += OnWorldEvent;
        }

        public void Dispose()
        {
            if (w != null) w.Event -= OnWorldEvent;
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
            pe.adviceTrust = r.Range(0.35f, 0.95f);
            // A Mech Bay is cheap and needs nothing, so every personality raises one early;
            // how early is a matter of taste (and of the opening).
            pe.mechBayAt = r.Range(40f, 120f) * (pe.opening == Opening.FastExpand ? 1.3f : 1f);
            for (int i = 0; i < pe.taste.Length; i++) pe.taste[i] = r.Range(-0.12f, 0.12f);
            var r2 = new Rng(seed ^ 0x7EC4A1u);
            pe.techPath = r2.IRange(0, 3);
            pe.armoryAt = r2.Range(180f, 330f);
            pe.researchTaste = r2.F01();
            pe.haulerTaste = r2.F01();
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
                // Deserters (GameWorld.Morale) take no orders: not part of any squad.
                if (e == null || e.dying || e.team != team || e.Type != t || !e.Complete || e.deserted) continue;
                if (idleOnly && e.order != Order.Idle) continue;
                v.Add(e);
            }
            return v;
        }

        /// <summary>Its workers of every kind (Diggers, Haulers, Prospectors): what mines, and what
        /// a gun or their Mech by the ore must not catch. Builders are the Diggers alone.</summary>
        List<Unit> OwnHarvesters(bool idleOnly = false)
        {
            var v = new List<Unit>();
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.team != team || !e.def.harvester || !e.Complete || e.deserted) continue;
                if (idleOnly && e.order != Order.Idle) continue;
                v.Add(e);
            }
            return v;
        }

        /// <summary>Every fighting type, in the order squads take them (the raiders first).</summary>
        static UnitType[] squadTypes;
        static UnitType[] SquadTypes
        {
            get
            {
                if (squadTypes != null) return squadTypes;
                var l = new List<UnitType> { UnitType.Skimmer, UnitType.Razorwing, UnitType.Skitter, UnitType.Kestrel, UnitType.Trooper };
                for (int i = 0; i < (int)UnitType.Count; i++)
                {
                    var d = Defs.Get((UnitType)i);
                    if (d != null && d.IsArmy && !d.Autonomous && !l.Contains(d.type)) l.Add(d.type);
                }
                return squadTypes = l.ToArray();
            }
        }

        /// <summary>Fit for a raid: fast and light (the Mauler never was).</summary>
        static bool Raider(UnitType t) => t == UnitType.Skimmer || t == UnitType.Trooper || t == UnitType.Razorwing ||
                                          t == UnitType.Skitter || t == UnitType.Kestrel;

        IReadOnlyList<Unit> Single(Unit u)
        {
            single.Clear();
            single.Add(u);
            return single;
        }

        /// <summary>A human pays for the selection and then for the order: re-ordering the same
        /// group costs one action, switching groups costs two.</summary>
        bool Issue(IReadOnlyList<Unit> sel, int kind, Vector2 pos, Unit target,
                   [System.Runtime.CompilerServices.CallerMemberName] string site = "",
                   [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (sel.Count == 0) return false;
            float h = 0f;
            foreach (var u in sel) if (u != null) h += u.id & 0xFFFF;
            h += sel.Count * 7919f;
            int cost = Mathf.Abs(h - lastSelHash) > 0.5f ? 2 : 1;
            if (!Pay(cost, site, line)) return false;
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

        /// <summary>Ore kept back from everything but Diggers, so a side whose Diggers are wiped out
        /// can always train another: one Digger's price once the opening is done, two while it is
        /// down to its last Diggers (a side starts with five). Spent to the last ore on soldiers
        /// while a Mech killed its Diggers in a burst, a side was left 3-27 ore and never mined
        /// again (a long match ended with both sides so, standing for good). Checked at every
        /// purchase: a pass spends at several buildings, so a check once a pass let it through.</summary>
        int DiggerReserve(UnitType what)
        {
            var s = Perception.Snap;
            if (what == UnitType.Worker || s == null || s.foundries == 0) return 0;
            if (MapMinedOut()) return 0;   // a Digger brings nothing in any more: nothing to keep ore for
            // With no room for a Digger the Bunkhouse that makes room is not held back (refused
            // for the reserve, it left the side unable to train either). Nothing else is: at the
            // cap dead Diggers free their own supply, and the reserve must still be there then.
            var F = w.factions[team];
            var digger = Defs.Get(UnitType.Worker);
            if (what == UnitType.Bunkhouse && F.supplyUsed + digger.supplyCost > Mathf.Min(F.supplyCap, GameWorld.MaxSupply)) return 0;
            if (s.workers + s.queuedType[(int)UnitType.Worker] < 3) return 2 * digger.cost;
            return w.time > 180f ? digger.cost : 0;
        }

        /// <summary>What it must hold before buying: the price and the reserve. Every step gates
        /// on it, so a step the reserve would refuse does not place and pick a builder first, and
        /// the expansion's saving and its build agree (gated on the price alone, the saving ended
        /// at 400 and the build waited for 450: between, the ore went on soldiers every pass and
        /// the second ore line never went up).</summary>
        int Need(UnitType what) => Defs.Get(what).cost + DiggerReserve(what);

        bool Train(Unit building, UnitType what)
        {
            if (what != UnitType.None && what < UnitType.Count && w.factions[team].ore - Defs.Get(what).cost < DiggerReserve(what)) return false;
            if (!Pay(2, "Train")) return false;            // select structure + hotkey
            Budget.Note(w.time);
            lastSelHash = -1f;
            return w.CmdTrain(building, what);
        }

        bool Build(Unit worker, UnitType what, Vector2 where)
        {
            if (what != UnitType.None && what < UnitType.Count && w.factions[team].ore - Defs.Get(what).cost < DiggerReserve(what)) return false;
            if (!Pay(3, "Build")) return false;            // select Digger + hotkey + click
            Budget.Note(w.time);
            lastSelHash = -1f;
            return w.CmdBuild(Single(worker), what, where);
        }

        // What its actions went on and what the cap refused, by the step that asked: a step asking
        // every pass for what it cannot have shows here (the evaluation line prints it).
        // Keyed by the order's source line (or a fixed negative key for Train, Build, Tend, Upgrade),
        // named once: a string built per order cost an allocation on every click of every match.
        readonly Dictionary<int, int> spentBy = new Dictionary<int, int>(), refusedBy = new Dictionary<int, int>();
        readonly Dictionary<int, string> siteNames = new Dictionary<int, string>();

        bool Pay(int n, string site, int line = 0)
        {
            bool paid = Budget.Spend(n);
            int key = line > 0 ? line : site == "Train" ? -1 : site == "Build" ? -2 : site == "Tend" ? -3 : site == "Research" ? -5 : -4;
            if (!siteNames.ContainsKey(key)) siteNames[key] = line > 0 ? site + ":" + line : site;
            var into = paid ? spentBy : refusedBy;
            into.TryGetValue(key, out int had);
            into[key] = had + n;
            return paid;
        }

        /// <summary>Actions refused and spent by the step that asked, largest first (evaluation only).</summary>
        public string BudgetBySite()
        {
            string Top(Dictionary<int, int> d)
            {
                var l = new List<KeyValuePair<int, int>>(d);
                l.Sort((a, b) => b.Value.CompareTo(a.Value));
                var parts = new List<string>();
                for (int i = 0; i < l.Count && i < 5; i++) parts.Add($"{siteNames[l[i].Key]} {l[i].Value}");
                return parts.Count > 0 ? string.Join(", ", parts) : "none";
            }
            return $"refused {Top(refusedBy)}; spent {Top(spentBy)}";
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
        /// <summary>Editor trials only (MechTrials.Duel): every commander stands idle.</summary>
        public static bool Suspended;

        public void Update(float dt)
        {
            if (w == null || w.winner >= 0 || Suspended) return;
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
                if (!Reserved(h) && h.order != Order.Build && !(h == explorer && h.order == Order.Move)) freeWorkers.Add(h);
            foundriesNow.Clear();
            foreach (var f in w.units)
                if (f != null && !f.dying && f.team == team && f.Type == UnitType.Foundry) foundriesNow.Add(f);

            // Enemy guns it has seen by its ore: a tower or a Mech Bay (see 5b).
            guns.Clear();
            foreach (var r in Perception.Enemies)
            {
                if (r.unfinished || (r.type != UnitType.MechBay && r.type != UnitType.Sentinel)) continue;
                if (w.time - r.lastSeen > 60f || !Perception.Reactable(r, reactionDelay)) continue;
                guns.Add(new Vector3(r.pos.x, r.pos.y, Defs.Get(r.type).range + 5f));
            }
            staticGuns = guns.Count;
            // Their Mech seen by its ore in the last few seconds is a gun too, at the 40 m the other
            // rules read it at (its guns are not known): with it walking in from 70 m, seen all the
            // way, 16-17 Diggers mined on until it shot them. It does not stay, so it is never mined
            // on under at a loss as a tower over the main Foundry is (coreCovered).
            foreach (var r in Perception.Enemies)
                if (r.type == UnitType.Mech && w.time - r.lastSeen < 4f && Perception.Reactable(r, reactionDelay))
                { guns.Add(new Vector3(r.pos.x, r.pos.y, MechReach)); mechGunAt = w.time; }
            if (guns.Count > 0)
            {
                gunFoundries = OwnOf(UnitType.Foundry);
                gunWorkers = OwnHarvesters();
            }
            // Not a Digger in their Mech's reach, nor one cleared from any gun these 8 s: sent to build, it
            // was cleared again in the same pass (and a new site handed to one stood abandoned, its ore spent).
            if (!LegacyDiggerSafety)
                for (int i = freeWorkers.Count - 1; i >= 0; i--)
                    if (UnderMech(freeWorkers[i].pos) || (clearedAt.TryGetValue(freeWorkers[i], out float clearedT) && w.time - clearedT < 8f))
                        freeWorkers.RemoveAt(i);
            Unit Builder() => freeWorkers.Count == 0 ? null : freeWorkers[rng.IRange(0, freeWorkers.Count)];

            // A human macros in bursts and is limited by hands, not by a one-action-
            // per-tick rule. The budget is the real constraint, so this is a
            // prioritised pass rather than returning after the first success.
            int issued = 0;
            const int MaxPerPass = 4;

            // 0. No Foundry: nothing trains Diggers or takes their ore in. Raise one first,
            //    whatever the plan -- only the expanding plans built Foundries, so a side whose
            //    Foundry fell stood with its Diggers idle and its ore unspent on one.
            if (s.foundries + s.pendingType[(int)UnitType.Foundry] == 0 && F.ore >= Defs.Get(UnitType.Foundry).cost)
            {
                var b = Builder();
                if (b != null && (PlaceNear(UnitType.Foundry, home, 0f, 14f, out var spot) || PlaceNear(UnitType.Foundry, home, 14f, 32f, out spot)) &&
                    Build(b, UnitType.Foundry, spot)) issued++;
            }

            // Its next ore line, decided before anything is spent (step 3 raises it): when the plan
            // wants one, or when the ore by its Foundries runs low, whatever the plan. A base's eight
            // seams (12,000 ore) are mined out in seven to nine minutes; after that its Diggers
            // hauled from the nearest ore anywhere, and only the expanding plans had ever taken a
            // second line.
            int foundryCost = Need(UnitType.Foundry);
            int foundriesUp = s.foundries + s.pendingType[(int)UnitType.Foundry];
            bool dryingUp = w.time > 120f && foundriesUp > 0 && OreByFoundries() < 4500;
            Vector2 site = default;
            bool expand = (plan.wantExpand || dryingUp) && foundriesUp < (dryingUp ? 4 : 2) && s.pending < 2 &&
                          (F.ore >= foundryCost || dryingUp) && ExpansionSite(home, out site);
            // Running dry with no field it has seen to go to: a Digger walks to the nearest one it
            // has not (the fields are on the map for anyone to read; whether one is free is not).
            // Each field once, so one lost there is not followed by another; it then mines on as any
            // idle Digger (step 6). Without it, a side that never looked 26-110 m out ran dry with
            // nowhere to go (1 in 6 runs with the home seams cut at 240 s).
            if (dryingUp && !expand && foundriesUp < 4 && s.pending < 2 && !(Unit.Live(explorer) && explorer.order == Order.Move))
                SendExplorer(home, freeWorkers);
            // Running dry, the ore goes to that Foundry first -- not while it is under attack.
            // Spent as it came in (nine Bunkhouses, four Garrisons), it had 17-151 ore when its
            // second line ran out, and its Diggers walked to far ore and died to the last one.
            bool saving = expand && dryingUp && F.ore < foundryCost && s.sinceAggression > 20f;

            // 1. Supply, gated on bunkhouses specifically.
            if (issued < MaxPerPass && !saving && s.supplyCap - s.supplyUsed < 6 && s.supplyCap < GameWorld.MaxSupply &&
                F.ore >= Need(UnitType.Bunkhouse) && s.pendingType[(int)UnitType.Bunkhouse] < 2)
            {
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Bunkhouse, home, 10f, 26f, out var spot) && Build(b, UnitType.Bunkhouse, spot)) issued++;
            }

            // 1a. A structure left half-built -- its Digger killed, a raider got it -- gets
            //     another one. (The Mech Bay above all: against a harasser it once stood
            //     half-raised all match, and the Mech never came.)
            if (issued < MaxPerPass)
                foreach (var e in w.units)
                {
                    if (e == null || e.dying || e.team != team || !e.def.building || e.Complete) continue;
                    bool tended = false;
                    foreach (var h in w.units)
                        if (h != null && !h.dying && h.team == team && h.Type == UnitType.Worker && h.order == Order.Build && h.buildTarget == e) { tended = true; break; }
                    if (tended) continue;
                    // Not into their Mech's reach, nor where it was seen these 30 s (MechLingers): each
                    // Digger sent was killed on the way or at the site, 19 of one match's at a site it was
                    // hunting round. (Not towers: under one by its base, the site may be all there is.)
                    if (!LegacyDiggerSafety && (UnderMech(e.pos) || MechLingers(e.pos))) continue;
                    Unit best = null;
                    float bestD = float.MaxValue;
                    foreach (var h in freeWorkers)
                    {
                        float d = h.Dist(e);
                        if (d < bestD) { bestD = d; best = h; }
                    }
                    if (best == null || !Pay(2, "Tend")) break;
                    Budget.Note(w.time);
                    lastSelHash = -1f;
                    w.CmdSmart(Single(best), e.pos, e);
                    freeWorkers.Remove(best);
                    issued++;
                    break;
                }

            // 1b. The Mech Bay: cheap, needs nothing, and calls down a Mech worth an army.
            //     Tucked in behind the Foundry, away from where the enemy comes from.
            if (issued < MaxPerPass && !saving && !F.bayPlaced && w.time >= Personality.mechBayAt &&
                F.ore >= Need(UnitType.MechBay) && s.pending < 2)
            {
                Vector2 face = s.enemyBaseKnown ? s.enemyBase : Perception.BaseGuesses[0];
                Vector2 around = home - Norm(face - home) * 12f;
                var b = Builder();
                if (b != null && (PlaceNear(UnitType.MechBay, around, 0f, 12f, out var spot) || PlaceNear(UnitType.MechBay, home, 12f, 26f, out spot)) &&
                    Build(b, UnitType.MechBay, spot)) issued++;
            }

            // 1c. Upgrades for the Mech, from ore it can spare.
            if (issued < MaxPerPass && !saving && BuyMechUpgrade(F)) issued++;
            // 1d. The Armory's research for what it fields most, from ore it can spare.
            if (issued < MaxPerPass && !saving && BuyResearch(s, F)) issued++;

            // 2. Production structures, at most two going up at once.
            int extraProd = F.ore > 550 ? 2 : (F.ore > 320 ? 1 : 0);
            int wantRax = Mathf.Min(5, plan.garrisons + extraProd);
            int wantFac = Mathf.Min(3, plan.workshops + (plan.workshops > 0 ? extraProd : 0));
            if (issued < MaxPerPass && !saving && s.garrisons + s.pendingType[(int)UnitType.Garrison] < wantRax &&
                F.ore >= Need(UnitType.Garrison) && s.pending < 2)
            {
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Garrison, home, 12f, 28f, out var spot) && Build(b, UnitType.Garrison, spot)) issued++;
            }
            if (issued < MaxPerPass && !saving && s.garrisons >= 1 && s.workshops + s.pendingType[(int)UnitType.Workshop] < wantFac &&
                F.ore >= Need(UnitType.Workshop) && s.pending < 2)
            {
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Workshop, home, 13f, 29f, out var spot) && Build(b, UnitType.Workshop, spot)) issued++;
            }

            // 2b. Sentinels on the approach: the turtle's answer, and insurance
            //     whenever the model smells early aggression.
            int wantSent = plan.sentinels + (Opponent.ThreatOfEarlyAggression > 0.55f ? 1 : 0);
            if (issued < MaxPerPass && !saving && s.garrisons >= 1 && s.sentinels + s.pendingType[(int)UnitType.Sentinel] < wantSent &&
                F.ore >= Need(UnitType.Sentinel) && s.pending < 2)
            {
                Vector2 face = s.enemyBaseKnown ? s.enemyBase : Perception.BaseGuesses[0];
                Vector2 around = home + Norm(face - home) * 17f;
                var b = Builder();
                if (b != null && PlaceNear(UnitType.Sentinel, around, 0f, 10f, out var spot) && Build(b, UnitType.Sentinel, spot)) issued++;
            }

            // 2c. The wider roster's buildings: an Armory once established (its upgrades, and the
            //     Thumper and the Marksman), then the tech its personality leans to -- an Airfield
            //     or a Biolab -- and the other one as an answer later: an Airfield against an army
            //     that cannot shoot up, a Biolab once it is rich.
            if (issued < MaxPerPass && !saving && s.pending < 2)
            {
                UnitType want = TechBuildingWanted(s, F);
                if (want != UnitType.None && F.ore >= Need(want))
                {
                    var b = Builder();
                    // The Airfield is the widest footprint after the Foundry: the inner ring of a
                    // grown base often has no room for it (an air-path AI never raised one), so further out.
                    // ...and only where its builder can walk (the outer ring can be a plateau top or a
                    // clearing no ground unit reaches: a site there stood pending and blocked the rest).
                    if (b != null && ((PlaceNear(want, home, 12f, 30f, out var spot) && w.Reaches(b, spot)) ||
                                      (PlaceNear(want, home, 30f, 46f, out spot) && w.Reaches(b, spot))) && Build(b, want, spot)) issued++;
                }
            }

            // 3. Expansion, at the site chosen above.
            if (issued < MaxPerPass && expand && F.ore >= foundryCost)
            {
                if (PlaceNear(UnitType.Foundry, site, 8f, 16f, out var spot))
                {
                    var b = Builder();
                    if (b != null && Build(b, UnitType.Foundry, spot)) issued++;
                    missSince = -1f;
                }
                else
                {
                    // No room round it (a slope, the lake, its own seams): after 10 s of tries at that
                    // field (about 36 passes of 26 random spots) it is passed over for 90 s, or the
                    // best-scoring site kept it waiting for good (103 s with 2,000+ ore in hand, once
                    // never in four minutes). By time, not count: four misses would bar a tight but valid
                    // site (1-3 spots of 120) 7-42% of the time; 10 s of them, well under 1%.
                    if (missSince < 0f || (site - missSite).sqrMagnitude > 12f * 12f || w.time - lastMiss > 5f) { missSite = site; missSince = w.time; }
                    lastMiss = w.time;
                    if (w.time - missSince >= 10f) { barredSites.Add(new Vector3(site.x, site.y, w.time + 90f)); missSince = -1f; }
                }
            }

            // 4. Workers -- never allowed to block army production below.
            // A Hauler brings in what about two Diggers do: fewer Diggers once it has some (never under
            // ten: they build).
            int wantDiggers = Mathf.Max(Mathf.Min(10, plan.workers), plan.workers - 2 * (s.own[(int)UnitType.Hauler] + s.queuedType[(int)UnitType.Hauler])
                                        - s.own[(int)UnitType.Prospector] / 2);
            if (issued < MaxPerPass && s.workers + s.queuedType[(int)UnitType.Worker] < wantDiggers &&
                F.ore >= Defs.Get(UnitType.Worker).cost && !MapMinedOut())
            {
                foreach (var e in w.units)
                {
                    if (e == null || e.dying || e.team != team || e.Type != UnitType.Foundry || !e.Complete || e.queue.Count > 0) continue;
                    // Not at a Foundry their Mech stands by: a new Digger walks out into its guns
                    // (with a Mech 7 m from its main Foundry for two minutes it trained 3-10 there,
                    // and one side kept training into a Mech parked in its base until it was broke).
                    if (s.eMech && s.eMechSeenAgo < 10f && (s.eMechPos - e.pos).magnitude < 40f) continue;
                    if (Train(e, UnitType.Worker)) issued++;
                    break;
                }
            }

            // 4b. The heavy workers: Haulers once a Workshop stands (a taste for them, or Diggers
            //     being lost to raids), Prospectors -- miners that fight back -- while its workers are
            //     being raided.
            if (issued < MaxPerPass && !saving && s.workers >= 8 && !MapMinedOut())
            {
                bool raided = w.time - lastWorkerHitAt < 90f;
                int wantHaulers = s.workshops == 0 || w.time < 240f ? 0
                                : Mathf.Min(4, s.foundries * (Personality.haulerTaste > 0.5f || raided ? 2 : 1));
                int wantProspectors = s.garrisons == 0 || w.time < 200f ? 0 : raided ? 3 : Personality.haulerTaste > 0.75f ? 1 : 0;
                UnitType pick = UnitType.None;
                if (s.own[(int)UnitType.Hauler] + s.queuedType[(int)UnitType.Hauler] < wantHaulers) pick = UnitType.Hauler;
                else if (s.own[(int)UnitType.Prospector] + s.queuedType[(int)UnitType.Prospector] < wantProspectors) pick = UnitType.Prospector;
                if (pick != UnitType.None && F.ore >= Need(pick))
                    foreach (var e in w.units)
                    {
                        if (e == null || e.dying || e.team != team || e.Type != UnitType.Foundry || !e.Complete || e.queue.Count > 0) continue;
                        if (s.eMech && s.eMechSeenAgo < 10f && (s.eMechPos - e.pos).magnitude < 40f) continue;
                        if (Train(e, pick)) { issued++; s.queuedType[(int)pick]++; }
                        break;
                    }
            }

            // 5. Army from every idle production structure. Floating ore is the
            //    single most common way for an RTS bot to lose a game it should win.
            bool rich = F.ore > 400;
            // Only while it has the hands for it (here and in 5b, 5c): a loop that went on past
            // an empty budget had every later order refused, pass after pass -- over a thousand
            // refused in a match, which said nothing about what it actually left undone.
            // The tech its personality leans to (an Airfield, a Biolab) trains first: last in the unit
            // list, it found the ore spent by the Garrisons and Workshops before it, pass after pass, and
            // on a thin economy a Biolab stood a whole match without breeding anything.
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < w.units.Count && issued < MaxPerPass && !saving && Budget.Can(2); i++)
            {
                var e = w.units[i];
                if (e == null || e.dying || e.team != team || !e.Complete || e.queue.Count >= 2) continue;
                if ((e.Type == UnitType.Airfield || e.Type == UnitType.Biolab) != (pass == 0)) continue;
                if (e.Type == UnitType.Garrison)
                {
                    int skimHave = s.skimmers + s.queuedType[(int)UnitType.Skimmer];
                    if (skimHave < plan.skimmers && F.ore >= Need(UnitType.Skimmer))
                    {
                        if (Train(e, UnitType.Skimmer)) { issued++; s.queuedType[(int)UnitType.Skimmer]++; }
                    }
                    else if (F.ore >= Need(UnitType.Trooper) &&
                             (rich || s.workshops == 0 || rng.F01() < plan.trooperBias))
                    {
                        var pick = PickInfantry(s);
                        if (F.ore < Need(pick)) pick = UnitType.Trooper;
                        if (Train(e, pick)) issued++;
                    }
                }
                else if (e.Type == UnitType.Workshop && F.ore >= Need(UnitType.Mauler))
                {
                    var pick = PickTank(s);
                    // Rolled something it cannot afford yet: the base tank, rather than an idle Workshop.
                    if (F.ore < Need(pick)) pick = UnitType.Mauler;
                    if (F.ore >= Need(pick) && Train(e, pick)) issued++;
                }
                else if (e.Type == UnitType.Airfield || e.Type == UnitType.Biolab)
                {
                    var pick = e.Type == UnitType.Airfield ? PickAircraft(s) : PickCreature(s);
                    if (F.ore < Need(pick)) pick = e.Type == UnitType.Airfield ? UnitType.Kestrel : UnitType.Skitter;
                    if (F.ore >= Need(pick) && Train(e, pick)) issued++;
                }
            }

            // 5b. Ore under an enemy gun -- a tower or a Mech Bay raised by its base -- costs
            //     Diggers, not ore: they go to ore out of its reach, with a Foundry out of it to
            //     bring it to. Left to mine on, they walked into a proxy bay's gun one by one,
            //     10-18 of them in a match. With no such ore they stand clear of it -- unless
            //     the gun is over its main Foundry: then that is all the income there is, and
            //     they mine on at a loss (stood clear, they brought in nothing, the army never
            //     grew to take the gun on, and the base fell to the Mech that came down there).
            bool coreCovered = false, anySafe = false;
            if (clearedAt.Count + stuckUntil.Count > 48)
            {
                pruneBuf.Clear();
                foreach (var kv in clearedAt) if (!Unit.Live(kv.Key) || w.time - kv.Value > 8f) pruneBuf.Add(kv.Key);
                foreach (var u in pruneBuf) clearedAt.Remove(u);
                pruneBuf.Clear();
                foreach (var kv in stuckUntil) if (!Unit.Live(kv.Key) || w.time > kv.Value) pruneBuf.Add(kv.Key);
                foreach (var u in pruneBuf) stuckUntil.Remove(u);
            }
            if (guns.Count > 0)
            {
                Unit core = null;
                float coreD = float.MaxValue;
                foreach (var f in gunFoundries)
                {
                    float d = (f.pos - home).sqrMagnitude;
                    if (d < coreD) { coreD = d; core = f; }
                }
                coreCovered = core != null && UnderGun(core.pos, 0f, staticGuns);
                var safe = SafeOreNear(home);
                anySafe = safe != null;
                int moved = 0, cleared = 0, unclearable = 0;
                foreach (var h in gunWorkers)
                {
                    if (moved + cleared >= 3 || issued >= MaxPerPass || !Budget.Can(2)) break;
                    // A builder is left to build under a tower or bay (by its main Foundry that is the
                    // whole economy), but not under their Mech, which hunts it down.
                    if (Reserved(h) || (h.order == Order.Build && (LegacyDiggerSafety || !UnderMech(h.pos)))) continue;
                    // Cleared lately and walking out of every gun's reach: left to get there (re-ordered
                    // each pass while still in it, a Mech walking through the line ate the army's orders).
                    // Anything else on the move (a scout walked home, the explorer) is still pulled off.
                    if (h.order == Order.Move && clearedAt.TryGetValue(h, out float clearedWhen) && w.time - clearedWhen < 8f && !UnderGun(h.orderPos)) continue;
                    // A Digger mining safe ore is left alone, even walking past the gun (moved
                    // again each pass, one was re-ordered 93 times in a match); one mining ore
                    // under it, or standing under it idle, is moved.
                    // One holding its load where it stopped, with no Foundry to bring it to, is not at
                    // its ore: it is judged by where it stands (judged by its ore, one stopped under
                    // the gun that had taken the Foundry stood there until it died).
                    bool onDuty = (h.order == Order.Harvest || (h.order == Order.Return && (h.waitAtOre || w.NearestDropoff(h) != null)))
                                  && Unit.Live(h.harvestNode);
                    if (onDuty ? SafeOre(h.harvestNode) : !UnderGun(h.pos)) continue;
                    if (safe != null)
                    {
                        Unit from = onDuty ? h.harvestNode : null;
                        bool forMech = UnderMech(onDuty ? h.harvestNode.pos : h.pos);
                        if ((safe != h.harvestNode || !onDuty) && Issue(Single(h), 4, safe.pos, safe))
                        {
                            moved++;
                            if (forMech && from != null && from != safe && !movedForMech.ContainsKey(h)) movedForMech[h] = from;
                        }
                        continue;
                    }
                    // Mined on at a loss under a tower over the main Foundry -- but not under their Mech.
                    if (coreCovered && !UnderMech(onDuty ? h.harvestNode.pos : h.pos)) continue;
                    if (stuckUntil.TryGetValue(h, out float stuck) && w.time < stuck) continue;
                    // Cleared to a point out of every gun's reach: straight away from the nearest, else
                    // 60 or 120 degrees round, else home, else left be (straight away from a Mech between
                    // the Foundry and its ore ran into the map's corner, still in reach, and was cleared
                    // again every pass).
                    Vector2 g = NearestGun(h.pos, out float reach);
                    Vector2 d = h.pos - g;
                    Vector2 dir = d.sqrMagnitude > 0.01f ? d.normalized : (home - g).normalized;
                    Vector2 to = default;
                    bool clear = false;
                    // From their Mech, home first, among its towers and army -- unless the way home runs
                    // towards it or through a tower's or bay's reach: cleared only past its reach, a Digger
                    // stood there and the Mech, hunting the knot, walked up to it again (24-30 a match
                    // killed at its outlying lines, 41-104 clears).
                    if (!LegacyDiggerSafety && UnderMech(onDuty ? h.harvestNode.pos : h.pos) && !WayBlocked(h.pos, home))
                    {
                        to = w.NearestReachable(home, home);
                        clear = !UnderGun(to);
                    }
                    for (int k = 0; k < 5 && !clear; k++)
                    {
                        float ang = (k == 0 ? 0f : ((k & 1) == 1 ? 1f : -1f) * ((k + 1) / 2) * 60f) * Mathf.Deg2Rad;
                        var v = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
                        to = w.NearestReachable(home, g + v * (reach + 6f));
                        clear = !UnderGun(to);
                    }
                    if (!clear) { to = w.NearestReachable(home, home); clear = !UnderGun(to); }
                    if (clear && Issue(Single(h), 0, to, null)) { cleared++; clearedAt[h] = w.time; }
                    // Nowhere out of reach from here: left be 2 s before it is tried again (searched each
                    // pass, a knot of them cost a handful of NavMesh queries apiece every 0.28 s).
                    else if (!clear) { stuckUntil[h] = w.time + 2f; if (++unclearable >= 3) break; }
                }
                if (moved + cleared > 0) issued++;
                Dbg.diggersMoved += moved;
                Dbg.diggersCleared += cleared;
                Dbg.guns = $"{guns.Count} gun(s) by its ore; safe ore {(safe != null ? $"{(safe.pos - home).magnitude:0} m out" : "none")}" +
                           $"{(coreCovered ? ", main Foundry covered" : "")}";
            }

            // 5c. Diggers their Mech moved off their seams go back once it has not been seen at all
            //     for 10 s (anywhere: it may still be about), to the seam if it is still there, has a
            //     finished Foundry beside it to bring the ore to and is out of any tower's reach (sent
            //     to another line's ore, they mined there for good and the home seams waited; sent
            //     back to seams whose Foundry the Mech had taken, they would haul to the far line).
            if (movedForMech.Count > 0 && w.time - mechGunAt > 10f && issued < MaxPerPass)
            {
                int back = 0;
                pruneBuf.Clear();
                foreach (var kv in movedForMech)
                {
                    var h = kv.Key; var n = kv.Value;
                    if (!Unit.Live(h) || !Unit.Live(n) || n.oreLeft <= 0 || h.order == Order.Build || Reserved(h)) { pruneBuf.Add(h); continue; }
                    if (back >= 3 || !Budget.Can(2)) continue;
                    if (!FinishedFoundryNear(n.pos, 25f)) continue;   // kept: back once one stands again
                    if (MechLingers(n.pos)) continue;                 // kept: back once the place is quiet
                    if (staticGuns > 0 && !SafeOre(n)) { pruneBuf.Add(h); continue; }
                    if (h.harvestNode == n) { pruneBuf.Add(h); continue; }   // back there already
                    if (Issue(Single(h), 4, n.pos, n)) { back++; pruneBuf.Add(h); }
                }
                foreach (var u in pruneBuf) movedForMech.Remove(u);
                if (back > 0) { issued++; Dbg.diggersMoved += back; }
            }

            // 6. Idle workers back to ore (scouts and squad members excluded): by home, or by
            //    any other Foundry of its own (once home runs dry, that is where the ore is),
            //    the seam nearest where they stand.
            var idle = new List<Unit>();
            foreach (var h in OwnHarvesters(true)) if (!Reserved(h)) idle.Add(h);
            if (idle.Count > 0 && issued < MaxPerPass)
            {
                Vector2 at = Vector2.zero;
                foreach (var h in idle) at += h.pos;
                at /= idle.Count;
                Unit pick = null;
                float pickD = float.MaxValue;
                foreach (var n in w.units)
                {
                    if (n == null || n.dying || n.Type != UnitType.Ore || n.oreLeft <= 0) continue;
                    if ((n.pos - home).magnitude > 50f && !NearOwnFoundry(n.pos, 25f)) continue;
                    if (guns.Count > 0 && ((!SafeOre(n) && (anySafe || !coreCovered)) || UnderMech(n.pos))) continue;
                    if (MechLingers(n.pos)) continue;
                    float d = (n.pos - at).sqrMagnitude;
                    if (d < pickD) { pickD = d; pick = n; }
                }
                if (pick != null) Issue(idle, 4, pick.pos, pick);
            }
        }

        // Its Foundries, standing or going up, refreshed each macro pass (RunMacro).
        readonly List<Unit> foundriesNow = new List<Unit>(4);

        bool NearOwnFoundry(Vector2 p, float r)
        {
            foreach (var f in foundriesNow)
                if ((f.pos - p).sqrMagnitude < r * r) return true;
            return false;
        }

        /// <summary>Where its next Foundry goes: an ore field it has explored, 26-110 m from home,
        /// with ore left, clear of their guns, not a line it has already nor one it has seen them
        /// hold -- near, quiet and full first.</summary>
        bool ExpansionSite(Vector2 home, out Vector2 best)
        {
            best = default;
            float bestScore = -1e30f;
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.Type != UnitType.Ore || e.oreLeft <= 0) continue;
                if (!w.Explored(team, e.pos)) continue;
                float dHome = (e.pos - home).magnitude;
                if (dHome < 26f || dHome > 110f) continue;
                if (Barred(e.pos)) continue;
                if (UnderGun(e.pos, 16f) || MechLingers(e.pos, 16f)) continue;   // the Foundry goes up to 16 m from the ore
                // Not a line it already has, nor one it has seen them hold.
                if (NearOwnFoundry(e.pos, 22f) || NearTheirStructure(e.pos, 25f)) continue;
                float score = -dHome * 0.02f - Influence.Sample(2, e.pos) * 3f + FieldOre(e.pos) * 0.0001f;
                if (score > bestScore) { bestScore = score; best = e.pos; }
            }
            return bestScore > -1e29f;
        }

        bool FinishedFoundryNear(Vector2 p, float r)
        {
            foreach (var f in foundriesNow)
                if (Unit.Live(f) && f.Complete && (f.pos - p).sqrMagnitude < r * r) return true;
            return false;
        }

        bool EconomyDead(Snapshot s) =>
            // No Digger and none coming, and no Foundry or (nothing mining either) no Digger's price: a
            // Hauler or Prospector left with a Foundry still brings in what buys one.
            (s.workers + s.queuedType[(int)UnitType.Worker] == 0 &&
             (s.foundries == 0 || (s.harvesters == 0 && w.factions[team].ore < Defs.Get(UnitType.Worker).cost)))
            // The map mined out (a seam that runs dry is gone from the map for everyone) and less
            // in hand than a rifleman: nothing it does can grow its army again either (two sides
            // with Diggers idle and armies under their thresholds stood the last 20 minutes apart).
            // (Against what a rifleman costs to buy, reserve and all, so the two cannot part again:
            // tested on the bare price, the Diggers' reserve held it at 50-99 and it never fired.)
            || (MapMinedOut() && w.factions[team].ore < Need(UnitType.Trooper));

        bool MapMinedOut()
        {
            if (w.time < minedOutCheckAt) return minedOut;
            minedOutCheckAt = w.time + 5f;
            minedOut = true;
            foreach (var n in w.units)
                if (n != null && !n.dying && n.Type == UnitType.Ore && n.oreLeft > 0) { minedOut = false; break; }
            return minedOut;
        }

        bool Barred(Vector2 p)
        {
            for (int i = barredSites.Count - 1; i >= 0; i--)
            {
                var b = barredSites[i];
                if (w.time >= b.z) { barredSites.RemoveAt(i); continue; }
                if ((p - new Vector2(b.x, b.y)).sqrMagnitude < 12f * 12f) return true;
            }
            return false;
        }

        void SendExplorer(Vector2 home, List<Unit> free)
        {
            Unit field = null;
            float best = float.MaxValue;
            foreach (var n in w.units)
            {
                if (n == null || n.dying || n.Type != UnitType.Ore || w.Explored(team, n.pos)) continue;
                float d = (n.pos - home).magnitude;
                if (d < 26f || d > 110f || UnderGun(n.pos, 16f) || MechLingers(n.pos, 16f)) continue;
                bool tried = false;
                foreach (var f in fieldsTried) if ((f - n.pos).sqrMagnitude < 12f * 12f) { tried = true; break; }
                if (!tried && d < best) { best = d; field = n; }
            }
            if (field == null) return;
            if (free.Count == 0) return;
            var h = free[rng.IRange(0, free.Count)];
            if (Issue(Single(h), 0, w.NearestReachable(home, field.pos), null)) { explorer = h; fieldsTried.Add(field.pos); }
        }

        /// <summary>Ore left on the seams within 25 m of its Foundries.</summary>
        int OreByFoundries()
        {
            int ore = 0;
            foreach (var n in w.units)
                if (n != null && !n.dying && n.Type == UnitType.Ore && n.oreLeft > 0 && NearOwnFoundry(n.pos, 25f)) ore += n.oreLeft;
            return ore;
        }

        /// <summary>Ore left on the seams of the field round <paramref name="p"/>.</summary>
        int FieldOre(Vector2 p)
        {
            int ore = 0;
            foreach (var n in w.units)
                if (n != null && !n.dying && n.Type == UnitType.Ore && (n.pos - p).sqrMagnitude < 12f * 12f) ore += n.oreLeft;
            return ore;
        }

        /// <summary>Whether it remembers a structure of theirs near <paramref name="p"/>.</summary>
        bool NearTheirStructure(Vector2 p, float r)
        {
            foreach (var e in Perception.Enemies)
                if (Defs.Get(e.type).building && (e.pos - p).sqrMagnitude < r * r) return true;
            return false;
        }

        // Enemy guns near its ore, as (x, y, reach), refreshed each macro pass, and its
        // Foundries and Diggers at the time (only gathered while there are any).
        readonly List<Vector3> guns = new List<Vector3>(4);
        /// <summary>How many of <see cref="guns"/> are towers and bays (the rest: their Mech).</summary>
        int staticGuns;
        const float MechReach = 40f;

        /// <summary>Whether the straight way from a to b passes one of their Mechs it has in sight (step
        /// 5b's guns past the towers) within 25 m and nearer than a stands now (Diggers it killed died
        /// 16-33 m from it on average), or runs through a tower's or bay's reach.</summary>
        bool WayBlocked(Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = Mathf.Max(0.01f, ab.sqrMagnitude);
            for (int i = 0; i < guns.Count; i++)
            {
                var g = new Vector2(guns[i].x, guns[i].y);
                float t = Mathf.Clamp01(Vector2.Dot(g - a, ab) / len2);
                float closest = (a + ab * t - g).magnitude;
                if (i < staticGuns ? closest < guns[i].z : closest < Mathf.Min((a - g).magnitude - 1f, 25f)) return true;
            }
            return false;
        }

        /// <summary>Where their Mech was last seen stays a danger for half a minute (its last sighting;
        /// a player keeps away from where it was): no Digger is sent to ore there, moved back there,
        /// sent to build, expand or explore there. At its reach by home, 20 m past it at the outlying
        /// lines (a Mech guarding a bay 45-60 m from its main seams would leave them unworked). As a
        /// gun (4 s), Diggers mined there until it went out of sight and were sent straight back: it
        /// stood beyond their sight at their outlying lines and killed 20-37 of them a match in three
        /// of five Macro games.</summary>
        bool MechLingers(Vector2 p, float margin = 0f)
        {
            var s = Perception.Snap;
            if (LegacyDiggerSafety || !s.eMech || s.eMechSeenAgo > 30f) return false;
            float r = MechReach + ((p - Perception.Home).sqrMagnitude > 40f * 40f ? 20f : 0f) + margin;
            return (p - s.eMechPos).sqrMagnitude < r * r;
        }
        /// <summary>Expansion sites a Foundry would not go up round, barred until z (see step 3).</summary>
        readonly List<Vector3> barredSites = new List<Vector3>();
        float missSince = -1f, lastMiss;
        Vector2 missSite;
        /// <summary>When step 5b last cleared each Digger out of a gun's reach.</summary>
        readonly Dictionary<Unit, float> clearedAt = new Dictionary<Unit, float>();
        readonly List<Unit> pruneBuf = new List<Unit>();
        /// <summary>Diggers 5b moved off a seam their Mech covered, and that seam (5c sends them back).</summary>
        readonly Dictionary<Unit, Unit> movedForMech = new Dictionary<Unit, Unit>();
        float mechGunAt = -1e9f;
        bool minedOut; float minedOutCheckAt;
        /// <summary>Diggers 5b found nowhere out of reach for, and until when it leaves them be.</summary>
        readonly Dictionary<Unit, float> stuckUntil = new Dictionary<Unit, float>();
        /// <summary>The Digger sent to look at an unseen field for a site, and the fields tried.</summary>
        Unit explorer;
        readonly List<Vector2> fieldsTried = new List<Vector2>();
        List<Unit> gunFoundries, gunWorkers;

        /// <summary>In reach of their Mech (the guns past <see cref="staticGuns"/>).</summary>
        bool UnderMech(Vector2 p)
        {
            for (int i = staticGuns; i < guns.Count; i++)
            {
                var g = guns[i];
                if ((p - new Vector2(g.x, g.y)).sqrMagnitude < g.z * g.z) return true;
            }
            return false;
        }

        bool UnderGun(Vector2 p, float margin = 0f, int count = int.MaxValue)
        {
            for (int i = 0; i < guns.Count && i < count; i++)
            {
                var g = guns[i];
                if ((p - new Vector2(g.x, g.y)).sqrMagnitude < (g.z + margin) * (g.z + margin)) return true;
            }
            return false;
        }

        Vector2 NearestGun(Vector2 p, out float reach)
        {
            Vector2 best = p; reach = 0f;
            float bestD = float.MaxValue;
            foreach (var g in guns)
            {
                var at = new Vector2(g.x, g.y);
                float d = (p - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = at; reach = g.z; }
            }
            return best;
        }

        /// <summary>Ore a Digger can mine without walking under an enemy gun: the node, the
        /// Foundry it would bring the ore to and the way between them all out of reach.</summary>
        bool SafeOre(Unit n)
        {
            if (UnderGun(n.pos)) return false;
            Unit drop = null;
            float bestD = float.MaxValue;
            foreach (var f in gunFoundries)
            {
                float d = (f.pos - n.pos).sqrMagnitude;
                if (d < bestD) { bestD = d; drop = f; }
            }
            if (drop == null) return true;
            if (UnderGun(drop.pos)) return false;
            Vector2 a = n.pos, ab = drop.pos - n.pos;
            float len2 = Mathf.Max(0.01f, ab.sqrMagnitude);
            foreach (var g in guns)
            {
                var at = new Vector2(g.x, g.y);
                float t = Mathf.Clamp01(Vector2.Dot(at - a, ab) / len2);
                if ((a + ab * t - at).sqrMagnitude < g.z * g.z) return false;
            }
            return true;
        }

        /// <summary>The safe ore nearest its base (within 110 m, where it would expand to),
        /// spread over the nodes.</summary>
        Unit SafeOreNear(Vector2 home)
        {
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var n in w.units)
            {
                if (n == null || n.dying || n.Type != UnitType.Ore || n.oreLeft <= 0) continue;
                float d = (n.pos - home).magnitude;
                if (d > 110f || !SafeOre(n) || MechLingers(n.pos)) continue;
                int load = 0;
                foreach (var h in gunWorkers) if (h.harvestNode == n) load++;
                float score = d + load * 6f;
                if (score < bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        /// <summary>Buy the next upgrade for its Mech at the bay, when the ore is there to
        /// spare: early on only once the army is being paid for, later freely. What it buys
        /// first follows its plan -- guns and hardpoints for an attacker, armour for a turtle.</summary>
        static readonly MechUpgrade[] HoldOrder = { MechUpgrade.Armour, MechUpgrade.Hardpoint, MechUpgrade.Weapons, MechUpgrade.Targeting, MechUpgrade.Servos };
        static readonly MechUpgrade[] RaidOrder = { MechUpgrade.Servos, MechUpgrade.Weapons, MechUpgrade.Hardpoint, MechUpgrade.Armour, MechUpgrade.Targeting };
        static readonly MechUpgrade[] AttackOrder = { MechUpgrade.Hardpoint, MechUpgrade.Weapons, MechUpgrade.Armour, MechUpgrade.Servos, MechUpgrade.Targeting };

        // ------------------------------------------------------------ the wider roster
        /// <summary>The tech building it raises next, or None: the Armory once established; then
        /// the tech its personality leans to (an Airfield or a Biolab), and the other later as an
        /// answer -- an Airfield against an army that cannot shoot up, either once rich.</summary>
        UnitType TechBuildingWanted(Snapshot s, Faction F)
        {
            if (s.garrisons < 1) return UnitType.None;
            int Have(UnitType t) => s.own[(int)t] + s.pendingType[(int)t];
            if (Have(UnitType.Armory) == 0 && w.time >= Personality.armoryAt) return UnitType.Armory;
            float theirs = s.eLightValue + s.eHeavyValue + s.eAirValue;
            float aa = theirs > 1f ? s.eAntiAirValue / theirs : 0.5f;
            int path = Personality.techPath;
            bool rich = F.ore > 700;
            if (Have(UnitType.Airfield) == 0 && s.workshops >= 1 && w.time >= 300f &&
                (path == 1 || (w.time >= 480f && theirs > 600f && aa < 0.3f) || (rich && w.time >= 600f)))
                return UnitType.Airfield;
            if (Have(UnitType.Biolab) == 0 && w.time >= 240f && (path == 2 || (rich && w.time >= 540f)))
                return UnitType.Biolab;
            // A second of its own tech's building once it has the ore.
            if (path == 2 && Have(UnitType.Biolab) == 1 && F.ore > 600 && w.time > 480f) return UnitType.Biolab;
            if (path == 1 && Have(UnitType.Airfield) == 1 && F.ore > 700 && w.time > 540f) return UnitType.Airfield;
            return UnitType.None;
        }

        /// <summary>Their army as it has seen it, by what answers it: shares of aircraft, armour
        /// (their Mech among it, as a target for armour-piercing guns), light ground troops, and
        /// what of theirs can shoot at aircraft.</summary>
        static void Shares(Snapshot s, out float air, out float heavy, out float light, out float aa)
        {
            float tot = s.eLightValue + s.eHeavyValue + s.eAirValue;
            if (tot < 1f) { air = 0f; heavy = 0.3f; light = 0.7f; aa = 0.5f; }
            else { air = s.eAirValue / tot; heavy = s.eHeavyValue / tot; light = s.eLightValue / tot; aa = s.eAntiAirValue / tot; }
            if (s.eMech) heavy = Mathf.Min(1f, heavy + 0.25f);
        }

        /// <summary>One of the options, by weight, on its own stream.</summary>
        UnitType Weighted(params (UnitType t, float w)[] options)
        {
            float sum = 0f;
            foreach (var o in options) if (o.w > 0f) sum += o.w;
            if (sum <= 0f) return options[0].t;
            float r = mixRng.F01() * sum;
            foreach (var o in options)
            {
                if (o.w <= 0f) continue;
                r -= o.w;
                if (r <= 0f) return o.t;
            }
            return options[0].t;
        }

        /// <summary>A specialist's weight falls off as it has more of them (a third of the rest).</summary>
        static float Few(Snapshot s, UnitType t, float wgt) => wgt * 3f / (3f + s.own[(int)t]);

        /// <summary>The Garrison's rifleman of the moment: Troopers by default (they shoot at
        /// aircraft too), Rocketeers against armour, Scorchers against light troops, Thumpers against towers and crowds,
        /// Marksmen against aircraft and riflemen -- the last two once the Armory stands.</summary>
        UnitType PickInfantry(Snapshot s)
        {
            Shares(s, out float air, out float heavy, out float light, out _);
            bool armory = s.own[(int)UnitType.Armory] > 0;
            return Weighted((UnitType.Trooper, 1f + 0.6f * air),
                            (UnitType.Rocketeer, Few(s, UnitType.Rocketeer, 0.15f + 1.4f * heavy)),
                            (UnitType.Scorcher, Few(s, UnitType.Scorcher, 0.2f + 1.1f * light)),
                            (UnitType.Thumper, armory ? Few(s, UnitType.Thumper, 0.15f + (s.eSentinels > 0 ? 0.6f : 0f) + 0.4f * light) : 0f),
                            (UnitType.Marksman, armory ? Few(s, UnitType.Marksman, 0.1f + 0.9f * air + 0.2f * light) : 0f));
        }

        /// <summary>The Workshop's tank: Maulers by default (towers above all), Lancers against
        /// armour and their Mech, the Aegis once it has seen aircraft, the Ironclad (once the Armory
        /// stands) against both.</summary>
        UnitType PickTank(Snapshot s)
        {
            Shares(s, out float air, out float heavy, out float light, out _);
            return Weighted((UnitType.Mauler, 1f + (s.eSentinels > 0 ? 0.8f : 0f) + 0.3f * light),
                            (UnitType.Lancer, Few(s, UnitType.Lancer, 0.3f + 1.6f * heavy)),
                            (UnitType.Aegis, air > 0.05f ? Few(s, UnitType.Aegis, 0.2f + 3f * air) : 0f),
                            (UnitType.Ironclad, s.own[(int)UnitType.Armory] > 0 ? Few(s, UnitType.Ironclad, 0.25f + 0.8f * air + 0.5f * heavy) : 0f));
        }

        /// <summary>The Airfield's: gunships, more where little of theirs shoots up; interceptors
        /// against their aircraft.</summary>
        UnitType PickAircraft(Snapshot s)
        {
            Shares(s, out float air, out _, out _, out float aa);
            return Weighted((UnitType.Kestrel, 1f + 0.6f * (1f - aa)),
                            (UnitType.Swift, air > 0.05f ? 0.3f + 3f * air : 0.1f));
        }

        /// <summary>The Biolab's: Skitters by default, Spitters against aircraft, Razorwings where
        /// little of theirs shoots up, the Gorehorn (once the Armory stands) to break their line.</summary>
        UnitType PickCreature(Snapshot s)
        {
            Shares(s, out float air, out _, out _, out float aa);
            Shares(s, out _, out _, out float light, out _);
            return Weighted((UnitType.Skitter, 1f),
                            (UnitType.Spitter, 0.8f + 2.5f * air),
                            (UnitType.Razorwing, Few(s, UnitType.Razorwing, 0.4f + 0.6f * (1f - aa))),
                            (UnitType.Gorehorn, s.own[(int)UnitType.Armory] > 0 ? Few(s, UnitType.Gorehorn, 0.35f + 0.6f * light) : 0f));
        }

        /// <summary>Armory research for the class it fields most, weapons and armour levelled out,
        /// from ore it can spare (more readily the keener its personality).</summary>
        bool BuyResearch(Snapshot s, Faction F)
        {
            if (w.time < 240f) return false;
            Unit armory = null;
            foreach (var u in w.units)
                if (u != null && !u.dying && u.team == team && u.Type == UnitType.Armory && u.Complete && u.researchLine < 0) { armory = u; break; }
            if (armory == null) return false;
            int spare = F.ore - (int)Mathf.Lerp(450f, 200f, Personality.researchTaste);
            if (spare <= 0) return false;
            float inf = 0f, veh = 0f, bio = 0f;
            for (int i = 0; i < (int)UnitType.Count; i++)
            {
                var d = Defs.Get((UnitType)i);
                if (d == null || !d.IsArmy || s.own[i] == 0) continue;
                float v = s.own[i] * d.cost;
                if (d.upgradeClass == UnitClass.Infantry) inf += v;
                else if (d.upgradeClass == UnitClass.Vehicle) veh += v;
                else if (d.upgradeClass == UnitClass.Creature) bio += v;
            }
            var classes = new List<(float v, ArmyUpgrade weapons, ArmyUpgrade armour)>
            {
                (inf, ArmyUpgrade.InfantryWeapons, ArmyUpgrade.InfantryArmour),
                (veh, ArmyUpgrade.VehicleWeapons, ArmyUpgrade.VehiclePlating),
                (bio, ArmyUpgrade.CreatureWeapons, ArmyUpgrade.CreatureCarapace),
            };
            classes.Sort((a, b) => b.v.CompareTo(a.v));
            foreach (var c in classes)
            {
                if (c.v < 300f) break;
                var first = F.armyUpgrades[(int)c.armour] < F.armyUpgrades[(int)c.weapons] ? c.armour : c.weapons;
                foreach (var up in new[] { first, first == c.weapons ? c.armour : c.weapons })
                {
                    int cost = w.ArmyUpgradeCost(team, up);
                    if (cost < 0 || cost > spare || w.ResearchingAt(team, up) != null) continue;
                    if (!Pay(2, "Research")) return false;           // select the Armory + hotkey
                    Budget.Note(w.time);
                    lastSelHash = -1f;
                    if (!w.CmdArmoryResearch(armory, up)) return false;
                    Dbg.researchBought++;
                    return true;
                }
            }
            return false;
        }

        bool BuyMechUpgrade(Faction F)
        {
            var bay = w.BayOf(team);
            if (bay == null || !bay.Complete || F.researching >= 0 || F.mechLost) return false;
            int spare = F.ore - (w.time < 300f ? 250 : 150);
            if (spare <= 0) return false;
            var st = Selector.Current;
            MechUpgrade[] order = st == Strategy.TurtleTech || st == Strategy.Eco || st == Strategy.Expand ? HoldOrder
                : st == Strategy.Harass || st == Strategy.Feint ? RaidOrder : AttackOrder;
            // Level the lines out: a second level of anything waits for a first of the rest.
            for (int pass = 0; pass < 3; pass++)
                foreach (var up in order)
                {
                    if (F.upgrades[(int)up] > pass) continue;
                    int cost = w.UpgradeCost(team, up);
                    if (cost < 0 || cost > spare) continue;
                    if (!Pay(2, "Upgrade")) return false;           // select the bay + hotkey
                    Budget.Note(w.time);
                    lastSelHash = -1f;
                    if (!w.CmdMechUpgrade(bay, up)) return false;
                    Dbg.upgradesBought++;
                    return true;
                }
            return false;
        }

        // ------------------------------------------------------------ the Mech's advice
        /// <summary>Its Mech speaks. The AI hears it as a player would -- a message and a
        /// place -- and acts on it or not by its own read and its personality's trust: a
        /// call to defend is usually worth answering, a call to attack only with an army
        /// that can, and never with the base under attack.</summary>
        void OnWorldEvent(GameEvent e)
        {
            if (e.kind == GameEventKind.UnderAttack && e.team == team && e.unit != null && e.unit.def.harvester) lastWorkerHitAt = w.time;
            if (e.kind == GameEventKind.UnitReady && e.team == team && e.type >= 0 && e.type < UnitType.Count) Dbg.trained[(int)e.type]++;
            if (e.kind != GameEventKind.MechAdvice || e.team != team || w.winner >= 0) return;
            HearMech(e.advice, new Vector2(e.pos.x, e.pos.z));
        }

        /// <summary>A call from its Mech: from the comms event (a Commander on the player's
        /// side, spectating), or straight from the opponent's MechBrain, which says nothing
        /// aloud. The same kind and place either way.</summary>
        public void HearMech(MechAdviceKind kind, Vector2 at)
        {
            if (w == null || w.winner >= 0) return;
            var s = Perception.Snap;
            var plan = Plan(Selector.Current);
            float trust = Personality.adviceTrust;
            bool heed;
            switch (kind)
            {
                case MechAdviceKind.Attack:
                {
                    float p = trust;
                    if (s.armyValue < plan.pushThreshold * 0.45f) p *= 0.25f;
                    if (w.time < adviceDefendUntil) p = 0f;
                    heed = rng.F01() < p;
                    if (heed) { adviceAttackUntil = w.time + 45f; adviceAttackAt = at; adviceWave = false; }
                    break;
                }
                case MechAdviceKind.Defend:
                    heed = rng.F01() < 0.5f + 0.5f * trust;
                    if (heed) { adviceDefendUntil = w.time + 30f; adviceDefendAt = at; }
                    break;
                case MechAdviceKind.Regroup:
                    heed = rng.F01() < trust;
                    if (heed) adviceRegroupUntil = w.time + 12f;
                    break;
                default:
                    return;   // news and morale: nothing to decide
            }
            if (heed) Dbg.adviceHeeded++; else Dbg.adviceIgnored++;
            Dbg.mechAdvice = $"{kind} at {at.x:0},{at.y:0}: {(heed ? "heeded" : "ignored")}";
        }

        // ------------------------------------------------------------ scouting
        void RunScouts()
        {
            var s = Perception.Snap;
            scouts.RemoveAll(u => !Unit.Live(u) || u.deserted);

            // A Digger scouts only to find their base. Once it is known, keeping the picture
            // fresh is the army's job: a Digger sent to look again each time it went stale (about
            // 37 s after the last look) walked into their towers, 70 of one side's 101 Diggers in
            // a 40-minute match, and a side behind never got past six. One still out comes home
            // once it has seen their Foundry (the first building it sees is often a Bunkhouse or
            // a tower out from it: stopped there, it left the base believed 10-26 m off), walked
            // back rather than stopped (it could stand under a bay's gun); step 6 takes it on.
            if (s.eFoundries > 0)
                for (int i = scouts.Count - 1; i >= 0; i--)
                {
                    var h = scouts[i];
                    if (h.Type != UnitType.Worker) continue;
                    if (!Issue(Single(h), 0, Perception.Home, null)) return;   // no orders to spare: next pass
                    scouts.RemoveAt(i);
                }

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
                        if (ws.Count >= 6 && !s.enemyBaseKnown) pick = ws[ws.Count - 1];
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
                    var c = w.NearestReachable(Perception.Home, anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
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
            // (For the mop-up below: set before any return, as a scout often sees their Foundry before
            // it has an army.)
            if (s.eFoundries > 0) sawFoundry = true;

            main.RemoveAll(u => !Unit.Live(u) || u.deserted);
            harass.RemoveAll(u => !Unit.Live(u) || u.deserted);
            scouts.RemoveAll(u => !Unit.Live(u) || u.deserted);
            prong.RemoveAll(u => !Unit.Live(u) || u.deserted);

            // Assign fresh army units to a squad. Skimmers are the natural raiders. A personality
            // with a taste for raids (about half of them) keeps a pair of Skimmers at their Diggers
            // whatever the plan: against the scripted players the plan was never Harass, so the
            // Skimmers only ever scouted or walked with the army, and every match went the same way.
            bool sideRaid = st != Strategy.Harass && Personality.raidTaste > 0.5f && w.time > 180f && s.skimmers >= 2;
            int harassWant = st == Strategy.Harass ? 4 : sideRaid ? 2 : 0;
            foreach (var t in SquadTypes)
                foreach (var h in OwnOf(t))
                {
                    if (Reserved(h)) continue;
                    if (Raider(t) && harass.Count < harassWant && (!sideRaid || t == UnitType.Skimmer)) harass.Add(h);
                    else main.Add(h);
                }
            if (sideRaid)
            {
                // A side raid is two Skimmers: whatever else the Harass plan had sent goes back to
                // the army when the plan turns...
                for (int i = harass.Count - 1; i >= 0; i--)
                    if (harass[i].Type != UnitType.Skimmer) { main.Add(harass[i]); harass.RemoveAt(i); }
                while (harass.Count > harassWant) { main.Add(harass[harass.Count - 1]); harass.RemoveAt(harass.Count - 1); }
                // ...and Skimmers already with the army are taken back out for it.
                for (int i = main.Count - 1; i >= 0 && harass.Count < harassWant; i--)
                    if (main[i].Type == UnitType.Skimmer) { harass.Add(main[i]); main.RemoveAt(i); }
            }
            if (harassWant == 0 && harass.Count > 0)
            {
                main.AddRange(harass);   // fold raiders back in when the plan changes
                harass.Clear();
            }
            Dbg.squads = main.Count;

            // Home defence. Only fresh sightings count: remembered attackers linger,
            // and counting them pinned the whole army at home in the original.
            float threatHome = 0f, structThreat = 0f;
            Vector2 home = Perception.Home;
            Vector2 threatAt = home, structAt = home, siteAt = home;
            bool site = false;
            foreach (var r in Perception.Enemies)
            {
                // A tower or a Mech Bay raised in or beside its base is an attack, not scenery:
                // the bay's gun shoots its Diggers and its Mech would come down among them.
                // Left to chance, a bay built 12 m into its base stood for nearly two minutes.
                bool structure = Defs.Get(r.type).building;
                if (structure && r.type != UnitType.MechBay && r.type != UnitType.Sentinel) continue;
                if (!Perception.Reactable(r, reactionDelay)) continue;   // no instant reactions
                // stale: not a live threat (a site only while in sight -- it is gone or finished soon)
                if (w.time - r.lastSeen > (structure ? (r.unfinished ? 5f : 60f) : 10f)) continue;
                float d = (r.pos - home).magnitude;
                if (d >= 55f) continue;
                if (structure)
                {
                    // Still going up, it has no gun yet: whatever army there is can stop it, and
                    // the Digger raising it. Left alone it was finished in 35 s and its tower
                    // shot four Diggers before the army was big enough to take it on.
                    if (r.unfinished)
                    {
                        if (!site || d < (siteAt - home).magnitude) siteAt = r.pos;
                        site = true;
                        continue;
                    }
                    structThreat += r.type == UnitType.MechBay ? 400f : 250f;
                    if (d < (structAt - home).magnitude || structAt == home) structAt = r.pos;
                    continue;
                }
                threatHome += Defs.IsWorkerType(r.type) && !Defs.Get(r.type).Armed ? 15f : r.type == UnitType.Worker ? 15f : Defs.ArmyValue(r.type);
                if (d < (threatAt - home).magnitude || threatAt == home) threatAt = r.pos;
            }
            // ...but it is taken on with an army that can: a handful of Troopers sent at a Mech
            // Bay's gun tower only fed it, the bay stood, and its Mech came down among them. With
            // their Mech standing guard over it, that means an army that can break the Mech too
            // (round a bay 44-66 m out it fed 21-22 units to the Mech, and lost them all).
            // Not inside its own base, though: there the Mech is in among its Foundry and Diggers,
            // and waiting for that army only gave it the base (a bay 12 m in stood, and the match
            // was lost with the army idle beside it).
            float structNeed = Mathf.Max(450f, plan.pushThreshold * 0.5f);
            bool mechByIt = s.eMech && s.eMechSeenAgo < 10f && (s.eMechPos - structAt).magnitude < 40f;
            // The readings the in-base rule decides (BayEdgeCases case 9's A/B counts them).
            if (structThreat > 0f && mechByIt && (structAt - home).magnitude <= 25f && threatHome <= Mathf.Max(45f, s.armyValue * 0.14f) &&
                s.armyValue >= structNeed && s.armyValue < MechNeed(s))
                Dbg.inBaseMechSplit++;
            if (mechByIt && (MechGateInBase || (structAt - home).magnitude > 25f))
                structNeed = Mathf.Max(structNeed, MechNeed(s));
            if (structThreat > 0f && threatHome <= Mathf.Max(45f, s.armyValue * 0.14f) &&
                s.armyValue >= structNeed)
            {
                threatHome = structThreat;
                threatAt = structAt;
            }
            if (site && !IgnoreSites && threatHome <= Mathf.Max(45f, s.armyValue * 0.14f))
            {
                threatHome = Mathf.Max(45f, s.armyValue * 0.14f) + 1f;
                threatAt = siteAt;
            }

            Vector2 mainCentre = Vector2.zero;
            int n = 0;
            foreach (var h in main) { mainCentre += h.pos; n++; }
            if (n > 0) mainCentre /= n;

            // The Mech saw them coming before we could: meet them where it says.
            if (w.time < adviceDefendUntil && main.Count > 0 && threatHome <= 0f)
            {
                threatHome = Mathf.Max(45f, s.armyValue * 0.14f) + 1f;
                threatAt = w.NearestReachable(home, adviceDefendAt);
            }

            // Divert only if the raid matters relative to what we field.
            float divertAt = Mathf.Max(45f, s.armyValue * 0.14f);
            if (threatHome > divertAt && main.Count > 0)
            {
                // Called home, not beaten back: booked as a repulse, it marked the way in as
                // failed and (their Mech about) raised the next push's threshold by a fifth.
                // Ended first, so the prong it folds back into the army is called home with it
                // (ordered before, the prong walked on to its own point).
                bool prongBack = prong.Count > 0;
                EndWave(true);
                if ((prongBack || WorthReissuing(threatAt, rally, 12f)) && Issue(main, 1, threatAt, null)) rally = threatAt;
                committed = false;
                return;
            }

            // Retreat when the local balance is bad -- or when its Mech, which sees what
            // it cannot, says the attack is walking into too much.
            if (n > 0 && committed)
            {
                float mine = Influence.Sample(0, mainCentre);
                float theirs = Influence.Sample(1, mainCentre);
                bool told = w.time < adviceRegroupUntil;
                if ((theirs > mine * 1.5f + 0.5f || told) && w.time > retreatUntil)
                {
                    var safe = w.NearestReachable(home, Influence.SafestNear(home, 30f));
                    if (Issue(main, 0, safe, null))
                    {
                        rally = safe;
                        committed = false;
                        retreatUntil = w.time + 8f;   // don't oscillate
                        // ...and gather before going again. Nothing held it back: the next pass,
                        // 0.4 s later, started a new wave from where it had just run from (32
                        // waves in a match). It goes again in 25 s, or sooner a third stronger.
                        regroupUntil = w.time + 25f;
                        regroupArmy = s.armyValue * 1.3f;
                        adviceRegroupUntil = -1f;   // heard and done (not dropped unheeded)
                        Retreats++;
                        EndWave(false);
                    }
                    return;
                }
            }

            // Harass squad: go for workers, never trade with the army.
            if (harass.Count > 0)
            {
                Vector2 hc = Vector2.zero;
                foreach (var h in harass) hc += h.pos;
                hc /= harass.Count;
                // The Digger it can get at: near the raiders and away from what guards them. (It
                // went for the one nearest the middle of their base -- the best guarded of all.)
                Vector2 target = s.enemyBase;
                Unit victim = null;
                float bestD = 1e30f;
                foreach (var e in w.units)
                {
                    if (e == null || e.dying || e.team == team || e.team == 2 || !e.def.harvester || e.def.Heavy) continue;
                    if (!w.Visible(team, e.pos)) continue;           // must actually see it
                    float d = (e.pos - hc).magnitude + 25f * Influence.Sample(1, e.pos);
                    if (d < bestD) { bestD = d; victim = e; target = e.pos; }
                }
                float theirs = Influence.Sample(1, hc), mine = Influence.Sample(0, hc);
                // ...or once one of them is being shot to pieces: going by the field alone, a raid
                // never once pulled back in a trial and lost 7 Skimmers for 7 Diggers on one side,
                // 3 for none on the other.
                bool bleeding = false;
                foreach (var h in harass)
                    if (h.hp < 0.5f * h.MaxHp && w.time - h.lastDamagedT < 2f) { bleeding = true; break; }
                if (theirs > mine * 2f + 0.8f || bleeding)
                {
                    var safe = w.NearestReachable(home, Influence.SafestNear(home, 40f));
                    if (WorthReissuing(safe, harassTarget, 14f) && Issue(harass, 0, safe, null)) harassTarget = safe;
                    // Driven off, it rests before the next sortie: straight back in, it ran into
                    // the same guns as soon as it was clear of them.
                    if (raiding) raidRestUntil = w.time + 25f;
                    raiding = false;
                }
                else if (w.time >= raidRestUntil)
                {
                    bool sent = false;
                    if (victim != null)
                    {
                        if (WorthReissuing(target, harassTarget, 9f) && Issue(harass, 2, target, victim)) { harassTarget = target; sent = true; }
                    }
                    else if (WorthReissuing(target, harassTarget, 14f) && Issue(harass, 1, target, null)) { harassTarget = target; sent = true; }
                    if (sent && !raiding)
                    {
                        raiding = true;
                        Dbg.raidSorties++;
                        if (sideRaid) Dbg.sideRaidSorties++;
                    }
                }
            }
            else raiding = false;

            if (main.Count == 0)
            {
                // A wave that did not come back was beaten, whatever is built next.
                if (waveOn && w.time - waveStart > 8f) { committed = false; EndWave(false); }
                return;
            }

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
                    // To one side for 22 s, then onto the base and there to the end. (The phase
                    // used to wrap, walking an army already in their base back out to the side
                    // every 44 s.)
                    if (!committed || w.time - commitT < 22f)
                    {
                        Vector2 dir = Norm(s.enemyBase - home);
                        attackAt = w.NearestReachable(home, s.enemyBase + Perp(dir) * 42f);
                    }
                    break;
                }
                default:
                    wantAttack = s.armyValue >= plan.pushThreshold;
                    break;
            }
            // Beaten back twice by their Mech at home: no new plan wave until the army is massed
            // (`MassedArmy`), as a player stops trickling into a turtle and masses. Against the macro
            // player its waves went in at 870-1,460 of army, every one lost to the Mech, its bay's
            // tower and the base, while its supply never passed 100 of 200: each wave bled what it
            // had built. An army that has stopped growing (no new high by a twentieth for a minute;
            // cut to under four fifths of its high, it counts again from there, or losses would read
            // as a stall) is as massed as it gets: without its own fit Mech the massed army is
            // 4,400-7,600, more than it ever fielded. Only the start waits (a feint out never sets
            // `waveOn`, only `committed`): a wave out goes on down to the plan's threshold (held to the
            // massed army every pass, it would be called off at its first losses and set out again
            // with the next rifleman). A broke side, the mop-up, its Mech's call and theirs away or
            // hurt still go, and home defence and the Mech's advice read the plan's threshold as
            // before (raised there, a tower put up by its base waited for half a maxed army).
            Dbg.pushThreshold = plan.pushThreshold;
            Dbg.massedArmy = MaxingOut(s) ? MassedArmy(s, plan) : 0f;
            bool feintOut = committed && st == Strategy.Feint;
            if (!MaxingOut(s) || waveOn || feintOut) massHigh = 0f;
            else if (s.armyValue > massHigh * 1.05f || s.armyValue < massHigh * 0.8f) { massHigh = s.armyValue; massHighAt = w.time; }
            bool stalled = massHigh > 0f && w.time - massHighAt > 60f;
            if (wantAttack && !waveOn && !feintOut && MaxingOut(s) && s.armyValue < Dbg.massedArmy && !stalled)
                wantAttack = false;
            // It can never grow -- no Digger and none coming, and no ore for one or no Foundry to
            // train it at -- so waiting for the push threshold is waiting for ever: it goes with
            // what it has (two such sides, armies standing under their thresholds, stood a whole
            // match apart; with the Diggers' reserve a side that can still restart never reads so).
            // Only the threshold is waived: the Mech, regroup and pull-out gates below still hold
            // (set after them, a beaten army walked back into the fight every 8 s).
            // Every Foundry of theirs it has seen has fallen, their Mech is not about, and nothing it
            // remembers of theirs (army, towers, a bay's gun) is a match for its army: what they have
            // left is scattered -- a Digger stopped on the road, a structure put down anywhere -- and
            // it goes looking with whatever army it has (waiting for its push threshold, it stood at
            // home to the cap against a side down to one Digger). A mop-up wave goes on to what it
            // finds while that holds (a Bunkhouse found would end it, and the army stood at home
            // beside it). Not before it has seen a Foundry of theirs: any first building (a tower
            // out front, a site) reads as their base.
            bool weaker = !s.eMech && TheirStrength(s) <= s.armyValue;
            bool mopUp = !LegacySweep && sawFoundry && s.eFoundries == 0 && weaker;
            if (waveOn && mopUp) mopUpWave = true;   // a plan wave that took their last Foundry, too
            if (mopUp || (waveOn && mopUpWave && weaker)) wantAttack = true;
            bool byPlan = wantAttack, broke = EconomyDead(s);
            if (broke) wantAttack = true;
            // Its Mech called the moment: go now, at what it named, with less than the
            // plan would wait for (the Mech walks with it).
            bool advised = w.time < adviceAttackUntil && s.armyValue >= plan.pushThreshold * 0.45f;
            if (advised && st != Strategy.Feint) wantAttack = true;
            // Their Mech seen out in the field, far from its base: the base is open, and the
            // bay that mends it with it. Go now, with the army the plan would push with
            // were there no Mech at all.
            bool window = MechAway(s) && s.armyValue >= PlanFor(st).pushThreshold * Personality.timing * 0.9f && w.time >= windowNotBefore;
            if (window && !waveOn && st != Strategy.Feint)
            {
                wantAttack = true;
                mechAwayWave = true;
            }
            // A wave sent through the window presses on until their Mech is seen back near its
            // base, then pulls out: hit and run. (Calling it off whenever their Mech was out of
            // sight for a while had the army walking to their base and back every nine
            // seconds; holding it to the end, whatever came home, ran it into the Mech.)
            if (waveOn && mechAwayWave)
            {
                bool mechHome = s.eMech && s.eMechSeenAgo < 3f && s.enemyBaseKnown && (s.eMechPos - s.enemyBase).magnitude < 55f;
                wantAttack = !mechHome;
            }
            // Their Mech seen badly hurt: go now, with the army the plan would push with were there
            // no Mech, before its bay mends it (at 50 a second once left alone it is whole again
            // inside a minute). The wave presses on until their Mech is seen mended.
            bool hurt = s.eMech && s.eMechSeenAgo < 10f && s.eMechHpFrac < 0.4f &&
                        s.armyValue >= PlanFor(st).pushThreshold * Personality.timing && w.time >= windowNotBefore;
            if (hurt && !waveOn && st != Strategy.Feint)
            {
                wantAttack = true;
                hurtWave = true;
            }
            if (waveOn && hurtWave) wantAttack = !(s.eMech && s.eMechSeenAgo < 3f && s.eMechHpFrac > 0.65f);
            // Their Mech about and its own in the gantry being mended: the next wave waits
            // for it (unless it called the attack itself, or theirs is away) -- without it, a
            // wave is fed to theirs.
            if (wantAttack && !advised && !window && !waveOn && s.eMech && s.mechAlive && s.mechHpFrac < 0.6f) wantAttack = false;
            // Regrouping after a retreat: no new wave yet, whatever calls for one.
            if (w.time < regroupUntil && s.armyValue < regroupArmy) wantAttack = false;

            if (wantAttack)
            {
                if (!committed) { committed = true; commitT = w.time; }
                // (Not a mop-up: there is nothing to feint at, and it would stand at an empty base.)
                if (st == Strategy.Feint && !mopUp && !mopUpWave)
                {
                    // The feint does its own positioning (above); a wave still on is called off
                    // first, its prong back with the rest (left on, the prong was never ordered again).
                    bool prongBack = prong.Count > 0;
                    if (waveOn) EndWave(true);
                    bool idle = false;
                    foreach (var h in main) if (h.order == Order.Idle) { idle = true; break; }
                    if ((idle || prongBack || WorthReissuing(attackAt, rally, 12f)) && Issue(main, 1, attackAt, null)) rally = attackAt;
                    return;
                }
                if (!waveOn)
                {
                    // What sent a wave at a Mech it knows of (the evaluation line): its threshold,
                    // its own Mech's call, their Mech away, their Mech hurt, or nothing to wait for.
                    if (s.eMech) Dbg.mechWavesBy[hurtWave ? 3 : mechAwayWave ? 2 : byPlan ? 0 : advised ? 1 : broke ? 4 : 0]++;
                    // A plan wave while maxing out went with the massed army (the evaluation line).
                    if (byPlan && MaxingOut(s) && !hurtWave && !mechAwayWave)
                    {
                        Dbg.massedWaves++;
                        if (stalled && s.armyValue < Dbg.massedArmy) Dbg.stalledWaves++;
                        if (Dbg.firstMassedAt < 0f) Dbg.firstMassedAt = w.time;
                    }
                    StartWave(s, st, plan, mainCentre);
                    mopUpWave = mopUp;
                }
                if (advised && !adviceWave)
                {
                    // Aim the wave at what the Mech named, and skip the staging: the moment is now.
                    adviceWave = true;
                    waveAt = adviceAttackAt;
                    staging = false;
                    Dbg.wave = $"{Dbg.wave} -> advised target";
                }
                RunWave(s, mainCentre);
            }
            else
            {
                committed = false;
                EndWave(true);
                // Hold a defensible spot between home and the likely approach.
                Vector2 face = s.enemyBaseKnown ? s.enemyBase : Perception.BaseGuesses[0];
                Vector2 guard = home + Norm(face - home) * 16f;
                guard = w.NearestReachable(home, Influence.SafestNear(w.NearestReachable(home, guard), 14f));
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
            // Their Mech Bay: before their Mech lands, killing it means it never does;
            // after, it is where that Mech goes to be mended.
            // Their Mech Bay: before their Mech lands, it never will if the bay falls; while
            // their Mech is out in the field, the bay is what would mend it.
            float rBay = s.eMechBay ? (s.eMech || Remembers(UnitType.Mech) ? (MechAway(s) ? 1.6f : 0.5f) : 2.2f) : 0f;
            if (st == Strategy.TimingPush) rProd += 0.8f;
            if (st == Strategy.CounterAttack) { rWork += 0.9f; rBase += 0.4f; }
            if (st == Strategy.Expand || st == Strategy.TurtleTech) rExp += 0.7f;
            if (!Remembers(UnitType.Garrison) && !Remembers(UnitType.Workshop)) rProd = 0f;
            if (OutlyingFoundry(s, out _) == false) rExp = 0f;
            if (mechAwayWave) { rBase *= 1.6f; rBay *= 1.5f; rExp *= 0.3f; rWork *= 0.3f; }
            // Their Mech hurt: the bay that would mend it, where it will be.
            if (hurtWave) { rBay *= 4f; rExp *= 0.3f; rWork *= 0.3f; }
            float roll = rng.F01() * (rBase + rProd + rExp + rWork + rBay);
            waveTarget = roll < rBase ? WaveTarget.Base : roll < rBase + rProd ? WaveTarget.Production
                       : roll < rBase + rProd + rExp ? WaveTarget.Expansion
                       : roll < rBase + rProd + rExp + rWork ? WaveTarget.Workers : WaveTarget.MechBay;
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
            stagePoint = w.NearestReachable(home, ApproachPoint(approach, from, waveAt));
            staging = approach != Approach.Direct || (waveAt - from).magnitude > 70f;
            // Its own Mech, fit and near the way in: gather on it, so they go in together
            // (MechBrain walks with an attacking army; a wave that left it behind met the
            // enemy on its own).
            var ownMech = w.MechOf(team);
            stageWithMech = false;
            if (ownMech != null && ownMech.mech.Landed && ownMech.hp / Mathf.Max(1f, ownMech.MaxHp) > 0.6f &&
                (ownMech.pos - stagePoint).magnitude < 70f)
            {
                stagePoint = w.NearestReachable(home, Vector2.Lerp(stagePoint, ownMech.pos, 0.5f));
                staging = true;
                stageWithMech = true;
            }
            stageUntil = w.time + 22f;

            // A second prong: a few fast units at something else, when there is army
            // to spare and the style likes it.
            // (Not a massed wave at a Mech-held base: it masses to go in together.)
            prongOn = false;
            if (!MaxingOut(s) && s.armyValue > plan.pushThreshold * 1.6f && rng.F01() < 0.25f + Personality.raidTaste * 0.5f)
            {
                var other = waveTarget == WaveTarget.Workers ? WaveTarget.Expansion : WaveTarget.Workers;
                if (other == WaveTarget.Expansion && !OutlyingFoundry(s, out _)) other = WaveTarget.Production;
                prongAt = TargetOf(other, s, from);
                if ((prongAt - waveAt).magnitude > 25f)
                {
                    prong.Clear();
                    int want = Mathf.Max(2, main.Count / 4);
                    for (int i = main.Count - 1; i >= 0 && prong.Count < want; i--)
                        if (main[i].Type != UnitType.Mauler && !main[i].def.heavyArmour && main[i].Type != UnitType.Thumper) { prong.Add(main[i]); main.RemoveAt(i); }
                    prongOn = prong.Count >= 2;
                    if (!prongOn) { main.AddRange(prong); prong.Clear(); }
                }
            }
            Dbg.wave = $"{waveTarget} via {approach}{(staging ? " (staging)" : "")}{(prongOn ? $", prong of {prong.Count}" : "")}" +
                       $"{(mechAwayWave ? ", their Mech away" : "")}{(hurtWave ? ", their Mech hurt" : "")}{(stageWithMech ? ", on its Mech" : "")}";
            if (mechAwayWave) Dbg.mechWindows++;
            if (hurtWave) Dbg.hurtWindows++;
            if (stageWithMech) Dbg.stagedWithMech++;
        }

        void RunWave(Snapshot s, Vector2 mainCentre)
        {
            // Gather at the staging point until most of the army is there, or it has
            // waited long enough: going in strung out is how waves die.
            if (staging)
            {
                int near = 0;
                foreach (var h in main) if (h.Dist(stagePoint) < 13f) near++;
                var ownMech = stageWithMech ? w.MechOf(team) : null;
                bool mechThere = ownMech == null || ownMech.Dist(stagePoint) < 25f;
                if ((near >= main.Count * 0.7f && mechThere) || w.time > stageUntil) staging = false;
                else
                {
                    bool idle = false;
                    foreach (var h in main) if (h.order == Order.Idle && h.Dist(stagePoint) > 13f) { idle = true; break; }
                    if ((idle || WorthReissuing(stagePoint, rally, 10f)) && Issue(main, 1, stagePoint, null)) rally = stagePoint;
                    return;
                }
            }
            // Once the target is gone, go on to the nearest structure it remembers -- or, none
            // left, to Diggers it saw lately (a side stands while it has either).
            if (!StillStanding(waveAt))
            {
                float bestD = 1e30f;
                foreach (var r in Perception.Enemies)
                {
                    if (!Defs.Get(r.type).building) continue;
                    float d = (r.pos - mainCentre).magnitude;
                    if (d < bestD) { bestD = d; waveAt = r.pos; }
                }
                if (bestD > 1e29f)
                    foreach (var r in Perception.Enemies)
                    {
                        if (!Defs.IsWorkerType(r.type) || r.type == UnitType.Hauler || w.time - r.lastSeen > 30f) continue;
                        float d = (r.pos - mainCentre).magnitude;
                        if (d < bestD) { bestD = d; waveAt = r.pos; }
                    }
                // Nothing of theirs remembered: their base -- or, once that is seen empty, the
                // ground it has not looked at for longest. (It re-clicked the empty square,
                // and whatever they had left elsewhere stood to the end of the match.)
                if (bestD > 1e29f) waveAt = w.Visible(team, s.enemyBase) ? Sweep(mainCentre) : s.enemyBase;
            }
            bool anyIdle = false;
            foreach (var h in main) if (h.order == Order.Idle) { anyIdle = true; break; }
            if ((anyIdle || WorthReissuing(waveAt, rally, 12f)) && Issue(main, 1, waveAt, null)) rally = waveAt;

            if (prongOn && prong.Count > 0)
            {
                bool idle = false;
                foreach (var h in prong) if (h.order == Order.Idle) { idle = true; break; }
                if (idle || WorthReissuing(prongAt, prongTarget, 12f))
                    if (Issue(prong, 1, prongAt, null)) prongTarget = prongAt;
            }
        }

        /// <summary>Their Mech seen lately and far from its base: out in the field, away from
        /// what defends and mends it.</summary>
        bool MechAway(Snapshot s) =>
            s.eMech && s.eMechSeenAgo < 15f && s.enemyBaseKnown && (s.eMechPos - s.enemyBase).magnitude > 70f;

        void EndWave(bool calledOff)
        {
            if ((mechAwayWave || hurtWave) && waveOn) windowNotBefore = w.time + 20f;
            mechAwayWave = hurtWave = false;
            if (!waveOn) return;
            if (calledOff) Dbg.wavesCalledOff++;
            // A wave beaten back (not merely called off by a change of plan) marks
            // its approach as one to avoid next time -- and, with their Mech about, makes
            // the next push wait for a bigger army. Without that it fed their Mech a wave
            // at a time: 22 waves in a match, and a Mech on 156 kills still standing.
            if (!calledOff && w.time - waveStart > 8f)
            {
                lastFailedApproach = approach;
                // ...a wave their Mech beat, that is: seen in the last half minute (believed about all
                // match now, it would count waves the towers beat with the Mech far off).
                if (Perception.Snap.eMech && Perception.Snap.eMechSeenAgo < 30f) mechRepulses = Mathf.Min(mechRepulses + 1, 4);
            }
            waveOn = staging = false;
            adviceWave = mopUpWave = false;
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
                case WaveTarget.MechBay:
                    return s.eMechBay ? s.eMechBayPos : s.enemyBase;
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
            {
                // Their Mech counts only in sight: remembered now until it is seen to fall, it held
                // a wave on a target it had emptied, where the Mech had stood a while ago.
                if (r.type == UnitType.Mech && !r.stillVisible) continue;
                if ((r.pos - at).sqrMagnitude < 12f * 12f) return true;
            }
            return !w.Visible(team, at);   // not seen emptied yet: keep going
        }

        /// <summary>Where to look for what they have left: the ore field or likely base site it
        /// has not seen for longest, nearer ones first, on ground its army can reach. (The ore
        /// fields are on the map for anyone to read, like the base sites.)</summary>
        Vector2 Sweep(Vector2 from)
        {
            double t0 = Time.realtimeSinceStartupAsDouble;   // its cost, for the trials (decides nothing)
            Vector2 best = Perception.Snap.enemyBase;
            float bestScore = -1e30f;
            void Consider(Vector2 p)
            {
                p = w.NearestReachable(Perception.Home, p);
                if (w.Visible(team, p)) return;
                float score = Mathf.Min(w.Staleness(team, p), 240f) - (p - from).magnitude * 0.6f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            foreach (var g in Perception.BaseGuesses) Consider(g);
            foreach (var n in w.units)
                if (n != null && !n.dying && n.Type == UnitType.Ore) Consider(n.pos);
            // The best of those looked at in the last minute, and nothing found: what is left stands
            // elsewhere, so the ground at large, where it has not looked for longest (16 m apart; its
            // own record of what it has seen, as a player's minimap shows explored ground). A point
            // is snapped to reachable ground only if its own score could beat the best so far.
            if (!LegacySweep && w.Staleness(team, best) < 60f)
            {
                float size = w.MapSize;
                for (float x = 8f; x < size; x += 16f)
                    for (float y = 8f; y < size; y += 16f)
                    {
                        var q = new Vector2(x, y);
                        if (Mathf.Min(w.Staleness(team, q), 240f) - (q - from).magnitude * 0.6f > bestScore) Consider(q);
                    }
            }
            Dbg.sweeps++;
            Dbg.sweepMsMax = Mathf.Max(Dbg.sweepMsMax, (float)((Time.realtimeSinceStartupAsDouble - t0) * 1000.0));
            return best;
        }

        /// <summary>What it remembers of their fighting strength: their army, and a finished tower 250
        /// and a finished Mech Bay's gun 400, as home defence weighs them.</summary>
        float TheirStrength(Snapshot s)
        {
            float v = s.eArmyValue;
            foreach (var r in Perception.Enemies)
                if (!r.unfinished) v += r.type == UnitType.Sentinel ? 250f : r.type == UnitType.MechBay ? 400f : 0f;
            return v;
        }

        /// <summary>Editor A/B only: Diggers as before -- half-built sites tended and builders left be
        /// whatever is there, their Mech a danger only while seen (4 s), Diggers cleared just past its
        /// reach. The evaluation runner resets it.</summary>
        public static bool LegacyDiggerSafety;

        // A Foundry of theirs seen at some point this match, and the wave on being a mop-up.
        bool sawFoundry, mopUpWave;
        // While massing: its army's last high (a twentieth over the one before) and when.
        float massHigh, massHighAt;
        /// <summary>Editor A/B only: no mop-up and no open-map search, as before. The evaluation
        /// runner resets it.</summary>
        public static bool LegacySweep;

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
            LetGoOfRunaway();

            Unit best = null;
            float bestScore = 1e30f;
            // Their Mech in the middle of the fight: everything on it. It does more harm
            // a second than a dozen of anything else, and wounded it falls back to mend.
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.mech == null || e.team == team || e.Untargetable) continue;
                if (!w.Visible(team, e.pos) || (e.pos - c).magnitude > 30f || (e == letGo && w.time < letGoUntil)) continue;
                if (Perception.Snap.armyValue < 500f && !Perception.Snap.mechAlive) continue;
                best = e;
                bestScore = -1e30f;
            }
            foreach (var e in w.units)
            {
                if (best != null && bestScore < -1e29f) break;
                if (e == null || e.dying || e.team == team || e.team == 2 || e.def.building) continue;
                if (!w.Visible(team, e.pos) || (e == letGo && w.time < letGoUntil)) continue;
                float d = (e.pos - c).magnitude;
                if (d > 28f) continue;
                // Prefer the weakest, nearest thing that can shoot back.
                float score = e.hp + d * 2.5f - (e.def.range > 0f ? 45f : 0f);
                if (score < bestScore) { bestScore = score; best = e; }
            }
            if (best == null) { focusTarget = null; return; }
            if (best == focusTarget && Unit.Live(focusTarget)) return;
            // Only what is near it turns on it, and nothing on a plain move (a retreat, or a
            // Mauler moving to where its shells clear): ordered onto one target, the whole army
            // turned back out of a retreat, and its far end walked past the enemy in between.
            focusGroup.Clear();
            foreach (var h in main)
                if (Unit.Live(h) && h.def.Armed && h.order != Order.Move && h.Dist(best) < h.def.range + 14f) focusGroup.Add(h);
            if (focusGroup.Count > 0 && Issue(focusGroup, 2, Vector2.zero, best)) { focusTarget = best; focusAt = best.pos; }
        }

        /// <summary>A focus target that has run out of sight, or led the chase well away from
        /// where it was picked, is let go: an Attack order follows its target anywhere and looks
        /// at nothing else on the way, so one Skimmer could draw the army off. Those still on
        /// it go back to where the army was headed.</summary>
        void LetGoOfRunaway()
        {
            if (!Unit.Live(focusTarget)) return;
            if (w.Visible(team, focusTarget.pos) && (focusTarget.pos - focusAt).magnitude < 35f) return;
            focusGroup.Clear();
            foreach (var h in main)
                if (Unit.Live(h) && h.order == Order.Attack && h.target == focusTarget) focusGroup.Add(h);
            if (focusGroup.Count == 0 || Issue(focusGroup, 1, rally, null))
            {
                // ...and not picked again straight away, or the chase starts over.
                letGo = focusTarget;
                letGoUntil = w.time + 8f;
                focusTarget = null;
            }
        }

        Unit letGo;
        float letGoUntil;

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
                        if (!w.Reaches(u, spot)) continue;                      // a plateau, a ringed clearing
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
