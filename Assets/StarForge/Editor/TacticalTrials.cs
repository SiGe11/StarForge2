// TacticalTrials.cs -- checks for the new combat's shots and sight (Scripts/World/GameWorld.Tactical.cs).
// Each turns TacticalRules.On on for the match it starts, so call them from a fresh Play at the menu,
// and turns it off again. Targets: ../Scripts/World/CLAUDE.md, "Tactical combat".
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class TacticalTrials
    {
        static string Stage<T>(string name) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(name + "Trial").AddComponent<T>();
            return $"staging; call {name}Report in about a minute";
        }

        static string Report<T>() where T : BugTrial
        {
            var r = Object.FindAnyObjectByType<T>();
            return r == null ? "no trial running" : r.report;
        }

        /// <summary>Hit rate by range, posture and cover, and 1x against 8x with the guns really firing.
        /// Target: falls with range, with a lower body and behind cover; 1x and 8x agree within chance.</summary>
        public static string Hits() => Stage<TacticalHitsTrial>("Hits");
        public static string HitsReport() => Report<TacticalHitsTrial>();
        /// <summary>The open-ground table alone, field by field, with what else took the rounds.</summary>
        public static string HitsFields() { TacticalHitsTrial.FieldsOnly = true; return Stage<TacticalHitsTrial>("Hits"); }
        /// <summary>The guns really firing at one man crouching 25 m off, known to them: behind a rock, at
        /// its edge leaning out, and in the open. Target: no shots when nothing shows, leaning well under the open.</summary>
        public static string Cover() => Stage<TacticalCoverTrial>("Cover");
        public static string CoverReport() => Report<TacticalCoverTrial>();

        /// <summary>A soldier behind a ridge 20 m away is not seen, one in the open at 20 m is; it fires
        /// nothing at what it cannot reach. Target: unseen on every field, 0 shots from behind the ridge.</summary>
        public static string Ridge() => Stage<TacticalRidgeTrial>("Ridge");
        public static string RidgeReport() => Report<TacticalRidgeTrial>();

        /// <summary>From how far a standing soldier sees another, by posture and in the woods.
        /// Target, standing still (0.85 of the walking figure): standing to 38 m, crouching to 27 m,
        /// prone to 15 m; about half that in thick woods; seen within 4 m however it lies.</summary>
        public static string Stealth() => Stage<TacticalStealthTrial>("Stealth");
        public static string StealthReport() => Report<TacticalStealthTrial>();

        /// <summary>What the sight rules cost with 100 soldiers a side standing in sight of each other,
        /// and the fog the player gets from it. Target outside a battle: under 0.5 ms a frame on average.</summary>
        public static string Cost() => Stage<TacticalCostTrial>("Cost");
        public static string CostReport() => Report<TacticalCostTrial>();

        /// <summary>Six in cover against eight in the open: six soldiers among rocks and trunks, told nothing,
        /// eight attack-moving at them over open ground, both thinking, real rounds and hit points, on the
        /// same fixed fields of one map (UseSeed1000 first). Then both plain, doing no thinking, and six that
        /// think against eight who do not and feel no fire. Target: the six win 4 of 5 with both thinking.</summary>
        public static string Fight() => Stage<TacticalFightTrial>("Fight");
        public static string FightReport() => Report<TacticalFightTrial>();

        /// <summary>Squads and reports. Men near each other are one squad, apart two. Eight attack-moving at six:
        /// how often more than half of a squad in contact is moving. A hurried Move runs at 6 m/s, a plain one
        /// walks at 3.5. Four against ten: a fall-back report; four held by a Mauler: an air-support report, and
        /// clicking selects them. Target: at most half moving in 95% of frames; each report once.</summary>
        public static string Squads() => Stage<TacticalSquadsTrial>("Squads");
        public static string SquadsReport() => Report<TacticalSquadsTrial>();

        /// <summary>Eight attack-moving over empty ground: the man ahead, the rest and what each is about.</summary>
        public static string SquadWalk() => Stage<TacticalSquadWalkTrial>("SquadWalk");
        public static string SquadWalkReport() => Report<TacticalSquadWalkTrial>();

        /// <summary>At the menu: does the title's row hold the combat button, and does it switch and say so?</summary>
        public static string TitleRow()
        {
            var doc = Object.FindAnyObjectByType<UnityEngine.UIElements.UIDocument>();
            var root = doc != null ? doc.rootVisualElement : null;
            var b = root != null ? UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(root, "combatButton") : null;
            var quit = root != null ? UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(root, "quitButton") : null;
            if (b == null || quit == null) return "no combat button";
            string first = b.text;
            var invoke = typeof(UnityEngine.UIElements.Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            invoke?.Invoke(b.clickable, new object[] { null });
            string on = $"{b.text} (On {TacticalRules.On})";
            invoke?.Invoke(b.clickable, new object[] { null });
            return $"combat {b.worldBound}, quit {quit.worldBound}, row {b.parent.worldBound}, panel {root.worldBound}; text {first} -> {on} -> {b.text} (On {TacticalRules.On})";
        }

        /// <summary>Orders over his own judgement. Under fire and in cover: Move leaves it and gets there,
        /// Hold stays and shoots, the same attack-move asked again every half second stays in cover. Idle and
        /// hit, he stays idle and fights from where he is. Pinned flat, a Move is obeyed once it eases.
        /// Target: all of those; a new order in under 1 s.</summary>
        public static string Orders() => Stage<TacticalOrdersTrial>("Orders");
        public static string OrdersReport() => Report<TacticalOrdersTrial>();

        /// <summary>Soldiers of both sides in each stance, moving and firing, close up (Temp/rig_look_*.png),
        /// then one shot dead lying down and one on his feet. Judge by reading the pictures.</summary>
        public static string RigLook() => Stage<TacticalRigLookTrial>("RigLook");
        public static string RigLookReport() => Report<TacticalRigLookTrial>();
        /// <summary>The fight's A/B on twelve fields, to tell the two apart (no both-thinking pass).</summary>
        public static string FightWide() { TacticalFightTrial.Fields = 12; return Stage<TacticalFightTrial>("Fight"); }

        /// <summary>The map the next Play draws: for runs that must compare. ClearSeed puts it back.</summary>
        public static string UseSeed1000() { StarForge.Game.MatchSettings.mapSeed = 1000; return "next Play on map 1000"; }
        public static string ClearSeed() { StarForge.Game.MatchSettings.mapSeed = 0; return "map seed free"; }

        /// <summary>How long steady fire takes to pin a man: one and then three rifles at a soldier holding
        /// in the open at 25 m. Target: about 5 s from three, and the brave later than the timid.</summary>
        public static string Pin() => Stage<TacticalPinTrial>("Pin");
        public static string PinReport() => Report<TacticalPinTrial>();
        /// <summary>A whole AI-against-AI match under the new rules at 8x, in the Editor (no screen taken).
        /// Target: 0 exceptions, both sides fighting; read the log for the exceptions.</summary>
        public static string Match() => Stage<TacticalMatchTrial>("Match");
        public static string MatchReport() => Report<TacticalMatchTrial>();
        /// <summary>The same with both armies walking at each other, then split by a ridge with a spotter
        /// neither way: every soldier walks a line to every enemy in reach. Target: under 0.5 ms a frame.</summary>
        public static string CostMoving() { TacticalCostTrial.Moving = true; return Stage<TacticalCostTrial>("Cost"); }

        /// <summary>A structure sees out of itself: fog cells against its sight's circle, and a lone Sentinel
        /// fires at a soldier 16 m off. Target: at least 70% of the circle on open ground, shots over 0.</summary>
        public static string Structures() => Stage<TacticalStructuresTrial>("Structures");
        public static string StructuresReport() => Report<TacticalStructuresTrial>();

        /// <summary>What a round does per hit, through the same call a real round uses (the Mech by its
        /// definition: it cannot be stood up alone). Target: 16 on a rifleman, what the old round's 7
        /// was on everything else; a Digger its worker bonus on top.</summary>
        public static string Damage() => Stage<TacticalDamageTrial>("Damage");
        public static string DamageReport() => Report<TacticalDamageTrial>();

        /// <summary>Walls and ground it cannot reach. Behind its own Garrison, attack-moving, with a friend
        /// spotting the enemy past it: it walks round or takes another, next to no rounds on its own wall.
        /// Sent at an enemy behind a rise with a long way round that gets there: it walks it and fires,
        /// without giving up. Attack-moving away from two enemies on ground with no path, known through a
        /// friend, in reach of the gun and just past it: it gives them up within seconds and gets where it
        /// was sent, or stands firing if a round reaches them; then one of them comes out into the open
        /// and it answers at once. Target: those, and the first shot back within a second.</summary>
        public static string Walls() => Stage<TacticalWallsTrial>("Walls");
        public static string WallsReport() => Report<TacticalWallsTrial>();
    }

    public abstract class TacticalTrial : BugTrial
    {
        protected Unit Soldier(int team, Vector2 at)
        {
            var u = Put(UnitType.Trooper, team, at);
            u.hp = 1e6f;   // little enough that a round still shows on it
            world.CmdHold(new List<Unit> { u });
            return u;
        }

        protected Vector3 Eye(Vector2 at) => map.Ground(at) + Vector3.up * 1.7f;
        protected Vector3 Chest(Vector2 at) => map.Ground(at) + Vector3.up * 1f;

        protected bool WalkClear(Vector2 p) =>
            map.InBounds(p, 12f) && map.WaterDepth(p) < -0.3f && world.Walkable(p, 1f) &&
            (p - map.StartPos(0)).magnitude > 45f && (p - map.StartPos(1)).magnitude > 45f;

        /// <summary>A straight run of open, nearly level ground this long, nothing in the way of a round.</summary>
        protected bool FindRange(System.Random rng, float length, out Vector2 at, out Vector2 dir)
        {
            at = dir = default;
            var ore = new List<Vector2>();
            foreach (var u in world.units) if (u != null && u.def.neutral) ore.Add(u.pos);
            for (int tries = 0; tries < 4000; tries++)
            {
                var a = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var b = a + d * length;
                if (!WalkClear(a) || !WalkClear(b) || !WalkClear(a + d * length * 0.5f)) continue;
                bool byOre = false;
                foreach (var o in ore) if ((o - a).sqrMagnitude < 12f * 12f) { byOre = true; break; }
                if (byOre) continue;
                if (world.Sight.FirstStop(Eye(a), Eye(b) + (Eye(b) - Eye(a)).normalized * 8f, world.Plants, world.boulders) < 1f) continue;
                if (world.Sight.FirstStop(Chest(a), Chest(b), world.Plants, world.boulders) < 1f) continue;
                if (Mathf.Abs(map.HeightAt(a) - map.HeightAt(b)) > 1.5f) continue;
                // Level all the way: a spawn is put on the NavMesh under it, and a terrace on the way
                // puts the soldier metres below his mark with the ground in the line.
                bool level = true;
                float h0 = map.HeightAt(a), h1 = map.HeightAt(b);
                for (float x = 2.5f; x < length && level; x += 2.5f)
                    if (Mathf.Abs(map.HeightAt(a + d * x) - Mathf.Lerp(h0, h1, x / length)) > 0.5f) level = false;
                if (!level) continue;
                if (world.Sight.WoodsAt(a) > 0.01f || world.Sight.WoodsAt(b) > 0.01f) continue;
                at = a; dir = d;
                return true;
            }
            return false;
        }

        /// <summary>The new rules are read when a match begins: only a match this trial started has them.</summary>
        protected IEnumerator TacticalMatch()
        {
            TacticalRules.On = true;
            yield return EnsureMatch();
            TacticalRules.On = false;
            if (Ready && !world.Tactical) { report = "this match began under the old rules: stop, play and call again at the menu"; world = null; }
        }
    }

    public sealed class TacticalHitsTrial : TacticalTrial
    {
        const int Shots = 3000;
        public static bool FieldsOnly;

        static string Pct(int a, int b) => b == 0 ? "-" : $"{100f * a / b:0}%";

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(2718);
            HoldStatics();
            float[] ranges = { 5f, 10f, 15f, 20f, 25f, 30f, 35f };
            Posture[] postures = { Posture.Stand, Posture.Crouch, Posture.Prone };
            var hit = new int[ranges.Length, postures.Length];
            var all = new int[ranges.Length, postures.Length];
            int fields = 0;
            for (int f = 0; f < 5; f++)
            {
                if (!FindRange(rng, 36f, out var a, out var d)) continue;
                fields++;
                var shooter = Soldier(0, a);
                shooter.noMind = true;
                shooter.posture = Posture.Stand;
                for (int r = 0; r < ranges.Length; r++)
                {
                    var target = Soldier(1, a + d * ranges[r]);
                    target.noMind = true;
                    yield return null;
                    for (int p = 0; p < postures.Length; p++)
                    {
                        target.posture = postures[p];
                        int h = world.ProbeShots(shooter, target, Shots, out int other, out int blocked);
                        hit[r, p] += h;
                        all[r, p] += Shots;
                        if (FieldsOnly && p == 0)
                            sb.AppendLine($"  field {f} {ranges[r]:0} m at {(target.pos - shooter.pos).magnitude:0.0} m: {Pct(h, Shots)}, others {other}, blocked {blocked}, shooter speed {shooter.Velocity.magnitude:0.0}, target speed {target.Velocity.magnitude:0.0}, rise {target.Ground.y - shooter.Ground.y:0.0} m");
                    }
                    Remove(target);
                    yield return null;
                }
                ClearSpawned();
                yield return Wait(1.5f);
            }
            sb.AppendLine($"open ground, {fields} fields, {Shots} rounds each case, soldier standing still, target still (target: falls with range, and with a lower body):");
            for (int r = 0; r < ranges.Length; r++)
                sb.AppendLine($"  {ranges[r],2:0} m: standing {Pct(hit[r, 0], all[r, 0])}, crouching {Pct(hit[r, 1], all[r, 1])}, prone {Pct(hit[r, 2], all[r, 2])}");
            if (FieldsOnly) { FieldsOnly = false; report = sb.ToString(); ReleaseStatics(); yield break; }

            // Behind a boulder: the line from the shooter runs through the rock's middle, the target hard behind it.
            int rocks = 0;
            var open = new int[postures.Length]; var cover = new int[postures.Length]; int coverAll = 0;
            foreach (var b in new List<Boulder>(world.boulders))
            {
                if (rocks >= 5 || b == null || b.smashed) continue;
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                for (int tries = 0; tries < 8; tries++)
                {
                    ang += 0.8f;
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var from = b.Pos + d * 20f;
                    var behind = b.Pos - d * (b.Radius + 0.6f);
                    if (!WalkClear(from) || !world.Walkable(behind, 0.3f)) continue;
                    if (world.Sight.FirstStop(Eye(from), b.Pos.x * Vector3.right + b.Pos.y * Vector3.forward + Vector3.up * (map.HeightAt(b.Pos) + 1f), world.Plants, null) < 1f) continue;
                    var shooter = Soldier(0, from);
                    var target = Soldier(1, behind);
                    shooter.noMind = target.noMind = true;
                    yield return null;
                    for (int p = 0; p < postures.Length; p++)
                    {
                        target.posture = postures[p];
                        cover[p] += world.ProbeShots(shooter, target, Shots);
                    }
                    coverAll += Shots;
                    rocks++;
                    ClearSpawned();
                    yield return Wait(1.2f);
                    break;
                }
            }
            sb.AppendLine($"behind a boulder at about 20 m, {rocks} rocks, against the open at 20 m (target: well under the open):");
            int r20 = System.Array.IndexOf(ranges, 20f);
            for (int p = 0; p < postures.Length; p++)
                sb.AppendLine($"  {postures[p]}: {Pct(cover[p], coverAll)} (open {Pct(hit[r20, p], all[r20, p])})");

            // The guns really firing, at 1x and 8x: a shot is decided when fired, so the speed must not show.
            foreach (float scale in new[] { 1f, 8f, 1f, 8f })
            {
                if (!FindRange(rng, 21f, out var a, out var d)) { sb.AppendLine("no range found"); break; }
                var shooter = Soldier(0, a);
                var target = Soldier(1, a + d * 20f);
                shooter.noMind = target.noMind = true;
                int shots0 = world.ShotsFired, hits0 = world.ShotsHit;
                Time.timeScale = scale;
                yield return Wait(35f);
                Time.timeScale = 3f;
                int shots = world.ShotsFired - shots0, hits = world.ShotsHit - hits0;
                sb.AppendLine($"real firing at {scale:0}x, 20 m, 35 s of match: {hits}/{shots} = {Pct(hits, shots)} (each side fires; both counted)");
                ClearSpawned();
                yield return Wait(1.2f);
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalRidgeTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(1618);
            HoldStatics();
            int fields = 0, unseen = 0, seen = 0, hiddenShots = 0, controlShots = 0;
            for (int tries = 0; tries < 6000 && fields < 6; tries++)
            {
                var a = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var b = a + d * 20f;
                var c = a - d * 20f;
                if (!WalkClear(a) || !WalkClear(b) || !WalkClear(c)) continue;
                // Behind a real rise: its top well over a line from eye to head, and nothing else in it.
                if (world.Sight.FireClear(Eye(a), map.Ground(b) + Vector3.up * 1.7f, new List<Boulder>())) continue;
                if (!world.Sight.Clear(Eye(a), map.Ground(c) + Vector3.up * 1.6f) || !world.Sight.Clear(Eye(a), map.Ground(c) + Vector3.up * 1.2f)) continue;
                fields++;
                // Plain soldiers: this is sight's rule alone, not what they would do about it.
                var viewer = Soldier(0, a);
                var hidden = Soldier(1, b);
                var control = Soldier(1, c);
                viewer.noMind = hidden.noMind = control.noMind = true;
                int h = 0, k = 0;
                ListenWhileHeld(e =>
                {
                    if (e.kind != GameEventKind.Fire) return;
                    if (e.unit == hidden) h++;
                    if (e.unit == control) k++;
                });
                yield return Wait(3f);
                ListenWhileHeld(null);
                bool hiddenUnseen = !world.Sees(0, hidden) && !hidden.visibleToPlayer;
                bool controlSeen = world.Sees(0, control) && control.visibleToPlayer;
                if (hiddenUnseen) unseen++;
                if (controlSeen) seen++;
                hiddenShots += h; controlShots += k;
                sb.AppendLine($"  field {fields}: at ({a.x:0},{a.y:0}), hidden {(hiddenUnseen ? "unseen" : "SEEN")} at {(hidden.pos - viewer.pos).magnitude:0.0} m, control {(controlSeen ? "seen" : "UNSEEN")} at {(control.pos - viewer.pos).magnitude:0.0} m, shots from behind the ridge {h}, from the open {k}, viewer on {(viewer.target == control ? "control" : viewer.target == hidden ? "HIDDEN" : "nothing")}");
                ClearSpawned();
                yield return Wait(2f);
            }
            sb.Insert(0, $"{fields} fields: hidden unseen on {unseen}, control seen on {seen}, {hiddenShots} shots from behind the ridge, {controlShots} from the open (target: unseen on all, seen on all, 0 from behind)\n");
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalStealthTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(31415);
            HoldStatics();
            float[] ranges = { 10f, 15f, 20f, 25f, 30f, 35f, 40f, 44f };
            Posture[] postures = { Posture.Stand, Posture.Crouch, Posture.Prone };
            if (FindRange(rng, 44.5f, out var a, out var d))
            {
                var viewer = Soldier(0, a);
                var watched = Soldier(1, a + d * 10f);
                viewer.noMind = watched.noMind = true;
                // Guns held: firing gives a soldier away, which is another check.
                viewer.cooldown = watched.cooldown = 1e6f;
                foreach (var p in postures)
                {
                    watched.posture = p;
                    sb.Append($"  {p} in the open, no gun fired:");
                    foreach (float r in ranges)
                    {
                        watched.agent.Warp(map.Ground(a + d * r));
                        watched.firedAt = -99f;
                        watched.target = null;
                        watched.Spot(0, 0f, null);
                        yield return Wait(1.6f);
                        sb.Append($" {r:0} m {(world.Sees(0, watched) ? "seen" : "no")}");
                    }
                    sb.AppendLine();
                }
                ClearSpawned();
                yield return Wait(1f);
            }
            else sb.AppendLine("no open range 44 m long found");

            // In the woods: a soldier in the thickest cell found, a looker 25 m off with a clear line.
            float bestW = 0f; Vector2 woods = default, look = default;
            for (int tries = 0; tries < 3000; tries++)
            {
                var b = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                if (!WalkClear(b)) continue;
                float w = world.Sight.WoodsAt(b);
                if (w <= bestW) continue;
                for (float ang = 0f; ang < 6.2f; ang += 0.4f)
                {
                    var c = b + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 12f;
                    if (WalkClear(c) && world.Sight.Clear(Eye(c), Eye(b))) { bestW = w; woods = b; look = c; break; }
                }
            }
            if (bestW > 0f)
            {
                var v = Soldier(0, look);
                var t = Soldier(1, woods);
                v.noMind = t.noMind = true;
                v.cooldown = t.cooldown = 1e6f;
                sb.AppendLine($"  in the woods (density {bestW:0.00}), looked at from 12 m:");
                foreach (var p in postures)
                {
                    t.posture = p;
                    t.firedAt = -99f;
                    t.Spot(0, 0f, null);
                    yield return Wait(1.6f);
                    sb.AppendLine($"    {p}: conceal {world.Conceal(t):0.00}, reach {45f * world.Conceal(t):0} m, {(world.Sees(0, t) ? "seen" : "not seen")}");
                }
                ClearSpawned();
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalMatchTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            StarForge.Game.MatchSettings.spectate = true;
            StarForge.Game.MatchSettings.aiMemory = false;
            TacticalRules.On = true;
            var boot = StarForge.Game.GameBootstrap.Instance;
            world = GameWorld.Instance;
            map = MapInfo.Instance;
            if (boot == null || world == null || world.running) { TacticalRules.On = false; report = "call this from a fresh Play at the menu"; yield break; }
            boot.StartMatch();
            TacticalRules.On = false;
            yield return new WaitForSecondsRealtime(1.5f);
            Time.timeScale = 8f;
            float hurt = 0f;
            System.Action<GameEvent> hits = e => { if (e.kind == GameEventKind.UnderAttack) hurt++; };
            world.Event += hits;
            while (world.running && world.winner < 0 && world.time < 480f) yield return null;
            world.Event -= hits;
            int[] alive = new int[2];
            foreach (var u in world.units) if (u != null && !u.dying && u.team < 2) alive[u.team]++;
            Time.timeScale = 1f;
            StarForge.Game.MatchSettings.spectate = false;
            report = $"match under the new rules {world.Tactical}, {world.time:0} s, winner {world.winner}: {world.ShotsFired} rounds, {world.ShotsHit} struck ({100f * world.ShotsHit / Mathf.Max(1, world.ShotsFired):0}%), {hurt:0} under-attack alarms, units left {alive[0]} and {alive[1]}, kills {world.factions[0].killed} and {world.factions[1].killed}; squads now {world.Squads.Count}, {60f * (world.SquadsMade[0] + world.SquadsMade[1]) / Mathf.Max(1f, world.time):0.0} formed a minute, reports {world.ReportsMade[0]} and {world.ReportsMade[1]} ({60f * (world.ReportsMade[0] + world.ReportsMade[1]) / Mathf.Max(1f, world.time):0.0} a minute both sides, target about 2 or fewer a side)";
        }
    }

    public sealed class TacticalRigLookTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            Time.timeScale = 1f;
            var rng = new System.Random(11);
            if (!FindRange(rng, 30f, out var a, out var d)) { report = "no range found"; ReleaseStatics(); yield break; }
            var side = new Vector2(-d.y, d.x);
            var rigCam = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            rigCam.JumpTo(a + d * 2f);
            rigCam.ZoomTo(22f);
            var men = new List<Unit>();
            Posture[] stance = { Posture.Stand, Posture.Crouch, Posture.Prone };
            for (int team = 0; team < 2; team++)
                for (int i = 0; i < 3; i++)
                {
                    var still = Soldier(team, a + side * (i * 3f - 3f) + d * (team * 6f));
                    still.noMind = true; still.posture = stance[i]; still.cooldown = 1e6f; men.Add(still);
                }
            for (int i = 0; i < 3; i++)
            {
                var walker = Soldier(0, a + side * (i * 3f - 3f) - d * 8f);
                walker.noMind = true; walker.posture = stance[i]; walker.cooldown = 1e6f; men.Add(walker);
                world.CmdMove(new List<Unit> { walker }, walker.pos + d * 22f, false);
            }
            for (float t = 0f; t < 2.5f; t += Time.unscaledDeltaTime) { rigCam.JumpTo(a + d * 2f); rigCam.ZoomTo(22f); yield return null; }
            ScreenCapture.CaptureScreenshot("Temp/rig_look_stances.png", 2);
            yield return new WaitForSecondsRealtime(1f);
            // The still ones fire at each other: guns live, each side's stances known.
            foreach (var m in men) m.cooldown = 0f;
            yield return new WaitForSecondsRealtime(1.2f);
            ScreenCapture.CaptureScreenshot("Temp/rig_look_firing.png", 2);
            yield return new WaitForSecondsRealtime(1f);
            // One of each dies: the standing one topples as a ragdoll, the prone one stays flat.
            foreach (var m in men) if (Unit.Live(m) && m.team == 1 && m.posture != Posture.Crouch) world.Damage(m, 1e7f, m.pos - d, null);
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot("Temp/rig_look_dead.png", 2);
            yield return new WaitForSecondsRealtime(1f);
            ClearSpawned();
            report = "pictures in Temp/rig_look_stances.png, _firing.png, _dead.png";
            ReleaseStatics();
        }
    }

    public sealed class TacticalOrdersTrial : TacticalTrial
    {
        /// <summary>A soldier in cover, in the open three rifles' sights: he gets there himself.</summary>
        IEnumerator InCover(System.Random rng, System.Action<Unit> then)
        {
            foreach (var b in new List<Boulder>(world.boulders))
            {
                if (b == null || b.smashed || b.Radius < 0.9f) continue;
                float ang0 = (float)rng.NextDouble() * 6.28f;
                for (float ang = ang0; ang < ang0 + 6.2f; ang += 0.8f)
                {
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var from = b.Pos + d * 25f;
                    var away = b.Pos - d * 16f;
                    var side = new Vector2(-d.y, d.x);
                    if (!WalkClear(from) || !WalkClear(away) || !WalkClear(b.Pos - d * (b.Radius + 2f))) continue;
                    if (world.Sight.FirstStop(Eye(from), Eye(b.Pos + d * (b.Radius + 0.3f)), world.Plants, null) < 1f) continue;
                    for (int r = 0; r < 3; r++) Soldier(0, from + side * (r - 1) * 3f).noMind = true;
                    var man = Soldier(1, b.Pos - d * (b.Radius + 2f));
                    man.order = Order.Idle;
                    float t0 = world.time;
                    while (world.time - t0 < 15f && man.mode != SoldierMode.InCover) yield return null;
                    coverGot = man.mode == SoldierMode.InCover;
                    goal = away;
                    then(man);
                    yield break;
                }
            }
            coverGot = false;
            then(null);
        }

        bool coverGot;
        Vector2 goal;
        Unit got;

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(2024);
            HoldStatics();

            yield return InCover(rng, u => got = u);
            if (got != null)
            {
                var man = got;
                world.CmdMove(new List<Unit> { man }, goal, false);
                float t0 = world.time, left = -1f, there = -1f;
                while (world.time - t0 < 25f)
                {
                    if (left < 0f && man.mode != SoldierMode.InCover) left = world.time - t0;
                    if ((man.pos - goal).magnitude < 4f) { there = world.time - t0; break; }
                    yield return null;
                }
                sb.AppendLine($"  Move from cover: in cover first {coverGot}, left it after {left:0.0} s, {(there >= 0f ? $"there after {there:0.0} s" : $"NOT there, {(man.pos - goal).magnitude:0} m off, order {man.order}, mode {man.mode}, moving {man.Moving}")}");
                ClearSpawned();
                yield return Wait(1.5f);
            }

            yield return InCover(rng, u => got = u);
            if (got != null)
            {
                var man = got;
                var at = man.pos;
                world.CmdHold(new List<Unit> { man });
                int shots = 0;
                ListenWhileHeld(e => { if (e.kind == GameEventKind.Fire && e.unit == man) shots++; });
                yield return Wait(12f);
                ListenWhileHeld(null);
                sb.AppendLine($"  Hold in cover: in cover first {coverGot}, moved {(man.pos - at).magnitude:0.0} m in 12 s, {shots} shots, mode {man.mode}, order {man.order}");
                ClearSpawned();
                yield return Wait(1.5f);
            }

            yield return InCover(rng, u => got = u);
            if (got != null)
            {
                var man = got;
                var at = man.pos;
                world.CmdMove(new List<Unit> { man }, goal, true);
                float t0 = world.time;
                int inCover = 0, frames = 0;
                float last = t0;
                while (world.time - t0 < 15f)
                {
                    if (world.time - last > 0.5f) { last = world.time; world.CmdMove(new List<Unit> { man }, goal, true); }
                    frames++;
                    if (man.mode == SoldierMode.InCover || man.mode == SoldierMode.ToCover || man.mode == SoldierMode.Pinned) inCover++;
                    yield return null;
                }
                sb.AppendLine($"  The same attack-move asked again every 0.5 s, in contact: under cover or pinned {100f * inCover / Mathf.Max(1, frames):0}% of 15 s, moved {(man.pos - at).magnitude:0.0} m, mode {man.mode}");
                ClearSpawned();
                yield return Wait(1.5f);
            }

            yield return InCover(rng, u => got = u);
            if (got != null)
            {
                var man = got;
                var at = man.pos;
                world.Damage(man, 5f, man.pos + Vector2.up, null);
                yield return Wait(6f);
                sb.AppendLine($"  Idle and hit, in cover: order {man.order} (target Idle), mode {man.mode}, moved {(man.pos - at).magnitude:0.0} m in 6 s");
                ClearSpawned();
                yield return Wait(1.5f);
            }

            // Pinned flat in the open, told to move: he goes when it eases.
            if (FindRange(rng, 30f, out var a, out var dir))
            {
                var man = Soldier(1, a);
                var far = a - dir * 12f;
                man.Suppress(1.5f);
                yield return Wait(0.6f);
                string first = man.mode.ToString();
                if (WalkClear(far))
                {
                    world.CmdMove(new List<Unit> { man }, far, false);
                    float t0 = world.time, freed = -1f, there = -1f;
                    while (world.time - t0 < 30f)
                    {
                        if (freed < 0f && man.mode != SoldierMode.Pinned) freed = world.time - t0;
                        if ((man.pos - far).magnitude < 4f) { there = world.time - t0; break; }
                        yield return null;
                    }
                    sb.AppendLine($"  Move while pinned: {first} at the order, up after {freed:0.0} s, {(there >= 0f ? $"there after {there:0.0} s" : $"NOT there, {(man.pos - far).magnitude:0} m off, order {man.order}, mode {man.mode}")}");
                }
                ClearSpawned();
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalSquadWalkTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(77);
            HoldStatics();
            for (int f = 0; f < 4; f++)
            {
                if (!FindRange(rng, 53f, out var w, out var wd)) break;
                var side = new Vector2(-wd.y, wd.x);
                var eight = new List<Unit>();
                for (int i = 0; i < 8; i++) eight.Add(Put(UnitType.Trooper, 0, w + side * (i - 3.5f) * 2.5f));
                yield return null;
                world.CmdMove(eight, w + wd * 50f, true);
                yield return Wait(8f);
                var q = eight[0].squad;
                sb.Append($"  field {f}, at ({w.x:0},{w.y:0}), {(q != null ? q.men.Count : 0)} in the squad, contact {q?.contact}:");
                foreach (var u in eight)
                    sb.Append($" [{Vector2.Dot(u.pos - w, wd):0} m {(u == q?.point ? "ahead " : "")}{u.order} {u.mode} {u.posture} {u.Velocity.magnitude:0.0} m/s{(u.target != null ? " at " + u.target.Type : "")}]");
                sb.AppendLine();
                ClearSpawned();
                yield return Wait(1.5f);
            }
            // Sent after one enemy 38 m off, just out of reach: stops for his turn lose no one the order.
            for (int f = 0; f < 3; f++)
            {
                if (!FindRange(rng, 40f, out var a, out var d)) break;
                var side = new Vector2(-d.y, d.x);
                var eight = new List<Unit>();
                for (int i = 0; i < 8; i++) { var u = Put(UnitType.Trooper, 0, a + side * (i - 3.5f) * 2.5f); u.hp = 1e6f; eight.Add(u); }
                var target = Soldier(1, a + d * 38f);
                yield return null;
                world.CmdAttack(eight, target);
                int lostAt = 0, stops = 0;
                bool[] wasMoving = new bool[8];
                float t0 = world.time;
                while (world.time - t0 < 20f)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        bool m = eight[i].Moving;
                        if (wasMoving[i] && !m) stops++;
                        wasMoving[i] = m;
                    }
                    yield return null;
                }
                foreach (var u in eight) if (u.order != Order.Attack || u.target != target) lostAt++;
                sb.AppendLine($"  sent after one 38 m off, 20 s: {lostAt} of 8 lost the order or the target (target 0), {stops} stops, nearest {NearestTo(eight, target):0.0} m, enemy hit {1e6f - target.hp:0}");
                ClearSpawned();
                yield return Wait(1.5f);
            }
            report = sb.ToString();
            ReleaseStatics();
        }

        static float NearestTo(List<Unit> men, Unit target)
        {
            float best = float.MaxValue;
            foreach (var u in men) best = Mathf.Min(best, (u.pos - target.pos).magnitude);
            return best;
        }
    }

    public sealed class TacticalSquadsTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(303);
            HoldStatics();

            // Forming: five together, three 50 m off.
            if (FindRange(rng, 52f, out var a, out var d))
            {
                var side = new Vector2(-d.y, d.x);
                var five = new List<Unit>(); var three = new List<Unit>();
                for (int i = 0; i < 5; i++) five.Add(Soldier(0, a + side * (i - 2) * 2f));
                for (int i = 0; i < 3; i++) three.Add(Soldier(0, a + d * 50f + side * (i - 1) * 2f));
                yield return Wait(1.5f);
                bool oneA = five.TrueForAll(u => u.squad == five[0].squad && u.squad != null);
                bool oneB = three.TrueForAll(u => u.squad == three[0].squad && u.squad != null);
                sb.AppendLine($"  forming: five together one squad {oneA} ({five[0].squad?.men.Count}), three apart one squad {oneB} ({three[0].squad?.men.Count}), the same squad {five[0].squad == three[0].squad} (target true, true, false)");
                // Two sent off together leave and are their own.
                var two = new List<Unit> { five[0], five[1] };
                world.CmdMove(two, a - d * 20f, false);
                yield return Wait(0.2f);
                yield return Wait(1.5f);
                sb.AppendLine($"  sent off, 1.7 s on: the two together {two[0].squad == two[1].squad}, {two[0].squad?.men.Count} in it; the three left behind {five[2].squad?.men.Count} (target true, 2, 3)");
                // A whole squad and a fresh man sent together: the squad stays, with what it remembers.
                var before = three[0].squad;
                before.reportedAt = world.time;
                float told = before.reportedAt;
                var fresh = Soldier(0, a + d * 50f + side * 4f);
                var four = new List<Unit>(three) { fresh };
                world.CmdMove(four, a + d * 30f, false);
                sb.AppendLine($"  a squad and a fresh man sent: the same squad {three[0].squad == before}, he in it {fresh.squad == before}, it remembers {before.reportedAt == told} (target true, true, true)");
                ClearSpawned();
                yield return Wait(1.5f);
            }

            // Hurry against a plain walk.
            foreach (bool hurried in new[] { false, true })
            {
                if (!FindRange(rng, 32f, out var r, out var e)) break;
                var man = Soldier(0, r);
                yield return Wait(0.5f);
                world.CmdMove(new List<Unit> { man }, r + e * 30f, false, hurried);
                float t0 = world.time, top = 0f;
                while (world.time - t0 < 12f && (man.pos - (r + e * 30f)).magnitude > 4f) { top = Mathf.Max(top, man.Velocity.magnitude); yield return null; }
                float took = world.time - t0;
                yield return Wait(1.5f);
                sb.AppendLine($"  {(hurried ? "hurried" : "plain")} Move: 26 m in {took:0.0} s, top speed {top:0.0} m/s, afterwards order {man.order}, hurry {man.hurry}");
                ClearSpawned();
                yield return Wait(1.2f);
            }

            // The walk up: a man goes ahead, the rest close behind; a man alone goes crouched.
            if (FindRange(rng, 53f, out var w, out var wd))
            {
                var side = new Vector2(-wd.y, wd.x);
                var eight = new List<Unit>();
                for (int i = 0; i < 8; i++) eight.Add(Put(UnitType.Trooper, 0, w + side * (i - 3.5f) * 2.5f));
                yield return null;
                world.CmdMove(eight, w + wd * 50f, true);
                yield return Wait(4f);
                var q = eight[0].squad;
                var p = q?.point;
                float lead = 0f, t0 = world.time;
                var was = new Vector2[8];
                for (int i = 0; i < 8; i++) was[i] = eight[i].pos;
                Vector2 pointWas = p != null ? p.pos : default;
                yield return Wait(4f);
                float rest = 0f, restN = 0f;
                for (int i = 0; i < 8; i++) if (eight[i] != p) { rest += (eight[i].pos - was[i]).magnitude; restN++; if (p != null) lead = Mathf.Max(lead, Vector2.Dot(p.pos - eight[i].pos, wd)); }
                sb.AppendLine($"  the walk up: {(p != null ? (p.pos - pointWas).magnitude / (world.time - t0) : 0f):0.0} m/s the man ahead, {rest / Mathf.Max(1f, restN) / (world.time - t0):0.0} the rest, {lead:0.0} m ahead (target 3.5, 2.8)");
                ClearSpawned();
                yield return Wait(1.5f);
            }
            if (FindRange(rng, 32f, out var alone, out var aloneDir))
            {
                var man = Put(UnitType.Trooper, 0, alone);
                yield return null;
                world.CmdMove(new List<Unit> { man }, alone + aloneDir * 30f, true);
                yield return Wait(4f);
                sb.AppendLine($"  a man alone on an attack-move: {man.posture}, {man.Velocity.magnitude:0.0} m/s (target Crouch, 2.3)");
                ClearSpawned();
                yield return Wait(1.5f);
            }

            // Hurried under fire: out of cover at once; pinned he stays down until it eases.
            for (int run = 0; run < 4; run++)
            {
                if (!FindRange(rng, 26f, out var a2, out var d2)) break;
                var side = new Vector2(-d2.y, d2.x);
                var man = Put(UnitType.Trooper, 0, a2);
                man.hp = 1e6f;
                for (int r = 0; r < 3; r++) Soldier(1, a2 + d2 * 25f + side * (r - 1) * 2f).noMind = true;
                float t0 = world.time;
                while (world.time - t0 < 12f && man.mode == SoldierMode.Free) yield return null;
                var wasIn = man.mode;
                world.CmdMove(new List<Unit> { man }, a2 - d2 * 25f, false, true);
                float t1 = world.time, ran = -1f;
                while (world.time - t1 < 10f && ran < 0f)
                {
                    if (man.Velocity.magnitude > 4.5f) ran = world.time - t1;
                    yield return null;
                }
                sb.AppendLine($"  hurried while {wasIn}: running after {(ran < 0f ? "more than 10" : ran.ToString("0.0"))} s, suppression {man.suppression:0.00} (target: at once, or pinned until it eases)");
                ClearSpawned();
                yield return Wait(1.5f);
            }

            // Turns: eight sent at six on the attack, then eight sent through them on a plain Move.
            foreach (bool attack in new[] { true, false })
            {
                int over = 0, frames = 0, worst = 0, turns = 0, turnsMoved = 0, there = 0, sent = 0, squadCount = 0;
                float gained = 0f;
                float[] wideOf = new float[2]; int[] wideCount = new int[2];
                int shotsFrom = world.ShotsFired;
                for (int f = 0; f < 3; f++)
                {
                    if (!FindRange(rng, 53f, out var c, out var e)) break;
                    var side = new Vector2(-e.y, e.x);
                    var six = new List<Unit>(); var eight = new List<Unit>();
                    for (int i = 0; i < 6; i++) { var u = Put(UnitType.Trooper, 0, c + side * (i - 2.5f) * 2.5f); u.hp = 400f; six.Add(u); }
                    for (int i = 0; i < 8; i++) { var u = Put(UnitType.Trooper, 1, c + e * 52f + side * (i - 3.5f) * 2.5f); u.hp = 400f; eight.Add(u); }
                    yield return null;
                    Vector2 goal = attack ? c : c - e * 2f;
                    world.CmdMove(eight, goal, attack);
                    var groups = new HashSet<Squad>();
                    foreach (var u in eight) if (u.squad != null) groups.Add(u.squad);
                    squadCount = Mathf.Max(squadCount, groups.Count);
                    float t0 = world.time, overSince = -1f;
                    int lastTeam = -1;
                    bool movedThisTurn = false;
                    float startOff = (eight[0].pos - goal).magnitude;
                    while (world.time - t0 < 40f)
                    {
                        Squad q = null;
                        foreach (var u in eight) if (Unit.Live(u) && u.squad != null) { q = u.squad; break; }
                        if (q == null) break;
                        if (q.contact && q.men.Count >= 2)
                        {
                            int moving = 0;
                            foreach (var u in q.men) if (u.Velocity.magnitude > 0.5f) moving++;
                            frames++;
                            worst = Mathf.Max(worst, moving);
                            if (moving > 0) movedThisTurn = true;
                            // Longer than a halted man takes to brake.
                            if (moving > q.men.Count / 2) { if (overSince < 0f) overSince = world.time; if (world.time - overSince > 0.3f) over++; }
                            else overSince = -1f;
                            if (lastTeam >= 0 && q.movingTeam != lastTeam)
                            {
                                turns++;
                                if (movedThisTurn) turnsMoved++;
                                movedThisTurn = false;
                            }
                            lastTeam = q.movingTeam;
                        }
                        yield return null;
                    }
                    foreach (var u in eight)
                    {
                        sent++;
                        if (!Unit.Live(u)) continue;
                        gained += startOff - (u.pos - goal).magnitude;
                        if ((u.pos - goal).magnitude < 8f) there++;
                        float wide = Mathf.Abs(Vector2.Dot(u.pos - c, side));
                        wideOf[u.fireTeam & 1] += wide; wideCount[u.fireTeam & 1]++;
                    }
                    ClearSpawned();
                    yield return Wait(1.5f);
                }
                sb.AppendLine($"  {(attack ? "attack-move" : "plain Move")}: {squadCount} squad(s) among the eight (target 1); {100f * over / Mathf.Max(1, frames):0.0}% of {frames} contact frames over half moving for more than 0.3 s, at most {worst}; men moved in {turnsMoved} of {turns} turns; {gained / Mathf.Max(1, sent):0} m gained a man, {there} of {sent} there after 40 s, {world.ShotsFired - shotsFrom} rounds; off the line at the end, the halves {wideOf[0] / Mathf.Max(1, wideCount[0]):0.0} and {wideOf[1] / Mathf.Max(1, wideCount[1]):0.0} m (target under 5%, moved in most turns)");
            }

            // Reports.
            var heard = new List<string>();
            Unit[] menHeard = null;
            ListenWhileHeld(ev => { if (ev.kind == GameEventKind.SquadReport && ev.team == 0) { heard.Add($"{(SquadReportKind)ev.index} at {world.time:0}: {ev.text}"); menHeard = ev.men; } });
            if (FindRange(rng, 30f, out var f1, out var g))
            {
                var side = new Vector2(-g.y, g.x);
                var four = new List<Unit>();
                for (int i = 0; i < 4; i++) { var u = Put(UnitType.Trooper, 0, f1 + side * (i - 1.5f) * 2f); four.Add(u); }
                for (int i = 0; i < 10; i++) Soldier(1, f1 + g * 26f + side * (i - 4.5f) * 2f);
                var player = Object.FindAnyObjectByType<StarForge.View.PlayerController>();
                float t0 = world.time;
                while (menHeard == null && world.time - t0 < 25f) yield return null;
                float firstAt = world.time - t0;
                if (menHeard != null && player != null)
                {
                    int picked = 0;
                    // The way a player does it: a click on the line in the feed.
                    var hudRoot = Object.FindAnyObjectByType<UnityEngine.UIElements.UIDocument>()?.rootVisualElement;
                    UnityEngine.UIElements.VisualElement line = null;
                    if (hudRoot != null)
                        foreach (var l in UnityEngine.UIElements.UQueryExtensions.Query(hudRoot, null, "comms-line").ToList())
                            if (UnityEngine.UIElements.UQueryExtensions.Q(l, null, "comms-hint") != null) line = l;
                    player.Selection.Clear();
                    if (line != null)
                        using (var click = UnityEngine.UIElements.ClickEvent.GetPooled())
                        {
                            click.target = line;
                            line.SendEvent(click);
                        }
                    yield return null;
                    yield return null;
                    int alive = 0;
                    foreach (var u in menHeard) if (Unit.Live(u)) alive++;
                    picked = player.Selection.Count;
                    sb.AppendLine($"  first report after {firstAt:0.0} s; the line in the feed {(line != null ? "found" : "NOT FOUND")}; a click selects {picked}, {alive} of the {menHeard.Length} who reported are alive (target the same)");
                    player.Selection.Clear();
                }
                while (world.time - t0 < 25f) yield return null;
                sb.AppendLine($"  four against ten, 25 s: {heard.Count} reports (target 1){(heard.Count > 0 ? ": " + heard[0] : "")}");
                ClearSpawned();
                yield return Wait(2f);
            }
            heard.Clear();
            if (FindRange(rng, 30f, out var f2, out var h))
            {
                var side = new Vector2(-h.y, h.x);
                var four = new List<Unit>();
                for (int i = 0; i < 4; i++) { var u = Soldier(0, f2 + side * (i - 1.5f) * 2f); four.Add(u); }
                var tank = Put(UnitType.Mauler, 1, f2 + h * 26f);
                tank.hp = 1e6f;
                world.CmdHold(new List<Unit> { tank });
                yield return Wait(16f);
                sb.AppendLine($"  four held by a Mauler, 16 s: {heard.Count} reports{(heard.Count > 0 ? ": " + heard[0] : "")}");
                ClearSpawned();
            }
            ListenWhileHeld(null);
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalPinTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(555);
            HoldStatics();
            foreach (int rifles in new[] { 1, 3 })
            {
                sb.Append($"  {rifles} rifle{(rifles > 1 ? "s" : "")} at 25 m:");
                for (int run = 0; run < 5; run++)
                {
                    if (!FindRange(rng, 26f, out var a, out var d)) { sb.Append(" no range"); break; }
                    var side = new Vector2(-d.y, d.x);
                    // He fires back, so they keep knowing where he is; his hit points hold whatever he is hit by.
                    var man = Soldier(1, a + d * 25f);
                    for (int r = 0; r < rifles; r++) Soldier(0, a + side * (r - 1) * 2f).noMind = true;
                    float t0 = world.time, pinned = -1f;
                    while (world.time - t0 < 20f)
                    {
                        if (man.mode == SoldierMode.Pinned) { pinned = world.time - t0; break; }
                        yield return null;
                    }
                    sb.Append($" {(pinned >= 0f ? pinned.ToString("0.0") + " s" : "never")} (nerve {man.Courage():0.00})");
                    ClearSpawned();
                    yield return Wait(1.5f);
                }
                sb.AppendLine();
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalFightTrial : TacticalTrial
    {
        public static int Fields = 5;

        /// <summary>Somewhere with things to hide behind within a few metres, and open ground 52 m off.</summary>
        bool FindCoverSite(System.Random rng, out Vector2 at, out Vector2 dir)
        {
            at = dir = default;
            var near = new List<Vector3>(12);
            for (int tries = 0; tries < 6000; tries++)
            {
                var c = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                if (!WalkClear(c)) continue;
                near.Clear();
                foreach (var b in world.boulders)
                    if (b != null && !b.smashed && (b.Pos - c).sqrMagnitude < 10f * 10f) near.Add(new Vector3(b.Pos.x, b.Pos.y, b.Radius));
                world.Sight.TrunksNear(c, 8f, world.Plants, near, 10);
                int rocks = 0;
                foreach (var n in near) if (n.z >= 0.8f) rocks++;
                if (rocks < 2) continue;
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                for (int k = 0; k < 8; k++, ang += 0.8f)
                {
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var e = c + d * 52f;
                    if (!WalkClear(e) || !WalkClear(c + d * 26f)) continue;
                    if (world.Sight.WoodsAt(e) > 0.01f || !world.Sight.Clear(Eye(c), Eye(e))) continue;
                    if (Mathf.Abs(map.HeightAt(c) - map.HeightAt(e)) > 3f) continue;
                    at = c; dir = d;
                    return true;
                }
            }
            return false;
        }

        IEnumerator OneFight(StringBuilder sb, string what, Vector2 c, Vector2 d, bool sixThink, bool eightThink, int[] score)
        {
            var side = new Vector2(-d.y, d.x);
            var six = new List<Unit>(); var eight = new List<Unit>();
            for (int i = 0; i < 6; i++) { var u = Put(UnitType.Trooper, 0, c + side * (i - 2.5f) * 2.5f); u.noMind = !sixThink; six.Add(u); }
            for (int i = 0; i < 8; i++) { var u = Put(UnitType.Trooper, 1, c + d * 52f + side * (i - 3.5f) * 2.5f); u.noMind = !eightThink; eight.Add(u); }
            yield return null;
            world.CmdMove(eight, c, true);
            float t0 = world.time;
            int cover = 0, run = 0, pinned = 0, frames = 0;
            var at0 = (int[])world.RoundsAt.Clone();
            var hit0 = (int[])world.StruckAt.Clone();
            while (world.time - t0 < 120f)
            {
                int a = 0, b = 0;
                foreach (var u in six) if (Unit.Live(u)) { a++; frames++; if (u.mode == SoldierMode.InCover) cover++; if (u.mode == SoldierMode.ToCover) run++; if (u.mode == SoldierMode.Pinned) pinned++; }
                foreach (var u in eight) if (Unit.Live(u)) b++;
                if (a == 0 || b == 0) break;
                yield return null;
            }
            int left = 0, right = 0;
            foreach (var u in six) if (Unit.Live(u)) left++;
            foreach (var u in eight) if (Unit.Live(u)) right++;
            string won = right == 0 && left > 0 ? "six" : left == 0 && right > 0 ? "eight" : "no one";
            if (won == "six") score[0]++; else if (won == "eight") score[1]++;
            string Modes(int team)
            {
                var names = new[] { "free", "running", "in cover", "pinned", "panic" };
                var line = new StringBuilder();
                for (int m = 0; m < 5; m++)
                {
                    int n = world.RoundsAt[team * 5 + m] - at0[team * 5 + m], h = world.StruckAt[team * 5 + m] - hit0[team * 5 + m];
                    if (n > 0) line.Append($" {names[m]} {h}/{n}");
                }
                return line.ToString();
            }
            sb.AppendLine($"  {what}: {won} won in {world.time - t0:0} s, {left} of 6, {right} of 8; the six in cover {100f * cover / Mathf.Max(1, frames):0}%, running {100f * run / Mathf.Max(1, frames):0}%, pinned {100f * pinned / Mathf.Max(1, frames):0}%; rounds at the six{Modes(0)}; at the eight{Modes(1)}");
            ClearSpawned();
            yield return Wait(2f);
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(1944);
            HoldStatics();
            Time.timeScale = 4f;
            var sites = new List<(Vector2 c, Vector2 d)>();
            for (int f = 0; f < Fields; f++)
                if (FindCoverSite(rng, out var c, out var d)) sites.Add((c, d));
            var thinking = new int[2]; var plain = new int[2]; var both = new int[2];
            foreach (var (c, d) in sites) yield return OneFight(sb, "both thinking", c, d, true, true, both);
            foreach (var (c, d) in sites) yield return OneFight(sb, "plain six, plain eight", c, d, false, false, plain);
            if (Fields <= 5) foreach (var (c, d) in sites) yield return OneFight(sb, "thinking six, plain eight", c, d, true, false, thinking);
            Fields = 5;
            sb.Insert(0, $"map {map.seed}, {sites.Count} fields, the same ones each time: six among cover against eight coming over open ground, both thinking, won {both[0]} of {sites.Count} (target 4 of 5); plain six against plain eight {plain[0]}; thinking six against plain eight, who feel no fire at all, {thinking[0]}\n");
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalCoverTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            int rocks = 0;
            var rng = new System.Random(77);
            foreach (var b in new List<Boulder>(world.boulders))
            {
                if (rocks >= 4 || b == null || b.smashed || b.Radius < 0.9f) continue;
                float ang0 = (float)rng.NextDouble() * 6.28f;
                for (float ang = ang0; ang < ang0 + 6.2f; ang += 0.8f)
                {
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var from = b.Pos + d * 25f;
                    var side = new Vector2(-d.y, d.x);
                    if (!WalkClear(from) || !WalkClear(from + side * 3f) || !WalkClear(from - side * 3f)) continue;
                    if (Mathf.Abs(map.HeightAt(from) - map.HeightAt(b.Pos)) > 1.5f) continue;
                    if (world.Sight.FirstStop(Eye(from), Eye(b.Pos + d * (b.Radius + 0.3f)), world.Plants, null) < 1f) continue;
                    foreach (string where in new[] { "behind the rock, not leaning", "at its edge, leaning out", "in the open at the same range" })
                    {
                        var r1 = Soldier(0, from); var r2 = Soldier(0, from + side * 3f); var r3 = Soldier(0, from - side * 3f);
                        Vector2 spot = where[0] == 'b' ? b.Pos - d * (b.Radius + 0.6f) : where[0] == 'a' ? b.Pos - d * (b.Radius + 0.45f) + side * (b.Radius - 0.15f) : b.Pos + side * (b.Radius + 6f);
                        var man = Soldier(1, spot);
                        man.noMind = true;
                        man.posture = Posture.Crouch;
                        man.mode = where[0] == 'i' ? SoldierMode.Free : SoldierMode.InCover;
                        if (where[0] == 'a') { man.leanSide = 1; man.leanDir = side; }
                        man.cooldown = 1e6f;
                        foreach (var r in new[] { r1, r2, r3 }) { r.noMind = true; r.posture = Posture.Stand; }
                        int shots = 0;
                        ListenWhileHeld(e => { if (e.kind == GameEventKind.Fire && (e.unit == r1 || e.unit == r2 || e.unit == r3) && e.unit.target == man) shots++; });
                        man.Spot(0, 1e9f, r1);
                        yield return Wait(20f);
                        ListenWhileHeld(null);
                        int hits = Mathf.RoundToInt((1e6f - man.hp) / world.ShotDamage(r1, man));
                        sb.AppendLine($"  rock {rocks + 1}, radius {b.Radius:0.0}, {where}: {hits}/{shots} from three rifles at {(man.pos - from).magnitude:0} m");
                        ClearSpawned();
                        yield return Wait(1.5f);
                    }
                    rocks++;
                    break;
                }
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalStructuresTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(808);
            HoldStatics();
            foreach (var type in new[] { UnitType.Sentinel, UnitType.Garrison, UnitType.Bunkhouse, UnitType.Workshop })
            {
                if (!FindField(rng, out var at, out var dir)) { sb.AppendLine("no field"); continue; }
                var u = Put(type, 0, at);
                yield return Wait(1f);
                float sight = u.def.sight;
                float circle = Mathf.PI * sight * sight / (world.VisCell * world.VisCell);
                sb.AppendLine($"  {type}: sight {sight:0}, sees {u.viewCount} fog cells of about {circle:0} in its circle ({100f * u.viewCount / circle:0}%)");
                ClearSpawned();
                yield return Wait(1f);
            }
            if (FindField(rng, out var b, out var d))
            {
                // On a clear line: this is whether a tower sees out of itself, not the ground's business.
                for (int k = 0; k < 8 && !world.Sight.Clear(Eye(b) + Vector3.up, Eye(b + d * 16f)); k++) d = new Vector2(d.x * 0.7071f - d.y * 0.7071f, d.x * 0.7071f + d.y * 0.7071f);
                var tower = Put(UnitType.Sentinel, 0, b);
                var rifle = Soldier(1, b + d * 16f);
                rifle.noMind = true;
                int shots = 0, back = 0;
                ListenWhileHeld(e =>
                {
                    if (e.kind != GameEventKind.Fire) return;
                    if (e.unit == tower) shots++;
                    if (e.unit == rifle) back++;
                });
                yield return Wait(10f);
                ListenWhileHeld(null);
                sb.AppendLine($"  a lone Sentinel and a soldier 16 m off, 10 s: tower {shots} shots, soldier {back}, tower sees him {world.Sees(0, rifle)} (target: over 0)");
                ClearSpawned();
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalWallsTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(4242);
            HoldStatics();
            for (int run = 0; run < 3; run++)
            {
                if (!FindField(rng, out var g, out var d)) { sb.AppendLine("no field"); continue; }
                var wall = Put(UnitType.Garrison, 0, g);
                var side = new Vector2(-d.y, d.x);
                var soldier = Soldier(0, g - d * (wall.def.radius + 2.5f));
                var enemy = Soldier(1, g + d * 22f);
                var spotter = Soldier(0, g + d * 8f + side * 14f);
                soldier.cooldown = 0f;
                spotter.cooldown = 1e6f;
                enemy.cooldown = 1e6f;
                yield return Wait(1f);
                world.CmdMove(new List<Unit> { soldier }, g + d * 16f, true);
                int shots = 0, onWall = 0;
                float r2 = (wall.def.radius + 1.2f) * (wall.def.radius + 1.2f);
                ListenWhileHeld(e =>
                {
                    if (e.kind == GameEventKind.Fire && e.unit == soldier) shots++;
                    if (e.kind == GameEventKind.Impact && (new Vector2(e.pos.x, e.pos.z) - wall.pos).sqrMagnitude < r2) onWall++;
                });
                float start = (soldier.pos - enemy.pos).magnitude;
                yield return Wait(20f);
                ListenWhileHeld(null);
                sb.AppendLine($"  behind its own wall {run + 1}: {shots} shots, {onWall} on the wall, {start:0} m from the enemy to {(soldier.pos - enemy.pos).magnitude:0}; its side sees him {world.Sees(0, enemy)}, target {(soldier.target == enemy ? "him" : soldier.target == null ? "none" : "other")}, blocked {soldier.fireBlocked}, woods there {world.Sight.WoodsAt(soldier.pos):0.00}/{world.Sight.WoodsAt(enemy.pos):0.00}, clear eye-to-head {world.Sight.Clear(soldier.Ground + Vector3.up * 1.7f, enemy.Ground + Vector3.up * 1.62f)}");
                ClearSpawned();
                yield return Wait(2f);
            }

            // An enemy behind a rise, and a long way round that gets there.
            int detours = 0;
            for (int tries = 0; tries < 12000 && detours < 3; tries++)
            {
                var a = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float dist = 15f + (float)rng.NextDouble() * 13f;
                var t = a + d * dist;
                if (!WalkClear(a) || !WalkClear(t)) continue;
                if (world.Sight.FireClear(map.Ground(a) + Vector3.up * 1.25f, map.Ground(t) + Vector3.up, world.boulders)) continue;
                var path = new UnityEngine.AI.NavMeshPath();
                if (!UnityEngine.AI.NavMesh.CalculatePath(map.Ground(a), map.Ground(t), GameWorld.GroundAreas, path) || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;
                float len = 0f;
                for (int i = 1; i < path.corners.Length; i++) len += (path.corners[i] - path.corners[i - 1]).magnitude;
                if (len < dist + 14f || len > dist + 70f) continue;
                detours++;
                var walker = Soldier(0, a);
                var enemy = Soldier(1, t);
                var spotter = Soldier(0, t + d * 4f);
                spotter.cooldown = enemy.cooldown = 1e6f;
                yield return Wait(1.5f);
                bool known = world.Sees(0, enemy);
                walker.lineOnlyUntil = 0f;
                world.CmdAttack(new List<Unit> { walker }, enemy);
                int shots = 0;
                float first = -1f, t0 = world.time, gaveUp = -1f;
                ListenWhileHeld(e => { if (e.kind == GameEventKind.Fire && e.unit == walker) { shots++; if (first < 0f) first = world.time - t0; } });
                float limit = len / 3.5f + 12f;
                while (world.time - t0 < limit && shots < 3)
                {
                    if (gaveUp < 0f && walker.lineOnlyUntil > world.time) gaveUp = world.time - t0;
                    yield return null;
                }
                ListenWhileHeld(null);
                sb.AppendLine($"  way round {detours}: {dist:0} m off in a line, {len:0} m by path, known {known}; first shot {(first >= 0f ? $"after {first:0.0} s" : $"NONE in {limit:0} s, order {walker.order}, {(walker.pos - enemy.pos).magnitude:0} m off")}, gave up {(gaveUp >= 0f ? $"at {gaveUp:0.0} s" : "never")}");
                ClearSpawned();
                yield return Wait(2f);
            }
            if (detours == 0) sb.AppendLine("  no rise with a way round found on this map");

            // On the NavMesh, no ground path, no line of fire; two enemies, in the gun's reach and just past it.
            foreach (var band in new[] { new Vector2(14f, 28f), new Vector2(37f, 43f) })
            {
                int cases = 0;
                for (int tries = 0; tries < 8000 && cases < 2; tries++)
                {
                    var a = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float dist = band.x + (float)rng.NextDouble() * (band.y - band.x);
                    var cut = a + d * dist;
                    var goal = a - d * 26f;
                    if (!WalkClear(a) || !WalkClear(goal) || !map.InBounds(cut, 12f) || map.WaterDepth(cut) > -0.3f) continue;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(map.Ground(cut), out var hit, 1f, GameWorld.GroundAreas)) continue;
                    var path = new UnityEngine.AI.NavMeshPath();
                    if (UnityEngine.AI.NavMesh.CalculatePath(map.Ground(a), hit.position, GameWorld.GroundAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;
                    if (!UnityEngine.AI.NavMesh.CalculatePath(map.Ground(a), map.Ground(goal), GameWorld.GroundAreas, path) || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;
                    if (world.Sight.FireClear(map.Ground(a) + Vector3.up * 1.25f, hit.position + Vector3.up, world.boulders)) continue;
                    cases++;
                    var on = new Vector2(hit.position.x, hit.position.z);
                    var walker = Soldier(0, a);
                    var enemy = Soldier(1, on);
                    var enemy2 = Soldier(1, on + new Vector2(-d.y, d.x) * 2.5f);
                    var spotter = Soldier(0, on + d * 3f);
                    spotter.cooldown = enemy.cooldown = enemy2.cooldown = 1e6f;
                    yield return Wait(1.5f);
                    bool known = world.Sees(0, enemy);
                    int shots = 0;
                    ListenWhileHeld(e => { if (e.kind == GameEventKind.Fire && e.unit == walker) shots++; });
                    world.CmdMove(new List<Unit> { walker }, goal, true);
                    float t0 = world.time, arrived = -1f, gaveUp = -1f;
                    while (world.time - t0 < 45f)
                    {
                        if (arrived < 0f && (walker.pos - goal).magnitude < 4f) { arrived = world.time - t0; break; }
                        if (gaveUp < 0f && walker.lineOnlyUntil > world.time) gaveUp = world.time - t0;
                        if (shots >= 3) break;
                        yield return null;
                    }
                    string after = "";
                    if (arrived >= 0f && shots == 0)
                    {
                        // The enemy it gave up on comes out in the open, gun live: it answers.
                        Vector2 openAt = default; bool found = false;
                        for (float b = 0f; b < 6.2f && !found; b += 0.5f)
                        {
                            var o = walker.pos + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 14f;
                            if (world.Walkable(o, 1f) && world.Sight.FireClear(walker.Ground + Vector3.up * 1.25f, map.Ground(o) + Vector3.up, world.boulders) && world.Sight.Clear(walker.Ground + Vector3.up * 1.7f, map.Ground(o) + Vector3.up * 1.6f)) { openAt = o; found = true; }
                        }
                        if (found)
                        {
                            enemy.agent.Warp(map.Ground(openAt));
                            enemy.cooldown = 0f;
                            float t1 = world.time;
                            while (world.time - t1 < 6f && shots == 0) yield return null;
                            after = shots > 0 ? $"; answered the enemy in the open after {world.time - t1:0.0} s" : "; did NOT answer the enemy in the open in 6 s";
                        }
                    }
                    ListenWhileHeld(null);
                    sb.AppendLine($"  no path, {dist:0} m off, known {known}: {(arrived >= 0f ? $"arrived after {arrived:0.0} s" : shots >= 3 ? $"stood firing, a round reached them, {(walker.pos - a).magnitude:0} m from where it set out" : $"NOT there after 45 s, {(walker.pos - goal).magnitude:0} m off, order {walker.order} to {(walker.orderPos - goal).magnitude:0} m from the goal, moving {walker.Moving} at {walker.Velocity.magnitude:0.0} m/s, path {walker.agent.pathStatus} {walker.agent.remainingDistance:0} m left, after {(walker.target == null ? "nothing" : walker.target.Type + " " + walker.Dist(walker.target).ToString("0") + " m off")}, map seed {MapInfo.Instance.seed}")}; gave up {(gaveUp >= 0f ? $"at {gaveUp:0.0} s" : "never")} (a 26 m walk takes 7.4 s){after}");
                    ClearSpawned();
                    yield return Wait(2f);
                }
                if (cases == 0) sb.AppendLine($"  no ground with no path found {band.x:0}-{band.y:0} m off on this map");
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalDamageTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(303);
            HoldStatics();
            if (FindRange(rng, 30f, out var a, out var d))
            {
                var shooter = Soldier(0, a);
                sb.Append($"rounds from a Trooper of rank 0, {shooter.def.damage:0.0} on the definition:");
                int n = 0;
                foreach (var t in new[] { UnitType.Trooper, UnitType.Skimmer, UnitType.Worker, UnitType.Skitter, UnitType.Mauler, UnitType.Ironclad, UnitType.Kestrel, UnitType.Sentinel })
                {
                    var o = Put(t, 1, a + d * (10f + 2.5f * n++) + new Vector2(-d.y, d.x) * 6f);
                    yield return null;
                    sb.Append($" {t} {world.ShotDamage(shooter, o):0.0}");
                }
                var D = shooter.def;
                var m = Defs.Get(UnitType.Mech);
                sb.Append($" Mech {D.damage * (m.Heavy ? D.vsHeavy : D.vsLight) * (m.MechLike ? D.vsMech : 1f):0.0}");
                ClearSpawned();
            }
            else sb.Append("no range found");
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class TacticalCostTrial : TacticalTrial
    {
        public static bool Moving;

        IEnumerator Measure(StringBuilder sb, string what, float seconds)
        {
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(2f);
            double s0 = world.SightMs, l0 = world.LinesMs;
            int frames = 0;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) { frames++; yield return null; }
            Time.timeScale = 3f;
            sb.AppendLine($"  {what}: {world.units.Count} units, sight {(world.SightMs - s0) / frames:0.000} + lines {(world.LinesMs - l0) / frames:0.000} ms a frame over {frames} frames (target under 0.5 together); shots {world.ShotsFired}");
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(99);
            HoldStatics();
            if (!FindRange(rng, 44f, out var a, out var d)) { report = "no open range found"; ReleaseStatics(); yield break; }
            var side = new Vector2(-d.y, d.x);
            if (Moving)
            {
                Moving = false;
                var one = new List<Unit>(); var two = new List<Unit>();
                for (int i = 0; i < 100; i++)
                {
                    one.Add(Soldier(0, a + side * (i % 10 - 5) * 2f - d * (15f + (i / 10) * 2f)));
                    two.Add(Soldier(1, a + d * 44f + side * (i % 10 - 5) * 2f + d * (15f + (i / 10) * 2f)));
                }
                foreach (var u in one) u.cooldown = 1e6f;
                foreach (var u in two) u.cooldown = 1e6f;
                yield return null;
                world.CmdMove(one, a + d * 44f, true);
                world.CmdMove(two, a, true);
                yield return Measure(sb, "two armies walking at each other, guns held", 12f);
                ClearSpawned();
                yield return Wait(2f);
                // Either side of a ridge: no one sees anyone, so everyone asks about everyone.
                bool found = false;
                for (int tries = 0; tries < 6000; tries++)
                {
                    var r = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var e = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var q = r + e * 20f;
                    if (!WalkClear(r) || !WalkClear(q)) continue;
                    var sd = new Vector2(-e.y, e.x);
                    bool hidden = true;
                    foreach (float p1 in new[] { -10f, 0f, 8f })
                        foreach (float p2 in new[] { -10f, 0f, 8f })
                            foreach (float r1 in new[] { 0f, 18f })
                                foreach (float r2 in new[] { 0f, 18f })
                                    if (hidden && world.Sight.Clear(Eye(r + sd * p1 - e * r1), map.Ground(q + sd * p2 + e * r2) + Vector3.up * 1.6f)) hidden = false;
                    if (!hidden) continue;
                    for (int i = 0; i < 100; i++)
                    {
                        Soldier(0, r + sd * (i % 10 - 5) * 2f - e * (i / 10) * 2f);
                        Soldier(1, q + sd * (i % 10 - 5) * 2f + e * (i / 10) * 2f);
                    }
                    yield return Measure(sb, "two armies either side of a ridge, no one seeing anyone", 12f);
                    found = true;
                    break;
                }
                if (!found) sb.AppendLine("  no ridge wide enough on this map");
                ClearSpawned();
                report = sb.ToString();
                ReleaseStatics();
                yield break;
            }
            for (int i = 0; i < 100; i++)
            {
                Soldier(0, a + side * (i % 10 - 5) * 2f - d * (i / 10) * 2f);
                Soldier(1, a + d * 40f + side * (i % 10 - 5) * 2f + d * (i / 10) * 2f);
            }
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(3f);
            double ms0 = world.SightMs;
            int frames = 0;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 15f) { frames++; yield return null; }
            double ms = world.SightMs - ms0;
            Time.timeScale = 3f;
            sb.AppendLine($"{world.units.Count} units, 200 soldiers: sight rules {ms:0.0} ms over {frames} frames = {ms / frames:0.000} ms a frame (target under 0.5); shots {world.ShotsFired}, hits {world.ShotsHit}");
            ClearSpawned();
            report = sb.ToString();
            ReleaseStatics();
        }
    }
}
