// SanctuaryTrials.cs -- checks for the kept Vesper twin house (World/GameWorld.Sanctuary). Play mode, from a fresh
// Play at the menu: `editor.sh call StarForge.EditorTools.SanctuaryTrials.Run`, then
// `editor.sh wait StarForge.EditorTools.SanctuaryTrials.Report`. It forces the house on (GameWorld.SanctuaryForce,
// cleared when the run ends and by AgentInbox on stop), starts a match with the opponent silenced and stages:
// our riflemen in a court, theirs and a Mauler before the gates; a rifleman of ours behind the house with only
// enemies across it in sight; and four units sent into a court (a Trooper and a Digger may go in, a Mauler and a
// Hauler may not). Targets: 0 hull lost inside, 0 shots from inside, 0 hull lost behind the house, the fight in
// front going on (shots and hits > 0), the foot units inside, the vehicles outside.
using System.Collections;
using System.Collections.Generic;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;
using UnityEngine;

namespace StarForge.EditorTools
{
    public static class SanctuaryTrials
    {
        internal static string report = "no trial running";
        public static string Report() => report;

        /// <summary>The 200-seed cover sweep (BugTrials.MapSweepCover) with two cloisters forced on every map; play
        /// mode at the menu. Then SweepCloistersReport. CloisterForce is cleared by AgentInbox when play stops.</summary>
        public static string SweepCloisters()
        {
            if (!Application.isPlaying) return "error: play mode only";
            GameWorld.CloisterForce = 2;
            MapGenerator.CloistersPlaced = 0;
            return BugTrials.MapSweepCover();
        }

        /// <summary>Play mode at the menu, ~4 min: 200 fresh seeds (MapSweepTrial's stream), each made as a match makes it:
        /// the house forced, two cloisters asked, ditches, cover, the NavMesh rebuilt, the house taken away first if a
        /// way is cut. Then SweepHouseReport. Targets: placed on 80%+; bases joined, ore reached, a soldier's way from
        /// each base into both courts on every seed with the house; 0 vehicle ways in; 0 cover or ditches on it;
        /// 0 grounds points over Rubble, Vault or Wall; cloisters placed on 50%+ of the asks.</summary>
        public static string SweepHouse()
        {
            if (!Application.isPlaying) return "error: play mode only";
            foreach (var old in Object.FindObjectsByType<SanctuarySweep>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
            new GameObject("SanctuarySweep").AddComponent<SanctuarySweep>();
            SanctuarySweep.Last = null;
            return "started";
        }

        public static string SweepHouseReport() => SanctuarySweep.Last ?? SanctuarySweep.Progress;

        /// <summary>Play mode, a match running with the house: the area under points from 14 m before gate 0 to 4 m
        /// inside, and the path from the first to the last for a soldier, with its corners.</summary>
        public static string Probe()
        {
            var w = GameBootstrap.Instance != null ? GameBootstrap.Instance.world : null;
            if (w == null || w.Sanct == null) return "no house in this match";
            var s = w.Sanct;
            var sb = new System.Text.StringBuilder();
            for (float z = -Sanctuary.HalfZ - 14f; z <= -Sanctuary.HalfZ + 4f; z += 1f)
            {
                var q = w.Map.Ground(s.ToWorld(Sanctuary.GateX[0], z));
                bool on = UnityEngine.AI.NavMesh.SamplePosition(q, out var h, 0.4f, UnityEngine.AI.NavMesh.AllAreas);
                int a = -1; if (on) { a = 0; for (int m = h.mask; m > 1; m >>= 1) a++; }
                sb.Append($"z{z:0}:{(on ? a.ToString() : "-")}({(on ? (h.position - q).magnitude : 0f):0.0}) ");
            }
            var from = w.Map.Ground(s.ToWorld(Sanctuary.GateX[0], -Sanctuary.HalfZ - 14f));
            var to = w.Map.Ground(s.ToWorld(Sanctuary.GateX[0], -3f));
            UnityEngine.AI.NavMesh.SamplePosition(from, out var hf, 2f, GameWorld.FootAreas);
            UnityEngine.AI.NavMesh.SamplePosition(to, out var ht, 2f, 1 << MapGenerator.SanctuaryArea);
            var path = new UnityEngine.AI.NavMeshPath();
            UnityEngine.AI.NavMesh.CalculatePath(hf.position, ht.position, GameWorld.FootAreas, path);
            sb.Append($"\npath {path.status}, corners {path.corners.Length}:");
            foreach (var c in path.corners) { var l = s.Local(new Vector2(c.x, c.z)); sb.Append($" ({l.x:0.0},{l.y:0.0})"); }
            sb.Append($"\nfoot mask {GameWorld.FootAreas}, ground {GameWorld.GroundAreas}, house volumes {s.piece.GetComponentsInChildren<Unity.AI.Navigation.NavMeshModifierVolume>().Length}");
            return sb.ToString();
        }

        public static string SweepCloistersReport() =>
            $"cloisters placed {MapGenerator.CloistersPlaced} (2 asked a map) | " + BugTrials.MapSweepReport();

        public static string Run() => Start(SanctuaryRun.Kind.Rules);
        /// <summary>Their last Digger hides in the grounds: turned out after 30 s, kept out, fought. ~1.5 min.</summary>
        public static string Hide() => Start(SanctuaryRun.Kind.Hide);
        /// <summary>AI against AI at 8x with the house for 300 s (spectated). ~1 min.</summary>
        public static string Match() => Start(SanctuaryRun.Kind.Match);
        /// <summary>Rounds in flight when their target steps into the grounds (~30 s).</summary>
        public static string Backstop() => Start(SanctuaryRun.Kind.Backstop);

        static string Start(SanctuaryRun.Kind kind)
        {
            if (!Application.isPlaying) return "error: play mode only (a fresh Play at the menu)";
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.State == MatchState.Playing) return "error: start from the menu (the house is placed when a match begins)";
            var go = new GameObject("SanctuaryTrialRun");
            var run = go.AddComponent<SanctuaryRun>();
            run.kind = kind;
            run.StartCoroutine(run.Go());
            report = "running: staging";
            return "started";
        }
    }

