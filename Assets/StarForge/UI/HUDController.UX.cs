// HUDController.UX.cs — the RTS conveniences of 2026-11-24: alerts as field-net dispatches
// that remember where they happened (Tab goes there, PlayerController.JumpToAlert), the
// control-group bar above the selection and the idle-Digger button. Headings use the world's
// names and the text keeps the game's plain voice (docs/lore/starforge-lore.md, section 13).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.UI
{
    public sealed partial class HUDController
    {
        // The Host's colours: grey iron, steel-blue edges, the Brightest Star's blue-white for
        // news, ember for trouble, the wing's brighter cyan for what the interface itself says.
        static readonly Color DispatchFill = new Color(0.062f, 0.060f, 0.064f, 0.90f);
        static readonly Color DispatchEdge = new Color(0.47f, 0.65f, 0.82f, 0.30f);
        static readonly Color StarLight = new Color(0.66f, 0.83f, 1f);
        static readonly Color Ember = new Color(0.92f, 0.37f, 0.26f);
        static readonly Color Ash = new Color(0.62f, 0.64f, 0.67f);
        static readonly Color WingBlue = new Color(0.31f, 0.82f, 1f);

        const int KeptPlaces = 8;
        /// <summary>Where the latest alerts with a place happened, newest last (Tab steps back through them).</summary>
        readonly List<Vector2> alertPlaces = new List<Vector2>(KeptPlaces);
        /// <summary>Where under-attack dispatches went in the last 10 s, and when (unscaled time).</summary>
        readonly List<(Vector2 at, float t)> attackShown = new List<(Vector2, float)>(8);
        float lastAttackShown = -99f;
        // A new place held back by the 3 s gap: told when the gap ends, unless it is 5 s old by then.
        // Dropped instead, a raid that came and went inside the gap (a Bunkhouse under two rifles
        // lasts about that at 3x) was never told at all.
        bool attackHeld, heldBase;
        Vector2? heldPlace;
        float heldAt;
        /// <summary>Under-attack dispatches shown this match (UXTrials reads it).</summary>
        public int UnderAttackShown { get; private set; }
        /// <summary>"TAB" on the one dispatch Tab goes to first; any newer place takes it off.</summary>
        Label tabBadge;
        readonly HashSet<Unit> selSet = new HashSet<Unit>();

        VisualElement groupBar;
        VisualElement idleTab;
        Label idleCount;
        int shownIdle = -1;
        readonly GroupTab[] groupTabs = new GroupTab[10];
        int[] typeCount;
        float uxT;
        int shownGroupsVersion = -1;
        bool uxDirty = true;

        sealed class GroupTab
        {
            public VisualElement root;
            public Image icon;
            public Label count;
            public int shownCount = -1;
            public UnitType shownType = UnitType.None;
            public bool active;
        }

        /// <summary>A dispatch's identity (heading and text: the same again refreshes it) and its fade's token.</summary>
        sealed class DispatchMark
        {
            public string key;
            public int token;
        }

        // ------------------------------------------------------------ setup
        void InitUX()
        {
            groupBar = root.Q("groupBar");
            typeCount = new int[(int)UnitType.Count];
            if (groupBar != null)
            {
                idleTab = new VisualElement();
                idleTab.AddToClassList("idle-tab");
                var idleIcon = new Image { image = Defs.Get(UnitType.Worker)?.icon, pickingMode = PickingMode.Ignore };
                idleIcon.AddToClassList("group-tab-icon");
                var idleWord = new Label("IDLE") { pickingMode = PickingMode.Ignore };
                idleWord.AddToClassList("idle-tab-word");
                idleCount = new Label { pickingMode = PickingMode.Ignore };
                idleCount.AddToClassList("group-tab-count");
                idleTab.Add(idleIcon);
                idleTab.Add(idleWord);
                idleTab.Add(idleCount);
                idleTab.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.ctrlKey || e.commandKey) player.SelectIdleDiggers();
                    else player.NextIdleDigger();
                });
                groupBar.Add(idleTab);

                for (int k = 1; k <= 10; k++)
                {
                    int g = k % 10;   // the bar reads 1-9 then 0, as the keys do
                    var tab = new GroupTab { root = new VisualElement() };
                    tab.root.AddToClassList("group-tab");
                    var key = new Label(g.ToString()) { pickingMode = PickingMode.Ignore };
                    key.AddToClassList("group-tab-key");
                    tab.icon = new Image { pickingMode = PickingMode.Ignore };
                    tab.icon.AddToClassList("group-tab-icon");
                    tab.count = new Label { pickingMode = PickingMode.Ignore };
                    tab.count.AddToClassList("group-tab-count");
                    tab.root.Add(key);
                    tab.root.Add(tab.icon);
                    tab.root.Add(tab.count);
                    // Click selects, double-click goes there, Shift-click adds the group to the selection.
                    tab.root.RegisterCallback<ClickEvent>(e =>
                    {
                        if (e.clickCount >= 2) { player.CenterOnGroup(g); return; }
                        player.RecallGroup(g, e.shiftKey);
                    });
                    groupTabs[g] = tab;
                    groupBar.Add(tab.root);
                }
            }
            // Tab is the alert key now: UI Toolkit would also take it to walk focus through the HUD's
            // buttons, and Space or Enter would then press the one it landed on.
            foreach (var b in hud.Query<Button>().ToList()) b.focusable = false;

            if (player != null)
            {
                player.AlertPlace = AlertPlace;
                player.Hint += OnHint;
                player.SelectionChanged += MarkUXDirty;
            }
        }

        void OnDestroy()
        {
            if (player == null) return;
            player.Hint -= OnHint;
            player.SelectionChanged -= MarkUXDirty;
            player.AlertPlace = null;
        }

        void MarkUXDirty() => uxDirty = true;

        void OnHint(string text) => Dispatch(null, text, "alert--hint", 2.4f);

        // ------------------------------------------------------------ per frame
        /// <summary>The group bar and the idle count, five times a second or when a group or the
        /// selection changed; labels are written only when what they show changed (no garbage
        /// in a steady state: UXTrials.Run reads it).</summary>
        void UpdateUX(float dt)
        {
            FlushHeldAttack();
            if (player == null || groupBar == null) return;
            uxT -= dt;
            if (!uxDirty && uxT > 0f && player.GroupsVersion == shownGroupsVersion) return;
            uxT = 0.2f;
            uxDirty = false;
            shownGroupsVersion = player.GroupsVersion;

            int idle = player.CanCommand ? player.IdleDiggerCount() : 0;
            if (idle != shownIdle)
            {
                shownIdle = idle;
                idleTab.EnableInClassList("idle-tab--shown", idle > 0);
                idleCount.text = idle.ToString();
            }

            var sel = player.Selection;
            // A set, not List.Contains per member: a big army both selected and grouped was n squared.
            selSet.Clear();
            foreach (var u in sel) selSet.Add(u);
            for (int g = 0; g < 10; g++)
            {
                var tab = groupTabs[g];
                var members = player.Group(g);
                System.Array.Clear(typeCount, 0, typeCount.Length);
                int n = 0;
                bool allSelected = true;
                for (int i = 0; i < members.Count; i++)
                {
                    var u = members[i];
                    if (!Unit.Live(u) || u.team != player.team || u.deserted) continue;
                    n++;
                    typeCount[(int)u.Type]++;
                    if (allSelected && !selSet.Contains(u)) allSelected = false;
                }
                // The group's face: its most numerous type (ties to the earlier type).
                UnitType face = UnitType.None;
                int most = 0;
                for (int t = 0; t < typeCount.Length; t++)
                    if (typeCount[t] > most) { most = typeCount[t]; face = (UnitType)t; }
                bool active = n > 0 && allSelected && sel.Count == n;
                if (n != tab.shownCount)
                {
                    tab.root.EnableInClassList("group-tab--shown", n > 0);
                    tab.count.text = n.ToString();
                    tab.shownCount = n;
                }
                if (face != tab.shownType)
                {
                    tab.shownType = face;
                    tab.icon.image = face != UnitType.None ? Defs.Get(face)?.icon : null;
                }
                if (active != tab.active)
                {
                    tab.active = active;
                    tab.root.EnableInClassList("group-tab--active", active);
                }
            }
        }

        // ------------------------------------------------------------ dispatches
        /// <summary>An alert as a field-net dispatch: a heading in the world's names (or none), the text
        /// in the game's voice, and the place it is about (Tab goes there). The same heading and text
        /// again refreshes the one already up instead of stacking copies.</summary>
        void Dispatch(string head, string text, string cls, float seconds, Vector2? at = null)
        {
            if (string.IsNullOrEmpty(text) || alerts == null) return;
            if (at.HasValue) RememberPlace(at.Value);
            long hold = (long)(seconds * 1000f);
            string key = head + "|" + text;
            for (int i = alerts.childCount - 1; i >= 0; i--)
                if (alerts[i].userData is DispatchMark old && old.key == key)
                {
                    alerts[i].RemoveFromClassList("dispatch--fading");
                    FadeLater(alerts[i], old, hold);
                    if (at.HasValue) BadgeOn(alerts[i]);
                    return;
                }
            if (alerts.childCount >= 4) alerts.RemoveAt(0);

            Color ink = cls == "alert--warn" ? Ember : cls == "alert--refused" ? Ash : cls == "alert--hint" ? WingBlue : StarLight;
            var card = new SFFrame { Cut = 9f, Tab = true, Fill = DispatchFill, Edge = DispatchEdge, Accent = ink, pickingMode = PickingMode.Ignore };
            card.AddToClassList("dispatch");
            card.AddToClassList(cls);
            card.AddToClassList("dispatch--new");
            var star = new StarMark { Ink = ink };
            star.AddToClassList("dispatch-star");
            card.Add(star);
            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("dispatch-body");
            if (!string.IsNullOrEmpty(head))
            {
                var h = new Label(head) { pickingMode = PickingMode.Ignore };
                h.AddToClassList("dispatch-head");
                body.Add(h);
            }
            var t = new Label(text) { pickingMode = PickingMode.Ignore };
            t.AddToClassList("dispatch-text");
            body.Add(t);
            card.Add(body);
            if (at.HasValue) BadgeOn(card);
            var mark = new DispatchMark { key = key };
            card.userData = mark;
            alerts.Add(card);
            card.schedule.Execute(() => card.RemoveFromClassList("dispatch--new")).StartingIn(30);
            FadeLater(card, mark, hold);
        }

        /// <summary>Tab goes first to the newest place, so only the dispatch that has it says TAB.</summary>
        void BadgeOn(VisualElement card)
        {
            ClearTabBadge();
            tabBadge = new Label("TAB") { pickingMode = PickingMode.Ignore };
            tabBadge.AddToClassList("dispatch-key");
            card.Add(tabBadge);
        }

        void ClearTabBadge()
        {
            if (tabBadge != null) tabBadge.RemoveFromHierarchy();
            tabBadge = null;
        }

        /// <summary>A dispatch's fade, keyed to its latest showing: a repeat restarts its time and the
        /// earlier showing's callbacks then do nothing.</summary>
        static void FadeLater(VisualElement e, DispatchMark m, long hold)
        {
            int token = ++m.token;
            e.schedule.Execute(() => { if (m.token == token) e.AddToClassList("dispatch--fading"); }).StartingIn(hold);
            e.schedule.Execute(() => { if (m.token == token) e.RemoveFromHierarchy(); }).StartingIn(hold + 500);
        }

        /// <summary>One fight, one alert: a place told in the last 10 s (anything within 45 m of it) stays
        /// quiet; a new place is told, but no sooner than 3 s after the last (held till then, not
        /// dropped), so two fronts do not alternate (one place remembered, two fights flipped it back
        /// and forth every 3 s).</summary>
        void UnderAttack(GameEvent e)
        {
            if (bootstrap == null || bootstrap.State != MatchState.Playing) return;
            Vector2? place = PlaceOf(e);
            float now = Time.unscaledTime;
            if (ToldLately(place, now)) return;
            bool baseHit = e.unit != null && e.unit.def.building;
            if (now - lastAttackShown < 3f)
            {
                attackHeld = true; heldPlace = place; heldBase = baseHit; heldAt = now;
                return;
            }
            TellAttack(place, baseHit, now);
        }

        /// <summary>The held-back place, once the gap has passed (from UpdateUX, every frame in play).</summary>
        void FlushHeldAttack()
        {
            if (!attackHeld || bootstrap == null || bootstrap.State != MatchState.Playing) return;
            float now = Time.unscaledTime;
            if (now - lastAttackShown < 3f) return;
            attackHeld = false;
            if (now - heldAt > 5f || ToldLately(heldPlace, now)) return;
            TellAttack(heldPlace, heldBase, now);
        }

        /// <summary>Told within 10 s at anything within 45 m of this place (a place-less event counts as one place).</summary>
        bool ToldLately(Vector2? place, float now)
        {
            Vector2 at = place ?? new Vector2(-1e4f, -1e4f);
            for (int i = attackShown.Count - 1; i >= 0; i--)
            {
                if (now - attackShown[i].t > 10f) { attackShown.RemoveAt(i); continue; }
                if ((attackShown[i].at - at).sqrMagnitude < 45f * 45f) return true;
            }
            return false;
        }

        void TellAttack(Vector2? place, bool baseHit, float now)
        {
            lastAttackShown = underAttackT = now;
            attackShown.Add((place ?? new Vector2(-1e4f, -1e4f), now));
            UnderAttackShown++;
            Dispatch(Net(place), baseHit ? "Your base is under attack" : "Your forces are under attack", "alert--warn", 2.6f, place);
        }

        /// <summary>Where an event happened: its unit, else its position; null when it carries neither.</summary>
        static Vector2? PlaceOf(GameEvent e)
        {
            if (!ReferenceEquals(e.unit, null)) return e.unit.pos;
            if (e.pos != Vector3.zero) return new Vector2(e.pos.x, e.pos.z);
            return null;
        }

        /// <summary>The field net's heading, with where: the nearest structure of ours, else the map's quarter.</summary>
        string Net(Vector2? at) => at.HasValue ? "FIELD NET · " + Where(at.Value) : "FIELD NET";

        string Where(Vector2 at)
        {
            int me = player != null ? player.team : 0;
            Unit best = null;
            float bestD = 35f * 35f;
            foreach (var u in world.units)
            {
                if (!Unit.Live(u) || u.team != me || !u.def.building || !u.Complete) continue;
                float d = (u.pos - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = u; }
            }
            // Headings use the world's names: the Mech Bay is the gantry (lore section 7).
            if (best != null) return best.Type == UnitType.MechBay ? "BY THE GANTRY" : "BY THE " + best.def.displayName.ToUpperInvariant();
            return Corner(new Vector3(at.x, 0f, at.y)).ToUpperInvariant();
        }

        /// <summary>The order the player's knight is sworn to, as a dispatch's heading (the High Watch without one).</summary>
        string OrderHead(int team)
        {
            var d = world.factions[team].design;
            return d != null && !string.IsNullOrEmpty(d.order) ? d.order.ToUpperInvariant() : "HIGH WATCH";
        }

        void RememberPlace(Vector2 at)
        {
            // The same place again (one fight, a refreshed call) moves to the front instead of filling the list.
            for (int i = alertPlaces.Count - 1; i >= 0; i--)
                if ((alertPlaces[i] - at).sqrMagnitude < 12f * 12f) { alertPlaces.RemoveAt(i); break; }
            if (alertPlaces.Count >= KeptPlaces) alertPlaces.RemoveAt(0);
            alertPlaces.Add(at);
            ClearTabBadge();
        }

        /// <summary>The place of the newest alert (step 0), the one before (step 1) and so on.</summary>
        public Vector2? AlertPlace(int step)
        {
            int i = alertPlaces.Count - 1 - step;
            return step >= 0 && i >= 0 ? alertPlaces[i] : (Vector2?)null;
        }

        /// <summary>For UXTrials: the dispatches up now, newest last, as "HEAD | text".</summary>
        public string DispatchesNow()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in alerts.Children())
                if (c.userData is DispatchMark m) sb.Append(sb.Length > 0 ? " // " : "").Append(m.key);
            return sb.ToString();
        }

        /// <summary>For UXTrials: what the group bar shows, as "1:12 Trooper*" (* = the selection is that group).</summary>
        public string GroupBarNow()
        {
            var sb = new System.Text.StringBuilder();
            for (int g = 0; g < 10; g++)
            {
                var t = groupTabs[g];
                if (t == null || t.shownCount <= 0) continue;
                sb.Append(sb.Length > 0 ? ", " : "").Append(g).Append(':').Append(t.shownCount).Append(' ').Append(t.shownType).Append(t.active ? "*" : "");
            }
            sb.Append(sb.Length > 0 ? "; " : "").Append("idle ").Append(shownIdle);
            return sb.ToString();
        }
    }

    /// <summary>The Brightest Star as a mark (four long points, four short, as on the menus' emblem): the dispatches' seal.</summary>
    public class StarMark : VisualElement
    {
        Color ink = Color.white;
        public Color Ink { get => ink; set { ink = value; MarkDirtyRepaint(); } }

        public StarMark()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            float r = Mathf.Min(w, h) * 0.5f;
            if (r < 2f) return;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            var p = mgc.painter2D;
            p.fillColor = ink;
            p.BeginPath();
            for (int i = 0; i < 16; i++)
            {
                float rr = i % 2 == 1 ? r * 0.17f : (i / 2) % 2 == 0 ? r : r * 0.48f;
                float a = (-90f + 22.5f * i) * Mathf.Deg2Rad;
                var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
        }
    }
}
