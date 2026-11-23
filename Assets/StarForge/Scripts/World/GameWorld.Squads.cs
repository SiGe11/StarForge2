// GameWorld.Squads.cs -- soldiers who are together act together (TacticalRules). There is no squad to
// train, select or see: men sent together, or standing near each other, simply are one, and leave it by
// drifting too far off. In contact a squad takes turns (fire and move): half lies up and shoots while
// the other half bounds, and on a long fight one half works round the flank. It tells its commander when
// it is being beaten or is held by what its rifles cannot hurt: text in the comms feed, nothing to accept.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;

namespace StarForge.World
{
    public enum SquadReportKind : byte { FallBack, NeedAir }
    public enum SquadBlunder : byte { None, Rush, Stall }

    public sealed class Squad
    {
        public readonly List<Unit> men = new List<Unit>(MaxSize);
        public const int MaxSize = 8;
        public int team;
        public Vector2 centre;
        /// <summary>Whoever is furthest along its way, walking ahead of the rest.</summary>
        public Unit point;
        /// <summary>In contact: which half's turn it is to move, until when, and how many are moving.</summary>
        public bool contact;
        public float contactSince, turnUntil;
        public int movingTeam, moving;
        /// <summary>Which way this squad works round an enemy that holds it: -1 or 1.</summary>
        public int flankSide = 1;
        public float reportedAt = -99f;
        /// <summary>Its name on the radio, and the highest rank in it: the oldest hand steadies them all.</summary>
        public string callsign;
        public int veteran;
        /// <summary>It gets it wrong for a turn: everyone goes at once, or nobody goes at all.</summary>
        public SquadBlunder blunder;
        /// <summary>One man throws at a time: the others hold off until then.</summary>
        public float thrownUntil = -99f;
        /// <summary>One of theirs is about to go off: nobody moves up into it.</summary>
        public float fragUntil = -99f;
        /// <summary>When an order made it: it does not merge straight back into those it left.</summary>
        public float orderedAt = -99f;
        public int checkedAt;
        public bool staysWhole;
        public readonly List<float> lostAt = new List<float>(8);
    }

    public sealed partial class GameWorld
    {
        const float SquadJoin = 15f, SquadLeave = 30f, SquadMerge = 8f, SquadSameGoal = 12f, MergeAfterOrder = 10f;
        const float SquadEvery = 0.5f, TurnLasts = 4.5f;
        // 26 birth-star names (the lore's field net names squads by them; no Earth alphabet): squads
        // form about five a minute, and twelve came round again while the first were alive.
        static readonly string[] CallSigns = { "Wren", "Lark", "Heron", "Crane", "Tern", "Hart", "Hound", "Otter", "Badger", "Ram", "Plough", "Sickle",
                                               "Scythe", "Harp", "Bell", "Spindle", "Mill", "Weaver", "Wheel", "Key", "Quill", "Cup", "Comb", "Flint", "Rowan", "Thorn" };
        const float ReportSquadEvery = 30f, ReportSideEvery = 10f, ReportPlaceEvery = 60f, ReportPlace = 40f;
        const float NeedAirAfter = 8f, LossesLast = 20f, ContactAt = 40f;

        public readonly List<Squad> Squads = new List<Squad>(32);
        float squadsNext;
        int squadStamp;
        readonly float[] reportedSide = { -99f, -99f };
        readonly List<(Vector2 at, float t, int team, SquadReportKind kind)> reports = new List<(Vector2, float, int, SquadReportKind)>(16);
        readonly List<Unit> squadScratch = new List<Unit>(32);
        readonly List<Unit> squadThreats = new List<Unit>(32);
        readonly List<Unit> squadLoose = new List<Unit>(32);
        readonly List<Squad> squadsSent = new List<Squad>(8);
        /// <summary>Squads each side has formed (the trials': a match's churn).</summary>
        public readonly int[] SquadsMade = new int[2];
        /// <summary>Reports each side's squads have made (the trials').</summary>
        public readonly int[] ReportsMade = new int[2];

        void BeginSquads()
        {
            Squads.Clear();
            reports.Clear();
            reportedSide[0] = reportedSide[1] = -99f;
            ReportsMade[0] = ReportsMade[1] = 0;
            SquadsMade[0] = SquadsMade[1] = 0;
            squadsNext = 0f;
        }