    public sealed class SanctuaryRun : MonoBehaviour
    {
        public enum Kind { Rules, Hide, Match, Backstop }
        public Kind kind;
        GameWorld w;
        bool wasSuspended, setSuspended;
        float wasScale = -1f;
        int shotsInside, shotsAll, errors;
        // Hull lost by unit id, frame by frame (a site going up gains hull; the dead leave the list).
        readonly Dictionary<int, float> lastHp = new Dictionary<int, float>(), lost = new Dictionary<int, float>();
        float lostInsideAll;
        readonly Dictionary<int, int> shotsBy = new Dictionary<int, int>();
        int ShotsBy(Unit u) => u != null && shotsBy.TryGetValue(u.id, out int n) ? n : 0;

        void OnShot(GameEvent e)
        {
            if ((e.kind != GameEventKind.Fire && e.kind != GameEventKind.Beam) || e.team > 1) return;
            shotsAll++;
            if (e.unit != null && e.unit.inGrounds) shotsInside++;
            if (e.unit != null) shotsBy[e.unit.id] = (shotsBy.TryGetValue(e.unit.id, out int n) ? n : 0) + 1;
        }
        void OnLog(string m, string st, LogType t) { if (t == LogType.Exception || t == LogType.Error) errors++; }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            if (w != null) w.Event -= OnShot;
            if (setSuspended) Commander.Suspended = wasSuspended;
            if (wasScale >= 0f) Time.timeScale = wasScale;
            GameWorld.SanctuaryForce = GameWorld.CloisterForce = -1;
        }

        void Track()
        {
            foreach (var u in w.units)
            {
                if (u == null || u.team > 1) continue;
                float now = Mathf.Max(0f, u.hp);
                if (lastHp.TryGetValue(u.id, out float h) && now < h)
                {
                    lost[u.id] = (lost.TryGetValue(u.id, out float l) ? l : 0f) + h - now;
                    if (u.inGrounds) lostInsideAll += h - now;
                }
                lastHp[u.id] = now;
            }
        }
        float Lost(IEnumerable<Unit> list) { float l = 0f; foreach (var u in list) if (u != null && lost.TryGetValue(u.id, out float x)) l += x; return l; }

        float lastClock = -1f, stillSince;
        string Stalled()
        {
            var boot = GameBootstrap.Instance;
            if (w == null || boot == null || boot.world != w) return "the world was replaced or is gone";
            if (w.time != lastClock) { lastClock = w.time; stillSince = Time.realtimeSinceStartup; }
            else if (Time.realtimeSinceStartup - stillSince > 30f) return "the match clock stood still for 30 s";
            return null;
        }

        IEnumerator Fail(string why) { SanctuaryTrials.report = why; Destroy(gameObject); yield break; }

