// LoreTrials.cs -- checks for the lore's additions to the game (docs/lore/starforge-lore.md):
//   Iron      the iron ledger's rules on ids alone (UI/IronLedger): edit or play mode, answers at once.
//   Orders    the Field Manual's Orders page: its data against the standards' atlas and the mottos
//             (edit mode), and in play the page as built.
//   IronField (play, staged) soldiers of both sides die on the field and recruits are raised: whose
//             iron the recruits wear, read through the HUD's own ledger and the world's own events.
//   Halved    (play, staged) both sides' Mechs of one order: the knight's lines for a halved oath.
// Targets are in each summary. Call as StarForge.EditorTools.LoreTrials.<Name>.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.Game;
using StarForge.Sim;
using StarForge.UI;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class LoreTrials
    {
        /// <summary>The ledger's rules, on ids alone. Good: "0 failed". A line names the wearer, then
        /// the names before them, newest first; a seed repeats; a fallen soldier's iron goes to the
        /// next recruit of the side that took it, never to one raised before it came back; iron
        /// taken by the other wing reads "painted-over"; a side's stores hold 48 at most.</summary>
        public static string Iron()
        {
            var sb = new StringBuilder();
            int failed = 0;
            void Check(bool ok, string what) { if (!ok) failed++; sb.AppendLine((ok ? "ok   " : "FAIL ") + what); }

            var a = new IronLedger(); a.Begin(1000u);
            var b = new IronLedger(); b.Begin(1000u);
            var c = new IronLedger(); c.Begin(1037u);
            string l1 = a.Line(1, 0);
            Check(l1.Contains(" · in this iron before: "), "a fresh iron names its wearer and those before: " + l1);
            int same = 0, differ = 0;
            for (int id = 1; id <= 20; id++)
            {
                if (a.Line(id, 0) == b.Line(id, 0)) same++;
                if (a.Line(id, 0) != c.Line(id, 0)) differ++;
            }
            Check(same == 20, $"one seed repeats: {same} of 20 lines alike");
            Check(differ >= 15, $"another seed differs: {differ} of 20 lines");

            // Soldier 2 falls among friends (newest unit alive: 20). Recruit 21 wears it; 2's name comes first.
            string own2 = Own(a.Line(2, 0));
            a.Fallen(2, 0, 0, 20);
            Check(a.Waiting(0) == 1, $"own side keeps the iron: {a.Waiting(0)} waiting");
            string l21 = a.Line(21, 0);
            Check(l21.Contains("before: " + own2), $"the next recruit wears it, the fallen's name first ({own2}): {l21}");
            Check(a.Waiting(0) == 0, "and it has left the stores");

            // Soldier 3 falls when unit 30 is the newest alive: 25, raised before that, must not get it; 31 does.
            string own3 = Own(a.Line(3, 0));
            a.Fallen(3, 0, 0, 30);
            string l25 = a.Line(25, 0);
            Check(a.Waiting(0) == 1, $"one raised before it came back does not wear it (still {a.Waiting(0)} waiting): {l25}");
            string l31 = a.Line(31, 0);
            Check(l31.Contains("before: " + own3), $"one raised after does ({own3}): {l31}");

            // Soldier 4 falls among the other wing: team 1's next recruit wears it painted over, names unread.
            a.Fallen(4, 0, 1, 31);
            Check(a.Waiting(1) == 1 && a.Waiting(0) == 0, $"the other wing took it: {a.Waiting(1)} in its stores");
            string l32 = a.Line(32, 1);
            Check(l32.Contains("painted-over iron") && !l32.Contains("before:"), "its recruit's iron is painted over, the names not read out: " + l32);
            // That iron lost again on its new side stays painted.
            a.Fallen(32, 1, 1, 32);
            Check(a.Line(33, 1).Contains("painted-over iron"), "and stays painted when handed on: " + a.Line(33, 1));

            for (int id = 100; id < 160; id++) a.Fallen(id, 0, 0, 200);
            Check(a.Waiting(0) == 48, $"a side's stores hold 48 at most: {a.Waiting(0)} after 60 fell");
            sb.Insert(0, $"{failed} failed (issued {a.Issued}, reissued {a.Reissued}, painted {a.Painted}, returned {a.Returned})\n");
            return sb.ToString();
        }

        static string Own(string line)
        {
            int cut = line.IndexOf(" · ", System.StringComparison.Ordinal);
            return cut < 0 ? line : line.Substring(0, cut);
        }

        /// <summary>The Orders page. Good: "0 failed". Edit mode: all eight orders of the atlas have an
        /// entry, a motto and a standard, and no entry names an order the atlas lacks. In play also:
        /// the manual has five tabs and the page holds eight rows with a standard each.</summary>
        public static string Orders()
        {
            var sb = new StringBuilder();
            int failed = 0;
            void Check(bool ok, string what) { if (!ok) failed++; sb.AppendLine((ok ? "ok   " : "FAIL ") + what); }

            Check(LoreText.Orders.Length == OrderStandards.Count, $"{LoreText.Orders.Length} entries for {OrderStandards.Count} orders");
            for (int i = 0; i < OrderStandards.Count; i++)
            {
                string name = OrderStandards.NameOf(i);
                bool has = false;
                foreach (var o in LoreText.Orders) if (o.name == name) has = true;
                Check(has && LoreText.Motto(name) != null, $"{name}: entry {(has ? "yes" : "NO")}, motto {(LoreText.Motto(name) != null ? "yes" : "NO")}");
            }
            foreach (var o in LoreText.Orders)
                if (OrderStandards.IndexOf(o.name) < 0) Check(false, $"'{o.name}' is no order of the atlas");

            if (UnityEditor.EditorApplication.isPlaying)
            {
                ScrollView page = null;
                VisualElement tabs = null;
                foreach (var doc in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                {
                    if (doc.rootVisualElement == null) continue;
                    page = page ?? doc.rootVisualElement.Q<ScrollView>("ordersScroll");
                    tabs = tabs ?? doc.rootVisualElement.Q("manualTabs");
                }
                Check(page != null, "the manual has an Orders page");
                int tabCount = tabs != null ? tabs.Query<Label>(className: "m-tab").ToList().Count : 0;   // the Q and E key caps are m-tab-key
                Check(tabCount == 5, $"the manual has five tabs: {tabCount}");
                if (page != null)
                {
                    int rows = 0, standards = 0, mottos = 0;
                    foreach (var row in page.Query<VisualElement>(className: "m-prose-entry").ToList())
                    {
                        rows++;
                        var img = row.Q<Image>();
                        if (img != null && img.sprite != null) standards++;
                        foreach (var l in row.Query<Label>().ToList()) if (l.text.StartsWith("<i>")) { mottos++; break; }
                    }
                    Check(rows == 8 && standards == 8 && mottos == 8, $"rows {rows}, with a standard {standards}, with a motto {mottos} (8 each)");
                }
            }
            else sb.AppendLine("     (edit mode: the page itself is checked in play)");
            sb.Insert(0, $"{failed} failed\n");
            return sb.ToString();
        }

        public static string IronField() => Stage(LoreStage.Kind.Iron, "IronFieldReport");
        public static string IronFieldReport() => StageReport();
        public static string Halved() => Stage(LoreStage.Kind.Halved, "HalvedReport");
        public static string HalvedReport() => StageReport();

        static string Stage(LoreStage.Kind kind, string report)
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            foreach (var old in Object.FindObjectsByType<LoreStage>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
            new GameObject("LoreStage").AddComponent<LoreStage>().kind = kind;
            return $"staging; wait StarForge.EditorTools.LoreTrials.{report} \"done|failed\" (under a minute)";
        }

        static string StageReport()
        {
            LoreStage newest = null;
            foreach (var s in Object.FindObjectsByType<LoreStage>(FindObjectsSortMode.None)) if (newest == null || s.born > newest.born) newest = s;
            return newest == null ? "no trial running" : newest.report;
        }
    }

    // Not named after its file: Unity refuses to add a MonoBehaviour whose script lies in an Editor folder.
    public sealed class LoreStage : MonoBehaviour
    {
        public enum Kind { Iron, Halved }
        public Kind kind;
        public string report = "staging";
        public float born;
        bool memoryWas, spectateWas, saved;
        float scaleWas = 1f;
        GameWorld hooked;
        readonly List<string> said = new List<string>();

        void Awake() { born = Time.realtimeSinceStartup; }

        // The match statics outlive the play session: put back what this set, also when play stops part-way.
        void OnDestroy()
        {
            if (hooked != null) hooked.Event -= OnEvent;
            if (!saved) return;
            MatchSettings.aiMemory = memoryWas;
            MatchSettings.spectate = spectateWas;
            Time.timeScale = scaleWas;
        }

        void OnEvent(GameEvent e)
        {
            if (e.kind == GameEventKind.MechAdvice && e.text != null) said.Add($"team {e.team}: {e.text}");
        }

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) { report = "failed: no GameBootstrap"; yield break; }
            memoryWas = MatchSettings.aiMemory;
            spectateWas = MatchSettings.spectate;
            scaleWas = Time.timeScale;
            saved = true;
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.aiMemory = false;
                MatchSettings.spectate = true;   // both sides' cards and calls are the trial's to read
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            var world = GameWorld.Instance;
            if (world == null || !world.running) { report = "failed: no match running"; yield break; }
            Time.timeScale = 1f;
            Vector2 mid = new Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            if (kind == Kind.Iron) yield return IronField(world, mid);
            else yield return Halved(world, boot, mid);
        }

        /// <summary>Good: "0 failed". Three of ours beside five of theirs and one of ours falls: the
        /// other wing takes the iron (1 in its stores) and its next recruit's card reads "painted-over".
        /// Four of ours 60 m off and one falls: our stores keep it and our next recruit's card names the
        /// fallen first. Read through the HUD's ledger, fed by the world's own Death events.</summary>
        IEnumerator IronField(GameWorld world, Vector2 mid)
        {
            var hud = FindAnyObjectByType<HUDController>();
            if (hud == null) { report = "failed: no HUD"; yield break; }
            var iron = hud.Iron;
            var sb = new StringBuilder();
            int failed = 0;
            void Check(bool ok, string what) { if (!ok) failed++; sb.AppendLine((ok ? "ok   " : "FAIL ") + what); }

            Vector2 p = world.NearestWalkable(mid), q = world.NearestWalkable(mid + new Vector2(0f, 60f));
            var mixedOurs = new List<Unit>();
            var apart = new List<Unit>();
            for (int i = 0; i < 3; i++) mixedOurs.Add(world.Spawn(UnitType.Trooper, 0, world.NearestWalkable(p + new Vector2(i * 1.5f, 0f))));
            for (int i = 0; i < 5; i++) world.Spawn(UnitType.Trooper, 1, world.NearestWalkable(p + new Vector2(i * 1.5f, 6f)));
            for (int i = 0; i < 4; i++) apart.Add(world.Spawn(UnitType.Trooper, 0, world.NearestWalkable(q + new Vector2(i * 1.5f, 0f))));
            int w0 = iron.Waiting(0), w1 = iron.Waiting(1);
            if (w0 != 0 || w1 != 0) { report = $"failed: irons already wait in the stores ({w0}, {w1}): run it from a fresh play, before any soldier has fallen"; yield break; }
            string takenOwn = Own(iron.Line(mixedOurs[0], world)), keptOwn = Own(iron.Line(apart[0], world));
            sb.AppendLine($"     a card before: {iron.Line(apart[1], world)}");
            // Same frame as the spawn: nobody has fired yet.
            world.Damage(mixedOurs[0], 1e7f, mixedOurs[0].pos, null);
            world.Damage(apart[0], 1e7f, apart[0].pos, null);
            report = "running: the fallen";
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            Check(iron.Waiting(1) - w1 >= 1, $"beside five of theirs, the other wing took the iron: its stores {w1} -> {iron.Waiting(1)}");
            Check(iron.Waiting(0) - w0 >= 1, $"among friends, ours kept the iron: our stores {w0} -> {iron.Waiting(0)}");
            var recruit1 = world.Spawn(UnitType.Trooper, 1, world.NearestWalkable(p + new Vector2(-20f, 30f)));
            var recruit0 = world.Spawn(UnitType.Trooper, 0, world.NearestWalkable(q + new Vector2(0f, 20f)));
            yield return null;
            string l1 = iron.Line(recruit1, world), l0 = iron.Line(recruit0, world);
            Check(l1 != null && l1.Contains("painted-over iron"), $"their recruit wears it painted over (it was {takenOwn}'s): {l1}");
            Check(l0 != null && l0.Contains("before: " + keptOwn), $"our recruit wears the fallen's, {keptOwn} named first: {l0}");
            sb.Insert(0, $"done: {failed} failed (issued {iron.Issued}, reissued {iron.Reissued}, painted {iron.Painted}, returned {iron.Returned})\n");
            report = sb.ToString();
        }

        static string Own(string line)
        {
            if (line == null) return "?";
            int cut = line.IndexOf(" · ", System.StringComparison.Ordinal);
            return cut < 0 ? line : line.Substring(0, cut);
        }

        /// <summary>Good: "0 failed". Both sides' Mechs of the Sable Oath: our knight says once that the
        /// other is of his order ("my own order" / "of my order"; team 1's Mech never talks, so one call
        /// is all there is to hear); then theirs is killed and ours speaks of it soberly ("He was of my
        /// order"), not "Their Mech is down!".</summary>
        IEnumerator Halved(GameWorld world, GameBootstrap boot, Vector2 mid)
        {
            var sb = new StringBuilder();
            int failed = 0;
            void Check(bool ok, string what) { if (!ok) failed++; sb.AppendLine((ok ? "ok   " : "FAIL ") + what); }

            hooked = world;
            world.Event += OnEvent;
            var mechs = new Unit[2];
            for (int team = 0; team < 2; team++)
            {
                var d = new MechDesign
                {
                    locomotion = MechLocomotion.Biped, frame = MechFrame.Medium, utility = MechUtility.Shield,
                    className = "Medium-Biped", pilot = team == 0 ? "Marshal Anselm Wrenstar" : "Paladin Osric Larkstar",
                    callsign = team == 0 ? "ANVIL" : "GREYWALL", order = "the Sable Oath",
                };
                d.weapons.Add(MechWeapon.Autocannon);
                var at = world.NearestWalkable(Vector2.Lerp(world.Map.StartPos(team), mid, 0.35f));
                mechs[team] = world.TrialMech(team, d, at, 0f, false, true);   // its side's Mech
            }
            sb.AppendLine($"     brains {(boot.MechBrains[0] != null ? "0" : "-")}{(boot.MechBrains[1] != null ? "1" : "-")}, " +
                          $"side Mechs {(world.MechOf(0) != null ? "0" : "-")}{(world.MechOf(1) != null ? "1" : "-")}, " +
                          $"designs {(world.factions[0].design != null ? "0" : "-")}{(world.factions[1].design != null ? "1" : "-")}, " +
                          $"{(mechs[0].pos - mechs[1].pos).magnitude:0} m apart");
            Time.timeScale = 4f;
            report = "running: waiting for the knights";
            float until = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < until && Count("my own order", "of my order, Commander") < 1) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            int halved = Count("my own order", "of my order, Commander");
            Check(halved == 1, $"our knight names the halved oath, once: {halved} such call(s) heard");

            if (Unit.Live(mechs[1]))
            {
                world.Damage(mechs[1], 1e7f, mechs[1].pos, null);
                report = "running: their Mech has fallen";
                until = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < until && Count("He was of my order") < 1 && Count("Their Mech is down!", "Their champion has fallen") < 1) yield return null;
                yield return new WaitForSecondsRealtime(1f);
                Check(Count("He was of my order") >= 1, $"the survivor speaks of his order's fallen: {Count("He was of my order")}");
                Check(Count("Their Mech is down!", "Their champion has fallen") == 0, $"and not as of any other: {Count("Their Mech is down!", "Their champion has fallen")}");
            }
            else Check(false, "their Mech fell before the trial killed it");
            Time.timeScale = scaleWas;
            world.Event -= OnEvent;
            hooked = null;
            sb.AppendLine($"     every call heard ({said.Count}):");
            foreach (var s in said) sb.AppendLine("       " + s);
            sb.Insert(0, $"done: {failed} failed\n");
            report = sb.ToString();
        }

        int Count(params string[] parts)
        {
            int n = 0;
            foreach (var s in said)
                foreach (var p in parts)
                    if (s.Contains(p)) { n++; break; }
            return n;
        }
    }
}
