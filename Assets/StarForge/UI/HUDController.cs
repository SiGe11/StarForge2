// HUDController.cs — binds the UI Toolkit HUD to the match: resources, minimap,
// selection, command card, alerts, the AI inspector and the menus.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class HUDController : MonoBehaviour
    {
        public GameWorld world;
        public PlayerController player;
        public RTSCamera rig;
        public GameBootstrap bootstrap;

        const int CardSlots = 12;
        const int MinimapRes = 256;

        VisualElement root, hud, titleScreen, pauseMenu, endScreen, inspector, tooltip, alerts, commandCard;
        VisualElement singleInfo, multiGrid, queue;
        Label oreLabel, supplyLabel, clockLabel, perfLabel, intelLabel;
        Label unitName, unitStats, unitStatus, selectionHint;
        Label tooltipTitle, tooltipCost, tooltipBody;
        Label inspStrategy, inspReason, inspStats;
        Label endTitle, endSubtitle, endStats, endDossier;
        Label difficultyBlurb, memoryStatus, controlsText, qualityBlurb, opponentText;
        Button inspectorButton;
        Button[] segments;
        Button[] qualitySegments;
        Toggle memoryToggle;
        Image portrait;
        BarElement hpBar;
        BarChartElement beliefChart, strategyChart, profileChart, endStyleChart;
        MinimapElement minimap;
        OverlayElement screenOverlay;
        bool overlayHadBox;

        void LateUpdate()
        {
            bool box = player != null && player.Dragging;
            if (box || overlayHadBox) screenOverlay.MarkDirtyRepaint();
            overlayHadBox = box;
        }

        void DrawScreenOverlay(Painter2D p, float w, float h)
        {
            if (player == null || !player.Dragging || root.panel == null) return;
            Vector2 a = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(player.DragStart.x, Screen.height - player.DragStart.y));
            Vector2 b = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(player.DragEnd.x, Screen.height - player.DragEnd.y));
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            Paint.Rect(p, min.x, min.y, max.x - min.x, max.y - min.y, new Color(0.31f, 0.82f, 1f, 0.08f));
            p.strokeColor = new Color(0.31f, 0.82f, 1f, 0.9f);
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(min);
            p.LineTo(new Vector2(max.x, min.y));
            p.LineTo(max);
            p.LineTo(new Vector2(min.x, max.y));
            p.ClosePath();
            p.Stroke();
        }

        readonly Button[] cardButtons = new Button[CardSlots];
        readonly Image[] cardIcons = new Image[CardSlots];
        readonly Label[] cardHotkeys = new Label[CardSlots];
        readonly Label[] cardCosts = new Label[CardSlots];
        readonly Label[] cardGlyphs = new Label[CardSlots];
        readonly List<CardAction> cards = new List<CardAction>(CardSlots);
        int hoveredCard = -1;

        Texture2D minimapTex;
        Color32[] minimapBase, minimapPixels;
        float minimapT, cardT, inspT, perfT, underAttackT = -99f;
        float fpsSmooth = 60f;
        bool inspectorOpen, selectionDirty = true;
        readonly Vector3[] corners = new Vector3[4];

        static readonly string[] BeliefLabels = { "Rushing", "Macro", "Turtling", "Harassing", "Expanding", "Teching" };
        static readonly string[] ProfileLabels = { "Aggression", "Expansion", "Defensive", "Harassment", "Teching" };

        // ------------------------------------------------------------ setup
        void Awake()
        {
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (rig == null) rig = FindAnyObjectByType<RTSCamera>();
            if (bootstrap == null) bootstrap = FindAnyObjectByType<GameBootstrap>();

            root = GetComponent<UIDocument>().rootVisualElement;
            hud = root.Q("hud");
            titleScreen = root.Q("titleScreen");
            pauseMenu = root.Q("pauseMenu");
            endScreen = root.Q("endScreen");
            inspector = root.Q("inspector");
            tooltip = root.Q("tooltip");
            alerts = root.Q("alerts");
            commandCard = root.Q("commandCard");
            singleInfo = root.Q("singleInfo");
            multiGrid = root.Q("multiGrid");
            queue = root.Q("queue");
            oreLabel = root.Q<Label>("oreLabel");
            supplyLabel = root.Q<Label>("supplyLabel");
            clockLabel = root.Q<Label>("clockLabel");
            perfLabel = root.Q<Label>("perfLabel");
            intelLabel = root.Q<Label>("intelLabel");
            unitName = root.Q<Label>("unitName");
            unitStats = root.Q<Label>("unitStats");
            unitStatus = root.Q<Label>("unitStatus");
            selectionHint = root.Q<Label>("selectionHint");
            tooltipTitle = root.Q<Label>("tooltipTitle");
            tooltipCost = root.Q<Label>("tooltipCost");
            tooltipBody = root.Q<Label>("tooltipBody");
            inspStrategy = root.Q<Label>("inspStrategy");
            inspReason = root.Q<Label>("inspReason");
            inspStats = root.Q<Label>("inspStats");
            endTitle = root.Q<Label>("endTitle");
            endSubtitle = root.Q<Label>("endSubtitle");
            endStats = root.Q<Label>("endStats");
            endDossier = root.Q<Label>("endDossier");
            difficultyBlurb = root.Q<Label>("difficultyBlurb");
            memoryStatus = root.Q<Label>("memoryStatus");
            qualityBlurb = root.Q<Label>("qualityBlurb");
            opponentText = root.Q<Label>("opponentText");
            controlsText = root.Q<Label>("controlsText");
            portrait = root.Q<Image>("portrait");
            hpBar = root.Q<BarElement>("hpBar");
            beliefChart = root.Q<BarChartElement>("beliefChart");
            strategyChart = root.Q<BarChartElement>("strategyChart");
            profileChart = root.Q<BarChartElement>("profileChart");
            endStyleChart = root.Q<BarChartElement>("endStyleChart");
            minimap = root.Q<MinimapElement>("minimap");
            screenOverlay = root.Q<OverlayElement>("screenOverlay");
            screenOverlay.DrawOverlay = DrawScreenOverlay;
            memoryToggle = root.Q<Toggle>("memoryToggle");
            inspectorButton = root.Q<Button>("inspectorButton");

            BuildCommandCard();
            WireMenus();
            WireMinimap();
            controlsText.text = MatchSettings.debugAI ? ControlsHelp + " · I AI inspector" : ControlsHelp;
            opponentText.text = OpponentHelp;

            // The AI's internals are a developer tool, not part of the game: a player
            // learns the opponent by playing it (and from "About the opponent").
            SetDisplay(inspectorButton, MatchSettings.debugAI);
            SetDisplay(memoryToggle, MatchSettings.debugAI);
            SetDisplay(memoryStatus, MatchSettings.debugAI);
            SetDisplay(root.Q("resetMemoryButton"), MatchSettings.debugAI);
            SetDisplay(root.Q("endDossierColumn"), MatchSettings.debugAI);
        }

        void OnEnable()
        {
            if (bootstrap != null)
            {
                bootstrap.StateChanged += OnStateChanged;
                bootstrap.MatchEnded += OnMatchEnded;
            }
            if (world != null) world.Event += OnWorldEvent;
            if (player != null)
            {
                player.SelectionChanged += MarkSelectionDirty;
                player.MenuRequested += OnMenuRequested;
                player.InspectorToggled += ToggleInspector;
                player.PointerOverUI = PointerOverUI;
            }
            if (rig != null) rig.BlocksEdgeScroll = PointerOverUI;
        }

        void OnDisable()
        {
            if (bootstrap != null)
            {
                bootstrap.StateChanged -= OnStateChanged;
                bootstrap.MatchEnded -= OnMatchEnded;
            }
            if (world != null) world.Event -= OnWorldEvent;
            if (player != null)
            {
                player.SelectionChanged -= MarkSelectionDirty;
                player.MenuRequested -= OnMenuRequested;
                player.InspectorToggled -= ToggleInspector;
            }
        }

        void Start()
        {
            CaptureMinimapBase();
            OnStateChanged(bootstrap != null ? bootstrap.State : MatchState.Menu);
        }

        void MarkSelectionDirty() => selectionDirty = true;

        /// <summary>True when the pointer is over an interactive HUD element.</summary>
        public bool PointerOverUI(Vector2 screen)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = panel.Pick(p);
            return picked != null && picked != root && picked != hud;
        }

        // ------------------------------------------------------------ screens
        void OnStateChanged(MatchState s)
        {
            Show(titleScreen, s == MatchState.Menu);
            Show(pauseMenu, s == MatchState.Paused);
            Show(endScreen, s == MatchState.Ended);
            hud.style.display = s == MatchState.Playing || s == MatchState.Paused || s == MatchState.Ended
                ? DisplayStyle.Flex : DisplayStyle.None;
            if (s == MatchState.Menu) RefreshTitle();
            if (s == MatchState.Playing)
            {
                intelLabel.text = IntelText();
                SetInspector(MatchSettings.debugAI && (MatchSettings.spectate || inspectorOpen));
            }
        }

        static void SetDisplay(VisualElement e, bool visible)
        {
            if (e != null) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void Show(VisualElement e, bool visible) =>
            e.EnableInClassList("screen--visible", visible);

        void OnMenuRequested() => bootstrap.TogglePause();

        void WireMenus()
        {
            segments = new[] { root.Q<Button>("diffRecruit"), root.Q<Button>("diffVeteran"), root.Q<Button>("diffCommander") };
            for (int i = 0; i < segments.Length; i++)
            {
                int idx = i;
                segments[i].clicked += () => { MatchSettings.difficulty = (AIDifficulty)idx; RefreshTitle(); };
            }
            qualitySegments = new[] { root.Q<Button>("qualityHigh"), root.Q<Button>("qualityBalanced"), root.Q<Button>("qualityBattery") };
            for (int i = 0; i < qualitySegments.Length; i++)
            {
                int idx = i;
                qualitySegments[i].clicked += () => { QualityController.Current = (SFQuality)idx; RefreshTitle(); };
            }

            memoryToggle.value = MatchSettings.aiMemory;
            memoryToggle.RegisterValueChangedCallback(e => { MatchSettings.aiMemory = e.newValue; RefreshTitle(); });

            root.Q<Button>("playButton").clicked += () => { MatchSettings.spectate = false; bootstrap.StartMatch(); };
            root.Q<Button>("spectateButton").clicked += () => { MatchSettings.spectate = true; bootstrap.StartMatch(); };
            root.Q<Button>("controlsButton").clicked += () =>
            {
                opponentText.RemoveFromClassList("controls-text--visible");
                controlsText.ToggleInClassList("controls-text--visible");
            };
            root.Q<Button>("opponentButton").clicked += () =>
            {
                controlsText.RemoveFromClassList("controls-text--visible");
                opponentText.ToggleInClassList("controls-text--visible");
            };
            root.Q<Button>("resetMemoryButton").clicked += () => { AIMemory.Reset(); RefreshTitle(); };
            root.Q<Button>("quitButton").clicked += Quit;

            root.Q<Button>("resumeButton").clicked += () => bootstrap.TogglePause();
            root.Q<Button>("restartButton").clicked += () => bootstrap.Restart();
            root.Q<Button>("surrenderButton").clicked += () => bootstrap.Surrender();
            root.Q<Button>("fullscreenButton").clicked += ToggleFullscreen;
            root.Q<Button>("menuReturnButton").clicked += () => bootstrap.ReturnToMenu();
            root.Q<Button>("quitButton2").clicked += Quit;

            root.Q<Button>("rematchButton").clicked += () => bootstrap.Restart();
            root.Q<Button>("endMenuButton").clicked += () => bootstrap.ReturnToMenu();

            root.Q<Button>("menuButton").clicked += () => bootstrap.TogglePause();
            inspectorButton.clicked += ToggleInspector;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void ToggleFullscreen() => DisplayModes.Toggle();

        void RefreshTitle()
        {
            for (int i = 0; i < segments.Length; i++)
                segments[i].EnableInClassList("segment--active", (int)MatchSettings.difficulty == i);
            switch (MatchSettings.difficulty)
            {
                case AIDifficulty.Recruit:
                    difficultyBlurb.text = "90 actions per minute, slow reactions. Still scouts, still reads you."; break;
                case AIDifficulty.Veteran:
                    difficultyBlurb.text = "180 APM with half-second reactions. A solid club player."; break;
                default:
                    difficultyBlurb.text = "330 APM with 0.22 s reactions — sized against a top StarCraft II professional."; break;
            }
            for (int i = 0; i < qualitySegments.Length; i++)
                qualitySegments[i].EnableInClassList("segment--active", (int)QualityController.Current == i);
            qualityBlurb.text = QualityController.Blurb(QualityController.Current);

            if (!MatchSettings.debugAI) return;
            var mem = AIMemory.Load();
            if (!MatchSettings.aiMemory) memoryStatus.text = "Memory off: every match starts from its default doctrine.";
            else if (mem.games == 0) memoryStatus.text = "It has never played you.";
            else memoryStatus.text =
                $"It has played you {mem.games} time{(mem.games == 1 ? "" : "s")} (won {mem.aiWins}). Last read: {mem.lastRead}.";
        }

        void ToggleInspector()
        {
            if (!MatchSettings.debugAI) return;
            inspectorOpen = !inspectorOpen;
            SetInspector(inspectorOpen || MatchSettings.spectate);
        }

        void SetInspector(bool open)
        {
            inspector.EnableInClassList("inspector--visible", open);
            inspectorButton.EnableInClassList("top-button--active", open);
        }

        string IntelText()
        {
            if (bootstrap?.AI == null) return "";
            string mode = MatchSettings.spectate ? "Spectating · " : "";
            if (!MatchSettings.debugAI) return $"{mode}Opponent: {bootstrap.AI.Difficulty}";
            int games = bootstrap.AI.Memory.games;
            string mem = !MatchSettings.aiMemory ? "memory off" : games == 0 ? "first meeting" : $"remembers {games} match{(games == 1 ? "" : "es")}";
            return $"{mode}Opponent: {bootstrap.AI.Difficulty} · {mem}";
        }

        // ------------------------------------------------------------ per frame
        void Update()
        {
            if (bootstrap == null || world == null) return;
            var state = bootstrap.State;
            if (state != MatchState.Playing && state != MatchState.Paused) return;
            float dt = Time.unscaledDeltaTime;

            var F = world.factions[player != null ? player.team : 0];
            oreLabel.text = F.ore.ToString();
            int cap = Mathf.Min(F.supplyCap, GameWorld.MaxSupply);
            supplyLabel.text = $"{F.supplyUsed} / {cap}";
            supplyLabel.EnableInClassList("resource-value--warn", F.supplyUsed >= cap);
            int t = Mathf.FloorToInt(world.time);
            clockLabel.text = $"{t / 60}:{t % 60:00}" + (Time.timeScale > 1.01f ? $"  x{Time.timeScale:0.#}" : "");

            fpsSmooth = Mathf.Lerp(fpsSmooth, 1f / Mathf.Max(1e-4f, dt), 0.05f);
            if ((perfT -= dt) <= 0f)
            {
                perfT = 0.5f;
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                perfLabel.text = $"{fpsSmooth:0} FPS · {(urp != null ? urp.renderScale : 1f) * 100f:0}%";
            }

            UpdateSelectionPanel();
            if ((cardT -= dt) <= 0f || selectionDirty) { cardT = 0.2f; RefreshCommandCard(); }
            if ((minimapT -= dt) <= 0f) { minimapT = 0.1f; UpdateMinimap(); }
            if (inspector.ClassListContains("inspector--visible") && (inspT -= dt) <= 0f) { inspT = 0.25f; UpdateInspector(); }
        }

        // ------------------------------------------------------------ selection
        void UpdateSelectionPanel()
        {
            var sel = player.Selection;
            bool single = sel.Count == 1;
            singleInfo.EnableInClassList("single-info--visible", single);
            multiGrid.EnableInClassList("multi-grid--visible", sel.Count > 1);
            selectionHint.style.display = sel.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (selectionDirty)
            {
                selectionDirty = false;
                multiGrid.Clear();
                if (sel.Count > 1)
                {
                    int shown = Mathf.Min(sel.Count, 30);
                    for (int i = 0; i < shown; i++)
                    {
                        var u = sel[i];
                        var tile = new VisualElement();
                        tile.AddToClassList("unit-tile");
                        var icon = new Image { image = u.def.icon, pickingMode = PickingMode.Ignore };
                        icon.AddToClassList("unit-tile-icon");
                        var bar = new BarElement { pickingMode = PickingMode.Ignore };
                        bar.AddToClassList("unit-tile-bar");
                        tile.Add(icon);
                        tile.Add(bar);
                        tile.userData = u;
                        tile.RegisterCallback<ClickEvent>(_ => player.SelectOnly(u));
                        multiGrid.Add(tile);
                    }
                }
                if (single)
                {
                    var u = sel[0];
                    portrait.image = u.def.icon;
                }
            }

            if (sel.Count > 1)
            {
                foreach (var child in multiGrid.Children())
                {
                    if (!(child.userData is Unit u) || !Unit.Live(u)) continue;
                    var bar = child.Q<BarElement>();
                    float f = u.hp / u.MaxHp;
                    bar.Value = f;
                    bar.FillColor = HealthColor(f);
                }
            }
            if (!single) return;

            var s = sel[0];
            if (!Unit.Live(s)) return;
            var d = s.def;
            string owner = s.team == 1 ? "  · ENEMY" : "";
            string rank = s.rank > 0 ? "  " + new string('★', s.rank) : "";
            unitName.text = d.displayName + rank + owner;
            float hpFrac = d.neutral ? 1f : s.hp / s.MaxHp;
            hpBar.Value = hpFrac;
            hpBar.FillColor = HealthColor(hpFrac);
            if (d.type == UnitType.Ore) unitStats.text = $"{s.oreLeft} ore remaining";
            else if (d.Armed)
                unitStats.text = $"HP {Mathf.CeilToInt(s.hp)}/{Mathf.CeilToInt(s.MaxHp)}  ·  DMG {d.damage * (1f + 0.15f * s.rank):0}  ·  RANGE {d.range:0}  ·  KILLS {s.kills}";
            else
                unitStats.text = $"HP {Mathf.CeilToInt(s.hp)}/{Mathf.CeilToInt(s.MaxHp)}" + (d.supplyGive > 0 ? $"  ·  +{d.supplyGive} supply" : "");
            unitStatus.text = StatusText(s);

            queue.Clear();
            if (s.team == player.team && s.queue.Count > 0)
                for (int i = 0; i < s.queue.Count; i++)
                {
                    var qd = Defs.Get(s.queue[i]);
                    var slot = new Image { image = qd.icon };
                    slot.AddToClassList("queue-slot");
                    if (i == 0)
                    {
                        slot.AddToClassList("queue-slot--active");
                        var prog = new BarElement();
                        prog.AddToClassList("queue-progress");
                        prog.Value = 1f - Mathf.Clamp01(s.queueTimer / Mathf.Max(0.01f, qd.buildTime));
                        prog.FillColor = new Color(0.31f, 0.82f, 1f);
                        slot.Add(prog);
                    }
                    queue.Add(slot);
                }
        }

        static Color HealthColor(float f) =>
            f > 0.6f ? new Color(0.35f, 0.9f, 0.55f) : f > 0.3f ? new Color(1f, 0.75f, 0.28f) : new Color(1f, 0.35f, 0.3f);

        static string StatusText(Unit u)
        {
            if (u.def.building)
            {
                if (!u.Complete) return $"Under construction · {u.buildProgress * 100f:0}%";
                if (u.queue.Count > 0) return $"Training {Defs.Get(u.queue[0]).displayName} · {u.queueTimer:0}s";
                if (u.def.Armed) return u.target != null ? "Engaging" : "Watching";
                return "Idle";
            }
            if (u.def.neutral) return "";
            switch (u.order)
            {
                case Order.Harvest: return u.working ? "Mining" : "Heading to ore";
                case Order.Return: return $"Returning {u.carrying} ore";
                case Order.Build: return u.working ? "Constructing" : "Moving to build site";
                case Order.Attack: return "Attacking";
                case Order.AttackMove: return u.target != null ? "Engaging" : "Attack-moving";
                case Order.Move: return "Moving";
                case Order.Hold: return "Holding position";
                default: return u.target != null ? "Engaging" : "Idle";
            }
        }

        // ------------------------------------------------------------ command card
        void BuildCommandCard()
        {
            for (int i = 0; i < CardSlots; i++)
            {
                int idx = i;
                var b = new Button();
                b.AddToClassList("card-button");
                var icon = new Image { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("card-icon");
                var glyph = new Label { pickingMode = PickingMode.Ignore };
                glyph.AddToClassList("card-glyph");
                var hk = new Label { pickingMode = PickingMode.Ignore };
                hk.AddToClassList("card-hotkey");
                var cost = new Label { pickingMode = PickingMode.Ignore };
                cost.AddToClassList("card-cost");
                b.Add(icon); b.Add(glyph); b.Add(hk); b.Add(cost);
                b.clicked += () => { if (idx < cards.Count) player.Execute(cards[idx]); };
                b.RegisterCallback<PointerEnterEvent>(_ => { hoveredCard = idx; UpdateTooltip(); });
                b.RegisterCallback<PointerLeaveEvent>(_ => { if (hoveredCard == idx) hoveredCard = -1; UpdateTooltip(); });
                commandCard.Add(b);
                cardButtons[i] = b; cardIcons[i] = icon; cardHotkeys[i] = hk; cardCosts[i] = cost; cardGlyphs[i] = glyph;
            }
        }

        void RefreshCommandCard()
        {
            player.BuildCard(cards);
            for (int i = 0; i < CardSlots; i++)
            {
                bool has = i < cards.Count;
                var b = cardButtons[i];
                b.EnableInClassList("card-button--empty", !has);
                b.SetEnabled(has);
                if (!has) continue;
                var c = cards[i];
                b.EnableInClassList("card-button--disabled", !c.enabled);
                cardIcons[i].image = c.icon;
                cardGlyphs[i].text = c.icon == null ? (c.glyph ?? "") : "";
                cardHotkeys[i].text = c.hotkey == '\0' ? "" : c.hotkey.ToString();
                cardCosts[i].text = c.cost > 0 ? c.cost.ToString() : "";
            }
            UpdateTooltip();
        }

        void UpdateTooltip()
        {
            bool show = hoveredCard >= 0 && hoveredCard < cards.Count;
            tooltip.EnableInClassList("tooltip--visible", show);
            if (!show) return;
            var c = cards[hoveredCard];
            tooltipTitle.text = c.hotkey != '\0' ? $"{c.title}  [{c.hotkey}]" : c.title;
            var sb = new StringBuilder();
            if (c.cost > 0) sb.Append($"{c.cost} ore");
            if (c.supply > 0) sb.Append($"   {c.supply} supply");
            if (c.time > 0f) sb.Append($"   {c.time:0}s");
            if (c.kind == CardKind.Build && !string.IsNullOrEmpty(c.glyph)) sb.Append(c.glyph);
            tooltipCost.text = sb.ToString();
            tooltipBody.text = c.body ?? "";
        }

        // ------------------------------------------------------------ alerts
        void OnWorldEvent(GameEvent e)
        {
            int me = player != null ? player.team : 0;
            switch (e.kind)
            {
                case GameEventKind.UnderAttack when e.team == me:
                    if (Time.unscaledTime - underAttackT > 10f && bootstrap.State == MatchState.Playing)
                    {
                        underAttackT = Time.unscaledTime;
                        Alert(e.unit != null && e.unit.def.building ? "Your base is under attack" : "Your forces are under attack", "alert--warn");
                    }
                    break;
                case GameEventKind.StructureComplete when e.team == me:
                    Alert($"{Defs.Get(e.type).displayName} complete", "alert--good");
                    break;
                case GameEventKind.Promoted when e.team == me:
                    Alert($"{Defs.Get(e.type).displayName} promoted to rank {e.scale:0}", "alert--good");
                    break;
                case GameEventKind.Refused when e.team == me:
                    Alert(e.text, "alert--warn");
                    break;
                case GameEventKind.Notice when e.team == me:
                    Alert(e.text, "alert--good");
                    break;
                case GameEventKind.Death when e.team == me && e.unit != null && e.unit.def.building:
                    Alert($"{Defs.Get(e.type).displayName} destroyed", "alert--warn");
                    break;
            }
        }

        void Alert(string text, string cls)
        {
            if (string.IsNullOrEmpty(text)) return;
            // The same message repeating (a supply block, say) is one situation, not
            // four: refresh the line that is already up instead of stacking copies.
            for (int i = alerts.childCount - 1; i >= 0; i--)
                if (alerts[i] is Label existing && existing.text == text)
                {
                    existing.RemoveFromClassList("alert--fading");
                    existing.schedule.Execute(() => existing.AddToClassList("alert--fading")).StartingIn(2600);
                    existing.schedule.Execute(() => existing.RemoveFromHierarchy()).StartingIn(3100);
                    return;
                }
            if (alerts.childCount >= 4) alerts.RemoveAt(0);
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("alert");
            l.AddToClassList(cls);
            alerts.Add(l);
            l.schedule.Execute(() => l.AddToClassList("alert--fading")).StartingIn(2600);
            l.schedule.Execute(() => l.RemoveFromHierarchy()).StartingIn(3100);
        }

        // ------------------------------------------------------------ minimap
        void WireMinimap()
        {
            minimap.DrawOverlay = DrawMinimapOverlay;
            minimap.RegisterCallback<PointerDownEvent>(OnMinimapPointer);
            minimap.RegisterCallback<PointerMoveEvent>(e => { if ((e.pressedButtons & 1) != 0) MinimapJump(e.localPosition); });
        }

        void OnMinimapPointer(PointerDownEvent e)
        {
            if (e.button == 0) MinimapJump(e.localPosition);
            else if (e.button == 1) player.MinimapCommand(MinimapToWorld(e.localPosition));
        }

        Vector2 MinimapToWorld(Vector2 local)
        {
            var r = minimap.contentRect;
            float s = world.MapSize;
            return new Vector2(Mathf.Clamp01(local.x / r.width) * s, (1f - Mathf.Clamp01(local.y / r.height)) * s);
        }

        void MinimapJump(Vector2 local) => rig.CenterOn(MinimapToWorld(local));

        /// <summary>Renders the map once, top-down, before any unit exists.</summary>
        void CaptureMinimapBase()
        {
            float size = world.MapSize;
            var go = new GameObject("MinimapCapture");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = size * 0.5f;
            cam.transform.SetPositionAndRotation(new Vector3(size * 0.5f, 300f, size * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.05f);
            cam.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            var rt = RenderTexture.GetTemporary(MinimapRes, MinimapRes, 24, RenderTextureFormat.ARGB32);
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            RenderSettings.fog = fog;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            minimapTex = new Texture2D(MinimapRes, MinimapRes, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            minimapTex.ReadPixels(new Rect(0, 0, MinimapRes, MinimapRes), 0, 0);
            minimapTex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);

            minimapBase = minimapTex.GetPixels32();
            minimapPixels = new Color32[minimapBase.Length];
            minimap.Texture = minimapTex;
        }

        float fogCompositeT;

        void UpdateMinimap()
        {
            if (minimapTex == null) return;
            // Fog composite at a lower rate than the unit overlay.
            if ((fogCompositeT -= 0.1f) <= 0f)
            {
                fogCompositeT = 0.3f;
                int team = player != null ? player.team : 0;
                float cellPerPx = (float)GameWorld.VIS / MinimapRes;
                for (int y = 0; y < MinimapRes; y++)
                {
                    int cz = Mathf.Min(GameWorld.VIS - 1, (int)(y * cellPerPx));
                    for (int x = 0; x < MinimapRes; x++)
                    {
                        int cx = Mathf.Min(GameWorld.VIS - 1, (int)(x * cellPerPx));
                        float k = MatchSettings.spectate ? 1f
                                : world.VisibleCell(team, cx, cz) != 0 ? 1f
                                : world.ExploredCell(team, cx, cz) != 0 ? 0.45f : 0.1f;
                        var c = minimapBase[y * MinimapRes + x];
                        minimapPixels[y * MinimapRes + x] = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);
                    }
                }
                minimapTex.SetPixels32(minimapPixels);
                minimapTex.Apply(false);
            }
            minimap.RepaintOverlay();
        }

        void DrawMinimapOverlay(Painter2D p, float w, float h)
        {
            if (world == null || !world.running) return;
            float s = world.MapSize;
            Vector2 ToUI(Vector2 wp) => new Vector2(wp.x / s * w, (1f - wp.y / s) * h);
            var blue = new Color(0.35f, 0.75f, 1f);
            var red = new Color(1f, 0.32f, 0.25f);
            var ore = new Color(0.6f, 0.95f, 1f, 0.8f);

            foreach (var u in world.units)
            {
                if (u == null || u.dying || u.def.type == UnitType.Boulder) continue;
                if (u.team == 1 && !MatchSettings.spectate && !u.visibleToPlayer && !(u.def.building && u.everSeenByPlayer)) continue;
                var c = ToUI(u.pos);
                float r = u.def.building ? 3.6f : (u.def.neutral ? 1.3f : 1.8f);
                var col = u.team == 0 ? blue : u.team == 1 ? red : ore;
                if (player.Selection.Contains(u)) col = Color.white;
                Paint.Rect(p, c.x - r, c.y - r, r * 2f, r * 2f, col);
            }

            foreach (var ping in world.pings)
            {
                if (ping.team != (player != null ? player.team : 0)) continue;
                var c = ToUI(ping.pos);
                float k = ping.t / 4f;
                p.strokeColor = new Color(1f, 0.3f, 0.2f, 1f - k);
                p.lineWidth = 2f;
                p.BeginPath();
                p.Arc(c, 4f + k * 18f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }

            if (rig != null && rig.GroundCorners(corners))
            {
                // The far edge of the view reaches well past the map at shallow
                // zoom; clamp to the map so the footprint stays a readable box.
                Vector2 Corner(int i) => ToUI(new Vector2(Mathf.Clamp(corners[i].x, 0f, s), Mathf.Clamp(corners[i].z, 0f, s)));
                p.strokeColor = new Color(1f, 1f, 1f, 0.85f);
                p.lineWidth = 1.5f;
                p.BeginPath();
                p.MoveTo(Corner(0));
                for (int i = 1; i < 4; i++) p.LineTo(Corner(i));
                p.ClosePath();
                p.Stroke();
            }

            // With the inspector open, show where the AI thinks you live and where it is looking.
            if (inspector.ClassListContains("inspector--visible") && bootstrap?.AI != null)
            {
                var dbg = bootstrap.AI.Dbg;
                var sc = ToUI(dbg.scoutTarget);
                p.strokeColor = new Color(1f, 0.72f, 0.28f, 0.9f);
                p.lineWidth = 1.5f;
                p.BeginPath();
                p.Arc(sc, 5f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
                var eb = ToUI(dbg.enemyBase);
                p.BeginPath();
                p.MoveTo(eb + new Vector2(-6f, -6f)); p.LineTo(eb + new Vector2(6f, 6f));
                p.MoveTo(eb + new Vector2(6f, -6f)); p.LineTo(eb + new Vector2(-6f, 6f));
                p.Stroke();
            }
        }

        // ------------------------------------------------------------ inspector
        void UpdateInspector()
        {
            var ai = bootstrap.AI;
            if (ai == null) return;
            var d = ai.Dbg;
            inspStrategy.text = AINames.Of(d.strategy).ToUpperInvariant();
            inspReason.text = d.reason;

            var bv = new float[BeliefLabels.Length];
            var bt = new string[BeliefLabels.Length];
            for (int i = 0; i < bv.Length; i++) { bv[i] = d.beliefs[i]; bt[i] = $"{d.beliefs[i] * 100f:0}%"; }
            beliefChart.SetData(BeliefLabels, bv, bt, (int)d.believed);

            int ns = (int)Strategy.Count;
            var labels = new string[ns];
            var sv = new float[ns];
            var st = new string[ns];
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < ns; i++)
                if (d.stratScores[i] > -1e29f) { lo = Mathf.Min(lo, d.stratScores[i]); hi = Mathf.Max(hi, d.stratScores[i]); }
            for (int i = 0; i < ns; i++)
            {
                labels[i] = AINames.Of((Strategy)i);
                bool legal = d.stratScores[i] > -1e29f;
                sv[i] = legal ? 0.08f + 0.92f * Mathf.InverseLerp(lo - 0.05f, hi + 1e-3f, d.stratScores[i]) : -1f;
                st[i] = legal ? d.stratScores[i].ToString("+0.00;-0.00") : "locked";
            }
            strategyChart.SetData(labels, sv, st, (int)d.strategy);

            var pv = new[] { d.aggression, d.expansion, d.defensive, d.harass, d.teching };
            var pt = new string[pv.Length];
            for (int i = 0; i < pv.Length; i++) pt[i] = $"{pv[i]:0.00}";
            profileChart.SetData(ProfileLabels, pv, pt, -1);

            string[] questions = { "their army", "their tech", "their expansions" };
            inspStats.text =
                $"uncertainty {d.entropy:0.00} bits · scouting confidence {d.scoutConfidence * 100f:0}%\n" +
                $"wants to learn about {questions[Mathf.Clamp(d.question, 0, 2)]}\n" +
                $"APM {d.apm:0} (peak {d.apmPeak:0}) · {d.actions} actions, {d.denied} refused by the cap\n" +
                $"plan switches {d.switches} · squad {d.squads} · volatility {d.volatility:0.00}\n" +
                (d.backend == "cpu"
                    ? $"influence field: CPU {d.influenceMs:0.00} ms"
                    : $"influence field: {d.backend}, {d.influenceMs:0} ms dispatch→readback");
        }

        // ------------------------------------------------------------ end of match
        void OnMatchEnded(int winner)
        {
            int me = player != null ? player.team : 0;
            bool won = winner == me;
            if (MatchSettings.spectate)
            {
                endTitle.text = winner == 0 ? "BLUE WINS" : "RED WINS";
                endTitle.EnableInClassList("end-title--defeat", winner == 1);
            }
            else
            {
                endTitle.text = won ? "VICTORY" : "DEFEAT";
                endTitle.EnableInClassList("end-title--defeat", !won);
            }
            int t = Mathf.FloorToInt(world.time);
            endSubtitle.text = $"{t / 60}:{t % 60:00} · opponent {bootstrap.AI.Difficulty}";

            var F = world.factions[me];
            var E = world.factions[1 - me];
            endStats.text =
                $"Ore mined          {F.oreMined}   vs {E.oreMined}\n" +
                $"Units produced     {F.unitsProduced}   vs {E.unitsProduced}\n" +
                $"Structures built   {F.structuresBuilt}   vs {E.structuresBuilt}\n" +
                $"Enemy losses       {F.killed}\n" +
                $"Your losses        {F.lost}";

            if (!MatchSettings.debugAI) return;   // the dossier is the AI's internal read of you
            var ai = bootstrap.AI;
            var vals = new float[BeliefLabels.Length];
            var txt = new string[BeliefLabels.Length];
            for (int i = 0; i < vals.Length; i++)
            {
                vals[i] = ai.StyleShare((PlayerStrat)i);
                txt[i] = $"{vals[i] * 100f:0}%";
            }
            endStyleChart.SetData(BeliefLabels, vals, txt, (int)ai.DominantStyle());

            int favourite = 0;
            for (int i = 1; i < (int)Strategy.Count; i++)
                if (ai.Selector.timeIn[i] > ai.Selector.timeIn[favourite]) favourite = i;
            var sb = new StringBuilder();
            sb.Append($"It read you as mostly {AINames.Of(ai.DominantStyle())}, and spent most of the match on ");
            sb.Append($"{AINames.Of((Strategy)favourite)} (switched plans {ai.Dbg.switches} times).");
            if (MatchSettings.spectate) sb.Append("\nSpectated matches are not remembered.");
            else if (MatchSettings.aiMemory)
                sb.Append($"\nIt will remember this. Matches on record: {ai.Memory.games}. Next time it starts from what worked against you.");
            else sb.Append("\nMemory was off, so it will not remember this match.");
            endDossier.text = sb.ToString();
        }

        const string ControlsHelp =
            "Camera   WASD / arrows / screen edge pan · Q E rotate · wheel zoom · middle-drag · Space centre\n" +
            "Select   click · drag box · double-click type · Shift add · Ctrl+1-0 group · 1-0 recall · F2 army\n" +
            "Orders   right-click move/attack/mine · R attack-move · C stop · H hold\n" +
            "Build    Digger: B bunkhouse · G garrison · V workshop · N sentinel · F foundry\n" +
            "Train    Foundry U · Garrison T K · Workshop M · X cancel\n" +
            "Other    P pause · , . game speed · Esc menu";

        const string OpponentHelp =
            "It plays by your rules. It sees only what its own units see, gives orders through the same commands you do, " +
            "and has a limited number of actions a minute: about 90 on Recruit, 180 on Veteran and 330 on Commander, " +
            "with reactions from nearly a second down to a fifth of one.\n\n" +
            "It scouts. It keeps a scout out whenever its picture of you is going stale, and a Skimmer is its favourite for " +
            "the job. Kill the scout or hide your army and it has to guess.\n\n" +
            "It reads you. From what it has seen it decides whether you are rushing, harassing, turtling, expanding, teching " +
            "or building a big economy, and picks a plan against that: an early Trooper rush, raids on your Diggers, a timing " +
            "push, a Sentinel wall while it techs to Maulers, a second ore line, a counter-attack while your army is away, " +
            "or a feint. When its picture of you changes, so does its plan.\n\n" +
            "It fights like a player. It focuses fire on the weakest target that can shoot back, pulls its army home when a " +
            "fight turns against it, sends raiders at your workers rather than your army, and comes back to defend when you hit its base.\n\n" +
            "It does not play the same match twice. Each match it opens differently and leans its own way -- more Troopers or more " +
            "Maulers, an early push or a late one. Its attacks go for different things (your base, your production, an outlying " +
            "Foundry, your Diggers), come in straight or round a flank, gather before they go in, and sometimes split to hit two " +
            "places at once. A plan that is not paying off is dropped, and a way in that you beat is not tried again soon.\n\n" +
            "It remembers you. Between matches it keeps a record of how you tend to play and which of its plans worked against " +
            "you, and each new match starts from that. It still scouts every game, so if you change how you play, it will notice.";
    }
}
