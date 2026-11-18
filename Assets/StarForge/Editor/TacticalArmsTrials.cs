// TacticalArmsTrials.cs -- the checks for what a soldier carries besides his gun, and for the other four
// infantry types on the new rules (phase 4). Called from a fresh Play at the menu, like TacticalTrials.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class TacticalArms
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

        /// <summary>Real fights, then enemies mingled with friends. Target: at least 6 thrown; 0 frags landing
        /// with a friend in reach; 0 friends hurt. Then a frag put on a friend on purpose: hurt, no alarm, no kill.</summary>
        public static string Grenades() => Stage<ArmsGrenadesTrial>("Grenades");
        public static string GrenadesReport() => Report<ArmsGrenadesTrial>();

        /// <summary>A cloud between two who see each other. Target: unseen both ways, the ground behind unseen,
        /// no rounds, sight back after it clears; and a man under fire in the open pops his own.</summary>
        public static string SmokeSight() => Stage<ArmsSmokeTrial>("SmokeSight");
        public static string SmokeSightReport() => Report<ArmsSmokeTrial>();

        /// <summary>A flash: in reach stunned, out of reach and behind a rock not.</summary>
        public static string Flash() => Stage<ArmsFlashTrial>("Flash");
        public static string FlashReport() => Report<ArmsFlashTrial>();

        /// <summary>Pistol and knife by type and distance, counted by what was fired.</summary>
        public static string Sidearms() => Stage<ArmsSidearmsTrial>("Sidearms");
        public static string SidearmsReport() => Report<ArmsSidearmsTrial>();

        /// <summary>Spent items come back near a Garrison and not in the field.</summary>
        public static string Restock() => Stage<ArmsRestockTrial>("Restock");
        public static string RestockReport() => Report<ArmsRestockTrial>();

        /// <summary>Magazines: rounds in a cycle, the wait for a reload, the pistol meanwhile.</summary>
        public static string Magazines() => Stage<ArmsMagazinesTrial>("Magazines");
        public static string MagazinesReport() => Report<ArmsMagazinesTrial>();

        /// <summary>Mistakes by rank, and one old hand among greens. Target: falling with rank.</summary>
        public static string Mistakes() => Stage<ArmsMistakesTrial>("Mistakes");
        public static string MistakesReport() => Report<ArmsMistakesTrial>();

        /// <summary>A dead tank and a live one as cover; the hulk lasts while it is used and goes after.</summary>
        public static string Hulks() => Stage<ArmsHulksTrial>("Hulks");
        public static string HulksReport() => Report<ArmsHulksTrial>();

        /// <summary>The Gunner under the new combat: fire, belts, how it pins.</summary>
        public static string Gunner() => Stage<ArmsGunnerTrial>("Gunner");
        public static string GunnerReport() => Report<ArmsGunnerTrial>();

        /// <summary>From a fresh Play with the classic combat: no Gunner to be had. Target: refused, not on the card.</summary>
        public static string GunnerClassic() => Stage<ArmsGunnerClassicTrial>("GunnerClassic");
        public static string GunnerClassicReport() => Report<ArmsGunnerClassicTrial>();

        /// <summary>Pictures: the five types in three stances, a frag, a flash, a cloud (Temp/arms_look_*.png).</summary>
        public static string Look() => Stage<ArmsLookTrial>("Look");
        public static string LookReport() => Report<ArmsLookTrial>();

        /// <summary>The four other types' numbers, their guns, and a shell held for a friend by the target.</summary>
        public static string Types() => Stage<ArmsTypesTrial>("Types");
        public static string TypesReport() => Report<ArmsTypesTrial>();
    }

    public sealed class ArmsGrenadesTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(404);
            HoldStatics();
            int f0 = world.ThrownCount[1], s0 = world.ThrownCount[2], l0 = world.ThrownCount[3], land0 = world.FragLandings;
            int near0 = world.FragFriendAtLanding, burst0 = world.FragFriendAtBurst, hurt0 = world.FriendHurt;
            int fields = 0;
            for (int run = 0; run < 6; run++)
            {
                if (!FindRange(rng, 30f, out var a, out var d)) break;
                fields++;
                var side = new Vector2(-d.y, d.x);
                var six = new List<Unit>(); var eight = new List<Unit>();
                for (int i = 0; i < 6; i++) { var u = Put(UnitType.Trooper, 0, a + side * (i - 2.5f) * 2.5f); u.hp = 500f; six.Add(u); }
                for (int i = 0; i < 6; i++) { var u = Put(UnitType.Trooper, 1, a + d * 22f + side * (i - 2.5f) * 2.5f); u.hp = 500f; eight.Add(u); }
                yield return null;
                world.CmdMove(six, a + d * 8f, true);
                yield return Wait(22f);
                ClearSpawned();
                yield return Wait(2.5f);
            }
            sb.AppendLine($"  {fields} open fights, 22 s: {world.ThrownCount[1] - f0} frags, {world.ThrownCount[2] - s0} smoke, {world.ThrownCount[3] - l0} flash thrown (target 6 or more in all); {world.FragLandings - land0} frags landed, {world.FragFriendAtLanding - near0} with a friend in reach as it landed and {world.FragFriendAtBurst - burst0} as it burst, {world.FriendHurt - hurt0} friends hurt (target 0, 0)");

            int f1 = world.ThrownCount[1], l1 = world.ThrownCount[3], land1 = world.FragLandings;
            int near1 = world.FragFriendAtLanding, hurt1 = world.FriendHurt;
            int mingled = 0;
            for (int run = 0; run < 4; run++)
            {
                if (!FindRange(rng, 24f, out var a, out var d)) break;
                mingled++;
                var side = new Vector2(-d.y, d.x);
                for (int i = 0; i < 4; i++) { var u = Put(UnitType.Trooper, 0, a + side * (i - 1.5f) * 2.5f); u.hp = 500f; }
                for (int i = 0; i < 3; i++)
                {
                    Soldier(1, a + d * 18f + side * ((i - 1f) * 4f));
                    Soldier(0, a + d * 18f + side * ((i - 1f) * 4f + 2f));
                }
                yield return Wait(16f);
                ClearSpawned();
                yield return Wait(2.5f);
            }
            sb.AppendLine($"  {mingled} with friends mixed among the enemy 18 m off, 16 s: {world.ThrownCount[1] - f1} frags, {world.ThrownCount[3] - l1} flash; {world.FragLandings - land1} landed, {world.FragFriendAtLanding - near1} with a friend in reach, {world.FriendHurt - hurt1} friends hurt (target 0, 0)");

            // Thrown onto a rock: it rolls off and still does its work.
            foreach (var rock in new List<Boulder>(world.boulders))
            {
                if (rock == null || rock.smashed || rock.Radius < 1.2f) continue;
                var east = new Vector2(1f, 0f);
                var from = rock.Pos - east * 14f;
                var beside = rock.Pos + east * (rock.Radius + 1.5f);
                if (!WalkClear(from) || !WalkClear(beside)) continue;
                var thrower = Soldier(0, from); thrower.noMind = true;
                var hit = Soldier(1, beside); hit.noMind = true;
                float before = hit.hp;
                yield return null;
                world.Throw(thrower, Thrown.Frag, rock.Pos);
                yield return Wait(3.5f);
                sb.AppendLine($"  a frag thrown at the middle of a {rock.Radius:0.0} m rock, a man {rock.Radius + 1.5f:0.0} m from it: he lost {before - hit.hp:0} hp (target some; it may land on his side or not)");
                ClearSpawned();
                yield return Wait(1f);
                break;
            }

            // An enemy running at a friend: the mortar holds for where he will be.
            int shells = 0, hurtBefore = world.FriendHurt;
            ListenWhileHeld(ev => { if (ev.kind == GameEventKind.Fire && ev.team == 0 && ev.projectileKind == GameWorld.ProjLob) shells++; });
            if (FindRange(rng, 40f, out var g0, out var g1))
            {
                var mortar = Put(UnitType.Thumper, 0, g0); mortar.hp = 1e6f;
                var friend = Soldier(0, g0 + g1 * 22f); friend.noMind = true;
                var runner = Put(UnitType.Trooper, 1, g0 + g1 * 36f); runner.hp = 1e6f; runner.noMind = true;
                world.CmdMove(new List<Unit> { runner }, friend.pos, false, true);
                yield return Wait(8f);
                sb.AppendLine($"  a mortar at an enemy running at a friend 8 s: {shells} shells, friends hurt {world.FriendHurt - hurtBefore} (target 0 hurt)");
                ClearSpawned();
                yield return Wait(1.5f);
            }
            ListenWhileHeld(null);

            // Put on a friend on purpose: the plumbing of a same-side hit.
            if (FindRange(rng, 14f, out var b, out var e))
            {
                var thrower = Soldier(0, b);
                thrower.noMind = true;
                var friend = Put(UnitType.Trooper, 0, b + e * 12f);
                friend.noMind = true;
                friend.hp = 10f;
                int killed = world.factions[1].killed, lost = world.factions[0].lost, hurt = world.FriendHurt, alarms = 0;
                ListenWhileHeld(ev => { if (ev.kind == GameEventKind.UnderAttack && ev.team == 0) alarms++; });
                yield return null;
                world.Throw(thrower, Thrown.Frag, friend.pos);
                yield return Wait(4f);
                ListenWhileHeld(null);
                sb.AppendLine($"  a frag on a friend at 10 hp: he is {(Unit.Live(friend) ? "alive" : "dead")}, friends hurt +{world.FriendHurt - hurt}, the side's losses +{world.factions[0].lost - lost}, enemy kills +{world.factions[1].killed - killed}, alarms {alarms} (target dead, 1, 1, 0, 0)");
                ClearSpawned();
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsSmokeTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(77);
            HoldStatics();
            for (int run = 0; run < 3; run++)
            {
                if (!FindRange(rng, 24f, out var a, out var d)) break;
                var one = Soldier(0, a); one.noMind = true;
                var two = Soldier(1, a + d * 22f); two.noMind = true;
                yield return Wait(2.5f);
                bool before = world.Sees(0, two) && world.Sees(1, one);
                int rounds0 = world.ShotsFired;
                world.StartSmoke(a + d * 11f);
                yield return Wait(2f);
                bool during0 = world.Sees(0, two), during1 = world.Sees(1, one);
                bool ground = world.Visible(0, two.pos);
                int rounds1 = world.ShotsFired;
                yield return Wait(6f);
                int roundsThrough = world.ShotsFired - rounds1;
                yield return Wait(TacticalRules.SmokeLasts - 8f + 3f);
                bool after = world.Sees(0, two) && world.Sees(1, one);
                sb.AppendLine($"  a cloud between two at 22 m: seen before {before}, during {during0}/{during1}, the ground behind in view {ground}, rounds through it in 6 s {roundsThrough}, seen after {after} (target true, false/false, false, 0, true); clouds now {world.SmokeClouds.Count}");
                ClearSpawned();
                yield return Wait(2.5f);
            }
            int popped = 0, runs = 0;
            for (int run = 0; run < 4; run++)
            {
                if (!FindRange(rng, 26f, out var a, out var d)) break;
                runs++;
                var side = new Vector2(-d.y, d.x);
                var man = Put(UnitType.Trooper, 0, a);
                man.hp = 1e6f;
                for (int r = 0; r < 3; r++) Soldier(1, a + d * 24f + side * ((r - 1) * 2f)).noMind = true;
                float t0 = world.time;
                yield return null;
                while (world.time - t0 < 22f && man.smokes > 0 || world.time - t0 < 0.5f) yield return null;
                bool used = man.smokes == 0;
                if (used) popped++;
                float at = world.time - t0;
                int r0 = world.ShotsFired;
                yield return Wait(5f);
                sb.AppendLine($"  under three rifles in the open: smoke {(used ? "popped after " + at.ToString("0.0") + " s" : "not used in 22 s")}, as {man.mode}; rounds at him in the next 5 s {world.ShotsFired - r0}, suppression {man.suppression:0.00}");
                ClearSpawned();
                yield return Wait(2.5f);
            }
            sb.AppendLine($"  popped {popped} of {runs} (target most)");
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsFlashTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(5);
            HoldStatics();
            for (int run = 0; run < 3; run++)
            {
                if (!FindRange(rng, 26f, out var a, out var d)) break;
                var side = new Vector2(-d.y, d.x);
                var thrower = Soldier(0, a); thrower.noMind = true;
                Vector2 at = a + d * 14f;
                var near = Put(UnitType.Trooper, 1, at + side * 3f); near.hp = 1e6f;
                var edge = Put(UnitType.Trooper, 1, at - side * 5.5f); edge.hp = 1e6f;
                var far = Put(UnitType.Trooper, 1, at + d * 10f); far.hp = 1e6f;
                yield return Wait(0.6f);
                world.Throw(thrower, Thrown.Flash, at);
                yield return Wait(GameWorld.ThrowFlight(14f) + TacticalRules.FlashFuse + 0.3f);
                sb.AppendLine($"  3 m off {near.mode} {near.blindUntil - world.time:0.0} s blind, 5.5 m {edge.mode} {edge.blindUntil - world.time:0.0} s, 10 m {far.mode} (target Panic, Panic, not Panic); stunned so far {world.Stunned}");
                ClearSpawned();
                yield return Wait(2f);
            }
            // Behind a rock.
            int rocks = 0;
            foreach (var b in new List<Boulder>(world.boulders))
            {
                if (rocks >= 2 || b == null || b.smashed || b.Radius < 1f) continue;
                var d = new Vector2(1f, 0f);
                var at = b.Pos + d * (b.Radius + 2f);
                var man = b.Pos - d * (b.Radius + 0.8f);
                if (!WalkClear(at) || !WalkClear(man) || !WalkClear(at + d * 12f)) continue;
                rocks++;
                var thrower = Soldier(0, at + d * 12f); thrower.noMind = true;
                var hid = Put(UnitType.Trooper, 1, man); hid.hp = 1e6f;
                yield return Wait(0.6f);
                world.Throw(thrower, Thrown.Flash, at);
                yield return Wait(GameWorld.ThrowFlight(12f) + TacticalRules.FlashFuse + 0.3f);
                sb.AppendLine($"  behind a rock of {b.Radius:0.0} m: {hid.mode} (target not Panic)");
                ClearSpawned();
                yield return Wait(2f);
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsSidearmsTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(31);
            HoldStatics();
            var fired = new Dictionary<int, int>();
            ListenWhileHeld(ev => { if (ev.kind == GameEventKind.Fire && ev.team == 0) { fired.TryGetValue(ev.projectileKind, out int n); fired[ev.projectileKind] = n + 1; } });
            string Say()
            {
                var parts = new List<string>();
                foreach (var kv in fired) parts.Add((kv.Key == GameWorld.ProjPistol ? "pistol" : kv.Key == GameWorld.ProjKnife ? "knife" : kv.Key == GameWorld.ProjLob ? "mortar" : kv.Key == GameWorld.ProjRocket ? "rocket" : kv.Key == GameWorld.ProjSniper ? "long rifle" : kv.Key == 0 ? "rifle" : "kind " + kv.Key) + " " + kv.Value);
                return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
            }
            var cases = new (UnitType type, float at, string want)[]
            {
                (UnitType.Thumper, 5f, "pistol, no mortar"), (UnitType.Thumper, 20f, "mortar, no pistol"),
                (UnitType.Rocketeer, 4f, "pistol, no rocket"), (UnitType.Rocketeer, 20f, "rocket"),
                (UnitType.Marksman, 5f, "pistol"), (UnitType.Marksman, 30f, "long rifle"),
                (UnitType.Scorcher, 14f, "pistol"), (UnitType.Trooper, 1.4f, "knife"), (UnitType.Scorcher, 1.4f, "knife"),
            };
            foreach (var c in cases)
            {
                if (!FindRange(rng, c.at + 4f, out var a, out var d)) { sb.AppendLine($"  {c.type} at {c.at}: no range"); continue; }
                var man = Put(c.type, 0, a);
                man.hp = 1e6f;
                var enemy = Soldier(1, a + d * c.at);
                enemy.noMind = true;
                fired.Clear();
                yield return Wait(8f);
                sb.AppendLine($"  {c.type} with an enemy at {c.at} m, 8 s: {Say()} (target {c.want})");
                ClearSpawned();
                yield return Wait(1.5f);
            }
            // At armour his main gun, not the pistol.
            if (FindRange(rng, 10f, out var m0, out var m1))
            {
                var man = Put(UnitType.Rocketeer, 0, m0);
                man.hp = 1e6f;
                var tank = Put(UnitType.Mauler, 1, m0 + m1 * 6.5f);
                tank.hp = 1e6f;
                world.CmdHold(new List<Unit> { tank });
                tank.cooldown = 1e6f;
                fired.Clear();
                yield return Wait(8f);
                sb.AppendLine($"  Rocketeer with a Mauler close by, 8 s: {Say()} (target rocket, no pistol)");
                ClearSpawned();
                yield return Wait(1.5f);
            }
            // The near one behind a rock, known through a friend; another in the open.
            foreach (var kind in new[] { UnitType.Thumper, UnitType.Marksman })
            {
                bool staged = false;
                foreach (var b in new List<Boulder>(world.boulders))
                {
                    if (b == null || b.smashed || b.Radius < 1.1f) continue;
                    var dir = new Vector2(1f, 0f);
                    var sideDir = new Vector2(0f, 1f);
                    var spot = b.Pos - dir * (b.Radius + 0.9f);
                    var hidden = b.Pos + dir * (b.Radius + 0.7f);
                    var open = spot - dir * 22f;
                    if (!WalkClear(spot) || !WalkClear(hidden) || !WalkClear(open)) continue;
                    staged = true;
                    var man = Put(kind, 0, spot);
                    man.hp = 1e6f;
                    man.noMind = false;
                    var near = Soldier(1, hidden); near.noMind = true;
                    var far = Soldier(1, open); far.noMind = true; far.hp = 1e6f;
                    var eyes = Soldier(0, hidden + sideDir * 12f); eyes.noMind = true;
                    fired.Clear();
                    float was = far.hp;
                    yield return Wait(10f);
                    sb.AppendLine($"  {kind} 2-3 m from a man behind a rock, another in the open 22 m off, 10 s: {Say()}; the one in the open lost {was - far.hp:0} hp; he is {man.mode}, hiding {man.hiding}, suppression {man.suppression:0.00}, target {(man.target == near ? "the near one" : man.target == far ? "the far one" : "other")} (target rounds at the far one unless he is hiding or pinned)");
                    ClearSpawned();
                    yield return Wait(1.5f);
                    break;
                }
                if (!staged) sb.AppendLine($"  {kind} behind a rock: no rock with room");
            }
            if (FindRange(rng, 34f, out var r, out var e))
            {
                var side = new Vector2(-e.y, e.x);
                var man = Put(UnitType.Trooper, 0, r);
                man.hp = 1e6f;
                var enemy = Soldier(1, r + e * 15f + side * 8f);
                enemy.noMind = true;
                yield return Wait(1f);
                fired.Clear();
                world.CmdMove(new List<Unit> { man }, r + e * 30f, false, true);
                yield return Wait(5f);
                sb.AppendLine($"  a Trooper hurried past an enemy 8 m to the side: {Say()}, he moved on {(man.pos - r).magnitude:0} m (target pistol)");
                ClearSpawned();
            }
            ListenWhileHeld(null);
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsRestockTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(9);
            HoldStatics();
            if (FindRange(rng, 90f, out var a, out var d))
            {
                var base0 = Put(UnitType.Garrison, 0, a);
                var home = Soldier(0, a + d * 10f);
                var field = Soldier(0, a + d * 80f);
                yield return Wait(1f);
                sb.AppendLine($"  given {home.frags} frags, {home.smokes} smoke, {home.flashes} flash (target 2, 1, 1)");
                home.frags = home.smokes = home.flashes = 0;
                field.frags = field.smokes = field.flashes = 0;
                yield return Wait(45f);
                sb.AppendLine($"  45 s spent: by the Garrison {home.frags}/{home.smokes}/{home.flashes} (target 2/1/1), in the field {field.frags}/{field.smokes}/{field.flashes} (target 0/0/0)");
                ClearSpawned();
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsMagazinesTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(71);
            HoldStatics();
            int main = 0, pistol = 0;
            ListenWhileHeld(ev =>
            {
                if (ev.kind == GameEventKind.Beam && ev.team == 0 && ev.projectileKind == (int)MechWeapon.Flamer) { main++; return; }
                if (ev.kind != GameEventKind.Fire || ev.team != 0) return;
                if (ev.projectileKind == GameWorld.ProjPistol) pistol++;
                else if (ev.projectileKind < GameWorld.ProjFrag || ev.projectileKind > GameWorld.ProjKnife || ev.projectileKind == GameWorld.ProjMG) main++;
            });
            foreach (var (type, at) in new[] { (UnitType.Trooper, 20f), (UnitType.Marksman, 30f), (UnitType.Rocketeer, 20f), (UnitType.Scorcher, 9f), (UnitType.Thumper, 25f), (UnitType.Gunner, 25f) })
            {
                if (!FindRange(rng, at + 4f, out var a, out var d)) continue;
                var man = Put(type, 0, a);
                man.hp = 1e6f;
                world.CmdHold(new List<Unit> { man });
                var e = Soldier(1, a + d * at);
                e.noMind = true;
                e.cooldown = 1e6f;
                yield return Wait(1f);
                main = 0; pistol = 0;
                int r0 = world.Reloads;
                float sawReload = 0f, t0 = world.time;
                while (world.time - t0 < 30f)
                {
                    if (man.Reloading) sawReload += Time.deltaTime;
                    yield return null;
                }
                var def = man.def;
                float cycle = def.magazine * def.cooldown + def.reloadTime;
                sb.AppendLine($"  {type} at {at} m, 30 s: {main} main rounds (at most {30f * def.magazine / cycle:0}: a magazine of {def.magazine} takes {cycle:0.0} s), {world.Reloads - r0} reloads of {def.reloadTime} s, pistol {pistol}, {man.rounds} left");
                ClearSpawned();
                yield return Wait(1.5f);
            }
            // Reloading with an enemy close: the pistol.
            if (FindRange(rng, 14f, out var b, out var c))
            {
                var man = Put(UnitType.Trooper, 0, b);
                man.hp = 1e6f;
                world.CmdHold(new List<Unit> { man });
                var e = Soldier(1, b + c * 11f);
                e.noMind = true;
                e.cooldown = 1e6f;
                yield return Wait(1f);
                main = 0; pistol = 0;
                yield return Wait(30f);
                sb.AppendLine($"  a Trooper with an enemy 11 m off, 30 s: {main} rifle rounds, {pistol} pistol rounds, {world.Reloads} reloads so far (target pistol while he reloads)");
                ClearSpawned();
            }
            ListenWhileHeld(null);
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsMistakesTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(55);
            HoldStatics();
            for (int rank = 0; rank <= 3; rank++)
            {
                int c0 = world.MistakeChances[rank], m0 = world.MistakesMade[rank], b0 = world.SquadBlunders;
                for (int run = 0; run < 3; run++)
                {
                    if (!FindRange(rng, 30f, out var a, out var d)) break;
                    var side = new Vector2(-d.y, d.x);
                    var six = new List<Unit>(); var other = new List<Unit>();
                    for (int i = 0; i < 6; i++) { var u = Put(UnitType.Trooper, 0, a + side * (i - 2.5f) * 2.5f); u.hp = 500f; u.rank = rank; six.Add(u); }
                    for (int i = 0; i < 6; i++) { var u = Put(UnitType.Trooper, 1, a + d * 26f + side * (i - 2.5f) * 2.5f); u.hp = 500f; u.rank = rank; other.Add(u); }
                    yield return null;
                    world.CmdMove(six, a + d * 26f, true);
                    world.CmdMove(other, a, true);
                    yield return Wait(22f);
                    ClearSpawned();
                    yield return Wait(1.5f);
                }
                int chances = world.MistakeChances[rank] - c0, made = world.MistakesMade[rank] - m0;
                sb.AppendLine($"  rank {rank}, three fights of twelve men, 22 s: {made} mistakes in {chances} judgements ({100f * made / Mathf.Max(1, chances):0}%), squads {world.SquadBlunders - b0} turns gone wrong");
            }
            // One old hand among greens.
            if (FindRange(rng, 10f, out var f, out var g))
            {
                var men = new List<Unit>();
                for (int i = 0; i < 4; i++) men.Add(Soldier(0, f + new Vector2(-g.y, g.x) * i * 2f));
                men[3].rank = 3;
                yield return Wait(1.5f);
                sb.AppendLine($"  three greens and an old hand: a green judges as rank {men[0].Experience} (target 3), the squad's veteran {men[0].squad?.veteran}");
                ClearSpawned();
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsHulksTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(88);
            HoldStatics();
            int HulkWrecks(Hulk of)
            {
                int n = 0;
                foreach (var w in Object.FindObjectsByType<StarForge.View.UnitWreck>(FindObjectsSortMode.None)) if (w.Hulk == of) n++;
                return n;
            }
            int fired = 0;
            ListenWhileHeld(ev => { if (ev.kind == GameEventKind.Fire && ev.team == 1) fired++; });
            // A field with nothing else to get behind within 13 m (a cover search's reach) of where the tank dies: the wreck or nothing.
            var near = new List<Vector3>();
            int ShelterNear(Vector2 p)
            {
                near.Clear();
                foreach (var bo in world.boulders) if (bo != null && !bo.smashed && (bo.Pos - p).sqrMagnitude < 13f * 13f) near.Add(new Vector3(bo.Pos.x, bo.Pos.y, bo.Radius));
                if (world.Plants != null) world.Sight.TrunksNear(p, 13f, world.Plants, near, 32);
                return near.Count;
            }
            // The field with the least else to get behind (none on most maps).
            Vector2 a = default, d = default;
            int fewest = int.MaxValue;
            for (int tries = 0; tries < 300 && fewest > 0; tries++)
                if (FindRange(rng, 36f, out var a1, out var d1))
                {
                    int n = ShelterNear(a1 + d1 * 8f);
                    if (n < fewest) { fewest = n; a = a1; d = d1; }
                }
            bool clear = fewest < int.MaxValue;
            if (clear)
            {
                var side = new Vector2(-d.y, d.x);
                var tank = Put(UnitType.Mauler, 1, a + d * 8f);
                yield return Wait(0.5f);
                world.Damage(tank, 1e7f, tank.pos, null);
                yield return Wait(2f);
                var h = world.Hulks.Count > 0 ? world.Hulks[world.Hulks.Count - 1] : null;
                if (h == null) { report = "no hulk left by a dead Mauler"; ReleaseStatics(); yield break; }
                float diedAt = world.time - 2f;
                sb.AppendLine($"  a Mauler dead: {world.Hulks.Count} hulk, radius {h.radius:0.0}, height {h.height:0.0}, {HulkWrecks(h)} wreck made (target 1, 1)");
                // Out in the open beside it, where the rifles see him: he should get behind it.
                var man = Put(UnitType.Trooper, 0, h.at - d * (h.radius + 2f) + side * 5f);
                man.hp = 1e6f;
                for (int r = 0; r < 3; r++) { var e = Soldier(1, h.at + d * 24f + side * ((r - 1) * 2f)); e.noMind = true; }
                yield return Wait(12f);
                sb.AppendLine($"  a man 5 m beside it under three rifles, {fewest} rocks or trunks within 13 m, 12 s: {man.mode}, cover {(man.coverAt - h.at).magnitude:0.0} m from its middle (target in cover within {h.radius + 2f:0.0}), now {(man.pos - h.at).magnitude:0.0} m from it, {fired} rounds at him, suppression {man.suppression:0.00}");
                // Why he went where he did: the place behind it, as the cover search tries it.
                {
                    Vector2 rifles = h.at + d * 24f;
                    Vector3 gun = map.Ground(rifles) + Vector3.up * 1.25f;
                    var lines = new System.Text.StringBuilder();
                    foreach (float extra in new[] { 0.6f, 1.0f, 1.4f })
                    {
                        Vector2 behind = h.at - d * (h.radius + extra);
                        int stopped = 0;
                        for (int k = -1; k <= 1; k++)
                        {
                            Vector2 q = behind + side * (k * 0.35f);
                            if (world.Sight.FirstStop(gun, map.Ground(q) + Vector3.up * 0.75f, world.Plants, world.boulders) < 1f) stopped++;
                        }
                        var path = new UnityEngine.AI.NavMeshPath();
                        bool walk = world.Walkable(behind, 0.5f);
                        bool got = UnityEngine.AI.NavMesh.CalculatePath(man.Ground, map.Ground(behind), GameWorld.GroundAreas, path);
                        lines.Append($" [{h.radius + extra:0.0} m behind: walkable {walk}, path {(got ? path.status.ToString() : "none")}, sheltered {stopped}/3]");
                    }
                    sb.AppendLine($"  the place behind it from the rifles:{lines}");
                }
                // A fight kept going 9 m off it, the man's cover or not.
                var keep = Soldier(0, h.at - d * 4f + side * 9f);
                keep.noMind = true;
                yield return Wait(70f);
                sb.AppendLine($"  {world.time - diedAt:0} s after it died, a fight 9 m off going on: still there {!h.gone}, wreck {HulkWrecks(h)} (the minute is up; target true, 1)");
                ClearSpawned();
                float endedAt = world.time;
                while (!h.gone && world.time - endedAt < 40f) yield return null;
                sb.AppendLine($"  the fight over: gone after {world.time - endedAt:0} s (target 20-22), wreck {HulkWrecks(h)} (it sinks after)");
                yield return Wait(15f);
                sb.AppendLine($"  15 s on: wreck {HulkWrecks(h)} (target 0)");
            }
            // A beast is no tank: a dead Gorehorn leaves no record.
            if (FindRange(rng, 20f, out var gA, out var gD))
            {
                int before = world.Hulks.Count;
                var beast = Put(UnitType.Gorehorn, 1, gA + gD * 8f);
                yield return Wait(0.5f);
                world.Damage(beast, 1e7f, beast.pos, null);
                yield return Wait(2f);
                sb.AppendLine($"  a Gorehorn dead: {world.Hulks.Count - before} records (target 0)");
                ClearSpawned();
            }
            // A friend's tank that is still going.
            if (FindRange(rng, 36f, out var b, out var c))
            {
                var side = new Vector2(-c.y, c.x);
                var tank = Put(UnitType.Mauler, 0, b);
                tank.hp = 1e6f;
                world.CmdHold(new List<Unit> { tank });
                tank.cooldown = 1e6f;
                var man = Put(UnitType.Trooper, 0, b - c * 6f + side * 3f);
                man.hp = 1e6f;
                for (int r = 0; r < 3; r++) { var e = Soldier(1, b + c * 26f + side * ((r - 1) * 2f)); e.noMind = true; }
                yield return Wait(12f);
                float was = 1e6f - tank.hp;
                sb.AppendLine($"  a man by a friend's Mauler under three rifles, 12 s: {man.mode}, {(man.coverAt - tank.pos).magnitude:0.0} m from it; the tank took {was:0} hp (rounds stopped by it)");
                ClearSpawned();
            }
            report = sb.ToString();
            ReleaseStatics();
        }

    }

    public sealed class ArmsGunnerTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(66);
            HoldStatics();
            var D = Defs.Get(UnitType.Gunner);
            sb.AppendLine($"  Gunner: walk {D.speed}, range {D.range}, {D.damage} a round every {D.cooldown} s, belt {D.magazine}, {D.reloadTime} s to load; requires {D.requires}, hotkey {D.hotkey}, rigged {Resources.Load<GameObject>("SoldierRig_Gunner_T0") != null}");
            foreach (var type in new[] { UnitType.Trooper, UnitType.Gunner })
            {
                string times = "";
                for (int run = 0; run < 4; run++)
                {
                    if (!FindRange(rng, 28f, out var a, out var d)) break;
                    var gun = Put(type, 0, a);
                    gun.hp = 1e6f;
                    gun.noMind = false;
                    world.CmdHold(new List<Unit> { gun });
                    var man = Soldier(1, a + d * 26f);
                    float t0 = world.time, pinned = -1f, peak = 0f, seen = 0f, frames = 0f;
                    int shots0 = world.ShotsFired;
                    while (world.time - t0 < 20f)
                    {
                        peak = Mathf.Max(peak, man.suppression);
                        frames++; if (world.Sees(0, man)) seen++;
                        if (man.mode == SoldierMode.Pinned) { pinned = world.time - t0; break; }
                        yield return null;
                    }
                    times += (pinned < 0f ? " never" : $" {pinned:0.0} s") + $" ({world.ShotsFired - shots0} rounds, peak {peak:0.00}, seen {100f * seen / Mathf.Max(1f, frames):0}%, {man.posture})";
                    ClearSpawned();
                    yield return Wait(1.5f);
                }
                sb.AppendLine($"  one {type} at a man 26 m off, on Hold: pinned after{times} (target the Gunner far sooner)");
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class ArmsGunnerClassicTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var rng = new System.Random(3);
            HoldStatics();
            var at = map.StartPos(0) + new Vector2(14f, 14f);
            var armory = Put(UnitType.Armory, 0, world.NearestWalkable(at + new Vector2(-14f, 0f), 12f));
            var garrison = Put(UnitType.Garrison, 0, world.NearestWalkable(at, 12f));
            yield return null;
            bool took = world.CmdTrain(garrison, UnitType.Gunner);
            report = $"  classic combat {!world.Tactical}: a Gunner asked of a Garrison with an Armory up: {(took ? "TRAINED" : "refused")} ({world.lastRefusal}) (target true, refused)";
            ClearSpawned();
            ReleaseStatics();
        }
    }

    public sealed class ArmsLookTrial : TacticalTrial
    {
        IEnumerator Shot(StarForge.View.RTSCamera cam, Vector2 at, float zoom, float secs, string file)
        {
            for (float t = 0f; t < secs; t += Time.unscaledDeltaTime) { cam.JumpTo(at); cam.ZoomTo(zoom); yield return null; }
            if (file != null) ScreenCapture.CaptureScreenshot("Temp/arms_look_" + file + ".png", 2);
            yield return null;
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            HoldStatics();
            Time.timeScale = 1f;
            var rng = new System.Random(21);
            // Open ground no crown hangs over, or the pictures show leaves.
            Vector2 a = default, d = default;
            bool found = false;
            for (int tries = 0; tries < 300 && !found; tries++)
            {
                if (!FindRange(rng, 34f, out a, out d)) continue;
                // No tree where the camera looks through (a bush is no matter).
                bool clear = true;
                for (float x = -20f; x <= 20f && clear; x += 4f)
                    for (float z = -30f; z <= 12f && clear; z += 4f)
                        if (world.Sight.WoodsAt(a + new Vector2(x, z)) > 0.4f) clear = false;
                found = clear;
            }
            if (!found) { report = "no open range found"; ReleaseStatics(); yield break; }
            var side = new Vector2(-d.y, d.x);
            var cam = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            UnitType[] kinds = { UnitType.Trooper, UnitType.Marksman, UnitType.Thumper, UnitType.Scorcher, UnitType.Rocketeer };
            Posture[] stance = { Posture.Stand, Posture.Crouch, Posture.Prone };
            for (int k = 0; k < kinds.Length; k++)
                for (int i = 0; i < 3; i++)
                {
                    var m = Put(kinds[k], 0, a + side * (k * 3f - 6f) + d * (i * 3.5f));
                    m.hp = 1e6f; m.noMind = true; m.posture = stance[i]; m.cooldown = 1e6f;
                    world.CmdHold(new List<Unit> { m });
                }
            yield return Shot(cam, a + d * 3.5f, 20f, 2.5f, "types");
            ClearSpawned();
            yield return Shot(cam, a, 26f, 1f, null);

            var thrower = Soldier(0, a - d * 12f);
            thrower.noMind = true;
            foreach (var off in new[] { side * 2f, -side * 3f, d * 3f }) { var e = Soldier(1, a + off); e.noMind = true; e.cooldown = 1e6f; }
            world.Throw(thrower, Thrown.Frag, a);
            float boom = GameWorld.ThrowFlight(12f) + TacticalRules.FragFuse;
            yield return Shot(cam, a, 26f, boom * 0.4f, "frag_flight");
            yield return Shot(cam, a, 26f, boom * 0.6f + 0.05f, "frag_0");
            yield return Shot(cam, a, 26f, 0.25f, "frag_1");
            yield return Shot(cam, a, 26f, 0.5f, "frag_2");
            yield return Shot(cam, a, 26f, 1.5f, null);

            world.Throw(thrower, Thrown.Flash, a);
            // The burst lasts a few frames: watched for, not timed.
            int stunnedBefore = world.Stunned;
            float flashWaited = 0f;
            var flashSeen = false;
            ListenWhileHeld(ev => { if (ev.kind == GameEventKind.Impact && ev.projectileKind == GameWorld.ProjFlash) flashSeen = true; });
            while (!flashSeen && flashWaited < 4f) { flashWaited += Time.unscaledDeltaTime; cam.JumpTo(a); cam.ZoomTo(26f); yield return null; }
            ListenWhileHeld(null);
            ScreenCapture.CaptureScreenshot("Temp/arms_look_flash_0.png", 2);
            yield return null;
            yield return Shot(cam, a, 26f, 0.06f, "flash_1");
            yield return Shot(cam, a, 26f, 1f, null);

            world.Throw(thrower, Thrown.Smoke, a);
            yield return Shot(cam, a, 30f, GameWorld.ThrowFlight(12f) + 1.2f, "smoke_0");
            yield return Shot(cam, a, 30f, 3f, "smoke_1");
            yield return Shot(cam, a, 30f, 6f, "smoke_2");
            yield return new WaitForSecondsRealtime(0.5f);
            ClearSpawned();
            report = "pictures in Temp/arms_look_types, frag_flight, frag_0-2, flash_0-1, smoke_0-2";
            ReleaseStatics();
        }
    }

    public sealed class ArmsTypesTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(13);
            HoldStatics();
            foreach (var t in new[] { UnitType.Trooper, UnitType.Marksman, UnitType.Thumper, UnitType.Scorcher, UnitType.Rocketeer })
            {
                var d = Defs.Get(t);
                TacticalRules.Loadout(t, out int f, out int s, out int l);
                sb.AppendLine($"  {t}: walk {d.speed}, range {d.range}, sight {d.sight}, spread {d.shotSpread:0.000}, body {d.bodyRadius}, direct {d.DirectFire}, pistol inside {d.pistolInside}, hp {d.hp}, frag {f} smoke {s} flash {l}; rigged {Resources.Load<GameObject>(t == UnitType.Trooper ? "SoldierRig_T0" : $"SoldierRig_{t}_T0") != null}");
            }
            // A long rifle against a rifle at the same range.
            foreach (var t in new[] { UnitType.Trooper, UnitType.Marksman })
            {
                if (!FindRange(rng, 32f, out var a, out var d)) break;
                var man = Put(t, 0, a); man.noMind = true;
                var target = Soldier(1, a + d * 30f); target.noMind = true;
                yield return Wait(1.5f);
                sb.AppendLine($"  {t} at a soldier 30 m off: {world.ProbeShots(man, target, 1000) / 10f:0}% of rounds strike");
                ClearSpawned();
                yield return Wait(1f);
            }
            // Holding a shell for a friend beside the target.
            int shots = 0;
            ListenWhileHeld(ev => { if (ev.kind == GameEventKind.Fire && ev.team == 0 && ev.projectileKind == GameWorld.ProjLob) shots++; });
            foreach (bool friendThere in new[] { true, false })
            {
                if (!FindRange(rng, 32f, out var a, out var d)) break;
                var side = new Vector2(-d.y, d.x);
                int s0, hurt;
                var mortar = Put(UnitType.Thumper, 0, a); mortar.hp = 1e6f;
                var aimed = Soldier(1, a + d * 30f); aimed.noMind = true;
                if (friendThere) { var f = Soldier(0, a + d * 30f + side * 2f); f.noMind = true; }
                s0 = shots;
                hurt = world.FriendHurt;
                yield return Wait(14f);
                sb.AppendLine($"  a Thumper at an enemy 30 m off, a friend beside him {friendThere}: {shots - s0} shells, friends hurt {world.FriendHurt - hurt} (target {(friendThere ? "0, 0" : "2 or more, 0")})");
                ClearSpawned();
                yield return Wait(1.5f);
            }
            ListenWhileHeld(null);
            report = sb.ToString();
            ReleaseStatics();
        }
    }
}