        /// <summary>Runs the match clock on to <paramref name="until"/>, tracking hull; false if it stalled.</summary>
        IEnumerator Until(float until, string what, System.Action each = null)
        {
            while (w.time < until)
            {
                string st = Stalled();
                if (st != null) { stalled = $"INCOMPLETE ({st}) {what}"; yield break; }
                Track();
                each?.Invoke();
                SanctuaryTrials.report = $"running: {what}, {until - w.time:0} s left";
                yield return null;
            }
        }
        string stalled;

        public IEnumerator Go()
        {
            var boot = GameBootstrap.Instance;
            GameWorld.SanctuaryForce = 1;
            MatchSettings.spectate = kind == Kind.Match;
            boot.StartMatch();
            yield return null;
            w = boot.world;
            if (w.Sanct == null) { yield return Fail("INCOMPLETE: no house this map (none fits, or the kit has no VESPER_HOUSE: Build step 3)"); yield break; }
            wasScale = Time.timeScale;
            w.Event += OnShot;
            if (kind != Kind.Match) { wasSuspended = Commander.Suspended; Commander.Suspended = setSuspended = true; }
            if (kind == Kind.Rules) yield return Rules();
            else if (kind == Kind.Hide) yield return Hide();
            else if (kind == Kind.Backstop) yield return Backstop();
            else yield return Match();
            Destroy(gameObject);
        }

        // On ground joined to the gates' (a pocket by a cliff left a staged unit with no way anywhere).
        Vector2 At(float x, float z, float r = 4f) =>
            w.NearestReachable(w.Sanct.ToWorld(Sanctuary.GateX[0], -Sanctuary.HalfZ - Sanctuary.Apron - 3f), w.NearestWalkable(w.Sanct.ToWorld(x, z), r));

