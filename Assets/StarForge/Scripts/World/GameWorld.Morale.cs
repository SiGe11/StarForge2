// GameWorld.Morale.cs — morale: a side that is losing badly may see a few of its
// soldiers break and run.
//
// Rare by design. Every couple of seconds each side is read: is it clearly losing the
// war (its army and Mech worth well under the enemy's, and its last minute's losses
// much heavier than what it dealt), and is a fight going badly for a group of its
// soldiers right now (outnumbered round them, several of them dead in the last 20 s)?
// Only then is it in a crisis, and a crisis gets one roll of the dice -- however long
// it lasts. Nothing breaks in the first five minutes, and a side breaks at most once a
// match (MaxEpisodes).
//
// Those who run are a few of that fight's soldiers (about one in seven, one to four):
// the green and the hurt first; veterans of rank 2 stand, Diggers never run. They make
// for the corner of the map behind their own base -- near it, not in it -- and stay
// there: no longer their side's to command, not counted against its supply, shooting
// only when the camp is set upon, and never straying far from it. Their side's Mech
// may go and punish them (AI/MechBrain): while it does (Purging), its guns -- only its
// guns -- treat those deserters as enemies.
//
// Its own random stream (from the match seed), so morale does not shift what the rest
// of a seeded match draws.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    /// <summary>One side's morale: its deserters' camp, who is in it, and the record.</summary>
    public sealed class MoraleState
    {
        /// <summary>Where its deserters sit (set at the first desertion, kept all match).</summary>
        public Vector2 camp;
        public bool campSet;
        /// <summary>Its deserters still alive (on their way or in the camp).</summary>
        public readonly List<Unit> deserters = new List<Unit>(8);
        /// <summary>Times it broke, soldiers who ran, deserters its own Mech put down,
        /// crises it went through (each one rolled once).</summary>
        public int episodes, deserted, executed, crises;
        public float lastEpisode = -999f;
        /// <summary>When an enemy last hit one of them: the camp fights for a while after.</summary>
        public float alarmT = -99f;
        /// <summary>Its Mech is on its way to punish them: its guns may shoot them.</summary>
        public bool purge;
        // A crisis lasts until the side has been out of one for CrisisGap seconds.
        public bool inCrisis;
        public float crisisUntil = -1f;
        /// <summary>For the debug view: why it is or is not in a crisis.</summary>
        public string reading = "";
    }

    public sealed partial class GameWorld
    {
        // ------------------------------------------------------------ the rules
        const float MoraleFrom = 300f;      // no one breaks in the first five minutes
        const float MoraleCheck = 2f;       // seconds between readings
        const int MaxEpisodes = 1;          // a side breaks at most once a match
        const float BreakChance = 0.25f;    // the one roll a crisis gets, at its mildest
        const float CrisisGap = 30f;        // calm this long ends a crisis
        const float LossWindow = 60f;       // the exchange is read over the last minute
        public const float CampInset = 16f; // the corner the camp looks for, this far in from the edges
        public const float CampNear = 40f, CampFar = 85f;   // the camp's distance from the base's middle
        public const float CampClear = 25f; // and how far it keeps from the side's structures
        public const float CampLeash = 14f; // how far from the camp a deserter will fight
        public const float CampAlarm = 8f;  // seconds the camp fights after an enemy hit

        public readonly MoraleState[] morale = { new MoraleState(), new MoraleState() };
        /// <summary>Morale runs only in a real match: GameBootstrap switches it on each tick
        /// unless the opponent is silenced for a trial (Commander.Suspended). Off, nobody
        /// breaks of their own accord (a trial can still force it with <see cref="Desert"/>),
        /// so the Mech duels and bay edge cases measure what they always did.</summary>
        [System.NonSerialized] public bool moraleOn;
        Rng moraleRng = new Rng(0x3A4A1u);
        float nextMoraleCheck;

        struct Loss { public float t; public int team; public Vector2 pos; public float value; public bool army; }
        readonly List<Loss> losses = new List<Loss>(128);
        readonly List<Unit> moraleScratch = new List<Unit>(64);
        readonly List<Unit> routGroup = new List<Unit>(32);

        public MoraleState MoraleOf(int team) => morale[Mathf.Clamp(team, 0, 1)];

        /// <summary>Its Mech is out to punish its deserters (see <see cref="Hostile"/>).</summary>
        public bool Purging(int team) => team >= 0 && team < 2 && morale[team].purge;

        public void SetPurge(int team, bool on)
        {
            if (team >= 0 && team < 2) morale[team].purge = on;
        }

        /// <summary>Does <paramref name="a"/> shoot at <paramref name="b"/>? The enemy; and a
        /// side's own deserters, but only to its Mech, and only while it is out to punish them.</summary>
        public bool Hostile(Unit a, Unit b) =>
            a.team != b.team ? a.team < 2 && b.team < 2 : a.mech != null && b.deserted && Purging(a.team);

        /// <summary><see cref="EnemiesNear"/>, and for a Mech out to punish its side's
        /// deserters, those of them in reach too.</summary>
        public void FoesNear(Unit u, float radius, List<Unit> into)
        {
            EnemiesNear(u, radius, into);
            if (u.mech == null || !Purging(u.team)) return;
            float r2 = (radius + 3f) * (radius + 3f);
            foreach (var d in morale[u.team].deserters)
                if (Unit.Live(d) && (d.pos - u.pos).sqrMagnitude <= r2) into.Add(d);
        }

        void BeginMorale(uint seed)
        {
            moraleRng = new Rng(seed ^ 0x3A4A1u);
            moraleOn = false;
            nextMoraleCheck = MoraleFrom;
            losses.Clear();
            for (int t = 0; t < 2; t++)
            {
                var M = morale[t];
                M.campSet = false;
                M.deserters.Clear();
                M.episodes = M.deserted = M.executed = M.crises = 0;
                M.lastEpisode = -999f;
                M.alarmT = -99f;
                M.purge = false;
                M.inCrisis = false;
                M.crisisUntil = -1f;
                M.reading = "";
            }
        }

        /// <summary>What a death cost its side, for the exchange (from Kill). Deserters were
        /// no longer the side's, and one its own side put down is not a loss to the enemy.</summary>
        void NoteLoss(Unit u, bool byOwnSide)
        {
            if (u.team > 1 || u.deserted || byOwnSide) return;
            var d = u.def;
            float v = u.mech != null ? 1500f : d.building ? Mathf.Max(100f, d.cost) : Defs.ArmyValue(d.type);
            if (v <= 0f) return;
            losses.Add(new Loss { t = time, team = u.team, pos = u.pos, value = v, army = d.IsArmy });
        }

        /// <summary>A deserter was hit: by the enemy (or a shell whose firer is gone), the
        /// camp turns to fight; by its own Mech, it does not -- they do not fire on it.</summary>
        void DeserterHit(Unit u, Unit source)
        {
            if (source != null && source.team == u.team) return;
            morale[u.team].alarmT = time;
        }

        // ------------------------------------------------------------ the reading
        void TickMorale()
        {
            for (int t = 0; t < 2; t++)
                morale[t].deserters.RemoveAll(d => !Unit.Live(d));
            if (!moraleOn || time < nextMoraleCheck) return;
            nextMoraleCheck = time + MoraleCheck;
            for (int i = losses.Count - 1; i >= 0; i--)
                if (time - losses[i].t > LossWindow) losses.RemoveAt(i);

            for (int t = 0; t < 2; t++)
            {
                var M = morale[t];
                if (M.episodes >= MaxEpisodes) continue;
                bool crisis = Crisis(t, out Vector2 at, out float severity, out M.reading);
                if (crisis)
                {
                    M.crisisUntil = time + CrisisGap;
                    if (M.inCrisis) continue;
                    // A new crisis: its one roll.
                    M.inCrisis = true;
                    M.crises++;
                    if (moraleRng.F01() < BreakChance * (0.6f + 0.8f * severity)) Desert(t, at, routGroup);
                }
                else if (M.inCrisis && time > M.crisisUntil) M.inCrisis = false;
            }
        }

        /// <summary>Is side <paramref name="t"/> losing the war, and a fight round a group of
        /// its soldiers going badly right now? <paramref name="at"/> is that fight, the
        /// group is left in routGroup; <paramref name="severity"/> 0..1 how badly it is losing.</summary>
        bool Crisis(int t, out Vector2 at, out float severity, out string why)
        {
            at = default;
            severity = 0f;
            routGroup.Clear();

            // The war: its army and Mech against theirs.
            float own = SideStrength(t), foe = SideStrength(1 - t);
            if (foe < 600f || own > 0.55f * foe) { why = $"strength {own:0} vs {foe:0}"; return false; }

            // The last minute: lost much more than it dealt.
            float lost = 0f, dealt = 0f;
            foreach (var l in losses)
                if (l.team == t) lost += l.value; else dealt += l.value;
            if (lost < 400f || lost < 2f * dealt) { why = $"exchange {lost:0} lost / {dealt:0} dealt"; return false; }

            // A fight going badly: soldiers of its under fire, where it is outgunned and
            // its own have been dying.
            // The fight: the thickest knot of its soldiers under fire (two fights at once must
            // not average to empty ground between them).
            moraleScratch.Clear();
            foreach (var u in units)
            {
                if (u == null || u.dying || u.team != t || u.deserted || !u.def.IsArmy) continue;
                if (time - u.lastDamagedT <= 5f) moraleScratch.Add(u);
            }
            int hit = 0;
            foreach (var a in moraleScratch)
            {
                Vector2 sum = Vector2.zero;
                int k = 0;
                foreach (var b in moraleScratch)
                    if ((b.pos - a.pos).sqrMagnitude < 25f * 25f) { sum += b.pos; k++; }
                if (k > hit) { hit = k; at = sum / k; }
            }
            if (hit < 3) { why = $"{hit} under fire together"; return false; }
            float mine = 0f, theirs = 0f;
            foreach (var u in UnitsNear(at, 32f))
            {
                if (u.dying || u.team > 1 || u.Untargetable || u.deserted) continue;
                float v = u.mech != null ? 1500f * (0.5f + 0.5f * Saturate(u.hp / Mathf.Max(1f, u.MaxHp)))
                        : u.def.building ? (u.def.Armed ? 250f : 0f) : Defs.ArmyValue(u.Type);
                if (u.Type == UnitType.Worker) v = 0f;
                if (u.team == t)
                {
                    mine += v;
                    if (u.def.IsArmy && u.Complete && (u.pos - at).sqrMagnitude < 28f * 28f) routGroup.Add(u);
                }
                else theirs += v;
            }
            if (routGroup.Count < 3 || theirs < 1.6f * Mathf.Max(1f, mine)) { why = $"fight {mine:0} vs {theirs:0}, {routGroup.Count} there"; return false; }
            int fallen = 0;
            foreach (var l in losses)
                if (l.team == t && l.army && time - l.t < 20f && (l.pos - at).sqrMagnitude < 35f * 35f) fallen++;
            if (fallen < 2) { why = $"{fallen} fallen there"; return false; }

            severity = Saturate((foe / Mathf.Max(1f, own) - 1.8f) / 3f);
            why = $"CRISIS strength {own:0} vs {foe:0}, fight {mine:0} vs {theirs:0}, {fallen} fallen";
            return true;
        }

        /// <summary>A side's fighting strength: its army, and its Mech by its hull (never
        /// under half: a Mech walking home to be mended has not left the war).</summary>
        float SideStrength(int t)
        {
            float v = 0f;
            foreach (var u in units)
            {
                if (u == null || u.dying || u.team != t || u.deserted) continue;
                if (u.mech != null) { if (u.mech.Landed) v += 1500f * (0.5f + 0.5f * Saturate(u.hp / Mathf.Max(1f, u.MaxHp))); }
                else if (u.def.IsArmy && u.Complete) v += Defs.ArmyValue(u.Type);
            }
            return v;
        }

        // ------------------------------------------------------------ breaking
        /// <summary>A few of <paramref name="group"/> (fighting round <paramref name="at"/>)
        /// throw down their oath and run for the camp. Returns how many did. Public for
        /// the morale trials (Editor/MoraleTrials), which may ask for <paramref name="count"/>.</summary>
        public int Desert(int t, Vector2 at, List<Unit> group, int count = 0)
        {
            moraleScratch.Clear();
            foreach (var u in group)
                if (Unit.Live(u) && u.team == t && !u.deserted && u.def.IsArmy && u.Complete && u.rank < 2) moraleScratch.Add(u);
            if (moraleScratch.Count == 0) return 0;
            int n = Mathf.Clamp(Mathf.RoundToInt(group.Count * 0.15f), 1, 4);
            if (count > 0) n = count;
            n = Mathf.Min(n, moraleScratch.Count);

            var M = morale[t];
            EnsureCamp(t);
            int troopers = 0, maulers = 0, skimmers = 0;
            Unit first = null;
            for (int k = 0; k < n; k++)
            {
                // By weight: riflemen before crews, the hurt before the whole, the green
                // before those with a stripe.
                float total = 0f;
                foreach (var u in moraleScratch) total += BreakWeight(u);
                float pick = moraleRng.F01() * total;
                Unit chosen = moraleScratch[moraleScratch.Count - 1];
                foreach (var u in moraleScratch)
                {
                    pick -= BreakWeight(u);
                    if (pick <= 0f) { chosen = u; break; }
                }
                moraleScratch.Remove(chosen);
                MakeDeserter(chosen);
                if (first == null) first = chosen;
                if (chosen.Type == UnitType.Trooper) troopers++;
                else if (chosen.Type == UnitType.Mauler) maulers++;
                else skimmers++;
            }
            M.episodes++;
            M.deserted += n;
            M.lastEpisode = time;
            Raise(new GameEvent
            {
                kind = GameEventKind.Desertion, team = t, unit = first, type = first.Type, index = n,
                pos = map.Ground(at), end = map.Ground(M.camp), text = Who(troopers, maulers, skimmers)
            });
            return n;
        }

        static float BreakWeight(Unit u)
        {
            float k = u.Type == UnitType.Trooper ? 1f : u.Type == UnitType.Skimmer ? 0.7f : 0.35f;
            k *= 1.6f - Saturate(u.hp / Mathf.Max(1f, u.MaxHp));
            if (u.rank == 1) k *= 0.35f;
            return k;
        }

        /// <summary>"A Trooper is", "3 Troopers and a Mauler are": who is deserting.</summary>
        static string Who(int troopers, int maulers, int skimmers)
        {
            var parts = new List<string>(3);
            void Add(int c, string name) { if (c > 0) parts.Add(c == 1 ? $"a {name}" : $"{c} {name}s"); }
            Add(troopers, "Trooper");
            Add(maulers, "Mauler");
            Add(skimmers, "Skimmer");
            string s = parts.Count == 1 ? parts[0] : string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
            s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return s + (troopers + maulers + skimmers == 1 ? " is" : " are");
        }

        void MakeDeserter(Unit u)
        {
            var M = morale[u.team];
            u.deserted = true;
            u.fleeing = true;
            u.order = Order.Move;
            u.target = null;
            u.harvestNode = null;
            u.buildTarget = null;
            u.orderPos = CampSlot(u.team, M.deserters.Count + M.executed);
            u.MoveTo(u.orderPos);
            M.deserters.Add(u);
        }

        /// <summary>The camp: the corner of the walkable ground behind the side's base (the
        /// reachable point nearest the map's corner there) -- near the base but not in it:
        /// at least CampNear from its middle and CampClear from any of its structures, and
        /// dry. Where that point fails (cliffs close the corner off near the base, or the
        /// side has built out there), rings round the base are tried from the corner's
        /// bearing outward (0, +-15 ... +-90 degrees). The bases sit 40-46 m from the
        /// walkable corner on many maps (cliff rims), so CampNear cannot be much more.</summary>
        void EnsureCamp(int t)
        {
            var M = morale[t];
            if (M.campSet) return;
            Vector2 home = map.StartPos(t);
            float s = MapSize;
            Vector2 corner = new Vector2(home.x < s * 0.5f ? CampInset : s - CampInset, home.y < s * 0.5f ? CampInset : s - CampInset);
            Vector2 nearest = NearestReachable(home, corner);
            M.campSet = true;
            M.camp = nearest;
            if (CampFits(t, home, nearest)) return;
            Vector2 toCorner = corner - home;
            float bearing = Mathf.Atan2(toCorner.y, toCorner.x);
            float[] radii = { CampNear + 5f, CampNear + 15f, CampNear + 25f, CampNear + 35f };
            for (int step = 0; step <= 12; step++)
            {
                float a = bearing + (step + 1) / 2 * (step % 2 == 1 ? 1f : -1f) * 15f * Mathf.Deg2Rad;
                foreach (float r in radii)
                {
                    if (r > CampFar) continue;
                    Vector2 p = home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!map.InBounds(p, 8f)) continue;
                    Vector2 q = NearestReachable(home, p);
                    if ((q - p).sqrMagnitude > 4f * 4f || !CampFits(t, home, q)) continue;
                    M.camp = q;
                    return;
                }
            }
        }

        bool CampFits(int t, Vector2 home, Vector2 q) =>
            (q - home).magnitude >= CampNear && (q - home).magnitude <= CampFar &&
            map.WaterDepth(q) <= 0.3f && !StructureWithin(t, q, CampClear);

        bool StructureWithin(int t, Vector2 p, float clear)
        {
            foreach (var u in units)
                if (u != null && !u.dying && u.team == t && u.def.building && (u.pos - p).magnitude - u.def.radius < clear) return true;
            return false;
        }

        /// <summary>Its place in the camp: a loose huddle round the middle.</summary>
        Vector2 CampSlot(int t, int i)
        {
            var M = morale[t];
            float a = i * 2.39996f + t * 1.1f;
            float r = 2.2f + 1.6f * Mathf.Sqrt(i);
            return NearestReachable(M.camp, M.camp + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }

        /// <summary>For the trials and the debug view: where side <paramref name="t"/>'s
        /// camp is (choosing it if no one has deserted yet).
        /// <paramref name="fresh"/>: choose it again (the camp check, after regenerating the map).</summary>
        public Vector2 DeserterCamp(int t, bool fresh = false)
        {
            if (fresh) morale[t].campSet = false;
            EnsureCamp(t);
            return morale[t].camp;
        }
    }
}
