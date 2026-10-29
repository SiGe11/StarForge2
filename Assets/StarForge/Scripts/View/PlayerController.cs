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
    public enum CardKind { None, Build, Train, AttackMove, Stop, Hold, CancelQueue, CancelConstruction, CancelPlacement, MechUpgrade, CancelUpgrade, Info,
                           ArmyUpgrade, CancelResearch, BuildMenu, MenuBack }

    /// <summary>Which of the Digger's build cards is open (StarCraft's basic/advanced structures).</summary>
    public enum BuildMenu { None, Basic, Advanced }

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
        public MechUpgrade upgrade;
        public ArmyUpgrade armyUpgrade;
        public BuildMenu menu;
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
        /// <summary>Orders go in only while the match is played: the command card's buttons
        /// and the minimap still take clicks while it is paused and behind the end screen,
        /// and orders went in through them.</summary>
        static bool InPlay => GameBootstrap.Instance != null && GameBootstrap.Instance.State == MatchState.Playing;

        /// <summary>Supplied by the HUD: is this screen point over an interactive panel?</summary>
        public Func<Vector2, bool> PointerOverUI;

        public event Action SelectionChanged;
        /// <summary>The card changed without the selection changing (a build card opened or closed).</summary>
        public event Action CardChanged;
        /// <summary>The Digger's build card that is open; a new selection closes it.</summary>
        public BuildMenu Menu { get; private set; }
        bool pruning;

        void SetMenu(BuildMenu m)
        {
            if (Menu == m) return;
            Menu = m;
            CardChanged?.Invoke();
        }
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
            // A new selection closes an open build card; a dead unit pruned from it does not.
            SelectionChanged += () => { if (!pruning) Menu = BuildMenu.None; };
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
            // A soldier who deserts leaves a group selection (one alone stays, to be looked at).
            if (Selection.Count > 1) Selection.RemoveAll(u => u.deserted);
            if (Selection.Count != before) { pruning = true; SelectionChanged?.Invoke(); pruning = false; }

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
                // An aircraft is where it flies, not over the ground under it.
                Vector3 c = u.def.flying ? GameWorld.AirPoint(u) : u.Ground + Vector3.up * (u.def.visualHeight * 0.45f);
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
                else if (Menu != BuildMenu.None) SetMenu(BuildMenu.None);
                else if (Selection.Count > 0) { Selection.Clear(); SelectionChanged?.Invoke(); }
                else MenuRequested?.Invoke();
                return;
            }
            if (kb.pKey.wasPressedThisFrame) { bs.TogglePause(); return; }
            if (kb.iKey.wasPressedThisFrame) InspectorToggled?.Invoke();
            if (kb.spaceKey.wasPressedThisFrame && Selection.Count > 0) rig.CenterOn(Centroid(Selection));
            if (kb.f2Key.wasPressedThisFrame) SelectAllArmy();
            if (kb.periodKey.wasPressedThisFrame) bs.SetSpeed(bs.Speed + 0.5f);
            if (kb.commaKey.wasPressedThisFrame) bs.SetSpeed(bs.Speed - 0.5f);

            for (int i = 0; i < 10; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + (i == 0 ? 9 : i - 1))];
                if (!key.wasPressedThisFrame) continue;
                if (ctrl && CanCommand)
                {
                    groups[i].Clear();
                    foreach (var u in Selection) if (Mine(u)) groups[i].Add(u);
                }
                else if (shift)
                {
                    foreach (var u in groups[i]) if (Unit.Live(u) && Mine(u) && !Selection.Contains(u)) Selection.Add(u);
                    SelectionChanged?.Invoke();
                }
                else
                {
                    groups[i].RemoveAll(u => !Unit.Live(u) || !Mine(u));
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

            // The card's hotkeys act only on a key pressed this frame: built every frame to look,
            // the card cost 200-500 B of garbage a frame (BugTrials.CardAlloc).
            if (!CanCommand || ctrl || !kb.anyKey.wasPressedThisFrame) return;
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
                    if (Unit.Live(o) && Mine(o) && o.Type == u.Type && OnScreen(o) && !Selection.Contains(o)) Selection.Add(o);
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
                if (!Unit.Live(u) || !Mine(u)) continue;
                Vector3 sp = cam.WorldToScreenPoint(u.def.flying ? GameWorld.AirPoint(u) : u.Ground + Vector3.up * (u.def.visualHeight * 0.4f));
                if (sp.z <= 0f || !r.Contains(new Vector2(sp.x, sp.y))) continue;
                scratch.Add(u);
                if (u.def.IsMobile) anyMobile = true;
            }
            // A box over an army and its base selects the army; the Mech, which takes no
            // orders, only if it is all the box holds.
            if (anyMobile) scratch.RemoveAll(u => !u.def.IsMobile);
            if (scratch.Exists(u => !u.def.Autonomous)) scratch.RemoveAll(u => u.def.Autonomous);
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
                if (Unit.Live(u) && Mine(u) && u.def.IsArmy) Selection.Add(u);
            SelectionChanged?.Invoke();
        }

        List<Unit> OwnSelection()
        {
            scratch.Clear();
            foreach (var u in Selection) if (Unit.Live(u) && Mine(u)) scratch.Add(u);
            return scratch;
        }

        /// <summary>The player's to command: their side's, and not a deserter (GameWorld.Morale).</summary>
        bool Mine(Unit u) => u.team == team && !u.deserted;

        void RightClick(Vector2 gp)
        {
            var own = new List<Unit>(OwnSelection());
            if (own.Count == 0) return;
            if (own.TrueForAll(u => u.def.Autonomous))
            {
                world.Raise(new GameEvent { kind = GameEventKind.Refused, team = team, text = "The Mech follows its own judgement -- buy it upgrades at the Mech Bay" });
                return;
            }
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
                if (!u.def.builder) continue;
                float d = (u.pos - gp).sqrMagnitude + (u.order == Order.Build ? 4000f : 0f);
                if (d < best) { best = d; builder = u; }
            }
            if (builder == null) { PlacingType = UnitType.None; return; }
            if (world.CmdBuild(new List<Unit> { builder }, PlacingType, gp))
            {
                OrderMarker?.Invoke(world.Map.Ground(gp), 3);
                if (!keepPlacing) { PlacingType = UnitType.None; SetMenu(BuildMenu.None); }
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
                if (u.def.builder) anyWorker = true;
                // The Mech takes no orders: it adds no orders to the card.
                if (u.def.IsMobile && !u.def.Autonomous) anyMobile = true;
                if (u.def.building)
                {
                    if (!u.Complete) anyIncomplete = true;
                    if (u.queue.Count > 0) anyQueue = true;
                    if (structureType == UnitType.None) structureType = u.Type;
                    else if (structureType != u.Type) mixedStructures = true;
                }
            }

            // An open build card is the whole card: the group's structures and the way back. The nine
            // structures and three orders together ran past the slots the card can show.
            if (anyWorker && Menu != BuildMenu.None)
            {
                foreach (var t in Menu == BuildMenu.Basic ? Defs.BasicBuildings : Defs.AdvancedBuildings)
                    into.Add(BuildAction(t, F));
                into.Add(new CardAction { kind = CardKind.MenuBack, hotkey = 'X', title = "Back", body = "Back to the Digger's orders (Esc also goes back).",
                                          glyph = "↩", enabled = true });
                return;
            }
            if (!anyWorker && Menu != BuildMenu.None) Menu = BuildMenu.None;

            if (anyMobile)
            {
                into.Add(new CardAction { kind = CardKind.AttackMove, hotkey = 'R', title = "Attack-move", body = "Move, engaging every enemy met on the way.", glyph = "⚔", enabled = true });
                into.Add(new CardAction { kind = CardKind.Stop, hotkey = 'C', title = "Stop", body = "Cancel current orders.", glyph = "■", enabled = true });
                into.Add(new CardAction { kind = CardKind.Hold, hotkey = 'H', title = "Hold position", body = "Fire at anything in range, never chase.", glyph = "⛨", enabled = true });
            }
            if (anyWorker)
            {
                into.Add(new CardAction { kind = CardKind.BuildMenu, menu = BuildMenu.Basic, hotkey = 'B', title = "Basic structures", glyph = "▣", enabled = true,
                    body = "Foundry, Bunkhouse, Garrison, Sentinel and Workshop: the economy and the core army." });
                into.Add(new CardAction { kind = CardKind.BuildMenu, menu = BuildMenu.Advanced, hotkey = 'V', title = "Advanced structures", glyph = "◈", enabled = true,
                    body = "Armory, Airfield, Biolab and Mech Bay: research, aircraft, creatures and your Mech." });
            }
            // The bay's card only for the bay (with its Mech): with Diggers as well, their
            // six builds and the bay's five upgrades and cancel ran past the card's twelve
            // slots, and the last ones were hidden but still answered their hotkeys.
            if (structureType == UnitType.MechBay && !mixedStructures && !anyIncomplete && !anyMobile)
            {
                BayCard(into, F);
                return;
            }
            // The Armory's research, likewise only for Armories on their own.
            if (structureType == UnitType.Armory && !mixedStructures && !anyIncomplete && !anyMobile)
            {
                ArmoryCard(into, F, own);
                return;
            }
            if (!anyMobile && structureType == UnitType.None && own.TrueForAll(u => u.def.Autonomous))
            {
                into.Add(new CardAction
                {
                    kind = CardKind.Info, title = "Autonomous", glyph = "◆", enabled = false,
                    body = "The Mech takes no orders. It fights for you by its own judgement: it defends the base, joins your " +
                           "attacks, hunts what it can beat and falls back to the Mech Bay to be repaired. Buy it upgrades " +
                           "at the bay, and watch its messages -- it sees the whole field."
                });
                return;
            }
            // Not with Diggers selected: their nine builds and the three orders fill the card's
            // twelve slots, and a production card past them would be hidden but still answer
            // its hotkey (as the Mech Bay's once did).
            if (structureType != UnitType.None && !mixedStructures && !anyIncomplete && !anyWorker)
            {
                for (int i = 0; i < (int)UnitType.Count; i++)
                {
                    var d = Defs.Get((UnitType)i);
                    if (d == null || d.producer != structureType) continue;
                    bool supplyOk = F.supplyUsed + d.supplyCost <= Mathf.Min(F.supplyCap, GameWorld.MaxSupply);
                    bool reqOk = d.requires == UnitType.None || world.HasComplete(team, d.requires);
                    // The Mech-Hunter: the Armory's programme, one at a time, a wait after it falls.
                    string barred = d.type == UnitType.MechHunter && reqOk ? world.HunterBarred(team) : null;
                    into.Add(new CardAction
                    {
                        kind = CardKind.Train, type = d.type, hotkey = d.hotkey, title = "Train " + d.displayName,
                        body = d.blurb + (reqOk ? "" : $"\nRequires a {Defs.Get(d.requires).displayName}.") +
                               (barred == null ? "" : $"\n{barred}.") +
                               (supplyOk ? "" : "\nNot enough supply — build a Bunkhouse."),
                        cost = d.cost, supply = d.supplyCost, time = d.buildTime, icon = d.icon,
                        enabled = F.ore >= d.cost && supplyOk && reqOk && barred == null
                    });
                }
                if (anyQueue)
                    into.Add(new CardAction { kind = CardKind.CancelQueue, hotkey = 'X', title = "Cancel last", body = "Refunds the most recently queued unit.", glyph = "✕", enabled = true });
            }
            // Not beside the Diggers' twelve: a hidden thirteenth would still answer X.
            if (anyIncomplete && !anyWorker)
                into.Add(new CardAction { kind = CardKind.CancelConstruction, hotkey = 'X', title = "Cancel construction", body = "Refunds 75% of the cost.", glyph = "✕", enabled = true });
        }

        /// <summary>A structure on a build card: its requirement, and the Mech Bay's one-a-match rule.</summary>
        CardAction BuildAction(UnitType t, Faction F)
        {
            var d = Defs.Get(t);
            bool reqOk = d.requires == UnitType.None || world.HasComplete(team, d.requires);
            string req = reqOk ? "" : $"\nRequires a {Defs.Get(d.requires).displayName}.";
            string supply = d.supplyGive > 0 ? $" · +{d.supplyGive} supply" : "";
            bool once = t == UnitType.MechBay && F.bayPlaced;
            if (once) req += F.bayLost ? "\nYour Mech Bay was destroyed; it can never be raised again." : "\nYou have raised your Mech Bay: there is only ever one.";
            return new CardAction
            {
                kind = CardKind.Build, type = t, hotkey = d.hotkey, title = "Build " + d.displayName,
                body = d.blurb + req, cost = d.cost, time = d.buildTime, icon = d.icon, enabled = reqOk && F.ore >= d.cost && !once,
                glyph = supply
            };
        }

        /// <summary>The Mech Bay's card: the Mech's upgrades, one level at a time.</summary>
        void BayCard(List<CardAction> into, Faction F)
        {
            var design = F.design;
            for (int i = 0; i < (int)MechUpgrade.Count; i++)
            {
                var up = (MechUpgrade)i;
                var info = MechParts.Upgrade(up);
                int lvl = F.upgrades[i];
                int cost = world.UpgradeCost(team, up);
                string body = info.blurb;
                if (up == MechUpgrade.Hardpoint && design != null && !F.designChosen && lvl < info.levels)
                    body += "\nNext: a weapon the pilot's order picks with the rest of the Mech's parts, at the drop.";
                else if (up == MechUpgrade.Hardpoint && design != null && lvl < info.levels && lvl < design.reserve.Count)
                    body += $"\nNext: a {MechParts.Weapon(design.reserve[lvl]).name} -- {MechParts.Weapon(design.reserve[lvl]).blurb}";
                string state = F.mechLost ? "\nThe Mech is lost." : cost < 0 ? "\nComplete." : F.researching >= 0 ? "\nThe bay is busy with another upgrade." : "";
                into.Add(new CardAction
                {
                    kind = CardKind.MechUpgrade, upgrade = up, hotkey = info.hotkey,
                    title = cost < 0 ? $"{info.name} (complete)" : $"{info.name} {MechParts.Roman(lvl + 1)}",
                    body = body + state + $"\nLevel {lvl} of {info.levels}.",
                    cost = Mathf.Max(0, cost), time = cost < 0 ? 0f : info.time[lvl],
                    glyph = UpgradeGlyph(up) + (lvl > 0 ? new string('·', lvl) : ""),
                    enabled = cost >= 0 && !F.mechLost && F.researching < 0 && F.ore >= cost
                });
            }
            if (F.researching >= 0)
                into.Add(new CardAction { kind = CardKind.CancelUpgrade, hotkey = 'X', title = "Cancel upgrade", body = "Refunds the upgrade in progress.", glyph = "✕", enabled = true });
        }

        /// <summary>The Armory's card: weapons and armour for each class, one level at a time; a
        /// line one Armory works on is not offered by another.</summary>
        void ArmoryCard(List<CardAction> into, Faction F, List<Unit> own)
        {
            Unit free = null;
            bool busy = false;
            foreach (var u in own)
                if (u.Type == UnitType.Armory && u.Complete)
                {
                    if (u.researchLine < 0) { if (free == null) free = u; }
                    else busy = true;
                }
            for (int i = 0; i < (int)ArmyUpgrade.Count; i++)
            {
                var up = (ArmyUpgrade)i;
                var info = GameWorld.ArmyUpgradeOf(up);
                int lvl = F.armyUpgrades[i];
                int cost = world.ArmyUpgradeCost(team, up);
                var at = world.ResearchingAt(team, up);
                string state = cost < 0 ? "\nComplete." : at != null ? $"\nResearching: {at.researchLeft:0}s left." : free == null ? "\nThe Armory is busy." : "";
                // A one-off (the Mech-Hunter Programme) has no levels to number.
                bool once = info.Levels == 1;
                into.Add(new CardAction
                {
                    kind = CardKind.ArmyUpgrade, armyUpgrade = up, hotkey = info.hotkey,
                    title = cost < 0 ? $"{info.name} (complete)" : once ? info.name : $"{info.name} {MechParts.Roman(lvl + 1)}",
                    body = info.blurb + state + (once ? "" : $"\nLevel {lvl} of {info.Levels}."),
                    cost = Mathf.Max(0, cost), time = cost < 0 ? 0f : info.time[lvl],
                    glyph = once ? "◎" : (info.armour ? "⛨" : "✹") + (lvl > 0 ? new string('·', lvl) : ""),
                    enabled = cost >= 0 && at == null && free != null && F.ore >= cost
                });
            }
            if (busy)
                into.Add(new CardAction { kind = CardKind.CancelResearch, hotkey = 'X', title = "Cancel research", body = "Refunds the research in progress.", glyph = "✕", enabled = true });
        }

        static string UpgradeGlyph(MechUpgrade up) => up switch
        {
            MechUpgrade.Servos => "»",
            MechUpgrade.Armour => "⛨",
            MechUpgrade.Weapons => "✹",
            MechUpgrade.Hardpoint => "+",
            _ => "◎"
        };

        public void Execute(CardAction c)
        {
            // Opening and closing a build card gives no order: allowed while paused.
            if (c.kind == CardKind.BuildMenu || c.kind == CardKind.MenuBack)
            {
                if (CanCommand) SetMenu(c.kind == CardKind.BuildMenu ? c.menu : BuildMenu.None);
                return;
            }
            if (!CanCommand || !InPlay) return;
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
                case CardKind.MechUpgrade:
                    foreach (var u in own) if (u.Type == UnitType.MechBay && u.Complete) { world.CmdMechUpgrade(u, c.upgrade); break; }
                    break;
                case CardKind.CancelUpgrade:
                    foreach (var u in own) if (u.Type == UnitType.MechBay) { world.CmdCancelMechUpgrade(u); break; }
                    break;
                case CardKind.ArmyUpgrade:
                    foreach (var u in own) if (u.Type == UnitType.Armory && u.Complete && u.researchLine < 0) { world.CmdArmoryResearch(u, c.armyUpgrade); break; }
                    break;
                case CardKind.CancelResearch:
                    foreach (var u in own) if (u.Type == UnitType.Armory && u.researchLine >= 0) { world.CmdCancelArmoryResearch(u); break; }
                    break;
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
            if (!CanCommand || !InPlay) return;
            var own = new List<Unit>(OwnSelection());
            if (own.Count == 0) return;
            if (own.TrueForAll(u => u.def.building)) world.CmdRally(own, world2D);
            else world.CmdMove(own, world2D, AttackMoveArmed);
            AttackMoveArmed = false;
            OrderMarker?.Invoke(world.Map.Ground(world2D), 0);
        }
    }
}