        IEnumerator Rules()
        {
            var s = w.Sanct;
            Time.timeScale = 2f;
            int errors0 = errors;
            // Ours in court 0, theirs and a Mauler before the gates, ours in the open beside them (the control),
            // one of ours behind the house with only enemies across it.
            var ins = new List<Unit>();
            for (int i = 0; i < 4; i++) ins.Add(w.Spawn(UnitType.Trooper, 0, s.ToWorld(-8f + i * 1.6f, -3f)));
            var foes = new List<Unit>();
            for (int i = 0; i < 4; i++) foes.Add(w.Spawn(UnitType.Trooper, 1, At(-8f + i * 3f, -24f)));
            foes.Add(w.Spawn(UnitType.Mauler, 1, At(4f, -26f, 6f)));
            var control = new List<Unit>();
            for (int i = 0; i < 3; i++) control.Add(w.Spawn(UnitType.Trooper, 0, At(18f + i * 1.6f, -16f)));
            var behind = w.Spawn(UnitType.Trooper, 0, At(0f, 13f));
            int insideAtStart = 0;
            foreach (var u in ins) if (u.inGrounds) insideAtStart++;
            Track();
            yield return Until(w.time + 30f, "the fight before the gates");
            if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            float lostIn = Lost(ins), lostBehind = Lost(new[] { behind }), lostOut = Lost(control) + Lost(foes);
            int shots1 = shotsAll, inside1 = shotsInside;
            string Where(Unit u) => !Unit.Live(u) ? "gone" : $"{u.Type} {(s.Contains(u.pos) ? "INSIDE" : "outside")} (local {s.Local(u.pos).x:0.0},{s.Local(u.pos).y:0.0}, " +
                $"order {u.order}, goal local {s.Local(u.orderPos).x:0.0},{s.Local(u.orderPos).y:0.0}, path {(u.agent != null && u.agent.enabled ? u.agent.pathStatus.ToString() : "-")})";

            // Out again: the fight ended, those inside sent 40 m out to the side; each must get there.
            foreach (var u in w.units) if (Unit.Live(u) && u.team == 1 && !u.def.building) w.Damage(u, 1e7f, u.pos, null);
            // 40 m off to the side, on ground joined to the gates' (the main ground).
            // 25 m straight out from gate 0, on ground joined to its apron (placement saw 8 m of it open).
            Vector2 goal = w.NearestReachable(s.ToWorld(Sanctuary.GateX[0], -Sanctuary.HalfZ - Sanctuary.Apron - 3f), s.ToWorld(Sanctuary.GateX[0], -Sanctuary.HalfZ - 25f));
            var outers = new List<Unit>();
            foreach (var u in ins) if (Unit.Live(u)) outers.Add(u);
            w.CmdMove(outers, goal, false, true);   // hurried: the way out is the test, not a careful man's pace
            // Meanwhile, the fight over, four sent into court 1 from 20 m off: who gets in.
            var walkers = new List<Unit> { w.Spawn(UnitType.Trooper, 0, At(24f, -8f)), w.Spawn(UnitType.Worker, 0, At(26f, -8f)),
                                           w.Spawn(UnitType.Mauler, 0, At(-30f, -20f, 6f)), w.Spawn(UnitType.Hauler, 0, At(-34f, -20f, 6f)) };
            yield return null;
            foreach (var u in walkers) w.CmdMove(new List<Unit> { u }, s.ToWorld(6f, -2f), false);
            yield return Until(w.time + 40f, "walking out and in");
            if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            int arrived = 0;
            foreach (var u in outers) if (Unit.Live(u) && (u.pos - goal).magnitude < 6f && !s.Contains(u.pos)) arrived++;
            bool footIn = Unit.Live(walkers[0]) && walkers[0].inGrounds && Unit.Live(walkers[1]) && walkers[1].inGrounds;
            bool heavyOut = Unit.Live(walkers[2]) && !s.Contains(walkers[2].pos) && Unit.Live(walkers[3]) && !s.Contains(walkers[3].pos);
            string walked = $"{Where(walkers[0])}, {Where(walkers[1])} (want inside), {Where(walkers[2])}, {Where(walkers[3])} (want outside)";

            // The sky stays open: our Kestrel over the house, one rifleman of theirs behind it, our riflemen and a
            // Mauler before it, their Thumper behind it. Fire to and from the Kestrel goes on; no ground gun of
            // either side holds a target across the house.
            // Left idle where it hovers: on a plain Move a gunship's gun holds.
            var kestrel = w.Spawn(UnitType.Kestrel, 0, s.ToWorld(0f, -1f));
            var them = new List<Unit> { w.Spawn(UnitType.Trooper, 1, At(6f, 15f)), w.Spawn(UnitType.Thumper, 1, At(-4f, 20f)) };
            var front = new List<Unit>();
            for (int i = 0; i < 3; i++) front.Add(w.Spawn(UnitType.Trooper, 0, At(-4f + i * 3f, -22f)));
            front.Add(w.Spawn(UnitType.Mauler, 0, At(10f, -26f, 6f)));
            // The rule itself, asked directly: from their rifleman behind the house to our Kestrel over it, no refusal and
            // an open line for a round (a man under its gun may well be keeping his head down instead).
            bool skyOpen = !w.SanctBlocks(them[0], kestrel) && !w.SanctBlocks(kestrel, them[0]) &&
                           w.Sight.FirstStop(them[0].Ground + Vector3.up * 1.5f, GameWorld.AimPoint(kestrel), null, null) >= 1f;
            int across = 0, frames = 0, shots2 = shotsAll;
            var acrossWho = new List<string>();
            void Across(Unit u)
            {
                if (!Unit.Live(u) || !Unit.Live(u.target) || u.target.def.flying || !s.Crosses(u.pos, u.target.pos)) return;
                across++;
                if (acrossWho.Count < 6) acrossWho.Add($"{u.Type}#{u.id} ({u.order}, {u.mode}) on {u.target.Type}#{u.target.id} at {w.time:0.0}");
            }
            yield return Until(w.time + 25f, "the sky over the house", () =>
            {
                frames++;
                foreach (var u in front) Across(u);
                foreach (var u in them) Across(u);
            });
            if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            float kestrelLost = Lost(new[] { kestrel }), themLost = Lost(them.GetRange(0, 1));
            int kestrelShots = ShotsBy(kestrel), themShots = ShotsBy(them[0]);
            var rifle = them[0];
            string rifleState = Unit.Live(rifle) ? $"{rifle.mode}, target {(rifle.target != null ? rifle.target.Type.ToString() : "none")}, seen the Kestrel {w.Sees(1, kestrel)}, hiding {rifle.hiding}, suppression {rifle.suppression:0.00}" : "dead";
            int errs = errors - errors0;
            bool pass = insideAtStart == ins.Count && lostIn <= 0f && inside1 == 0 && lostBehind <= 0f && shots1 > 0 &&
                        footIn && heavyOut && arrived == outers.Count && outers.Count > 0 && across == 0 && skyOpen && kestrelShots > 0 &&
                        lostInsideAll <= 0f && shotsInside == 0 && errs == 0;
            SanctuaryTrials.report = $"{(pass ? "PASS" : "FAIL")} Sanctuary at {s.centre} yaw {s.yawDeg:0}: inside {insideAtStart}/{ins.Count} at start, " +
                $"hull lost inside {lostIn:0} (want 0), shots from inside {inside1} (want 0), behind the house {lostBehind:0} (want 0); the fight outside: shots {shots1} (want > 0), hull lost {lostOut:0}; " +
                $"sent in: {walked}; walked out to local {s.Local(goal).x:0},{s.Local(goal).y:0}: {arrived}/{outers.Count} there in 40 s (want all){(arrived < outers.Count && outers.Count > 0 ? " [" + Where(outers[0]) + "]" : "")}; " +
                $"the sky: our Kestrel over the house fired {kestrelShots} and lost {kestrelLost:0}, their rifleman behind it fired {themShots} and lost {themLost:0} ({rifleState}) ; the line between them open both ways {skyOpen} (want true, and the Kestrel firing), frames a ground gun held a target across the house {across} of {frames} (want 0){(acrossWho.Count > 0 ? " [" + string.Join("; ", acrossWho) + "]" : "")}, shots {shotsAll - shots2}; " +
                $"over the whole run hull lost by anyone inside {lostInsideAll:0}, shots from inside {shotsInside} (want 0, 0); errors {errs} (want 0)";
        }

