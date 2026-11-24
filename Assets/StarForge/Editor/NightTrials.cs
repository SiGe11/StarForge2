// NightTrials.cs -- checks for the lore features (Art/Source/staged/lore_features/DESIGN.md):
// the verdict by order (edit mode), the night gate (edit mode), the Quenching's truce (play), the
// night's look (play), the Taking of the Light and the deserters gone quiet (play; night round 2,
// Art/Source/staged/lore_night2). Call through Tools/editor.sh: `call StarForge.EditorTools.NightTrials.<Name>`;
// the play trials report through `<Name>Report` ("running ..." until done, for `editor.sh wait`).
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class NightTrials
    {
        static readonly string[] Orders = { "the Ashen Covenant", "the Order of the Iron Lantern", "the Ninth Vigil", "the Sable Oath",
                                            "the Order of the Broken Crown", "the Emberguard", "the Wardens of the Long Night", "the Brazen Host" };

        static float OldWill(float temper, float pride, float mood)
        {
            float will = 0.12f + 0.4f * Mathf.Max(0f, -temper) - 0.1f * Mathf.Max(0f, temper) + 0.15f * pride + 0.35f * Mathf.Clamp01(-mood * 2f);
            return Mathf.Clamp(will, 0.03f, 0.9f);
        }

        /// <summary>Edit mode. The punish rate per order over 4,000 knights each (temper U(-1,1), pride
        /// U(0,1), mood U(-0.55,0.25) + 0.25 temper, one roll each on its own stream), against the targets
        /// in DESIGN.md; the extremes (a tendency, not a rule); and the old will kept exactly where no
        /// order leans it.</summary>
        public static string PunishOrders()
        {
            var sb = new StringBuilder("PunishOrders (MechBrain.PunishWill):\n");
            int fails = 0;
            var rates = new Dictionary<string, float>();
            foreach (string o in Orders)
            {
                var knights = new Rng(0xD0C5u);   // the same knights for every order
                var roll = new Rng(0x5011u);
                int go = 0;
                double will = 0;
                const int N = 4000;
                for (int i = 0; i < N; i++)
                {
                    float te = knights.Range(-1f, 1f), pr = knights.F01(), mood = knights.Range(-0.55f, 0.25f) + 0.25f * te;
                    float wl = MechBrain.PunishWill(o, te, pr, mood);
                    will += wl;
                    if (roll.F01() < wl) go++;
                }
                float rate = go / (float)N;
                rates[o] = rate;
                bool ok = o == "the Sable Oath" ? rate >= 0.85f : o == "the Emberguard" ? rate <= 0.18f : rate >= 0.28f && rate <= 0.58f;
                if (!ok) fails++;
                sb.AppendLine($"  {o}: punishes {rate:P1} (mean will {will / N:0.000}) {(ok ? "ok" : "MISSES TARGET")}");
            }
            float emberHard = MechBrain.PunishWill("the Emberguard", -1f, 1f, -0.8f);
            float sableSoft = MechBrain.PunishWill("the Sable Oath", 1f, 0f, 0.5f);
            bool tendency = emberHard >= 0.5f && sableSoft < 0.98f;
            if (!tendency) fails++;
            sb.AppendLine($"  coldest, proudest, angriest Emberguard knight {emberHard:0.00} (want >= 0.5); warmest, humblest, content Sable {sableSoft:0.00} (want < 0.98) {(tendency ? "ok" : "MISSES TARGET")}");
            // No design, and an order with no lean: the old will to the last bit.
            var r = new Rng(0x01Du);
            int diff = 0;
            for (int i = 0; i < 10000; i++)
            {
                float te = r.Range(-1f, 1f), pr = r.F01(), mood = r.Range(-1.5f, 1.5f);
                if (MechBrain.PunishWill(null, te, pr, mood) != OldWill(te, pr, mood)) diff++;
                if (MechBrain.PunishWill("the Brazen Host", te, pr, mood) != OldWill(te, pr, mood)) diff++;
            }
            if (diff > 0) fails++;
            sb.AppendLine($"  no design and the Brazen Host against the old will: {diff} of 20,000 differ (want 0)");
            sb.Insert(0, fails == 0 ? "PASS " : $"FAIL ({fails}) ");
            return sb.ToString();
        }

        /// <summary>Edit mode. The gate and the streams: night and Quenching shares over 200,000 map
        /// seeds, every measurement and trial seed day by its hash, day whatever the hash when the match
        /// is not a real one, and Force honoured.</summary>
        public static string NightGate()
        {
            var sb = new StringBuilder();
            int fails = 0, nights = 0, quench = 0, liturgyN = 0;
            const int N = 200000;
            for (uint s = 1; s <= N; s++)
            {
                var p = NightRules.ForMatch(s, true);
                if (p.night) nights++;
                if (p.quenching) quench++;
                if (p.liturgy) liturgyN++;
                if (p.quenching && (p.darkeningAt < 330f || p.darkeningAt > 480f)) fails++;
            }
            float nShare = nights / (float)N, qShare = quench / (float)N;
            if (nShare < 0.31f || nShare > 0.36f || qShare < 0.035f || qShare > 0.045f) fails++;
            sb.AppendLine($"  night {nShare:P2} of maps (want ~33%), the Quenching {qShare:P2} (want ~4%), {quench / (float)Mathf.Max(1, nights):P1} of nights; " +
                          $"the Taking of the Light {liturgyN / (float)Mathf.Max(1, nights):P1} of nights (want ~10%: 1 in 9 of those with no Quenching)");
            uint[] fixedSeeds = { 1000, 1037, 1074, 1111, 1148, 1185, 1222, 1259, 2090223571, 3469857940, 1370439406, 2694109903 };
            int nightFixed = 0;
            foreach (uint s in fixedSeeds) if (NightRules.ForMatch(s, true).night) nightFixed++;
            if (nightFixed > 0) fails++;
            sb.AppendLine($"  measurement and trial seeds night by their hash: {nightFixed} of {fixedSeeds.Length} (want 0)");
            int notReal = 0;
            for (uint s = 1; s <= 3000; s++) if (NightRules.ForMatch(s, false).night) notReal++;
            if (notReal > 0) fails++;
            sb.AppendLine($"  not a real match: {notReal} of 3,000 night (want 0)");
            bool fixedGate = !NightRules.RealMatch(true, true) && !NightRules.RealMatch(false, false);
            if (!fixedGate) fails++;
            sb.AppendLine($"  RealMatch with a fixed seed or not from the menu: {(fixedGate ? "false" : "TRUE")} (want false); from the menu now: {NightRules.RealMatch(true, false)} (an agent's play reads false)");
            int f = NightRules.Force; float fa = NightRules.ForceDarkeningAt;
            NightRules.Force = 2; NightRules.ForceDarkeningAt = 77f;
            var forced = NightRules.ForMatch(1000, false);
            NightRules.Force = f; NightRules.ForceDarkeningAt = fa;
            bool forceOk = forced.night && forced.quenching && forced.darkeningAt == 77f;
            if (!forceOk) fails++;
            sb.AppendLine($"  Force 2 at 77 s on seed 1000: night {forced.night}, Quenching {forced.quenching} at {forced.darkeningAt:0} (want true, true, 77)");
            var q = new NightPlan { night = true, quenching = true, darkeningAt = 400f };
            sb.AppendLine($"  a Quenching at 400 s: level at 0/90/400/580/610/640 s {q.Level(0):0.00}/{q.Level(90):0.00}/{q.Level(400):0.00}/{q.Level(580):0.00}/{q.Level(610):0.00}/{q.Level(640):0.00}, truce at 399/400/639/640 {q.TruceAt(399)}/{q.TruceAt(400)}/{q.TruceAt(639)}/{q.TruceAt(640)}");
            return (fails == 0 ? "PASS" : $"FAIL ({fails})") + " NightGate:\n" + sb;
        }

        // ------------------------------------------------------------ play mode
        internal static string report = "no trial running";
        public static string TruceWho() => string.Join("; ", NightTrialRun.truceWho);
        public static string TruceReport() => report;
        public static string LookReport() => report;

        /// <summary>Play mode, a match running (or one is started, spectated). Two rifle squads 28 m
        /// apart near the middle, a Sentinel of team 1 by team 0's, a Kestrel of team 0 over them; once
        /// they are firing the truce is set from 1 s ahead for 40 s. Counted: shots (Fire, Beam events)
        /// and hull lost from 2 s into the truce (rounds in flight land) to lauds, and the same in the
        /// 15 s after; soldiers of the two squads idle with nothing to shoot 15 s after lauds while the
        /// other squad stands; the AI's army's mean distance from its own base at the start and the end
        /// of the truce. Targets: 0 shots and 0 hull in the truce, shots again after it, 0 stuck.</summary>
        public static string Truce()
        {
            if (!Application.isPlaying) return "error: play mode only";
            var go = new GameObject("NightTrialRun");
            go.AddComponent<NightTrialRun>().StartCoroutine(go.GetComponent<NightTrialRun>().TruceRun());
            report = "running: staging";
            return "started";
        }

        /// <summary>Play mode, from the menu (a played match: the player's side has no Commander, theirs plays).
        /// A 40 s truce from 1 s ahead; 4 s in, the player's army (eight riflemen, two Maulers) stands 14-20 m
        /// from their Foundry, sent at it, beside their Sentinel and four riflemen of theirs. Counted: shots and
        /// hits from 4 s into the truce to lauds, hull lost meanwhile; after lauds the first shot of each side,
        /// shots and hull each side lost in 20 s; errors and exceptions logged. Targets: 0 shots, 0 hits, 0 hull
        /// in the truce; both sides firing within 5 s of lauds and hull lost by both; 0 exceptions.</summary>
        public static string TruceInvasion()
        {
            if (!Application.isPlaying) return "error: play mode only";
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.State == MatchState.Playing) return "error: start from the menu (a played match)";
            var go = new GameObject("NightTrialRun");
            go.AddComponent<NightTrialRun>().StartCoroutine(go.GetComponent<NightTrialRun>().InvasionRun());
            report = "running: staging";
            return "started";
        }

        /// <summary>Play mode. The camera on team 0's base close and at the overview, by day and then
        /// at full night (the plan set on the running world), mean luma of each frame and of the
        /// remembered ground; Temp/night_*.png. Target in DESIGN.md (night 0.30-0.45 of day).</summary>
        public static string Look()
        {
            if (!Application.isPlaying) return "error: play mode only";
            var go = new GameObject("NightTrialRun");
            go.AddComponent<NightTrialRun>().StartCoroutine(go.GetComponent<NightTrialRun>().LookRun());
            report = "running: staging";
            return "started";
        }

        /// <summary>For trials: make the counsel come at the next call it can answer (MechSpeech).</summary>
        public static string ForceCounsel()
        {
            if (!Application.isPlaying) return "error: play mode only (the switch is cleared at every play entry and when play stops)";
            MechSpeech.ForceCounsel = true;
            return "counsel forced for this play: the player's Mech answers the next Attack or Regroup call it can (cleared when play stops)";
        }

        /// <summary>Play mode. AI against AI (spectated), a Quenching forced once both Mechs have landed: the
        /// darkening 30 s on, a desertion and a forced verdict on team 0 about 20 s before it. Counted: waves
        /// called off at the darkening (+1 if one was out), the share of each army within 22 m of its truce post
        /// by lauds, Fire and Beam events from D+4 s to lauds, the first wave after lauds, and whether team 0's
        /// Mech still pursues after lauds (or has put them down) rather than timing out. Targets in the report.</summary>
        public static string QuenchingAI()
        {
            if (!Application.isPlaying) return "error: play mode only";
            var go = new GameObject("NightTrialRun");
            go.AddComponent<NightTrialRun>().StartCoroutine(go.GetComponent<NightTrialRun>().QuenchingAIRun());
            report = "running: staging";
            return "started";
        }
        public static string QuenchingAIReport() => report;

        /// <summary>Play mode, ~40 s (3x). A night with the Taking of the Light 12 s on: the lamps other than
        /// the hall-lamps (Foundries') before, in the dark middle and after, and the hall-lamps in the dark.
        /// Targets: before > 0, the middle under a tenth of before, after at least 90% of before, hall-lamps
        /// lit in the dark.</summary>
        public static string LookLiturgy()
        {
            if (!Application.isPlaying) return "error: play mode only";
            var go = new GameObject("NightTrialRun");
            go.AddComponent<NightTrialRun>().StartCoroutine(go.GetComponent<NightTrialRun>().LiturgyRun());
            report = "running: staging";
            return "started";
        }

        /// <summary>Play mode, ~10 s. A night, six of ours by the camera, three made to desert: the deserters
        /// show no mast lights and the field net says it lost contact. Targets: deserters dark >= those who
        /// ran, loyal ones showing mast lights >= the rest, a LOST CONTACT line.</summary>
        public static string GoQuiet()
        {
            if (!Application.isPlaying) return "error: play mode only";
            var go = new GameObject("NightTrialRun");
            go.AddComponent<NightTrialRun>().StartCoroutine(go.GetComponent<NightTrialRun>().GoQuietRun());
            report = "running: staging";
            return "started";
        }
        public static string LookLiturgyReport() => report;
        public static string GoQuietReport() => report;
    }

    /// <summary>The play trials' runner (not named after its file: an Editor-folder MonoBehaviour).</summary>
    public sealed class NightTrialRun : MonoBehaviour
    {
        GameWorld w;
        int shots, hits;
        readonly HashSet<int> fired = new HashSet<int>();
        public static readonly System.Collections.Generic.List<string> truceWho = new System.Collections.Generic.List<string>();
        float truceCountFrom = float.MaxValue;
        int truceFire, truceHit;
        void OnShot(GameEvent e)
        {
            if ((e.kind == GameEventKind.Fire || e.kind == GameEventKind.Beam) && e.team < 2) { shots++; if (e.unit != null) fired.Add(e.unit.id); }
            if ((e.kind == GameEventKind.Fire || e.kind == GameEventKind.Beam) && e.team < 2 && w != null && w.Truce && truceWho.Count < 12) truceWho.Add($"{e.kind}:{e.type}/{(e.unit != null ? e.unit.Type.ToString() : "-")} pk{e.projectileKind} t{w.time:0.0}");
            if (e.kind == GameEventKind.UnderAttack && e.team < 2) hits++;
            // Counted by the world's own flag: a shot in the frame that crosses lauds is not a truce shot.
            if (w != null && w.Truce && w.time >= truceCountFrom)
            {
                if (e.kind == GameEventKind.Fire || e.kind == GameEventKind.Beam) { if (e.team < 2) truceFire++; }
                else if (e.kind == GameEventKind.UnderAttack && e.team < 2) truceHit++;
            }   // a hit with an attacker (a fire's burn has none)
        }
        bool wasSuspended, setSuspended, setNight;
        float wasScale = -1f;
        void OnDisable()
        {
            // Cut off mid-run (play stopped, the object destroyed): nothing the trial set outlives it.
            if (w != null) { w.Event -= OnShot; if (setNight) w.SetNight(default); }
            if (setSuspended) Commander.Suspended = wasSuspended;
            if (wasScale >= 0f) Time.timeScale = wasScale;
            FXDirector.NightExposureNudge = 0f;
        }

        /// <summary>BugTrial.Wait's convention: the world replaced or gone, or its clock still for 30 s of real time.</summary>
        float lastClock = -1f, stillSince;
        string Stalled()
        {
            var boot = GameBootstrap.Instance;
            if (w == null || boot == null || boot.world != w) return "the world was replaced or is gone";
            if (w.time != lastClock) { lastClock = w.time; stillSince = Time.realtimeSinceStartup; }
            else if (Time.realtimeSinceStartup - stillSince > 30f) return "the match clock stood still for 30 s";
            return null;
        }

        float HullLost(Dictionary<int, float> was) => HullLost(was, out _);

        /// <summary>Hull lost since the last call, and the part of it on mobile units (a structure can burn
        /// from a fire the fight started: accepted, like the reckoning's bites, which no trial here stages).</summary>
        float HullLost(Dictionary<int, float> was, out float mobile)
        {
            float lost = 0f;
            mobile = 0f;
            foreach (var u in w.units)
            {
                if (u == null || u.team > 1 || u.def.neutral) continue;
                if (was.TryGetValue(u.id, out float h) && u.hp < h) { lost += h - u.hp; if (!u.def.building) mobile += h - u.hp; }
                was[u.id] = u.hp;
            }
            return lost;
        }

        IEnumerator EnsureMatch(bool spectate)
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.State != MatchState.Playing)
            {
                MatchSettings.spectate = spectate;
                boot.StartMatch();
            }
            yield return null;
            w = boot != null ? boot.world : FindAnyObjectByType<GameWorld>();
        }

        int errors;
        void OnLog(string msg, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) errors++; }

        public IEnumerator InvasionRun()
        {
            Application.logMessageReceived += OnLog;
            try { foreach (var x in Invasion()) yield return x; }
            finally { Application.logMessageReceived -= OnLog; }
        }

        IEnumerable Invasion()
        {
            yield return EnsureMatch(false);   // the player's side as played: no Commander of its own
            wasScale = Time.timeScale;
            if (w == null) { NightTrials.report = "FAILED: no world"; Destroy(gameObject); yield break; }
            Time.timeScale = 2f;
            Vector2 home = w.Map.StartPos(1), ours = w.Map.StartPos(0);
            Vector2 toUs = (ours - home).normalized, side = new Vector2(-toUs.y, toUs.x);
            Unit foundry = null;
            foreach (var u in w.units) if (u != null && u.team == 1 && u.Type == UnitType.Foundry) { foundry = u; break; }
            if (foundry == null) { NightTrials.report = "FAILED: no Foundry of theirs"; Destroy(gameObject); yield break; }
            var theirs = new List<Unit> { w.Spawn(UnitType.Sentinel, 1, w.NearestWalkable(foundry.pos + toUs * 11f + side * 8f, 6f)) };
            for (int i = 0; i < 4; i++) theirs.Add(w.Spawn(UnitType.Trooper, 1, w.NearestWalkable(foundry.pos + toUs * 9f - side * (4f + i * 1.6f), 4f)));
            var plan = new NightPlan { night = true, quenching = true, darkeningAt = w.time + 1f, span = 40f };
            float start = plan.darkeningAt, lauds = plan.LaudsAt;
            w.SetNight(plan);
            setNight = true;
            w.Event += OnShot;
            while (w.time < start + 4f) { string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) at the darkening"; Destroy(gameObject); yield break; } yield return null; }
            // The player's army walked in under the truce: it stands in their base, sent at their Foundry.
            var army = new List<Unit>();
            for (int i = 0; i < 8; i++) army.Add(w.Spawn(UnitType.Trooper, 0, w.NearestWalkable(foundry.pos + toUs * 16f + side * (i * 1.6f - 6f), 4f)));
            for (int i = 0; i < 2; i++) army.Add(w.Spawn(UnitType.Mauler, 0, w.NearestWalkable(foundry.pos + toUs * 20f + side * (i * 6f - 3f), 6f)));
            yield return null;
            w.CmdAttack(army, foundry);
            var hp = new Dictionary<int, float>();
            HullLost(hp);
            truceCountFrom = w.time; truceFire = 0; truceHit = 0;
            float truceHull = 0f, truceMobile = 0f;
            int errorsAtStaging = errors;
            while (w.time < lauds)
            {
                string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) in the truce"; Destroy(gameObject); yield break; }
                truceHull += HullLost(hp, out float m); truceMobile += m;
                NightTrials.report = $"running: truce, {truceFire} shots"; yield return null;
            }
            int truceShots = truceFire, truceHits = truceHit;
            truceCountFrom = float.MaxValue;
            float nearest = float.MaxValue;
            foreach (var u in army) if (Unit.Live(u)) nearest = Mathf.Min(nearest, (u.pos - foundry.pos).magnitude);
            float first0 = -1f, first1 = -1f;
            int shots0 = 0, shots1 = 0;
            void After(GameEvent e)
            {
                if ((e.kind != GameEventKind.Fire && e.kind != GameEventKind.Beam) || e.team > 1 || w.Truce) return;
                if (e.team == 0) { shots0++; if (first0 < 0f) first0 = w.time - lauds; }
                else { shots1++; if (first1 < 0f) first1 = w.time - lauds; }
            }
            w.Event += After;
            float lost0 = 0f, lost1 = 0f;
            var was = new Dictionary<int, float>();
            foreach (var u in w.units) if (u != null) was[u.id] = u.hp;
            // Drops only, frame by frame: a site going up gains hull, and the dead leave the list.
            while (w.time < lauds + 20f)
            {
                string st = Stalled(); if (st != null) { w.Event -= After; NightTrials.report = $"INCOMPLETE ({st}) after lauds"; Destroy(gameObject); yield break; }
                foreach (var u in w.units)
                {
                    if (u == null || u.team > 1) continue;
                    float now = Mathf.Max(0f, u.hp);
                    if (was.TryGetValue(u.id, out float h) && now < h) { if (u.team == 0) lost0 += h - now; else lost1 += h - now; }
                    was[u.id] = now;
                }
                NightTrials.report = $"running: after lauds, shots {shots0}/{shots1}"; yield return null;
            }
            w.Event -= After;
            int deadOurs = 0, deadTheirs = 0;
            foreach (var u in army) if (!Unit.Live(u)) deadOurs++;
            foreach (var u in theirs) if (!Unit.Live(u)) deadTheirs++;
            w.Event -= OnShot;
            Time.timeScale = wasScale; wasScale = -1f;
            w.SetNight(default); setNight = false;
            int errs = errors - errorsAtStaging;
            bool pass = truceShots == 0 && truceHits == 0 && truceMobile <= 0.01f && first0 >= 0f && first0 <= 5f && first1 >= 0f && first1 <= 5f &&
                        lost0 > 0f && lost1 > 0f && errs == 0;
            NightTrials.report = $"{(pass ? "PASS" : "FAIL")} TruceInvasion: our army of {army.Count} in their base (nearest {nearest:0.0} m from their Foundry at lauds); " +
                                 $"in the truce {truceShots} shots, {truceHits} hits (want 0, 0), hull lost {truceHull:0.0}, mobile {truceMobile:0.0} (want 0); " +
                                 $"after lauds first shot ours {first0:0.0} s, theirs {first1:0.0} s (want both 0-5 s), shots {shots0}/{shots1} in 20 s, " +
                                 $"hull lost ours {lost0:0}, theirs {lost1:0} (want both > 0), dead ours {deadOurs}/{army.Count}, theirs staged {deadTheirs}/{theirs.Count}; " +
                                 $"errors and exceptions {errs} (want 0)";
            Destroy(gameObject);
        }

        public IEnumerator TruceRun()
        {
            yield return EnsureMatch(true);
            // The staged squads are the trial's: both Commanders held (they would walk them home for the
            // truce: Commander.Truce is checked by reading and in a played Quenching).
            wasSuspended = Commander.Suspended;
            Commander.Suspended = setSuspended = true;
            wasScale = Time.timeScale;
            if (w == null) { NightTrials.report = "FAILED: no world"; Destroy(gameObject); yield break; }
            Time.timeScale = 2f;
            Vector2 mid = new Vector2(w.MapSize * 0.5f, w.MapSize * 0.5f);
            Vector2 a = w.NearestWalkable(mid + new Vector2(-14f, 0f), 12f), b = w.NearestWalkable(mid + new Vector2(14f, 0f), 12f);
            var sqA = new List<Unit>(); var sqB = new List<Unit>();
            for (int i = 0; i < 8; i++)
            {
                sqA.Add(w.Spawn(UnitType.Trooper, 0, w.NearestWalkable(a + new Vector2(0f, i * 1.6f - 6f), 4f)));
                sqB.Add(w.Spawn(UnitType.Trooper, 1, w.NearestWalkable(b + new Vector2(0f, i * 1.6f - 6f), 4f)));
            }
            w.Spawn(UnitType.Sentinel, 1, w.NearestWalkable(a + new Vector2(-12f, 8f), 6f));
            w.Spawn(UnitType.Kestrel, 0, a + new Vector2(-6f, 0f));
            w.CmdMove(sqA, b, true);
            w.CmdMove(sqB, a, true);
            w.Event += OnShot;
            float t0 = w.time;
            while (shots < 10 && w.time - t0 < 40f)
            {
                string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) waiting for the fight"; Destroy(gameObject); yield break; }
                NightTrials.report = $"running: waiting for the fight ({shots} shots)"; yield return null;
            }
            int before = shots;
            // A 40 s truce from 1 s ahead (the plan's own is 240 s).
            var plan = new NightPlan { night = true, quenching = true, darkeningAt = w.time + 1f, span = 40f };
            float start = plan.darkeningAt, laudsAt = plan.LaudsAt;
            w.SetNight(plan);
            setNight = true;
            // 4 s grace: a frag in its wind-up, flight and fuse, and rounds in the air, land.
            while (w.time < start + 4f) { string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) at the darkening"; Destroy(gameObject); yield break; } yield return null; }
            var hp = new Dictionary<int, float>();
            HullLost(hp);
            int truceShots0 = shots, truceHits0 = hits;
            truceCountFrom = start + 4f; truceFire = 0; truceHit = 0;
            float truceHull = 0f, truceHullMobile = 0f;
            bool wasTruce = true;
            while (w.time < laudsAt) { string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) in the truce"; Destroy(gameObject); yield break; } float lostNow = HullLost(hp, out float mobile); truceHull += lostNow; truceHullMobile += mobile; wasTruce &= w.Truce; NightTrials.report = $"running: truce, {shots - truceShots0} shots, {hits - truceHits0} hits"; yield return null; }
            int truceShots = truceFire, truceHits = truceHit;   // by the world's flag (the window's last frame crosses lauds)
            truceCountFrom = float.MaxValue;
            int after0 = shots;
            fired.Clear();
            float afterHull = 0f;
            while (w.time < laudsAt + 15f) { string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) after lauds"; Destroy(gameObject); yield break; } afterHull += HullLost(hp); NightTrials.report = "running: after lauds"; yield return null; }
            // Stuck: alive, an enemy of the other squad alive within his reach, and not one round of his own since lauds.
            int stuck = 0, aliveA = 0, aliveB = 0;
            foreach (var u in sqA) if (Unit.Live(u)) aliveA++;
            foreach (var u in sqB) if (Unit.Live(u)) aliveB++;
            // Each one named with what holds him (a man who cannot see or reach his foe is no truce fault).
            var stuckNames = new List<string>();
            int Stuck(List<Unit> mine, List<Unit> theirs)
            {
                int n = 0;
                foreach (var u in mine)
                {
                    if (!Unit.Live(u) || fired.Contains(u.id)) continue;
                    Unit near = null; float nd = float.MaxValue;
                    foreach (var e in theirs) if (Unit.Live(e)) { float d = (e.pos - u.pos).magnitude; if (d < nd) { nd = d; near = e; } }
                    if (near == null || nd > u.def.range) continue;
                    n++;
                    stuckNames.Add($"#{u.id} {u.Type} team {u.team}: {u.mode}, order {u.order}, nearest foe #{near.id} {nd:0.0} m " +
                                   $"{(w.Sees(u.team, near) ? "seen" : "UNSEEN")} ({near.mode}), target {(u.target != null ? "#" + u.target.id : "none")}" +
                                   $"{(u.fireBlocked ? ", no line" : "")}{(u.hiding ? ", hiding" : "")}{(u.Reloading ? ", reloading" : "")}, suppression {u.suppression:0.00}, rounds {u.rounds}");
                }
                return n;
            }
            stuck = Stuck(sqA, sqB) + Stuck(sqB, sqA);
            w.Event -= OnShot;
            Commander.Suspended = wasSuspended; setSuspended = false;
            Time.timeScale = wasScale; wasScale = -1f;
            w.SetNight(default); setNight = false;
            bool pass = truceShots == 0 && truceHits == 0 && truceHullMobile <= 0.01f && shots - after0 > 0 && stuck == 0 && wasTruce;
            NightTrials.report = $"{(pass ? "PASS" : "FAIL")} Truce: before {before} shots; in the truce (from 4 s in to lauds) {truceShots} shots and {truceHits} hits with an attacker " +
                                 $"(want 0, 0), hull lost by mobile units {truceHullMobile:0.0} (want 0; structures, which a fire can still burn, {truceHull - truceHullMobile:0.0}), Truce read true throughout {wasTruce}; after lauds {shots - after0} shots, {afterHull:0} hull in 15 s (want > 0); " +
                                 $"squads alive {aliveA}/{aliveB}, with an enemy in reach and silent since lauds {stuck} (want 0){(stuckNames.Count > 0 ? ": " + string.Join("; ", stuckNames) : "")}";
            Destroy(gameObject);
        }

        public IEnumerator QuenchingAIRun()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.State != MatchState.Playing) { MatchSettings.spectate = true; boot.StartMatch(); }
            // The new match's world comes up a few frames later; the old one may still hold a winner.
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 20f; )
            {
                boot = GameBootstrap.Instance;
                if (boot != null && boot.State == MatchState.Playing && boot.world != null && boot.world.winner < 0) break;
                yield return null;
            }
            w = boot != null ? boot.world : null;
            if (w == null || w.winner >= 0 || boot.PlayerProxy == null) { NightTrials.report = "FAILED: needs a spectated match (both sides the Commander)"; Destroy(gameObject); yield break; }
            wasScale = Time.timeScale;
            Time.timeScale = 8f;
            string Wait(string what) { string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) {what}"; } return st; }
            while (!(w.MechOf(0) != null && w.MechOf(0).mech.Landed && w.MechOf(1) != null && w.MechOf(1).mech.Landed))
            {
                if (Wait("waiting for both Mechs") != null) { Destroy(gameObject); yield break; }
                NightTrials.report = $"running: waiting for both Mechs to land ({w.time:0} s)";
                if (w.winner >= 0) { NightTrials.report = "INCOMPLETE (the match ended before both Mechs landed)"; Destroy(gameObject); yield break; }
                yield return null;
            }
            float D = w.time + 30f;
            var plan = new NightPlan { night = true, quenching = true, darkeningAt = D };
            w.SetNight(plan);
            setNight = true;
            float lauds = plan.LaudsAt;
            // A desertion on team 0 and its Mech told to go, ~20 s before the darkening.
            while (w.time < D - 20f) { if (Wait("before the desertion") != null) { Destroy(gameObject); yield break; } yield return null; }
            var group = new List<Unit>();
            foreach (var u in w.units) if (u != null && u.team == 0 && !u.def.building && u.def.bodyRadius > 0f && !u.deserted && group.Count < 6) group.Add(u);
            int ran = group.Count > 0 ? w.Desert(0, group[0].pos, group, Mathf.Min(3, group.Count)) : 0;
            var brain0 = boot.MechBrains[0];
            brain0?.PunishNow();
            // A forward fortress: a Sentinel of team 0's 47 m from team 1's base, toward the middle. In the truce
            // team 1's army must keep its post rather than stand round it.
            Vector2 home1 = w.Map.StartPos(1), toward = (w.Map.StartPos(0) - home1).normalized;
            Unit fort = null;   // raised at the darkening: raised 20 s before it, team 1 destroyed it first
            int calledOff0 = boot.AI.Dbg.wavesCalledOff + boot.PlayerProxy.Dbg.wavesCalledOff;
            bool waveOutAtD = false;
            while (w.time < D) { if (Wait("before the darkening") != null) { Destroy(gameObject); yield break; } waveOutAtD = boot.AI.WaveOut || boot.PlayerProxy.WaveOut; yield return null; }
            fort = w.Spawn(UnitType.Sentinel, 0, w.NearestWalkable(home1 + toward * 47f, 6f));
            while (w.time < D + 4f) { if (Wait("the darkening") != null) { Destroy(gameObject); yield break; } yield return null; }
            int calledOff = boot.AI.Dbg.wavesCalledOff + boot.PlayerProxy.Dbg.wavesCalledOff - calledOff0;
            bool pursuingAtD = brain0 != null && brain0.Pursuing;
            // Counted by the world's own flag from 4 s in (a shot in the frame that crosses lauds is no truce shot).
            truceCountFrom = D + 4f; truceFire = 0; truceHit = 0;
            w.Event += OnShot;
            // While the truce holds: team 1's post kept off the fortress; then, 30 s in, two sites of team 0's
            // with a Digger at each: one 25 m from team 1's home on the far side from the fort (72 m from it):
            // the army walks to it (no shot); one 30 m out within 40 m of the fort, under its cover: the post
            // keeps more than 15 m from it.
            bool fortKnown = false;
            float postNearFort = float.MaxValue, postNearCovered = float.MaxValue, siteAt = D + 30f, reachedAfter = -1f;
            Unit site = null, covered = null;
            while (w.time < lauds)
            {
                if (Wait("the truce") != null) { Destroy(gameObject); yield break; }
                if (Unit.Live(fort))
                {
                    postNearFort = Mathf.Min(postNearFort, (boot.AI.TrucePost - fort.pos).magnitude);
                    // Did team 1 know it (remembered and past its reaction delay)? Otherwise the fort test says nothing.
                    foreach (var r in boot.AI.Perception.Enemies)
                        if (r.type == UnitType.Sentinel && (r.pos - fort.pos).magnitude < 2f && boot.AI.Perception.Reactable(r, 0.9f)) { fortKnown = true; break; }
                }
                if (Unit.Live(covered)) postNearCovered = Mathf.Min(postNearCovered, (boot.AI.TrucePost - covered.pos).magnitude);
                if (site == null && w.time >= siteAt)
                {
                    Vector2 at = w.NearestWalkable(home1 - toward * 25f, 6f);
                    site = w.Spawn(UnitType.Sentinel, 0, at, false);
                    w.Spawn(UnitType.Worker, 0, w.NearestWalkable(at + new Vector2(2.5f, 0f), 4f));
                    Vector2 side = new Vector2(-toward.y, toward.x);
                    Vector2 cAt = w.NearestWalkable(home1 + (toward * 0.94f + side * 0.34f) * 30f, 6f);   // 30 m out, ~19 m from the fort
                    covered = w.Spawn(UnitType.Sentinel, 0, cAt, false);
                    w.Spawn(UnitType.Worker, 0, w.NearestWalkable(cAt + new Vector2(2.5f, 0f), 4f));
                }
                if (site != null && reachedAfter < 0f && Unit.Live(site))
                {
                    var keep = boot.AI.TrucePost;
                    if ((keep - site.pos).magnitude < 8f && boot.AI.TruceArmyAt(22f) >= 0.5f) reachedAfter = w.time - siteAt;
                }
                NightTrials.report = $"running: truce, {truceFire} truce shots"; yield return null;
            }
            w.Event -= OnShot;
            int truceShots = truceFire;
            truceCountFrom = float.MaxValue;
            float atPost1 = boot.AI.TruceArmyAt(22f), atPost0 = boot.PlayerProxy.TruceArmyAt(22f);
            bool pursuingAtLauds = brain0 != null && brain0.Punishing;
            float firstWave = -1f;
            bool stillPursuing = false;
            while (w.time < lauds + 240f && w.winner < 0)
            {
                if (Wait("after lauds") != null) { Destroy(gameObject); yield break; }
                if (firstWave < 0f && (boot.AI.WaveOut || boot.PlayerProxy.WaveOut)) firstWave = w.time - lauds;
                if (w.time > lauds + 10f && w.time < lauds + 11f) stillPursuing = brain0 != null && (brain0.Punishing || w.MoraleOf(0).deserters.Count == 0);
                if (firstWave >= 0f && w.time > lauds + 12f) break;
                NightTrials.report = $"running: after lauds ({w.time - lauds:0} s)";
                yield return null;
            }
            Time.timeScale = wasScale; wasScale = -1f;
            w.SetNight(default); setNight = false;
            // The army's own post was the site's for its last minutes: the share at the post is read on team 0's side.
            string pursuit = !pursuingAtD ? "untested (team 0's Mech was not pursuing at the darkening)" : stillPursuing ? "going on or done" : "TIMED OUT";
            float siteFromFort = site != null && fort != null ? (site.pos - fort.pos).magnitude : -1f;
            float coveredFromFort = covered != null && fort != null ? (covered.pos - fort.pos).magnitude : -1f;
            float fortReach = Mathf.Max(32f, Defs.Get(UnitType.Sentinel).range + 3f);
            string fortTest = !fortKnown ? "untested (team 1 never knew the fort)" : postNearFort >= fortReach ? "held" : "FAILED";
            bool pass = (!waveOutAtD || calledOff >= 1) && truceShots == 0 && atPost0 >= 0.8f && (!fortKnown || postNearFort >= fortReach)
                        && reachedAfter >= 0f && postNearCovered >= 8f && (!pursuingAtD || stillPursuing);
            NightTrials.report = $"{(pass ? "PASS" : "FAIL")} QuenchingAI: darkening at {D:0} s, lauds at {lauds:0} s; a wave out at the darkening {waveOutAtD}, waves called off {calledOff} (want >= 1 if one was out); " +
                                 $"Fire/Beam events from D+4 s to lauds {truceShots} (want 0); team 0's army within 22 m of its post at lauds {atPost0:P0} (want >= 80%), team 1's {atPost1:P0} (it walked to the site); " +
                                 $"team 1's post nearest the forward Sentinel {(postNearFort < float.MaxValue ? $"{postNearFort:0} m" : "n/a")} (want >= its reach + 3 m, {Mathf.Max(32f, Defs.Get(UnitType.Sentinel).range + 3f):0} m: the post holds), team 1 knew the fort {fortKnown}: {fortTest}; " +
                                 $"the far site 25 m from its home ({siteFromFort:0} m from the fort, want >= 45) reached {(reachedAfter >= 0f ? $"{reachedAfter:0.0} s" : "never")} after it went up (want after its reaction delay, within a minute); " +
                                 $"the covered site ({coveredFromFort:0} m from the fort, want < 40): post nearest it {(postNearCovered < float.MaxValue ? $"{postNearCovered:0} m" : "n/a")} (want >= 8 m); " +
                                 $"first wave after lauds {(firstWave >= 0f ? $"{firstWave:0} s" : "none in 240 s (soft: read it against the threshold)")}; deserters {ran}, pursuit {pursuit}, pursuing at lauds {pursuingAtLauds}; verdict: {brain0?.LastVerdict}";
            Destroy(gameObject);
        }

        public IEnumerator LiturgyRun()
        {
            yield return EnsureMatch(false);
            var fx = FXDirector.Current;
            if (w == null || fx == null) { NightTrials.report = "FAILED: no world or FXDirector"; Destroy(gameObject); yield break; }
            wasScale = Time.timeScale;
            Time.timeScale = 3f;
            // Lamps stand lit within 3 s of a night set after 79 s of match.
            while (w.time < 100f)
            {
                string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) waiting for 100 s of match"; Destroy(gameObject); yield break; }
                NightTrials.report = $"running: waiting for 100 s of match ({w.time:0})"; yield return null;
            }
            // A fresh match holds only the Foundry, whose hall-lamp stays lit by design: raise a few
            // finished structures of ours so there are other lamps to go out and come back.
            {
                var home = w.Map.StartPos(0);
                UnitType[] kinds = { UnitType.Bunkhouse, UnitType.Bunkhouse, UnitType.Garrison };
                for (int i = 0; i < kinds.Length; i++)
                {
                    float a = i * 2.1f + 0.4f;
                    var at = w.NearestWalkable(home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 18f, 8f);
                    if (w.CanPlace(kinds[i], at)) w.Spawn(kinds[i], 0, at);
                }
                float t1 = w.time;
                while (w.time < t1 + 4f) yield return null;   // their lamps light within 3 s
            }
            float L = w.time + 12f;
            w.SetNight(new NightPlan { night = true, liturgy = true, liturgyAt = L });
            setNight = true;
            var rig = FindAnyObjectByType<RTSCamera>();
            if (rig != null) { rig.CenterOn(w.Map.StartPos(0)); rig.distance = 70f; }
            int before = 0, middle = int.MaxValue, after = 0, hallDark = int.MaxValue;
            bool shotBefore = false, shotDark = false, shotRelight = false;
            while (w.time < L + 92f)
            {
                string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) in the liturgy"; Destroy(gameObject); yield break; }
                // Pictures for the eye: before, the dark middle, half relit (Temp/night_liturgy_*.png).
                string shot = !shotBefore && w.time >= L - 3f ? "before" : !shotDark && w.time >= L + 32f ? "dark" : !shotRelight && w.time >= L + 58f ? "relight" : null;
                if (shot != null)
                {
                    if (shot == "before") shotBefore = true; else if (shot == "dark") shotDark = true; else shotRelight = true;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    System.IO.File.WriteAllBytes($"Temp/night_liturgy_{shot}.png", tex.EncodeToPNG());
                    Destroy(tex);
                }
                int others = fx.StructureLamps - fx.HallLamps;
                if (w.time < L - 1f) before = Mathf.Max(before, others);
                else if (w.time > L + 26f && w.time < L + 38f) { middle = Mathf.Min(middle, others); hallDark = Mathf.Min(hallDark, fx.HallLamps); }
                else if (w.time > L + 86f) after = Mathf.Max(after, others);
                NightTrials.report = $"running: the Taking of the Light ({w.time - L:0} s), lamps {others}, hall-lamps {fx.HallLamps}"; yield return null;
            }
            Time.timeScale = wasScale; wasScale = -1f;
            w.SetNight(default); setNight = false;
            bool pass = before > 0 && middle <= before / 10 && after >= before * 9 / 10 && hallDark >= 1;
            NightTrials.report = $"{(pass ? "PASS" : "FAIL")} LookLiturgy: lamps other than the hall-lamps before {before}, in the dark {middle}, after {after} " +
                                 $"(want > 0, under a tenth, at least 90%); hall-lamps lit in the dark {hallDark} (want >= 1); Temp/night_liturgy_before|dark|relight.png";
            Destroy(gameObject);
        }

        public IEnumerator GoQuietRun()
        {
            yield return EnsureMatch(false);
            var fx = FXDirector.Current;
            var rig = FindAnyObjectByType<RTSCamera>();
            if (w == null || fx == null || rig == null) { NightTrials.report = "FAILED: no world, FXDirector or camera"; Destroy(gameObject); yield break; }
            w.SetNight(new NightPlan { night = true });   // the night's level is 0.55 or more from the first second
            setNight = true;
            Vector2 home = w.Map.StartPos(0), mid = (w.Map.StartPos(1) + home) * 0.5f;
            Vector2 at = w.NearestWalkable(home + (mid - home).normalized * 20f, 8f);
            rig.CenterOn(at);
            rig.distance = 22f;
            var squad = new List<Unit>();
            for (int i = 0; i < 6; i++) squad.Add(w.Spawn(UnitType.Trooper, 0, w.NearestWalkable(at + new Vector2(i * 1.5f - 4f, 0f), 4f)));
            for (float until = w.time + 3f; w.time < until; )
            {
                string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) staging"; Destroy(gameObject); yield break; }
                yield return null;
            }
            int lines0 = StarForge.UI.HUDController.LostContactLines;
            int ran = w.Desert(0, at, squad, 3);
            int dark = 0, lit = 0;
            bool shotMasts = false;
            float t0 = w.time;
            for (float until = w.time + 2f; w.time < until; )
            {
                string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) after the desertion"; Destroy(gameObject); yield break; }
                dark = Mathf.Max(dark, fx.DesertersDark); lit = Mathf.Max(lit, fx.MastsLit);
                if (!shotMasts && w.time >= t0 + 1f)
                {
                    // For the eye: the loyal three's mast lights, the deserters' none (Temp/night_masts.png).
                    shotMasts = true;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    System.IO.File.WriteAllBytes("Temp/night_masts.png", tex.EncodeToPNG());
                    Destroy(tex);
                }
                yield return null;
            }
            int lines = StarForge.UI.HUDController.LostContactLines - lines0;
            w.SetNight(default); setNight = false;
            bool pass = ran >= 1 && dark >= ran && lit >= 6 - ran && lines >= 1;
            NightTrials.report = $"{(pass ? "PASS" : "FAIL")} GoQuiet: {ran} deserted; deserters by the camera with dark masts {dark} (want >= {ran}), " +
                                 $"soldiers showing mast lights {lit} (want >= {6 - ran}), LOST CONTACT lines {lines} (want >= 1); Temp/night_masts.png (eye: the lights on the iron's back, not floating); the six are left in the match";
            Destroy(gameObject);
        }

        static float MeanLuma(Texture2D t)
        {
            var px = t.GetPixels32();
            double sum = 0;
            for (int i = 0; i < px.Length; i += 7) sum += 0.299 * px[i].r + 0.587 * px[i].g + 0.114 * px[i].b;
            return (float)(sum / (px.Length / 7.0) / 255.0);
        }

        public IEnumerator LookRun()
        {
            // Played, not spectated: the fog pass (and _SF_Night's lift of remembered ground) draws only then.
            yield return EnsureMatch(false);
            wasSuspended = Commander.Suspended;   // not set here: kept as found
            var rig = FindAnyObjectByType<RTSCamera>();
            if (w == null || rig == null) { NightTrials.report = "FAILED: no world or camera"; Destroy(gameObject); yield break; }
            while (w.time < 100f)
            {
                string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) waiting for 100 s of match"; Destroy(gameObject); yield break; }
                NightTrials.report = $"running: waiting for 100 s of match ({w.time:0})"; yield return null;
            }
            var sb = new StringBuilder();
            Vector2 home = w.Map.StartPos(0);
            float[] luma = new float[4];
            float probeDay = 0f, probeNight = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    w.SetNight(new NightPlan { night = true });
                    setNight = true;
                    // The lamps light within 3 s of a night that comes on late: wait for them (8 s at most).
                    var fxw = FXDirector.Current;
                    for (float until = w.time + 8f; w.time < until && (fxw == null || fxw.StructureLamps == 0); )
                    {
                        string st = Stalled(); if (st != null) { NightTrials.report = $"INCOMPLETE ({st}) waiting for the lamps"; Destroy(gameObject); yield break; }
                        yield return null;
                    }
                }
                for (int frame = 0; frame < 30; frame++) yield return null;
                for (int shot = 0; shot < 2; shot++)
                {
                    rig.CenterOn(home);
                    rig.distance = shot == 0 ? 40f : 140f;
                    for (int frame = 0; frame < 40; frame++) yield return null;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    luma[pass * 2 + shot] = MeanLuma(tex);
                    if (shot == 0) { float p0 = RenderSettings.ambientProbe[0, 0]; if (pass == 0) probeDay = p0; else probeNight = p0; }
                    System.IO.File.WriteAllBytes($"Temp/night_{(pass == 0 ? "day" : "night")}_{(shot == 0 ? "base" : "overview")}.png", tex.EncodeToPNG());
                    Destroy(tex);
                }
            }
            // The exposure either side of the set value, so one run says what to set (A1).
            string sweep = "";
            foreach (float nudge in new[] { -0.30f, -0.15f, 0.15f })
            {
                FXDirector.NightExposureNudge = nudge;
                float lb = 0f, lo = 0f;
                for (int shot = 0; shot < 2; shot++)
                {
                    rig.CenterOn(home);
                    rig.distance = shot == 0 ? 40f : 140f;
                    for (int frame = 0; frame < 40; frame++) yield return null;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (shot == 0) lb = MeanLuma(tex); else lo = MeanLuma(tex);
                    Destroy(tex);
                }
                sweep += $"{(sweep.Length > 0 ? ", " : "")}{nudge:+0.00;-0.00} EV {lb / Mathf.Max(1e-4f, luma[0]):0.00} / {lo / Mathf.Max(1e-4f, luma[1]):0.00}";
            }
            FXDirector.NightExposureNudge = 0f;
            var fx = FXDirector.Current;
            float rBase = luma[2] / Mathf.Max(1e-4f, luma[0]), rOver = luma[3] / Mathf.Max(1e-4f, luma[1]);
            bool pass2 = rBase >= 0.30f && rBase <= 0.45f && rOver >= 0.30f && rOver <= 0.45f;
            sb.Append($"{(pass2 ? "PASS" : "LOOK")} NightLook: mean luma day base {luma[0]:0.000} overview {luma[1]:0.000}, night {luma[2]:0.000} / {luma[3]:0.000}; " +
                      $"night/day {rBase:0.00} / {rOver:0.00} (want 0.30-0.45; exposure sweep {sweep}); level {(fx != null ? fx.NightLevel : -1f):0.00}, " +
                      $"lamps set {(fx != null ? fx.LampsLit : -1)}, held by the system {(fx != null ? fx.LampParticles : -1)} (want equal) (structures {(fx != null ? fx.StructureLamps : -1)}, want > 0; soldiers showing mast lights {(fx != null ? fx.MastsLit : -1)}), " +
                      $"celebrants shown {(fx != null ? fx.CelebrantsShown : -1)}; ambient probe L0 red day {probeDay:0.000} night {probeNight:0.000} (want lower at night: else URP did not take the Trilight change); Temp/night_*.png (judge units, team plates and the fog by eye)");
            w.SetNight(default); setNight = false;
            NightTrials.report = sb.ToString();
            Destroy(gameObject);
        }
    }
}
