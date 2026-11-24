// FeedbackTrials.cs -- checks for the user's play feedback of 2026-11-23 (README "Tactical combat"): rounds that loop round
// a target, small arms at aircraft decided when fired, the Swift's rockets, blind fire and mortars into smoke, and the
// A/B switches (TacticalRules.Legacy*, cleared at every play entry, so call these in play mode). Called from a fresh Play
// at the menu, like TacticalTrials.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class Feedback
    {
        static string Stage<T>(string name) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(name + "Trial").AddComponent<T>();
            return $"staging; call {name}Report in a few minutes";
        }

        static string Report<T>() where T : BugTrial
        {
            var r = Object.FindAnyObjectByType<T>();
            return r == null ? "no trial running" : r.report;
        }

        static string Switches() =>
            $"LegacyRounds={TacticalRules.LegacyRounds} LegacySmoke={TacticalRules.LegacySmoke} LegacyAir={TacticalRules.LegacyAir} LegacyMechPower={TacticalRules.LegacyMechPower} LegacyMechSensors={TacticalRules.LegacyMechSensors} LegacyMechReach={TacticalRules.LegacyMechReach}";

        static void MechAll(bool old) { TacticalRules.LegacyMechPower = TacticalRules.LegacyMechSensors = TacticalRules.LegacyMechReach = old; }

        /// <summary>Every switch to the old rules (until the next play entry), or back to the new.</summary>
        public static string AllOld() { TacticalRules.LegacyRounds = TacticalRules.LegacySmoke = TacticalRules.LegacyAir = true; MechAll(true); return Switches(); }
        public static string AllNew() { TacticalRules.LegacyRounds = TacticalRules.LegacySmoke = TacticalRules.LegacyAir = false; MechAll(false); return Switches(); }
        public static string OldSmoke() { TacticalRules.LegacySmoke = true; return Switches(); }
        public static string NewSmoke() { TacticalRules.LegacySmoke = false; return Switches(); }
        public static string State() => Switches();

        /// <summary>The bare Mech's twelve-design duel (MechDuel.RunBaseFreshAll) under the old power or the new; read
        /// MechDuel.Report. Target (the user, 2026-11-23): mostly against infantry, about 7 Maulers and 8 riflemen.</summary>
        public static string MechDuelOld() { MechAll(true); return MechDuel.RunBaseFreshAll() + " | " + Switches(); }
        public static string MechDuelNew() { MechAll(false); return MechDuel.RunBaseFreshAll() + " | " + Switches(); }
        /// <summary>Power and sensors new, reach old: what the reach alone is worth (read with MechDuelNew).</summary>
        public static string MechDuelNoReach() { MechAll(false); TacticalRules.LegacyMechReach = true; return MechDuel.RunBaseFreshAll() + " | " + Switches(); }
        /// <summary>The Mech's sensors, old senses against new: riflemen 28 m off lying down, in smoke and standing (the
        /// control), seen by its side and the hull its guns took off them in 6 s; a man standing still in deep woods 25 m
        /// off; a man lying behind a boulder 20 m off with one in the open 22 m off. Targets: new sees all three and the
        /// man in the woods; behind a rock he is sensed, the open man hit, at most 3 rounds stopped by cover.</summary>
        public static string MechSensors() => Stage<MechSensorsTrial>("MechSensors");
        public static string MechSensorsReport() => Report<MechSensorsTrial>();

        /// <summary>Real (homing) rounds from every gun that fires them, at men running back and forth and at aircraft,
        /// old rounds and new, at 1x and 3x: how many turned more than 90 degrees in flight (a loop). Target: new 0.</summary>
        public static string RoundLoops() => Stage<RoundLoopsTrial>("RoundLoops");
        public static string RoundLoopsReport() => Report<RoundLoopsTrial>();
        /// <summary>Riflemen and machine gunners at a circling Swift and a hovering Kestrel: hits a shot, old and new; then
        /// how long a Swift circling over six riflemen lives. Target: fewer hits, a longer life.</summary>
        public static string AirRounds() => Stage<AirRoundsTrial>("AirRounds");
        public static string AirRoundsReport() => Report<AirRoundsTrial>();
        /// <summary>A Swift sent at six riflemen, then at a Mauler, 35 s each, old (no rockets) and new.</summary>
        public static string SwiftStrike() => Stage<SwiftStrikeTrial>("SwiftStrike");
        public static string SwiftStrikeReport() => Report<SwiftStrikeTrial>();
        /// <summary>Four riflemen and a Thumper firing on three men 25 m off, then smoke over the three: rounds and bombs
        /// into it in the next 6 s, hits on the men, their suppression, old and new. Target: old 0, new some.</summary>
        public static string BlindFire() => Stage<BlindFireTrial>("BlindFire");
        public static string BlindFireReport() => Report<BlindFireTrial>();
        /// <summary>The player's "Fire at ground" (CmdFireAt): three riflemen and a Thumper ordered at a point 25 m off
        /// with a man hidden in smoke there; rounds and bombs in 12 s (bombs at most 6), hits on him, and a Move order
        /// that ends it.</summary>
        public static string FireAt() => Stage<FireAtTrial>("FireAt");
        public static string FireAtReport() => Report<FireAtTrial>();
    }

    /// <summary>The feedback trials' base: whatever a trial set, the switches are back on the new rules when it goes.</summary>
    public abstract class FeedbackTrial : TacticalTrial
    {
        protected override void OnDestroy()
        {
            TacticalRules.LegacyRounds = TacticalRules.LegacySmoke = TacticalRules.LegacyAir = false;
            TacticalRules.LegacyMechPower = TacticalRules.LegacyMechSensors = TacticalRules.LegacyMechReach = false;
            base.OnDestroy();
        }
    }

    public sealed class MechSensorsTrial : FeedbackTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            var rng = new System.Random(4545);
            var sb = new StringBuilder();
            int fields = 0, newAll = 0;
            // 1. Three men 28 m off on open ground: lying down, standing in smoke, standing (the control).
            for (int f = 0; f < 3; f++)
            {
                if (!FindRange(rng, 34f, out var a, out var d)) continue;
                fields++;
                var side = new Vector2(-d.y, d.x);
                foreach (bool old in new[] { true, false })
                {
                    TacticalRules.LegacyMechSensors = old;
                    world.SmokeClouds.Clear();
                    yield return Wait(2.2f);
                    var mech = world.TrialMech(0, MechParts.Generate((uint)(31 + f)), a, Mathf.Atan2(d.x, d.y), false);
                    spawned.Add(mech);
                    var men = new List<Unit>();
                    string[] what = { "lying down", "standing in smoke", "standing (control)" };
                    for (int i = 0; i < 3; i++)
                    {
                        var m = Put(UnitType.Trooper, 1, a + d * 28f + side * ((i - 1) * 7f));
                        m.hp = 1e6f;
                        m.noMind = true;
                        m.posture = i == 0 ? Posture.Prone : Posture.Stand;
                        men.Add(m);
                    }
                    world.CmdHold(men);
                    world.StartSmoke(men[1].pos);
                    var taken = new float[3];
                    var last = new float[3];
                    for (int i = 0; i < 3; i++) last[i] = men[i].hp;
                    float t0 = world.time;
                    while (world.time - t0 < 6f)
                    {
                        men[0].posture = Posture.Prone;
                        for (int i = 0; i < 3; i++)
                        {
                            if (men[i].hp < last[i]) taken[i] += last[i] - men[i].hp;
                            men[i].hp = last[i] = 1e6f;
                        }
                        if (Unit.Live(mech)) mech.hp = mech.MaxHp;
                        yield return null;
                    }
                    var line = new StringBuilder($"  field {f} {(old ? "old" : "new")} senses:");
                    int seen = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        bool s = world.Sees(0, men[i]);
                        if (s) seen++;
                        line.Append($" {what[i]} seen {s}, {taken[i]:0} hp;");
                    }
                    if (!old && seen == 3) newAll++;
                    sb.AppendLine(line.ToString());
                    ClearSpawned();
                    yield return null;
                }
            }

            // 2. A man standing still in the thickest woods found, the Mech 25 m off.
            float bestW = 0f;
            Vector2 woods = default, look = default;
            for (int tries = 0; tries < 3000; tries++)
            {
                var b = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                if (!WalkClear(b)) continue;
                float w = world.Sight.WoodsAt(b);
                if (w <= bestW) continue;
                for (float ang = 0f; ang < 6.2f; ang += 0.4f)
                {
                    var c = b + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 25f;
                    if (WalkClear(c) && world.Sight.WoodsAt(c) < 0.05f) { bestW = w; woods = b; look = c; break; }
                }
            }
            string woodsLine = "  woods: no thick woods with open ground 25 m off";
            bool woodsNew = false;
            if (bestW > 0f)
            {
                var seenBy = new bool[2];
                foreach (bool old in new[] { true, false })
                {
                    TacticalRules.LegacyMechSensors = old;
                    yield return Wait(1.5f);
                    var to = woods - look;
                    var mech = world.TrialMech(0, MechParts.Generate(31u), look, Mathf.Atan2(to.x, to.y), false);
                    spawned.Add(mech);
                    var m = Put(UnitType.Trooper, 1, woods);
                    m.hp = 1e6f;
                    m.noMind = true;
                    world.CmdHold(new List<Unit> { m });
                    float t0 = world.time;
                    while (world.time - t0 < 4f) { m.hp = 1e6f; if (Unit.Live(mech)) mech.hp = mech.MaxHp; yield return null; }
                    seenBy[old ? 0 : 1] = world.Sees(0, m);
                    ClearSpawned();
                    yield return null;
                }
                woodsNew = seenBy[1];
                woodsLine = $"  woods {bestW:0.00} at 25 m, standing still: old senses seen {seenBy[0]}, new {seenBy[1]}";
            }

            // 3. Behind a boulder: a man lying hard behind a rock 20 m off, another standing in the open 22 m off (inside
            //    every direct gun's reach); the Mech carries only guns cover stops (and flamers, out of reach), so what the
            //    hidden man takes is theirs. They should take the one in the open (rounds stopped by cover, few).
            var coverLines = new StringBuilder();
            int rocks = 0, coverOk = 0, rocksOpen = 0;
            uint designSeed = 0;
            for (uint s = 31; s < 3000 && designSeed == 0; s++)
            {
                var des = MechParts.Generate(s);
                int direct = 0, other = 0;
                foreach (var w in des.weapons)
                {
                    if (w == MechWeapon.Autocannon || w == MechWeapon.Gatling || w == MechWeapon.Laser) direct++;
                    else if (w != MechWeapon.Flamer && w != MechWeapon.FlameTower) other++;
                }
                if (direct >= 2 && other == 0) designSeed = s;
            }
            foreach (var b in new List<Boulder>(world.boulders))
            {
                if (rocks >= 3 || designSeed == 0 || b == null || b.smashed || b.Radius < 0.9f) continue;
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                for (int tries = 0; tries < 8; tries++)
                {
                    ang += 0.8f;
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var from = b.Pos + d * 20f;
                    var behind = b.Pos - d * (b.Radius + 0.6f);
                    var side = new Vector2(-d.y, d.x);
                    var open = from + (-d * Mathf.Cos(0.7f) + side * Mathf.Sin(0.7f)) * 22f;
                    if (!WalkClear(from) || !world.Walkable(behind, 0.3f) || !WalkClear(open)) continue;
                    if ((open - b.Pos).magnitude < 6f || !world.Sight.Clear(Eye(from) + Vector3.up * 3f, Eye(open))) continue;
                    if (!world.Sight.FireClear(Eye(from) + Vector3.up * 2f, Chest(open), world.boulders)) continue;
                    var mech = world.TrialMech(0, MechParts.Generate(designSeed), from, Mathf.Atan2(-d.x, -d.y), false);
                    spawned.Add(mech);
                    var hid = Put(UnitType.Trooper, 1, behind);
                    var shown = Put(UnitType.Trooper, 1, open);
                    var men = new List<Unit> { hid, shown };
                    foreach (var m in men) { m.hp = 1e6f; m.noMind = true; }
                    hid.posture = Posture.Prone;
                    world.CmdHold(men);
                    yield return Wait(0.5f);
                    // The rock must hide him from the guns, or the pick is not tested.
                    bool hiddenFromAll = true;
                    if (Unit.Live(mech))
                        foreach (var g in mech.mech.guns)
                            if (world.MechLineOpen(mech.mech.Muzzle(g), hid, mech)) hiddenFromAll = false;
                    if (!hiddenFromAll) { rocksOpen++; ClearSpawned(); yield return Wait(0.5f); continue; }
                    rocks++;
                    foreach (var m in men) m.hp = 1e6f;
                    int stopped0 = world.RoundsStoppedByCover;
                    var taken = new float[2];
                    float t0 = world.time;
                    while (world.time - t0 < 8f)
                    {
                        hid.posture = Posture.Prone;
                        for (int i = 0; i < 2; i++) { if (men[i].hp < 1e6f) taken[i] += 1e6f - men[i].hp; men[i].hp = 1e6f; }
                        if (Unit.Live(mech)) mech.hp = mech.MaxHp;
                        yield return null;
                    }
                    int stopped = world.RoundsStoppedByCover - stopped0;
                    bool ok = world.Sees(0, hid) && taken[1] > 0f && stopped <= 3;
                    if (ok) coverOk++;
                    coverLines.AppendLine($"  rock {rocks} (r {b.Radius:0.0} m): behind it, prone, 20 m: seen {world.Sees(0, hid)}, {taken[0]:0} hp; in the open 22 m: seen {world.Sees(0, shown)}, {taken[1]:0} hp; rounds stopped by cover {stopped}: {(ok ? "ok" : "FAIL")}");
                    ClearSpawned();
                    yield return Wait(1.2f);
                    break;
                }
            }

            TacticalRules.LegacyMechSensors = false;
            world.SmokeClouds.Clear();
            report = fields == 0 ? "mech sensors done: FAIL, no field found" :
                $"mech sensors done: {fields} fields, new senses saw all three on {newAll} (target all); woods seen with new senses {woodsNew} (target true); " +
                $"behind a rock {coverOk} of {rocks} ok (target all, 1+: the man behind it sensed, the open one hit, at most 3 rounds stopped by cover; design {designSeed}, rocks skipped as not hiding him {rocksOpen})\n" +
                sb + woodsLine + "\n" + coverLines;
            ReleaseStatics();
        }
    }

    public sealed class FireAtTrial : FeedbackTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            var rng = new System.Random(5353);
            var sb = new StringBuilder();
            int fields = 0, ok = 0;
            for (int f = 0; f < 3; f++)
            {
                if (!FindRange(rng, 32f, out var a, out var d)) continue;
                fields++;
                var side = new Vector2(-d.y, d.x);
                var ours = new List<Unit>();
                for (int i = 0; i < 3; i++) { var u = Put(UnitType.Trooper, 0, a + side * ((i - 1) * 2.2f)); u.hp = 1e6f; ours.Add(u); }
                var thumper = Put(UnitType.Thumper, 0, a - d * 6f + side * 4f);
                thumper.hp = 1e6f;
                ours.Add(thumper);
                Vector2 spot = a + d * 25f;
                var hidden = Put(UnitType.Trooper, 1, spot);
                hidden.hp = 1e6f;
                hidden.noMind = true;
                world.CmdHold(new List<Unit> { hidden });
                world.StartSmoke(spot);
                yield return null;
                int s0 = world.BlindShots, l0 = world.BlindLobs;
                world.CmdFireAt(ours, spot);
                float t0 = world.time, taken = 0f, last = hidden.hp;
                while (world.time - t0 < 12.5f)
                {
                    if (hidden.hp < last) taken += last - hidden.hp;
                    hidden.hp = last = 1e6f;
                    foreach (var u in ours) u.hp = 1e6f;
                    yield return null;
                }
                int shots = world.BlindShots - s0, lobs = world.BlindLobs - l0;
                // An enemy walks up to 6 m while they fire at the point: they answer him (fire at ground gives way).
                world.CmdFireAt(ours, spot);
                yield return null;
                var close = Put(UnitType.Trooper, 1, a + d * 6f);
                close.hp = 1e6f;
                close.noMind = true;
                world.CmdHold(new List<Unit> { close });
                float closeTaken = 0f, closeLast = close.hp;
                t0 = world.time;
                while (world.time - t0 < 4f)
                {
                    if (close.hp < closeLast) closeTaken += closeLast - close.hp;
                    close.hp = closeLast = 1e6f;
                    foreach (var u in ours) u.hp = 1e6f;
                    yield return null;
                }
                Remove(close);
                // Hold (H) ends it.
                world.CmdFireAt(ours, spot);
                yield return null;
                world.CmdHold(ours);
                int sh = world.BlindShots;
                t0 = world.time;
                while (world.time - t0 < 3f) { foreach (var u in ours) u.hp = 1e6f; yield return null; }
                int afterHold = world.BlindShots - sh;
                // Ordered again, then moved: the move ends it.
                world.CmdFireAt(ours, spot);
                yield return null;
                world.CmdMove(ours, a - d * 4f, false);
                int s1 = world.BlindShots;
                t0 = world.time;
                while (world.time - t0 < 3f) { foreach (var u in ours) u.hp = 1e6f; yield return null; }
                int afterMove = world.BlindShots - s1;
                int stillOrdered = 0;
                foreach (var u in ours) if (u.fireAtOrdered) stillOrdered++;
                bool good = shots > 0 && lobs > 0 && lobs <= TacticalRules.FireAtLobs && closeTaken > 0f && afterHold == 0 && afterMove == 0 && stillOrdered == 0;
                if (good) ok++;
                sb.AppendLine($"  field {f}: rounds {shots}, bombs {lobs} (at most {TacticalRules.FireAtLobs}), {taken:0} hp on the hidden man; an enemy at 6 m took {closeTaken:0} hp in 4 s; " +
                              $"after Hold rounds {afterHold}, after a Move order rounds {afterMove}, still ordered {stillOrdered}: {(good ? "ok" : "FAIL")}");
                ClearSpawned();
                yield return null;
            }
            report = fields == 0 ? "fire at ground done: FAIL, no field found" : $"fire at ground done: {ok} of {fields} fields ok (target all)\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class RoundLoopsTrial : FeedbackTrial
    {
        sealed class Track { public Vector3 dir; public float turned, age; public string who; }
        readonly Dictionary<Projectile, Track> tracks = new Dictionary<Projectile, Track>();
        readonly HashSet<Projectile> seen = new HashSet<Projectile>();
        readonly List<Projectile> gone = new List<Projectile>();
        readonly Dictionary<string, int> rounds = new Dictionary<string, int>(), loops = new Dictionary<string, int>();
        float longest;

        static bool Homing(Projectile p) =>
            !p.cosmetic && p.kind != 1 && p.kind != 6 && p.kind != 5 && p.kind != 7 && p.kind != GameWorld.ProjLob &&
            !(p.kind >= GameWorld.ProjFrag && p.kind <= GameWorld.ProjFlash);

        void Finish(Track t)
        {
            rounds.TryGetValue(t.who, out int n); rounds[t.who] = n + 1;
            if (t.turned > 90f) { loops.TryGetValue(t.who, out int m); loops[t.who] = m + 1; }
            longest = Mathf.Max(longest, t.age);
        }

        void Watch()
        {
            seen.Clear();
            foreach (var p in world.projectiles)
            {
                if (!p.alive || !Homing(p)) continue;
                seen.Add(p);
                Vector3 v = p.vel.sqrMagnitude > 1e-6f ? p.vel.normalized : Vector3.zero;
                if (!tracks.TryGetValue(p, out var t) || p.age + 1e-4f < t.age)
                {
                    if (t != null) Finish(t);
                    string who = p.shooter != null ? p.shooter.Type.ToString() : "?";
                    if (p.target != null && p.target.def.flying) who += " at air";
                    tracks[p] = t = new Track { dir = v, age = p.age, who = who };
                    continue;
                }
                if (v != Vector3.zero && t.dir != Vector3.zero) t.turned += Vector3.Angle(t.dir, v);
                if (v != Vector3.zero) t.dir = v;
                t.age = p.age;
            }
            gone.Clear();
            foreach (var kv in tracks) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var p in gone) { Finish(tracks[p]); tracks.Remove(p); }
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            var rng = new System.Random(8181);
            if (!FindRange(rng, 30f, out var a, out var d)) { report = "no range found"; ReleaseStatics(); yield break; }
            var side = new Vector2(-d.y, d.x);
            var shooters = new List<Unit>();
            UnitType[] kinds = { UnitType.Prospector, UnitType.Prospector, UnitType.Skimmer, UnitType.Skimmer, UnitType.Lancer, UnitType.Ironclad,
                                 UnitType.Spitter, UnitType.Spitter, UnitType.Trooper, UnitType.Trooper, UnitType.Trooper, UnitType.Gunner, UnitType.Aegis };
            for (int i = 0; i < kinds.Length; i++)
            {
                var u = Put(kinds[i], 0, a + side * ((i - kinds.Length / 2) * 2.2f));
                u.hp = 1e6f;
                shooters.Add(u);
            }
            var tower = Put(UnitType.Sentinel, 0, a - d * 4f + side * 8f);
            tower.hp = 1e6f;
            world.CmdHold(shooters);
            Vector2 far = a + d * 25f;
            var runners = new List<Unit>();
            for (int i = 0; i < 6; i++)
            {
                var m = Put(UnitType.Trooper, 1, far + side * ((i - 2.5f) * 2f));
                m.hp = 1e6f;
                m.noMind = true;
                runners.Add(m);
            }
            var swift = Put(UnitType.Swift, 1, far + d * 6f);
            swift.hp = 1e6f;
            var kestrel = Put(UnitType.Kestrel, 1, far + side * 10f);
            kestrel.hp = 1e6f;
            world.CmdMove(new List<Unit> { swift }, far, false);
            world.CmdMove(new List<Unit> { kestrel }, far + side * 8f, false);
            var sb = new StringBuilder();
            sb.AppendLine($"range at {a}, 13 shooters and a Sentinel, 6 men running 25 m off, a Swift circling over them and a Kestrel");
            int total = 0;
            foreach (bool legacy in new[] { true, false })
                foreach (float scale in new[] { 1f, 3f })
                {
                    TacticalRules.LegacyRounds = legacy;
                    Time.timeScale = scale;
                    tracks.Clear(); rounds.Clear(); loops.Clear(); longest = 0f;
                    int expired0 = world.HomingExpired;
                    float t0 = world.time, flip = 0f, dtSum = 0f;
                    int frames = 0;
                    bool out_ = false;
                    while (world.time - t0 < 20f)
                    {
                        if (world.time >= flip)
                        {
                            flip = world.time + 2.5f;
                            out_ = !out_;
                            for (int i = 0; i < runners.Count; i++)
                                world.CmdMove(new List<Unit> { runners[i] }, far + side * ((i - 2.5f) * 2f) + side * (out_ ? 7f : -7f), false, true);
                        }
                        foreach (var u in shooters) if (u.hp < 1e5f) u.hp = 1e6f;
                        foreach (var u in runners) if (u.hp < 1e5f) u.hp = 1e6f;
                        if (swift.hp < 1e5f) swift.hp = 1e6f;
                        if (kestrel.hp < 1e5f) kestrel.hp = 1e6f;
                        Watch();
                        dtSum += Time.deltaTime; frames++;
                        yield return null;
                    }
                    foreach (var kv in tracks) Finish(kv.Value);
                    tracks.Clear();
                    int r = 0, l = 0;
                    var line = new StringBuilder();
                    foreach (var kv in rounds)
                    {
                        loops.TryGetValue(kv.Key, out int lp);
                        r += kv.Value; l += lp;
                        line.Append($"{kv.Key} {lp}/{kv.Value}, ");
                    }
                    if (!legacy) total += l;
                    sb.AppendLine($"  {(legacy ? "old" : "new")} rounds at {scale:0}x (mean step {1000f * dtSum / Mathf.Max(1, frames):0} ms): looped {l} of {r} homing rounds, ran out of life without a hit {world.HomingExpired - expired0}, longest flight {longest:0.00} s");
                    sb.AppendLine($"    by gun (looped/fired): {line}");
                    Time.timeScale = 1f;
                }
            TacticalRules.LegacyRounds = false;
            report = $"round loops done: new rounds looped {total} (target 0)\n" + sb;
            ClearSpawned();
            ReleaseStatics();
        }
    }

    public sealed class AirRoundsTrial : FeedbackTrial
    {
        int shots;
        readonly HashSet<Unit> firing = new HashSet<Unit>();
        void OnFire(GameEvent e) { if (e.kind == GameEventKind.Fire && e.unit != null && firing.Contains(e.unit)) shots++; }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            ListenWhileHeld(OnFire);
            var rng = new System.Random(9191);
            if (!FindRange(rng, 30f, out var a, out var d)) { report = "no range found"; ReleaseStatics(); yield break; }
            var side = new Vector2(-d.y, d.x);
            var sb = new StringBuilder();
            foreach (var shooterType in new[] { UnitType.Trooper, UnitType.Gunner })
                foreach (var targetType in new[] { UnitType.Swift, UnitType.Kestrel })
                    foreach (bool legacy in new[] { true, false })
                    {
                        TacticalRules.LegacyRounds = legacy;
                        int n = shooterType == UnitType.Trooper ? 6 : 2;
                        var men = new List<Unit>();
                        firing.Clear();
                        for (int i = 0; i < n; i++) { var u = Put(shooterType, 0, a + side * ((i - n / 2f) * 2f)); u.hp = 1e6f; men.Add(u); firing.Add(u); }
                        world.CmdHold(men);
                        var air = Put(targetType, 1, a + d * 18f);
                        air.hp = 1e6f;
                        world.CmdMove(new List<Unit> { air }, targetType == UnitType.Swift ? a : a + d * 14f, false);
                        float t0 = world.time;
                        while (world.time - t0 < 3f) { air.hp = 1e6f; yield return null; }
                        shots = 0;
                        float taken = 0f, last = air.hp;
                        t0 = world.time;
                        while (world.time - t0 < 20f)
                        {
                            if (air.hp < last) taken += last - air.hp;
                            air.hp = last = 1e6f;
                            yield return null;
                        }
                        float per = world.ShotDamage(men[0], air);
                        float hits = per > 0f ? taken / per : 0f;
                        sb.AppendLine($"  {(legacy ? "old" : "new")}: {n} {shooterType} at a {(targetType == UnitType.Swift ? "circling" : "hovering")} {targetType}: {shots} shots, ~{hits:0} hits ({100f * hits / Mathf.Max(1, shots):0}%)");
                        ClearSpawned();
                        yield return null;
                    }
            // A Swift of its own hull circling over six riflemen: how long it lives.
            foreach (bool legacy in new[] { true, false })
            {
                TacticalRules.LegacyRounds = legacy;
                var men = new List<Unit>();
                for (int i = 0; i < 6; i++) { var u = Put(UnitType.Trooper, 0, a + side * ((i - 3) * 2f)); u.hp = 1e6f; men.Add(u); }
                world.CmdHold(men);
                var swift = Put(UnitType.Swift, 1, a + d * 18f);
                world.CmdMove(new List<Unit> { swift }, a, false);
                float t0 = world.time;
                while (world.time - t0 < 60f && Unit.Live(swift)) yield return null;
                sb.AppendLine($"  {(legacy ? "old" : "new")}: a Swift circling over six riflemen {(Unit.Live(swift) ? $"alive after 60 s at {swift.hp:0}" : $"down after {world.time - t0:0.0} s")}");
                ClearSpawned();
                yield return null;
            }
            TacticalRules.LegacyRounds = false;
            report = "air rounds done\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class SwiftStrikeTrial : FeedbackTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            var rng = new System.Random(7373);
            if (!FindRange(rng, 40f, out var a, out var d)) { report = "no range found"; ReleaseStatics(); yield break; }
            var side = new Vector2(-d.y, d.x);
            var sb = new StringBuilder();
            foreach (bool tank in new[] { false, true })
                foreach (bool legacy in new[] { true, false })
                {
                    TacticalRules.LegacyAir = TacticalRules.LegacyRounds = legacy;
                    var foes = new List<Unit>();
                    if (tank) foes.Add(Put(UnitType.Mauler, 1, a + d * 40f));
                    else for (int i = 0; i < 6; i++) foes.Add(Put(UnitType.Trooper, 1, a + d * 40f + side * ((i - 3) * 2.5f)));
                    world.CmdHold(foes);
                    float hp0 = 0f;
                    foreach (var f in foes) hp0 += f.hp;
                    var swift = Put(UnitType.Swift, 0, a);
                    world.CmdAttack(new List<Unit> { swift }, foes[0]);
                    int r0 = world.SwiftRocketsFired;
                    float t0 = world.time;
                    while (world.time - t0 < 35f && Unit.Live(swift))
                    {
                        bool any = false;
                        foreach (var f in foes) if (Unit.Live(f)) { any = true; if (!Unit.Live(swift.target)) world.CmdAttack(new List<Unit> { swift }, f); break; }
                        if (!any) break;
                        yield return null;
                    }
                    float hp1 = 0f;
                    int down = 0;
                    foreach (var f in foes) { if (Unit.Live(f)) hp1 += f.hp; else down++; }
                    sb.AppendLine($"  {(legacy ? "old" : "new")}: a Swift at {(tank ? "a Mauler" : "six riflemen")}: {down} down, {hp0 - hp1:0} hull dealt, {world.SwiftRocketsFired - r0} rockets, the Swift {(Unit.Live(swift) ? $"alive at {swift.hp:0}" : "down")} after {world.time - t0:0} s");
                    ClearSpawned();
                    yield return null;
                }
            TacticalRules.LegacyAir = TacticalRules.LegacyRounds = false;
            report = "swift strikes done\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class BlindFireTrial : FeedbackTrial
    {
        // Each pass on fresh ground with no smoke left from the last (a cloud lasts 20 s and would blind the next pass).
        IEnumerator Fresh(System.Random rng)
        {
            world.SmokeClouds.Clear();
            float t0 = world.time;
            while (world.time - t0 < 2.2f) yield return null;   // SightMap.Refresh re-stamps the leaf cells every 2 s
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            var rng = new System.Random(6262);
            var sb = new StringBuilder();
            int fields = 0, newRounds = 0, oldRounds = 0, mirrorRounds = 0;
            float nearSeen = 0f, nearNow = 0f;
            int leakShots = 0;
            var diag = new StringBuilder();
            for (int f = 0; f < 3; f++)
            {
                // 0 old, 1 new (and the men move 4 m inside the smoke once lost), 2 smoke on our own men, 3 our own smoke 8 m ahead.
                for (int pass = 0; pass < 4; pass++)
                {
                    yield return Fresh(rng);
                    if (!FindRange(rng, 32f, out var a, out var d)) continue;
                    if (pass == 0) fields++;
                    var side = new Vector2(-d.y, d.x);
                    TacticalRules.LegacySmoke = pass == 0;
                    var ours = new List<Unit>();
                    for (int i = 0; i < 4; i++) { var u = Put(UnitType.Trooper, 0, a + side * ((i - 1.5f) * 2.2f)); u.hp = 1e6f; ours.Add(u); }
                    if (pass < 2) { var th = Put(UnitType.Thumper, 0, a - d * 6f + side * 5f); th.hp = 1e6f; ours.Add(th); }
                    world.CmdHold(ours);
                    Vector2 them = a + d * 25f;
                    var foes = new List<Unit>();
                    for (int i = 0; i < 3; i++) { var m = Put(UnitType.Trooper, 1, them + side * ((i - 1) * 1.6f)); m.hp = 1e6f; m.noMind = true; foes.Add(m); }
                    world.CmdHold(foes);
                    // Seen and fired on first, then the smoke comes down.
                    float t0 = world.time;
                    while (world.time - t0 < 4f) { foreach (var m in foes) m.hp = 1e6f; foreach (var u in ours) u.hp = 1e6f; yield return null; }
                    int s0 = world.BlindShots, h0 = world.BlindHits, l0 = world.BlindLobs, e0 = world.BlindEpisodes;
                    if (pass < 2) world.StartSmoke(them);
                    else if (pass == 2) world.StartSmoke(a);
                    else world.StartSmoke(a + d * 8f, 0);
                    float peak = 0f, taken = 0f;
                    float[] last = new float[foes.Count];
                    for (int i = 0; i < foes.Count; i++) last[i] = foes[i].hp;
                    bool moved = false, noted = false;
                    int lastShots = world.BlindShots;
                    var seenAt = new List<Vector2>();
                    foreach (var m in foes) seenAt.Add(m.pos);
                    t0 = world.time;
                    while (world.time - t0 < 6f)
                    {
                        // Deeper into the smoke, across their own line (moved along it, a seen spot sat beside a neighbour's new one).
                        if (pass == 1 && !moved && world.time - t0 > 2.5f)
                        {
                            moved = true;
                            for (int i = 0; i < foes.Count; i++) world.CmdMove(new List<Unit> { foes[i] }, seenAt[i] + d * 4f, false);
                        }
                        if (pass == 1 && !noted && world.time - t0 > 3f)
                        {
                            noted = true;
                            diag.Append($"  field {f}, 3 s into the smoke:");
                            foreach (var u in ours)
                                diag.Append($" [{u.Type} {u.mode} {u.posture} lean {u.leanSide} hiding {u.hiding} target {(u.target != null ? "yes" : "none")} blind {u.FiringBlind}]");
                            diag.AppendLine();
                        }
                        if (pass == 1 && moved && world.BlindShots > lastShots && world.LastBlindAt != Vector2.zero)
                        {
                            float ms = float.MaxValue, mn = float.MaxValue;
                            for (int i = 0; i < foes.Count; i++)
                            {
                                ms = Mathf.Min(ms, (world.LastBlindAt - seenAt[i]).magnitude);
                                mn = Mathf.Min(mn, (world.LastBlindAt - foes[i].pos).magnitude);
                            }
                            nearSeen += ms;
                            nearNow += mn;
                            leakShots++;
                        }
                        lastShots = world.BlindShots;
                        for (int i = 0; i < foes.Count; i++)
                        {
                            if (foes[i].hp < last[i]) taken += last[i] - foes[i].hp;
                            foes[i].hp = last[i] = 1e6f;
                            peak = Mathf.Max(peak, foes[i].suppression);
                        }
                        foreach (var u in ours) u.hp = 1e6f;
                        yield return null;
                    }
                    int shots = world.BlindShots - s0, lobs = world.BlindLobs - l0;
                    string what = pass == 0 ? "old" : pass == 1 ? "new" : pass == 2 ? "mirror, smoke on our own four" : "mirror, our own smoke 8 m ahead";
                    if (pass == 0) oldRounds += shots + lobs;
                    else if (pass == 1) newRounds += shots + lobs;
                    else mirrorRounds += shots + lobs;
                    sb.AppendLine($"  field {f} {what}: episodes {world.BlindEpisodes - e0}, blind rounds {shots}, hits {world.BlindHits - h0}, mortar bombs {lobs}, " +
                                  $"{taken:0} hp taken by the three, their peak suppression {peak:0.00}");
                    ClearSpawned();
                    yield return null;
                }
            }
            TacticalRules.LegacySmoke = false;
            world.SmokeClouds.Clear();
            // Moved 2.5 s after the smoke fell (past the 1.2 s sight hold): each round is aimed at a spot a man was seen at.
            bool leakOk = leakShots > 0 && nearSeen / leakShots < 0.5f && nearNow / leakShots > 2f;
            string leak = leakShots == 0 ? "no blind round after the move" :
                $"after the men moved 4 m deeper into the smoke, blind rounds aimed {nearSeen / leakShots:0.00} m from the nearest spot a man was seen at and {nearNow / leakShots:0.0} m from the nearest man: {(leakOk ? "ok" : "FAIL")} (target under 0.5 and over 2)";
            report = fields == 0 ? "blind fire done: FAIL, no field found" :
                $"blind fire done: {fields} fields, rounds and bombs into the smoke old {oldRounds}, new {newRounds} (target old 0, new some); out of or through our own smoke {mirrorRounds} (target 0); {leak}\n" + sb + diag;
            ReleaseStatics();
        }
    }
}