        /// <summary>Their side cut to one Digger hiding in court 0: still there and the match on until the turn-out
        /// (30 s), walked out, refused when sent back in, then fought: the match ends.</summary>
        IEnumerator Hide()
        {
            var s = w.Sanct;
            Time.timeScale = 4f;
            int errors0 = errors;
            foreach (var u in w.units) if (Unit.Live(u) && u.team == 1) w.Damage(u, 1e7f, u.pos, null);
            var dig = w.Spawn(UnitType.Worker, 1, s.ToWorld(-6f, -3f));
            yield return null;
            float t0 = w.time;
            bool aliveInside = true, endedEarly = false;
            yield return Until(t0 + 25f, "hiding", () => { aliveInside &= Unit.Live(dig) && dig.inGrounds; endedEarly |= w.winner >= 0; });
            if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            float turnedAt = -1f, outAt = -1f;
            yield return Until(t0 + 60f, "the turn-out", () =>
            {
                if (turnedAt < 0f && w.TurnedOut(1)) turnedAt = w.time - t0;
                if (outAt < 0f && turnedAt >= 0f && Unit.Live(dig) && !s.Contains(dig.pos)) outAt = w.time - t0;
            });
            if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            // Out, it must still walk: on the NavMesh, at its turn-out goal, then 20 m further on an order of ours.
            bool onMesh = Unit.Live(dig) && dig.agent != null && dig.agent.isOnNavMesh;
            float toGoal = Unit.Live(dig) ? (dig.pos - dig.orderPos).magnitude : -1f;
            bool further = false;
            Vector2 on = default;
            if (Unit.Live(dig))
            {
                on = w.NearestReachable(dig, dig.pos + (dig.pos - s.centre).normalized * 20f);
                w.CmdMove(new List<Unit> { dig }, on, false);
                yield return Until(w.time + 20f, "walking on", () => further |= Unit.Live(dig) && (dig.pos - on).magnitude < 4f);
                if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            }
            // Sent back in: it must stay out (no shelter, no way in).
            bool back = false;
            if (Unit.Live(dig))
            {
                w.CmdMove(new List<Unit> { dig }, s.ToWorld(-6f, -3f), false);
                yield return Until(w.time + 15f, "sent back in", () => back |= Unit.Live(dig) && s.Contains(dig.pos));
                if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            }
            // Now fought: four rifles of ours by it.
            if (Unit.Live(dig)) for (int i = 0; i < 4; i++) w.Spawn(UnitType.Trooper, 0, w.NearestWalkable(dig.pos + new Vector2(8f + i, 4f), 4f));
            float fought = w.time;
            yield return Until(w.time + 40f, "the end", null);
            int errs = errors - errors0;
            bool pass = aliveInside && !endedEarly && turnedAt >= 29f && turnedAt <= 33f && outAt > 0f && outAt - turnedAt <= 20f && onMesh && toGoal >= 0f && toGoal < 3f && further && !back && w.winner == 0 && errs == 0;
            SanctuaryTrials.report = $"{(pass ? "PASS" : "FAIL")} Hide: their last Digger in court 0: inside and alive for 25 s {aliveInside}, match over early {endedEarly} (want true, false); " +
                $"turned out at {turnedAt:0.0} s (want 29-33), out of the grounds at {outAt:0.0} s (want within 20 s of it); at 60 s on the NavMesh {onMesh}, {toGoal:0.0} m from its goal (want true, under 3); " +
                $"sent 20 m on, there {further} (want true); sent back in, got in {back} (want false); " +
                $"winner {w.winner} (want 0) {(w.winner >= 0 ? "" : "still on")}; errors {errs} (want 0)";
        }