        /// <summary>Men sent together are a squad, in eights, by who stands near whom. A squad all of it sent,
        /// or sent where the rest of it is already going, stays as it is with what it remembers (the AI
        /// repeats its orders every pass, to all it has or to most); only the part sent somewhere else
        /// leaves, and joins the others sent if they are near.</summary>
        void SquadFromOrder(IReadOnlyList<Unit> sel, Vector2 dest, Unit at)
        {
            squadScratch.Clear();
            foreach (var u in sel)
                if (Unit.Live(u) && u.def.bodyRadius > 0f && !u.deserted && !u.noMind) squadScratch.Add(u);
            if (squadScratch.Count == 0) return;
            int stamp = ++squadStamp;
            foreach (var u in squadScratch) u.squadMark = stamp;
            squadsSent.Clear();
            squadLoose.Clear();
            foreach (var u in squadScratch)
            {
                var q = u.squad;
                if (q == null) { squadLoose.Add(u); continue; }
                if (q.checkedAt != stamp)
                {
                    q.checkedAt = stamp;
                    q.staysWhole = true;
                    foreach (var m in q.men)
                        if (m.squadMark != stamp && !GoingThere(m, dest, at)) { q.staysWhole = false; break; }
                    if (q.staysWhole) squadsSent.Add(q);
                }
                if (!q.staysWhole) squadLoose.Add(u);
            }
            foreach (var u in squadLoose)
            {
                LeaveSquad(u);
                Squad into = null;
                float bestD = SquadJoin * SquadJoin;
                foreach (var q in squadsSent)
                {
                    if (q.men.Count >= Squad.MaxSize) continue;
                    float d = NearestMan(q, u.pos);
                    if (d < bestD) { bestD = d; into = q; }
                }
                if (into == null)
                {
                    into = NewSquad(u);
                    into.orderedAt = time;
                    squadsSent.Add(into);
                }
                u.squad = into;
                into.men.Add(u);
            }
        }

        static bool GoingThere(Unit m, Vector2 dest, Unit at) => at != null
            ? (m.order == Order.Attack && m.target == at) || (m.squad != null && m.squad.contact)
            : (m.order == Order.Move || m.order == Order.AttackMove) && (m.orderPos - dest).sqrMagnitude < SquadSameGoal * SquadSameGoal;

        /// <summary>Squared, to the nearest of them: eight in a line are one squad, whoever is at its end.</summary>
        static float NearestMan(Squad q, Vector2 at)
        {
            float best = float.MaxValue;
            foreach (var m in q.men)
            {
                float d = (m.pos - at).sqrMagnitude;
                if (d < best) best = d;
            }
            return best;
        }

        Squad NewSquad(Unit u)
        {
            int made = SquadsMade[Mathf.Clamp(u.team, 0, 1)]++;
            var q = new Squad { team = u.team, centre = u.pos, flankSide = (u.id & 1) == 0 ? 1 : -1, callsign = CallSigns[made % CallSigns.Length] };
            Squads.Add(q);
            return q;
        }

        /// <summary>Two squads going the same way, or neither going anywhere.</summary>
        static bool SameWay(Squad x, Squad y)
        {
            if (x.men.Count == 0 || y.men.Count == 0) return false;
            Unit a = x.men[0], b = y.men[0];
            if (a.order == Order.Idle && b.order == Order.Idle) return true;
            if (a.order != b.order) return false;
            return a.order == Order.Attack ? a.target == b.target : (a.orderPos - b.orderPos).sqrMagnitude < SquadSameGoal * SquadSameGoal;
        }

        void LeaveSquad(Unit u)
        {
            if (u.squad != null) u.squad.men.Remove(u);
            u.squad = null;
        }

        /// <summary>A man down: his squad and his side remember it.</summary>
        void SquadLoss(Unit u)
        {
            if (u.squad != null) u.squad.lostAt.Add(time);
            LeaveSquad(u);
        }

        void TickSquads()
        {
            if (time < squadsNext) return;
            squadsNext = time + SquadEvery;

            // Who has left: too far from the others, or out of it for good.
            for (int s = Squads.Count - 1; s >= 0; s--)
            {
                var q = Squads[s];
                for (int i = q.men.Count - 1; i >= 0; i--)
                {
                    var u = q.men[i];
                    if (!Unit.Live(u) || u.deserted) { if (u != null) u.squad = null; q.men.RemoveAt(i); }
                }
                Measure(q);
                for (int i = q.men.Count - 1; i >= 0 && q.men.Count > 1; i--)
                    if ((q.men[i].pos - q.centre).sqrMagnitude > SquadLeave * SquadLeave) { q.men[i].squad = null; q.men.RemoveAt(i); }
                if (q.men.Count == 0) Squads.RemoveAt(s);
            }

            // Who has none: the squad he is near, or one of his own.
            foreach (var u in units)
            {
                if (u == null || u.dying || u.squad != null || u.def.bodyRadius <= 0f || u.deserted || u.noMind || u.team > 1) continue;
                Squad best = null;
                float bestD = SquadJoin * SquadJoin;
                foreach (var q in Squads)
                {
                    if (q.team != u.team || q.men.Count >= Squad.MaxSize) continue;
                    float d = NearestMan(q, u.pos);
                    if (d < bestD) { bestD = d; best = q; }
                }
                if (best == null) best = NewSquad(u);
                u.squad = best;
                best.men.Add(u);
            }

            // Two that have come together are one, if they fit.
            for (int a = Squads.Count - 1; a >= 1; a--)
                for (int b = a - 1; b >= 0; b--)
                {
                    Squad x = Squads[a], y = Squads[b];
                    if (x.team != y.team || x.men.Count + y.men.Count > Squad.MaxSize || (x.centre - y.centre).sqrMagnitude > SquadMerge * SquadMerge) continue;
                    if (time - x.orderedAt < MergeAfterOrder || time - y.orderedAt < MergeAfterOrder || !SameWay(x, y)) continue;
                    foreach (var u in x.men) { u.squad = y; y.men.Add(u); }
                    y.lostAt.AddRange(x.lostAt);
                    Squads.RemoveAt(a);
                    break;
                }

            foreach (var q in Squads)
            {
                Measure(q);
                TakeTurns(q);
                Report(q);
            }
        }

