// PlayerController.cs — mouse and keyboard to game commands: selection, orders,
// structure placement, control groups and the command card. Everything it does
// goes through GameWorld's Cmd* API, the same entry points the AI uses.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public enum CardKind { None, Build, Train, AttackMove, Stop, Hold, CancelQueue, CancelConstruction, CancelPlacement }

    public struct CardAction
    {
        public CardKind kind;
        public UnitType type;
        public char hotkey;
        public string title;
        public string body;
        public int cost;
        public int supply;
        public float time;
        public Texture2D icon;
        public string glyph;
        public bool enabled;
    }

    public sealed class PlayerController : MonoBehaviour
    {
        public GameWorld world;
        public RTSCamera rig;
        public int team;

        public readonly List<Unit> Selection = new List<Unit>();
        readonly List<Unit>[] groups = new List<Unit>[10];
        readonly List<Unit> scratch = new List<Unit>(64);

        public UnitType PlacingType { get; private set; } = UnitType.None;
        public Vector2 PlacementPos { get; private set; }
        public bool PlacementValid { get; private set; }
        public bool AttackMoveArmed { get; private set; }
        public Unit Hovered { get; private set; }
        public bool Dragging { get; private set; }
        public Vector2 DragStart { get; private set; }
        public Vector2 DragEnd { get; private set; }
        public bool CanCommand => !MatchSettings.spectate;

        /// <summary>Supplied by the HUD: is this screen point over an interactive panel?</summary>
        public Func<Vector2, bool> PointerOverUI;

        public event Action SelectionChanged;
        public event Action<Vector3, int> OrderMarker;   // 0 move, 1 attack, 2 rally, 3 build
        public event Action MenuRequested;
        public event Action InspectorToggled;

        Camera cam;
        TerrainCollider ground;
        bool pressedOnWorld;
        float lastClickTime;
        UnitType lastClickType = UnitType.None;
        int lastGroupTap = -1;
        float lastGroupTapTime;

        void Awake()
        {
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            if (rig == null) rig = FindAnyObjectByType<RTSCamera>();
            for (int i = 0; i < groups.Length; i++) groups[i] = new List<Unit>();
        }

        void Start()
        {
            cam = rig != null ? rig.cam : Camera.main;
            ground = world.Map.terrain.GetComponent<TerrainCollider>();
        }

        // ------------------------------------------------------------ per frame
        void Update()
        {
            var bs = GameBootstrap.Instance;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (bs == null || kb == null || mouse == null) return;

            if (bs.State == MatchState.Paused)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) bs.TogglePause();
                return;
            }
            if (bs.State != MatchState.Playing) return;

            int before = Selection.Count;
            Selection.RemoveAll(u => !Unit.Live(u) || (u.team != team && !u.visibleToPlayer && !u.def.building));
            if (Selection.Count != before) SelectionChanged?.Invoke();

            Vector2 mp = mouse.position.ReadValue();
            bool overUI = PointerOverUI != null && PointerOverUI(mp);
            bool hasGround = ScreenToGround(mp, out Vector2 gp);
            Hovered = overUI ? null : PickUnit(mp);

            if (PlacingType != UnitType.None)
            {
                PlacementPos = hasGround ? gp : PlacementPos;
                PlacementValid = hasGround && world.CanPlace(PlacingType, gp) &&
                                 world.factions[team].ore >= Defs.Get(PlacingType).cost;
            }

            HandleKeys(kb, bs);
            HandleMouse(mouse, mp, overUI, hasGround, gp);
        }

        public bool ScreenToGround(Vector2 screen, out Vector2 p)
        {
            p = default;
            if (cam == null || ground == null) return false;
            var ray = cam.ScreenPointToRay(screen);
            if (!ground.Raycast(ray, out var hit, 2000f)) return false;
            p = new Vector2(hit.point.x, hit.point.z);
            return true;
        }

        Unit PickUnit(Vector2 screen)
        {
            Unit best = null;
            float bestScore = float.MaxValue;
            float minPx = 14f * Mathf.Max(1f, Screen.dpi / 110f);
            foreach (var u in world.units)
            {
                if (u == null || u.dying || u.def.type == UnitType.Boulder) continue;
                if (u.team == 1 && !u.visibleToPlayer && !(u.def.building && u.everSeenByPlayer)) continue;
                Vector3 c = u.Ground + Vector3.up * (u.def.visualHeight * 0.45f);
                Vector3 sp = cam.WorldToScreenPoint(c);
                if (sp.z <= 0f) continue;
                Vector3 edge = cam.WorldToScreenPoint(c + cam.transform.right * u.def.visualRadius);
                float r = Mathf.Max(minPx, Mathf.Abs(edge.x - sp.x) * 1.1f);
                float d = Vector2.Distance(screen, sp);
                if (d > r) continue;
                // Units beat the structures they stand on; nearest centre wins.
                float score = d / r + (u.def.building ? 0.6f : 0f) + (u.def.neutral ? 0.3f : 0f);
                if (score < bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        // ------------------------------------------------------------ keyboard
        void HandleKeys(Keyboard kb, GameBootstrap bs)
        {
            bool ctrl = kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed;
            bool shift = kb.shiftKey.isPressed;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                // Esc unwinds one step at a time; only with nothing left does it open the menu.
                if (PlacingType != UnitType.None) PlacingType = UnitType.None;
                else if (AttackMoveArmed) AttackMoveArmed = false;
                else if (Selection.Count > 0) { Selection.Clear(); SelectionChanged?.Invoke(); }
                else MenuRequested?.Invoke();
                return;
            }
            if (kb.pKey.wasPressedThisFrame) { bs.TogglePause(); return; }
            if (kb.iKey.wasPressedThisFrame) InspectorToggled?.Invoke();
            if (kb.spaceKey.wasPressedThisFrame && Selection.Count > 0) rig.CenterOn(Centroid(Selection));
            if (kb.f2Key.wasPressedThisFrame) SelectAllArmy();
            if (kb.periodKey.wasPressedThisFrame) Time.timeScale = Mathf.Min(3f, Time.timeScale + 0.5f);
            if (kb.commaKey.wasPressedThisFrame) Time.timeScale = Mathf.Max(0.5f, Time.timeScale - 0.5f);

            for (int i = 0; i < 10; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + (i == 0 ? 9 : i - 1))];
                if (!key.wasPressedThisFrame) continue;
                if (ctrl && CanCommand)
                {
                    groups[i].Clear();
                    foreach (var u in Selection) if (u.team == team) groups[i].Add(u);
                }
                else if (shift)
                {
                    foreach (var u in groups[i]) if (Unit.Live(u) && !Selection.Contains(u)) Selection.Add(u);
                    SelectionChanged?.Invoke();
                }
                else
                {
                    groups[i].RemoveAll(u => !Unit.Live(u));
                    if (groups[i].Count == 0) continue;
                    Selection.Clear();
                    Selection.AddRange(groups[i]);
                    SelectionChanged?.Invoke();
                    if (lastGroupTap == i && Time.unscaledTime - lastGroupTapTime < 0.35f) rig.CenterOn(Centroid(Selection));
                    lastGroupTap = i;
                    lastGroupTapTime = Time.unscaledTime;
                }
            }

            // Testing cheat, documented only in the README: Ctrl (or Cmd) + Shift + M.
            if (ctrl && shift && kb.mKey.wasPressedThisFrame) world.CheatOre(team, 5000);

            if (!CanCommand || ctrl) return;
            var cards = new List<CardAction>(12);
            BuildCard(cards);
            foreach (var c in cards)
            {
                if (c.hotkey == '\0') continue;
                var key = KeyFor(c.hotkey);
                if (key != Key.None && kb[key].wasPressedThisFrame) { Execute(c); break; }
            }
        }

        static Key KeyFor(char c)
        {
            if (c >= 'A' && c <= 'Z') return (Key)((int)Key.A + (c - 'A'));
            return Key.None;
        }

        // ------------------------------------------------------------ mouse
        void HandleMouse(Mouse mouse, Vector2 mp, bool overUI, bool hasGround, Vector2 gp)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                pressedOnWorld = !overUI;
                if (!pressedOnWorld) return;
                if (PlacingType != UnitType.None)
                {
                    if (hasGround) TryPlace(gp, Keyboard.current.shiftKey.isPressed);
                    pressedOnWorld = false;
                    return;
                }
                if (AttackMoveArmed)
                {
                    if (hasGround) IssueAttackMove(gp);
                    if (!Keyboard.current.shiftKey.isPressed) AttackMoveArmed = false;
                    pressedOnWorld = false;
                    return;
                }
                DragStart = DragEnd = mp;
                Dragging = false;
            }

            if (pressedOnWorld && mouse.leftButton.isPressed)
            {
                DragEnd = mp;
                if (!Dragging && (DragEnd - DragStart).magnitude > 6f) Dragging = true;
            }

            if (pressedOnWorld && mouse.leftButton.wasReleasedThisFrame)
            {
                bool shift = Keyboard.current.shiftKey.isPressed;
                if (Dragging) BoxSelect(DragStart, DragEnd, shift);
                else ClickSelect(shift);
                Dragging = false;
                pressedOnWorld = false;
            }

            if (mouse.rightButton.wasPressedThisFrame && !overUI)
            {
                if (PlacingType != UnitType.None) { PlacingType = UnitType.None; return; }
                if (AttackMoveArmed) { AttackMoveArmed = false; return; }
                if (hasGround && CanCommand) RightClick(gp);
            }
        }

        void ClickSelect(bool shift)
        {
            var u = Hovered;
            if (u == null)
            {
                if (!shift && Selection.Count > 0) { Selection.Clear(); SelectionChanged?.Invoke(); }
                return;
            }
            bool doubleClick = Time.unscaledTime - lastClickTime < 0.3f && lastClickType == u.Type && u.team == team;
            lastClickTime = Time.unscaledTime;
            lastClickType = u.Type;

            if (doubleClick)
            {
                // Everything of that type on screen.
                if (!shift) Selection.Clear();
                foreach (var o in world.units)
                    if (Unit.Live(o) && o.team == team && o.Type == u.Type && OnScreen(o) && !Selection.Contains(o)) Selection.Add(o);
            }
            else if (shift && u.team == team)
            {
                if (Selection.Contains(u)) Selection.Remove(u);
                else { Selection.RemoveAll(s => s.team != team); Selection.Add(u); }
            }
            else
            {
                Selection.Clear();
                Selection.Add(u);
            }
            SelectionChanged?.Invoke();
        }

        void BoxSelect(Vector2 a, Vector2 b, bool shift)
        {
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            scratch.Clear();
            bool anyMobile = false;
            foreach (var u in world.units)
            {
                if (!Unit.Live(u) || u.team != team) continue;
                Vector3 sp = cam.WorldToScreenPoint(u.Ground + Vector3.up * (u.def.visualHeight * 0.4f));
                if (sp.z <= 0f || !r.Contains(new Vector2(sp.x, sp.y))) continue;
                scratch.Add(u);
                if (u.def.IsMobile) anyMobile = true;
            }
            // A box over an army and its base selects the army.
            if (anyMobile) scratch.RemoveAll(u => !u.def.IsMobile);
            if (!shift) Selection.Clear();
            else Selection.RemoveAll(s => s.team != team);
            foreach (var u in scratch) if (!Selection.Contains(u)) Selection.Add(u);
            SelectionChanged?.Invoke();
        }

        bool OnScreen(Unit u)
        {
            Vector3 sp = cam.WorldToScreenPoint(u.Ground);
            return sp.z > 0f && sp.x >= 0 && sp.y >= 0 && sp.x <= Screen.width && sp.y <= Screen.height;
        }

        void SelectAllArmy()
        {
            Selection.Clear();
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == team && u.def.IsArmy) Selection.Add(u);
            SelectionChanged?.Invoke();
        }

        List<Unit> OwnSelection()
        {
            scratch.Clear();
            foreach (var u in Selection) if (Unit.Live(u) && u.team == team) scratch.Add(u);
            return scratch;
        }

        void RightClick(Vector2 gp)
        {
            var own = new List<Unit>(OwnSelection());
            if (own.Count == 0) return;
            bool onlyStructures = own.TrueForAll(u => u.def.building);
            if (onlyStructures)
            {
                world.CmdRally(own, gp);
                OrderMarker?.Invoke(world.Map.Ground(gp), 2);
                return;
            }
            var target = Hovered;
            world.CmdSmart(own, gp, target);
            bool attack = target != null && target.team < 2 && target.team != team;
            OrderMarker?.Invoke(target != null ? target.Ground : world.Map.Ground(gp), attack ? 1 : 0);
        }

        void IssueAttackMove(Vector2 gp)
        {
            var own = new List<Unit>(OwnSelection());
            if (own.Count == 0) return;
            if (Hovered != null && Hovered.team != team && Hovered.team < 2) world.CmdAttack(own, Hovered);
            else world.CmdMove(own, gp, true);
            OrderMarker?.Invoke(world.Map.Ground(gp), 1);
        }

        void TryPlace(Vector2 gp, bool keepPlacing)
        {
            Unit builder = null;
            float best = float.MaxValue;
            foreach (var u in OwnSelection())
            {
                if (u.Type != UnitType.Worker) continue;
                float d = (u.pos - gp).sqrMagnitude + (u.order == Order.Build ? 4000f : 0f);
                if (d < best) { best = d; builder = u; }
            }
            if (builder == null) { PlacingType = UnitType.None; return; }
            if (world.CmdBuild(new List<Unit> { builder }, PlacingType, gp))
            {
                OrderMarker?.Invoke(world.Map.Ground(gp), 3);
                if (!keepPlacing) PlacingType = UnitType.None;
            }
        }

        static Vector2 Centroid(List<Unit> l)
        {
            Vector2 c = Vector2.zero;
            int n = 0;
            foreach (var u in l) if (Unit.Live(u)) { c += u.pos; n++; }
            return n > 0 ? c / n : Vector2.zero;
        }

        // ------------------------------------------------------------ command card
        /// <summary>The commands available for the current selection, in card order.
        /// The HUD draws these and the keyboard fires them, so the two cannot disagree.</summary>
        public void BuildCard(List<CardAction> into)
        {
            into.Clear();
            if (!CanCommand) return;
            var own = OwnSelection();
            if (own.Count == 0) return;
            var F = world.factions[team];

            if (PlacingType != UnitType.None)
            {
                into.Add(new CardAction { kind = CardKind.CancelPlacement, title = "Cancel placement", body = "Right-click or Esc also cancels.", glyph = "✕", enabled = true });
                return;
            }

            bool anyWorker = false, anyMobile = false;
            UnitType structureType = UnitType.None;
            bool mixedStructures = false, anyIncomplete = false, anyQueue = false;
            foreach (var u in own)
            {
                if (u.Type == UnitType.Worker) anyWorker = true;
                if (u.def.IsMobile) anyMobile = true;
                if (u.def.building)
                {
                    if (!u.Complete) anyIncomplete = true;
                    if (u.queue.Count > 0) anyQueue = true;
                    if (structureType == UnitType.None) structureType = u.Type;
                    else if (structureType != u.Type) mixedStructures = true;
                }
            }

            if (anyMobile)
            {
                into.Add(new CardAction { kind = CardKind.AttackMove, hotkey = 'R', title = "Attack-move", body = "Move, engaging every enemy met on the way.", glyph = "⚔", enabled = true });
                into.Add(new CardAction { kind = CardKind.Stop, hotkey = 'C', title = "Stop", body = "Cancel current orders.", glyph = "■", enabled = true });
                into.Add(new CardAction { kind = CardKind.Hold, hotkey = 'H', title = "Hold position", body = "Fire at anything in range, never chase.", glyph = "⛨", enabled = true });
            }
            if (anyWorker)
                foreach (var t in Defs.Buildable)
                {
                    var d = Defs.Get(t);
                    bool reqOk = d.requires == UnitType.None || world.HasComplete(team, d.requires);
                    string req = reqOk ? "" : $"\nRequires a {Defs.Get(d.requires).displayName}.";
                    string supply = d.supplyGive > 0 ? $" · +{d.supplyGive} supply" : "";
                    into.Add(new CardAction
                    {
                        kind = CardKind.Build, type = t, hotkey = d.hotkey, title = "Build " + d.displayName,
                        body = d.blurb + req, cost = d.cost, time = d.buildTime, icon = d.icon, enabled = reqOk && F.ore >= d.cost,
                        glyph = supply
                    });
                }
            if (structureType != UnitType.None && !mixedStructures && !anyIncomplete)
            {
                for (int i = 0; i < (int)UnitType.Count; i++)
                {
                    var d = Defs.Get((UnitType)i);
                    if (d == null || d.producer != structureType) continue;
                    bool supplyOk = F.supplyUsed + d.supplyCost <= Mathf.Min(F.supplyCap, GameWorld.MaxSupply);
                    into.Add(new CardAction
                    {
                        kind = CardKind.Train, type = d.type, hotkey = d.hotkey, title = "Train " + d.displayName,
                        body = d.blurb + (supplyOk ? "" : "\nNot enough supply — build a Bunkhouse."),
                        cost = d.cost, supply = d.supplyCost, time = d.buildTime, icon = d.icon,
                        enabled = F.ore >= d.cost && supplyOk
                    });
                }
                if (anyQueue)
                    into.Add(new CardAction { kind = CardKind.CancelQueue, hotkey = 'X', title = "Cancel last", body = "Refunds the most recently queued unit.", glyph = "✕", enabled = true });
            }
            if (anyIncomplete)
                into.Add(new CardAction { kind = CardKind.CancelConstruction, hotkey = 'X', title = "Cancel construction", body = "Refunds 75% of the cost.", glyph = "✕", enabled = true });
        }

        public void Execute(CardAction c)
        {
            if (!CanCommand) return;
            var own = new List<Unit>(OwnSelection());
            switch (c.kind)
            {
                case CardKind.Build:
                {
                    var d = Defs.Get(c.type);
                    if (d.requires != UnitType.None && !world.HasComplete(team, d.requires))
                        world.Raise(new GameEvent { kind = GameEventKind.Refused, team = team, text = $"Requires a {Defs.Get(d.requires).displayName}" });
                    else if (world.factions[team].ore < d.cost)
                        world.Raise(new GameEvent { kind = GameEventKind.Refused, team = team, text = "Not enough ore" });
                    else { PlacingType = c.type; AttackMoveArmed = false; }
                    break;
                }
                case CardKind.Train:
                {
                    Unit best = null;
                    foreach (var u in own)
                        if (u.def.building && u.Complete && Defs.Get(c.type).producer == u.Type &&
                            (best == null || u.queue.Count < best.queue.Count)) best = u;
                    if (best != null) world.CmdTrain(best, c.type);
                    break;
                }
                case CardKind.AttackMove: AttackMoveArmed = true; PlacingType = UnitType.None; break;
                case CardKind.Stop: world.CmdStop(own); break;
                case CardKind.Hold: world.CmdHold(own); break;
                case CardKind.CancelQueue:
                    foreach (var u in own) if (u.queue.Count > 0) { world.CmdCancelTrain(u); break; }
                    break;
                case CardKind.CancelConstruction:
                    foreach (var u in own) if (!u.Complete) world.CmdCancelConstruction(u);
                    break;
                case CardKind.CancelPlacement: PlacingType = UnitType.None; break;
            }
        }

        public void SelectOnly(Unit u)
        {
            if (!Unit.Live(u)) return;
            Selection.Clear();
            Selection.Add(u);
            SelectionChanged?.Invoke();
        }

        public void MinimapCommand(Vector2 world2D)
        {
            if (!CanCommand) return;
            var own = new List<Unit>(OwnSelection());
            if (own.Count == 0) return;
            if (own.TrueForAll(u => u.def.building)) world.CmdRally(own, world2D);
            else world.CmdMove(own, world2D, AttackMoveArmed);
            AttackMoveArmed = false;
            OrderMarker?.Invoke(world.Map.Ground(world2D), 0);
        }
    }
}