        /// <summary>Rounds already in flight when their target steps into the grounds: a Mauler's shell, a Rocketeer's
        /// rocket, a Thumper's bomb and a Kestrel's rockets fired at a man on gate 0's apron, who is put in court 0 the
        /// next frame; five volleys. Target: 0 impacts inside the grounds, 0 hull lost there.</summary>
        IEnumerator Backstop()
        {
            var s = w.Sanct;
            Time.timeScale = 1f;
            int errors0 = errors, inside = 0, impacts = 0;
            void OnImpact(GameEvent e)
            {
                if (e.kind != GameEventKind.Impact || e.team > 1) return;
                impacts++;
                if (s.Contains(new Vector2(e.pos.x, e.pos.z))) inside++;
            }
            w.Event += OnImpact;
            Vector2 apron = At(Sanctuary.GateX[0], -Sanctuary.HalfZ - Sanctuary.Apron - 1f);
            var shooters = new List<Unit>
            {
                w.Spawn(UnitType.Mauler, 0, At(Sanctuary.GateX[0], -Sanctuary.HalfZ - 34f, 6f)),
                w.Spawn(UnitType.Rocketeer, 0, At(Sanctuary.GateX[0] - 4f, -Sanctuary.HalfZ - 30f)),
                w.Spawn(UnitType.Thumper, 0, At(Sanctuary.GateX[0] + 4f, -Sanctuary.HalfZ - 32f)),
                w.Spawn(UnitType.Kestrel, 0, s.ToWorld(Sanctuary.GateX[0], -Sanctuary.HalfZ - 26f)),
            };
            yield return null;
            for (int v = 0; v < 5; v++)
            {
                var man = w.Spawn(UnitType.Trooper, 1, apron);
                yield return null;
                Track();
                foreach (var u in shooters) if (Unit.Live(u)) w.Fire(u, man);
                yield return null;
                // In he goes, the rounds already on their way.
                Vector2 court = s.ToWorld(Sanctuary.GateX[0], -3f);
                if (man.agent != null) man.agent.Warp(w.Map.Ground(court));
                man.transform.position = w.Map.Ground(court);
                yield return Until(w.time + 4f, $"volley {v + 1} of 5");
                if (stalled != null) { w.Event -= OnImpact; SanctuaryTrials.report = stalled; yield break; }
                if (Unit.Live(man)) w.Damage(man, 1e7f, man.pos, null);
                yield return null;
            }
            w.Event -= OnImpact;
            int errs = errors - errors0;
            bool pass = inside == 0 && lostInsideAll <= 0f && errs == 0;
            SanctuaryTrials.report = $"{(pass ? "PASS" : "FAIL")} Backstop: 5 volleys of shell, rocket, bomb and Kestrel fire at a man put inside after they left: " +
                $"impacts {impacts}, inside the grounds {inside} (want 0), hull lost inside {lostInsideAll:0} (want 0); errors {errs} (want 0)";
        }