        static void Measure(Squad q)
        {
            Vector2 sum = Vector2.zero;
            int veteran = 0;
            foreach (var u in q.men)
            {
                sum += u.pos;
                if (u.rank > veteran) veteran = u.rank;
            }
            q.veteran = veteran;
            if (q.men.Count > 0) q.centre = sum / q.men.Count;
        }

        void TakeTurns(Squad q)
        {
            bool contact = false;
            int moving = 0;
            float leastLeft = float.MaxValue;
            q.point = null;
            // A man keeps his half; only the two getting lopsided moves anyone.
            int ones = 0;
            foreach (var u in q.men) ones += u.fireTeam;
            int zeros = q.men.Count - ones;
            for (int i = 0; i < q.men.Count && Mathf.Abs(zeros - ones) > 1; i++)
            {
                var u = q.men[i];
                if (u.fireTeam != (zeros > ones ? 0 : 1)) continue;
                u.fireTeam = 1 - u.fireTeam;
                if (u.fireTeam == 1) { zeros--; ones++; } else { zeros++; ones--; }
            }
            for (int i = 0; i < q.men.Count; i++)
            {
                var u = q.men[i];
                if (time - u.lastFiredAtT < 4f || u.mode != SoldierMode.Free ||
                    (Unit.Live(u.target) && FightsBack(u.target) && (u.target.pos - u.pos).sqrMagnitude < ContactAt * ContactAt && Sees(u.team, u.target))) contact = true;
                if (u.IsMovingUnderOrder) moving++;
                if (u.order == Order.AttackMove || u.order == Order.Move)
                {
                    float left = (u.orderPos - u.pos).sqrMagnitude;
                    if (left < leastLeft) { leastLeft = left; q.point = u; }
                }
            }
            if (contact && !q.contact) { q.contactSince = time; q.turnUntil = time + TurnLasts; q.blunder = SquadBlunder.None; }
            q.contact = contact;
            q.moving = moving;
            if (!contact) return;
            if (time >= q.turnUntil)
            {
                q.turnUntil = time + TurnLasts;
                if (q.men.Count >= 2) q.movingTeam = 1 - q.movingTeam;
                // Now and then a squad gets it wrong for a turn, less the longer its oldest hand has served.
                q.blunder = SquadBlunder.None;
                if (q.men.Count >= 2 && !TacticalRules.NoMistakes && combatRng.F01() < TacticalRules.MistakeChance(q.veteran) * 0.6f)
                {
                    q.blunder = combatRng.F01() < 0.5f ? SquadBlunder.Rush : SquadBlunder.Stall;
                    SquadBlunders++;
                }
            }
            if (q.blunder == SquadBlunder.Rush && time >= q.fragUntil) { q.moving = moving; return; }
            // Those whose turn it is not, walking up on an attack, stop and shoot; so do any over the half,
            // and everyone while one of theirs is about to go off.
            int most = time < q.fragUntil || q.blunder == SquadBlunder.Stall ? 0 : Mathf.Max(1, q.men.Count / 2);
            if (q.men.Count >= 2)
                foreach (var u in q.men)
                    if (u.fireTeam != q.movingTeam && u.StopForTurn()) moving--;
            for (int i = 0; i < q.men.Count && moving > most; i++)
                if (q.men[i].StopForTurn()) moving--;
            q.moving = moving;
        }

        /// <summary>The way round the flank of what holds the squad, once it has held it a while: one half
        /// goes wide, in its bounds and when closing; the other goes straight and holds the enemy's eye.</summary>
        public Vector2 FlankPull(Unit u, Unit threat)
        {
            var q = u.squad;
            if (q == null || q.men.Count < 4 || !q.contact || u.fireTeam != 1 || time - q.contactSince < 6f) return Vector2.zero;
            Vector2 to = threat.pos - u.pos;
            float len = to.magnitude;
            return len < 1e-3f ? Vector2.zero : new Vector2(-to.y, to.x) / len * q.flankSide;
        }