        /// <summary>AI against AI at 8x with the house, 300 s: nothing fired from inside, no hull lost inside, 0 errors.</summary>
        IEnumerator Match()
        {
            var s = w.Sanct;
            Time.timeScale = 8f;
            int errors0 = errors, unitFramesInside = 0, unitsInside = 0;
            var seenInside = new HashSet<int>();
            yield return Until(w.time + 300f, "AI against AI", () =>
            {
                foreach (var u in w.units) if (u != null && u.inGrounds) { unitFramesInside++; seenInside.Add(u.id); }
            });
            if (stalled != null) { SanctuaryTrials.report = stalled; yield break; }
            unitsInside = seenInside.Count;
            int errs = errors - errors0;
            bool pass = shotsInside == 0 && lostInsideAll <= 0f && errs == 0;
            SanctuaryTrials.report = $"{(pass ? "PASS" : "FAIL")} Match: 300 s AI against AI at 8x with the house at {s.centre}: units that went in {unitsInside} ({unitFramesInside} unit-frames), " +
                $"shots from inside {shotsInside}, hull lost inside {lostInsideAll:0} (want 0, 0), shots in all {shotsAll}, winner {w.winner}; errors {errs} (want 0)";
        }
    }

    /// <summary>SanctuaryTrials.SweepHouse's runner (not named after its file).</summary>
    public sealed class SanctuarySweep : MonoBehaviour
    {
        public static string Last, Progress = "running: staging";
        public static int Count = 200;

        IEnumerator Start()
        {
            var info = MapInfo.Instance;
            var rt = info != null ? info.GetComponent<MapRuntime>() : null;
            var surface = info != null ? info.GetComponent<Unity.AI.Navigation.NavMeshSurface>() : null;
            if (rt == null || rt.kit == null || surface == null) { Last = "FAILED: no map runtime"; yield break; }
            var rng = new System.Random(20261021);
            int ground = GameWorld.GroundAreas, foot = GameWorld.FootAreas, heavy = UnityEngine.AI.NavMesh.AllAreas & ~(1 << MapGenerator.SanctuaryArea);
            int digger = GameWorld.GroundAreas | (1 << MapGenerator.SanctuaryArea), courtArea = 1 << MapGenerator.SanctuaryArea;
            int done = 0, placed = 0, dropped = 0, notJoined = 0, oreCut = 0, footIn = 0, footOut = 0, diggerOut = 0, vehicleIn = 0;
            int coverOn = 0, ditchOn = 0, badArea = 0, plantsCleared = 0, rocksCleared = 0, cloisterAsk = 0, cloisterGot = 0, threw = 0;
            int[] fails = new int[5];
            var walls = new List<CoverWall>();
            var runs = new List<CoverWall>();
            var ditches = new List<(Vector2 a, Vector2 b)>();
            var path = new UnityEngine.AI.NavMeshPath();
            var bad = new System.Text.StringBuilder();
            System.Array.Clear(MapGenerator.CloisterFails, 0, 4);
            float t0 = Time.realtimeSinceStartup;
            bool Complete(Vector3 a, Vector2 b2, int mask, int sampleMask, float near = 1.5f)
            {
                if (!UnityEngine.AI.NavMesh.SamplePosition(info.Ground(b2), out var hb, near, sampleMask)) return false;
                UnityEngine.AI.NavMesh.CalculatePath(a, hb.position, mask, path);
                return path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
            }
            for (int k = 0; k < Count; k++)
            {
                uint seed = (uint)rng.Next(1, int.MaxValue);
                string err = null;
                try { MapGenerator.Generate(info, rt.kit, seed, bakeNavMesh: true, sharedMaterials: false); }
                catch (System.Exception e) { err = e.GetType().Name; }
                done++;
                if (err != null) { threw++; bad.Append($"\n{seed}: Generate threw {err}"); yield return null; continue; }
                MapGenerator.CoverRoot(info, true);
                GroundDeformer.Instance?.Init(info);
                var s = MapGenerator.PlaceSanctuary(info, rt.kit, seed, runs);
                for (int f = 0; f < 5; f++) fails[f] += MapGenerator.SanctFails[f];
                MapGenerator.DigDitches(info, GroundDeformer.Instance, seed, ditches, tell: false);
                var cr = MapGenerator.PlaceCover(info, rt.kit, seed, walls, ditches, 2, s != null ? s.piece : null);
                MapGenerator.ClearSanctuaryReserve();
                cloisterAsk += 2; cloisterGot += cr.cloisters;
                MapGenerator.Rebuild(surface);
                if (s != null && (MapGenerator.CoverCut(info, out _) || MapGenerator.CourtsCut(info, s)))
                {
                    MapGenerator.RemoveSanctuary(s);
                    MapGenerator.Rebuild(surface);
                    dropped++;
                    bad.Append($"\n{seed}: the house cut a way or a court: taken away");
                    s = null;
                }
                MapGenerator.OpenCutWays(info, surface, walls);
                var problems = new List<string>();
                bool aOn = UnityEngine.AI.NavMesh.SamplePosition(info.Ground(info.StartPos(0)), out var ha, 8f, ground);
                bool bOn = UnityEngine.AI.NavMesh.SamplePosition(info.Ground(info.StartPos(1)), out var hb, 8f, ground);
                if (!aOn || !bOn || !Complete(ha.position, new Vector2(hb.position.x, hb.position.z), ground, ground)) { notJoined++; problems.Add("bases not joined"); }
                else
                {
                    int cut = 0;
                    foreach (Transform o in info.oreRoot)
                    {
                        var g = new Vector2(o.position.x, o.position.z);
                        if (!Complete(ha.position, g, ground, ground, 8f) && !Complete(hb.position, g, ground, ground, 8f)) cut++;
                    }
                    if (cut > 0) { oreCut++; problems.Add($"{cut} ore seams no base reaches"); }
                }
                if (s != null)
                {
                    placed++;
                    plantsCleared += s.plantsCleared.Count; rocksCleared += s.bouldersCleared.Count;
                    Vector2 c0 = s.ToWorld(-6f, -3f), c1 = s.ToWorld(6f, -3f), outside = s.ToWorld(Sanctuary.GateX[0], -Sanctuary.HalfZ - 8f);
                    if (aOn && bOn)
                    {
                        bool inA = Complete(ha.position, c0, foot, courtArea) && Complete(ha.position, c1, foot, courtArea);
                        bool inB = Complete(hb.position, c0, foot, courtArea) && Complete(hb.position, c1, foot, courtArea);
                        if (inA && inB) footIn++; else problems.Add($"a soldier cannot walk into both courts (from A {inA}, from B {inB})");
                        if (Complete(ha.position, c0, heavy, courtArea) || Complete(ha.position, c1, heavy, courtArea) ||
                            Complete(ha.position, c0, ground, courtArea)) { vehicleIn++; problems.Add("a vehicle's way into a court"); }
                    }
                    // Out again from a court, on foot and as a Digger (the turn-out's walk).
                    if (UnityEngine.AI.NavMesh.SamplePosition(info.Ground(c0), out var hc, 1.5f, courtArea))
                    {
                        if (Complete(hc.position, outside, foot, ground)) footOut++; else problems.Add("no soldier's way out of court 0");
                        if (!Complete(hc.position, outside, digger, ground)) { diggerOut++; problems.Add("no Digger's way out of court 0"); }
                    }
                    foreach (var w in walls) if (s.DistanceToPlan(w.Mid) < 1f) coverOn++;
                    foreach (var d in ditches) if (s.Crosses(d.a, d.b) || s.DistanceToPlan(d.a) < 2f || s.DistanceToPlan(d.b) < 2f) ditchOn++;
                    // The grounds' NavMesh over nothing but itself: no Rubble, Vault or Wall under the courts or aprons.
                    for (float x = -Sanctuary.HalfX + 1f; x <= Sanctuary.HalfX - 1f; x += 1f)
                        for (float z = -Sanctuary.HalfZ - Sanctuary.Apron + 0.5f; z <= Sanctuary.HallZ - 1f; z += 1f)
                        {
                            var q = info.Ground(s.ToWorld(x, z));
                            if (!UnityEngine.AI.NavMesh.SamplePosition(q, out var hq, 0.25f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                            if (new Vector2(hq.position.x - q.x, hq.position.z - q.z).sqrMagnitude > 0.04f) continue;
                            int a = 0; for (int m = hq.mask; m > 1; m >>= 1) a++;   // the hit's area from its bit
                            if (a == MapGenerator.RubbleArea || a == MapGenerator.VaultArea || a == MapGenerator.WallArea) badArea++;
                        }
                }
                if (problems.Count > 0) bad.Append($"\n{seed}: {string.Join("; ", problems)}");
                Progress = $"running: {done}/{Count}, placed {placed}";
                yield return null;
            }
            MapGenerator.CoverRoot(info, true);
            Last = $"{done} seeds in {Time.realtimeSinceStartup - t0:0} s: the house placed on {placed} ({placed * 100 / Mathf.Max(1, done)}%; want 80%+), " +
                   $"taken away for a cut way {dropped}; fails over all tries: base {fails[0]}, ore {fails[1]}, ground {fails[2]}, slope {fails[3]}, scenery {fails[4]}; " +
                   $"cleared {plantsCleared} plants and {rocksCleared} rocks ({plantsCleared / Mathf.Max(1f, placed):0} and {rocksCleared / Mathf.Max(1f, placed):0.0} a house); " +
                   $"bases not joined {notJoined}, seeds with ore cut {oreCut} (want 0); with the house: soldiers into both courts from both bases {footIn}/{placed}, " +
                   $"out again {footOut}/{placed}, Diggers with no way out {diggerOut}, vehicle ways in {vehicleIn} (want 0), cover pieces on it {coverOn}, ditches on it {ditchOn}, " +
                   $"grounds points over Rubble/Vault/Wall {badArea} (want 0); cloisters {cloisterGot} of {cloisterAsk} asked ({cloisterGot * 100 / Mathf.Max(1, cloisterAsk)}%), " +
                   $"cloister fails: spacing {MapGenerator.CloisterFails[0]}, open {MapGenerator.CloisterFails[1]}, shape {MapGenerator.CloisterFails[2]}, pieces {MapGenerator.CloisterFails[3]}; " +
                   $"Generate threw {threw}" + bad;
            Debug.Log("[SanctuarySweep] " + Last);
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }
}