        // ------------------------------------------------------------ reports
        void Report(Squad q)
        {
            if (q.men.Count == 0 || q.team > 1) return;
            for (int i = q.lostAt.Count - 1; i >= 0; i--)
                if (time - q.lostAt[i] > LossesLast) q.lostAt.RemoveAt(i);
            if (!q.contact || time - q.reportedAt < ReportSquadEvery || time - reportedSide[q.team] < ReportSideEvery) return;

            // What it knows of round it.
            squadThreats.Clear();
            CellRange(q.centre, 45f, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.deserted || o.Untargetable || o.team == q.team || o.team == 2 || !FightsBack(o) || !Sees(q.team, o)) continue;
                        if ((o.pos - q.centre).sqrMagnitude < 45f * 45f) squadThreats.Add(o);
                    }

            if (squadThreats.Count == 0) return;   // the last of them just went down: nothing to fall back from

            int pinned = 0;
            foreach (var u in q.men) if (u.mode == SoldierMode.Pinned || u.mode == SoldierMode.Panic) pinned++;
            float theirs = 0f, heavy = 0f;
            foreach (var o in squadThreats)
            {
                theirs += Mathf.Max(50f, Defs.ArmyValue(o.Type));
                if (o.def.Heavy || o.def.building) heavy += 1f;
            }
            float ours = 50f * q.men.Count;
            int started = q.men.Count + q.lostAt.Count;

            bool losing = q.lostAt.Count >= Mathf.Max(2, Mathf.CeilToInt(started / 3f));
            bool outgunned = theirs >= ours * 1.6f && pinned * 2 >= q.men.Count;
            bool airHelps = heavy > 0f && !AircraftNear(q);
            bool held = airHelps && time - q.contactSince >= NeedAirAfter;
            if (losing && MakeReport(q, SquadReportKind.FallBack, started)) return;
            if (held && MakeReport(q, SquadReportKind.NeedAir, started)) return;
            // Held by armour, the call is for aircraft: an "outgunned" first would use up the squad's turn to speak.
            if (outgunned && !(airHelps && time - q.contactSince < NeedAirAfter)) MakeReport(q, SquadReportKind.FallBack, started);
        }

        bool AircraftNear(Squad q)
        {
            foreach (var u in units)
                if (u != null && !u.dying && u.team == q.team && u.def.flying && u.def.Armed && (u.pos - q.centre).sqrMagnitude < 50f * 50f) return true;
            return false;
        }

        /// <summary>False if the same news from the same place has just been given.</summary>
        bool MakeReport(Squad q, SquadReportKind kind, int started)
        {
            // Not the same news from the same place: another squad beside it has nothing to add.
            for (int i = reports.Count - 1; i >= 0; i--)
            {
                var r = reports[i];
                if (time - r.t > ReportPlaceEvery) { reports.RemoveAt(i); continue; }
                if (r.team == q.team && r.kind == kind && (r.at - q.centre).sqrMagnitude < ReportPlace * ReportPlace) return false;
            }
            reports.Add((q.centre, time, q.team, kind));
            q.reportedAt = time;
            reportedSide[q.team] = time;
            ReportsMade[q.team]++;

            string where = Whereabouts(q.team, q.centre);
            string who = q.men.Count == 1 ? "1 man" : $"{q.men.Count} men";
            string text = kind == SquadReportKind.FallBack
                ? (started > q.men.Count
                    ? $"{who} left of {started} {where}, under heavy fire. We should fall back."
                    : $"{who} {where}, outgunned and pinned down. We should fall back.")
                : $"{who} {where}, held by what rifles cannot hurt. We need air support.";
            Raise(new GameEvent
            {
                kind = GameEventKind.SquadReport, team = q.team, pos = map.Ground(q.centre), text = text,
                index = (int)kind, men = q.men.ToArray(), who = q.callsign,
            });
            return true;
        }

        /// <summary>Where a place lies from the side's own base, in words.</summary>
        string Whereabouts(int team, Vector2 at)
        {
            Vector2 from = at - map.StartPos(Mathf.Clamp(team, 0, 1));
            float away = from.magnitude;
            if (away < 30f) return "at our base";
            float deg = Mathf.Atan2(from.x, from.y) * Mathf.Rad2Deg;
            if (deg < 0f) deg += 360f;
            string[] compass = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return $"{Mathf.RoundToInt(away / 10f) * 10} m {compass[Mathf.RoundToInt(deg / 45f) % 8]} of our base";
        }
    }
}
