// MenuController.cs — the menus. At the title, over the live battlefield (the camera
// drifting through slow shots): a splash, the main menu, Operation setup, Settings, the
// Field manual, the Service record and Credits. In a match: the pause menu and the
// after-action report. One page stack; arrows / WASD, Enter and Esc or the mouse drive it.
//
// Lives on the HUD's UIDocument (HUDController adds it), in StarForgeMenu.uxml, which the
// HUD's UXML instances. It never touches the simulation: matches start, restart and end
// only through GameBootstrap, settings go to their components (MenuData), and a scene
// reload is covered by a veil that the next scene's controller lifts.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
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
    [DefaultExecutionOrder(10)]
    public sealed class MenuController : MonoBehaviour
    {
        // ---- carried across scene loads (rematch, main menu, new map); cleared at every play entry
        static bool splashSeen, veilOnLoad, speedOnLoad, difficultyRestored;
        static string veilOnLoadTitle, openOnLoad;
        static bool openSpectate;
        /// <summary>The match was launched from Operation setup's Deploy (or is its rematch):
        /// only such a match goes into the service record.</summary>
        public static bool MatchFromMenu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            splashSeen = veilOnLoad = speedOnLoad = openSpectate = MatchFromMenu = false;
            difficultyRestored = false;
            veilOnLoadTitle = openOnLoad = null;
        }

        // ------------------------------------------------------------ model
        sealed class Nav
        {
            public VisualElement el;
            public Action activate;
            public Action<int> adjust;
            public string title, desc;
        }

        sealed class Page
        {
            public string name;
            public VisualElement el;
            public readonly List<Nav> items = new List<Nav>();
            public int focus;
            public bool visible, front;
            public int hideToken;
            public Action onShow;
            /// <summary>Rows' own refreshers: cleared with the rows (ResetItems), so a rebuilt page does not pile them up.</summary>
            public readonly List<Action> refresh = new List<Action>();
            public Action<Nav> onFocus;
            public Action<int> tab;
            public ScrollView scroll;
            public string[] hints;
        }

        struct Timer { public float at; public int frame; public Action act; }

        GameWorld world;
        GameBootstrap bootstrap;
        RTSCamera rig;
        PlayerController player;
        AudioDirector audioDir;

        VisualElement root, hud, backdrop, deep, dip, footer, footerHints, modal, splash, veil, veilFill;
        Label veilTitle, veilTip, splashPrompt, footerVersion, perfLabel;
        SFEmblem mainEmblem, splashEmblem, veilEmblem;
        VisualElement splashArt, veilArt;
        Page main, setup, settings, manual, record, credits, pause, debrief;
        Page[] pages;
        readonly List<Page> stack = new List<Page>();
        readonly List<Timer> timers = new List<Timer>();

        // the confirm dialog
        Label confirmTitle, confirmBody;
        Button confirmOk, confirmCancel;
        Action confirmAction;
        bool modalOpen;
        int modalFocus;

        // camera and settings baselines (the scene's own values)
        float camYaw = 45f, camDist = 72f, basePan = 1.1f, baseRotate = 90f, baseEdge = 24f;
        float hoverSoundAt, holdT, repeatT, veilT, creditsY, splashBlinkT, splashArtT;
        bool deploying, splashOut;
        int lastDir;

        // ------------------------------------------------------------ setup
        void Awake()
        {
            try { Build(); }
            catch (Exception e)
            {
                // A layout that did not import (an XML comment with '--' in it did, twice) must not take the game with it:
                // say what failed, once, and let the match start without the menus rather than leave a title nothing can leave
                // (GameBootstrap.Start reads skipMenu after every Awake) or throw on every frame.
                Debug.LogError($"[StarForge] The menu could not be built ({e.GetType().Name}: {e.Message}); the match starts without it. " +
                               $"Check that StarForgeMenu.uxml and .uss imported cleanly (an XML comment may not contain '--').\n{e.StackTrace}");
                MatchSettings.skipMenu = true;
                enabled = false;
            }
        }

        void Build()
        {
            world = FindAnyObjectByType<GameWorld>();
            bootstrap = FindAnyObjectByType<GameBootstrap>();
            rig = FindAnyObjectByType<RTSCamera>();
            player = FindAnyObjectByType<PlayerController>();
            audioDir = FindAnyObjectByType<AudioDirector>();
            if (rig != null)
            {
                camYaw = rig.yaw; camDist = rig.distance;
                basePan = rig.panSpeed; baseRotate = rig.rotateSpeed; baseEdge = rig.edgeBand;
            }

            root = GetComponent<UIDocument>().rootVisualElement;
            backdrop = root.Q("menuBackdrop");
            deep = root.Q("menuDeep");
            dip = root.Q("menuDip");
            footer = root.Q("menuFooter");
            footerHints = root.Q("footerHints");
            footerVersion = root.Q<Label>("footerVersion");
            modal = root.Q("confirmDialog");
            splash = root.Q("menuSplash");
            splashPrompt = root.Q<Label>("splashPrompt");
            veil = root.Q("menuVeil");
            veilFill = root.Q("veilFill");
            veilTitle = root.Q<Label>("veilTitle");
            veilTip = root.Q<Label>("veilTip");
            perfLabel = root.Q<Label>("perfLabel");
            hud = root.Q("hud");
            mainEmblem = root.Q<SFEmblem>("mainEmblem");
            splashEmblem = root.Q<SFEmblem>("splashEmblem");
            veilEmblem = root.Q<SFEmblem>("veilEmblem");
            splashArt = root.Q("splashArt");
            veilArt = root.Q("veilArt");
            confirmTitle = root.Q<Label>("confirmTitle");
            confirmBody = root.Q<Label>("confirmBody");
            confirmOk = root.Q<Button>("confirmOk");
            confirmCancel = root.Q<Button>("confirmCancel");

            footerVersion.text = $"STARFORGE {Application.version}  ·  UNITY {Application.unityVersion}";
            veil.pickingMode = PickingMode.Ignore;
            confirmOk.focusable = confirmCancel.focusable = false;
            confirmOk.clicked += () => CloseModal(true);
            confirmCancel.clicked += () => CloseModal(false);
            confirmOk.RegisterCallback<PointerEnterEvent>(_ => SetModalFocus(1));
            confirmCancel.RegisterCallback<PointerEnterEvent>(_ => SetModalFocus(0));
            splash.RegisterCallback<PointerDownEvent>(_ => DismissSplash());

            // Picked last in a built player only: the Editor's evaluation and trials set the
            // opponent themselves, and statics outlive a play session there.
            if (!Application.isEditor && !difficultyRestored)
            {
                difficultyRestored = true;
                int d = GameSettings.SavedDifficulty;
                if (d >= 0 && d <= 2 && !MatchSettings.skipMenu) MatchSettings.difficulty = (AIDifficulty)d;
            }

            BuildMain();
            BuildSetup();
            BuildSettings();
            BuildManual();
            BuildRecord();
            BuildCredits();
            BuildPause();
            BuildDebrief();
            pages = new[] { main, setup, settings, manual, record, credits, pause, debrief };
            foreach (var b in root.Q("menuRoot").Query<Button>().ToList()) b.focusable = false;
        }

        void OnEnable()
        {
            if (bootstrap != null)
            {
                bootstrap.StateChanged += OnStateChanged;
                bootstrap.MatchEnded += OnMatchEnded;
                bootstrap.MatchStarted += OnMatchStarted;
            }
            if (player != null) player.PausedEscape = OnPausedEscape;
        }

        void OnDisable()
        {
            GameSettings.Flush();
            if (intro != null)
            {
                // The menu goes with its scene, or play is stopping: the clip must not outlive it, nor its hush.
                intro.Abort();
                intro = null;
                if (audioDir != null) audioDir.masterVolume = GameSettings.MasterVolume;
            }
            if (bootstrap != null)
            {
                bootstrap.StateChanged -= OnStateChanged;
                bootstrap.MatchEnded -= OnMatchEnded;
                bootstrap.MatchStarted -= OnMatchStarted;
            }
            if (player != null && player.PausedEscape == (Func<bool>)OnPausedEscape) player.PausedEscape = null;
            if (rig != null) rig.inputLocked = false;
        }

        void Start()
        {
            ApplySettings();
            if (veilOnLoad)
            {
                veilOnLoad = false;
                ShowVeil(veilOnLoadTitle ?? "", true);
                HideVeil(0.35f);
            }
            OnStateChanged(bootstrap != null ? bootstrap.State : MatchState.Menu);
            if (openOnLoad == "setup" && bootstrap != null && bootstrap.State == MatchState.Menu)
            {
                splashSeen = true;
                HideSplash(true);
                OpenSetup(openSpectate);
            }
            openOnLoad = null;
        }

        /// <summary>Puts the saved settings on the camera, the audio and the top bar.</summary>
        void ApplySettings()
        {
            if (rig != null)
            {
                rig.panSpeed = basePan * GameSettings.PanSpeed;
                rig.rotateSpeed = baseRotate * GameSettings.RotateSpeed;
                rig.edgeBand = GameSettings.EdgePan ? baseEdge : 0f;
            }
            if (audioDir != null)
            {
                audioDir.masterVolume = GameSettings.MasterVolume;
                audioDir.musicVolume = GameSettings.MusicVolume;
            }
            if (perfLabel != null) perfLabel.style.display = GameSettings.ShowPerformance ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------ state
        void OnStateChanged(MatchState s)
        {
            CloseModal(false);
            switch (s)
            {
                case MatchState.Menu:
                    MatchFromMenu = false;   // only Deploy (and its rematches) sets it
                    SetRoot(main);
                    if (!splashSeen) ShowSplash();
                    StartAttract();
                    break;
                case MatchState.Paused:
                    SetRoot(pause);
                    break;
                case MatchState.Ended:
                    SetRoot(debrief);
                    SetViewField(false);
                    break;
                default:
                    ClearPages();
                    HideSplash(true);
                    break;
            }
            UpdateChrome();
        }

        void OnMatchStarted()
        {
            if (attractOn && attractDrove && rig != null && !rig.scripted && rig.Moves == attractMoves && world != null && world.Map != null)
                rig.Snap(world.Map.StartPos(player != null ? player.team : 0), camYaw, camDist);
            attractOn = attractDrove = false;
            if (dip != null) dip.RemoveFromClassList("m-dip--on");
            if (speedOnLoad && bootstrap != null)
            {
                speedOnLoad = false;
                bootstrap.SetSpeed(GameSettings.Speeds[GameSettings.StartSpeed]);
            }
        }

        /// <summary>The backdrop, the footer and the camera lock follow whatever is showing.</summary>
        void UpdateChrome()
        {
            var top = Top;
            bool front = top != null && top.front;
            backdrop.EnableInClassList("m-backdrop--visible", front || (bootstrap != null && bootstrap.State == MatchState.Menu));
            deep.EnableInClassList("m-deep--on", front && top != main);
            footer.EnableInClassList("m-footer--visible", top != null && !viewingField);
            if (rig != null) rig.inputLocked = top != null && !viewingField;
            // The HUD's panels under the pause menu and the report read as empty boxes: hidden
            // there (visibility only; HUDController owns its display), back to view the field.
            if (hud != null) hud.style.visibility = top != null && !top.front && !viewingField ? Visibility.Hidden : Visibility.Visible;
            mainEmblem.Animate = top == main;
            UpdateHints();
        }

        Page Top => stack.Count > 0 ? stack[stack.Count - 1] : null;

        // ------------------------------------------------------------ the page stack
        Page NewPage(string name, bool front, params string[] hints)
        {
            var p = new Page { name = name, el = root.Q(name), front = front, hints = hints };
            return p;
        }

        void SetRoot(Page p)
        {
            for (int i = stack.Count - 1; i >= 0; i--) if (stack[i] != p) HidePage(stack[i]);
            stack.Clear();
            stack.Add(p);
            ShowPage(p);
        }

        void ClearPages()
        {
            foreach (var p in stack) HidePage(p);
            stack.Clear();
        }

        void Push(Page p)
        {
            if (Top == p) return;
            if (Top != null) HidePage(Top);
            stack.Remove(p);
            stack.Add(p);
            ShowPage(p);
            Sound(AudioDirector.MenuCue.Open);
            UpdateChrome();
        }

        void Pop()
        {
            if (stack.Count <= 1) return;
            var p = Top;
            stack.RemoveAt(stack.Count - 1);
            HidePage(p);
            ShowPage(Top);
            Sound(AudioDirector.MenuCue.Back);
            UpdateChrome();
        }

        void ShowPage(Page p)
        {
            p.visible = true;
            p.hideToken++;
            p.el.AddToClassList("m-page--shown");
            After(0f, () => { if (p.visible) p.el.AddToClassList("m-page--in"); });
            p.onShow?.Invoke();
            foreach (var r in p.refresh) r();
            SetFocus(p, Mathf.Clamp(p.focus, 0, Mathf.Max(0, p.items.Count - 1)), false);
            if (p == credits) creditsY = 0f;
        }

        void HidePage(Page p)
        {
            GameSettings.Flush();   // a page may have changed settings (they are saved when it closes, not on each step)
            p.visible = false;
            p.el.RemoveFromClassList("m-page--in");
            int token = ++p.hideToken;
            After(0.24f, () => { if (!p.visible && p.hideToken == token) p.el.RemoveFromClassList("m-page--shown"); });
            if (p == setup && mapPreview != null) mapPreview.Animate = false;
        }

        // ------------------------------------------------------------ navigation
        /// <summary>Adds a nav target and wires its hover once. Pages that rebuild their rows put
        /// their fixed buttons back with <c>page.items.Add</c>, never by registering them again.</summary>
        Nav Register(Page page, Nav nav)
        {
            page.items.Add(nav);
            nav.el.RegisterCallback<PointerEnterEvent>(_ => Hover(page, nav));
            return nav;
        }

        void Hover(Page page, Nav nav)
        {
            int idx = page.items.IndexOf(nav);
            if (modalOpen || Top != page || idx < 0 || idx == page.focus) return;
            SetFocus(page, idx, false);
            if (Time.unscaledTime - hoverSoundAt > 0.04f) { hoverSoundAt = Time.unscaledTime; Sound(AudioDirector.MenuCue.Hover); }
        }

        void SetFocus(Page page, int idx, bool scroll)
        {
            if (page.items.Count == 0) { page.focus = 0; return; }
            idx = Mathf.Clamp(idx, 0, page.items.Count - 1);
            for (int i = 0; i < page.items.Count; i++)
            {
                bool on = i == idx;
                page.items[i].el.EnableInClassList("m-focus", on);
                page.items[i].el.EnableInClassList("m-nav-item--focus", on);
            }
            page.focus = idx;
            var nav = page.items[idx];
            if (scroll && page.scroll != null && page.scroll.Contains(nav.el)) page.scroll.ScrollTo(nav.el);
            page.onFocus?.Invoke(nav);
        }

        void Move(int dir)
        {
            var p = Top;
            if (p == null) return;
            // A page of reading (the opponent, the keys): the arrows scroll it.
            if (p.scroll != null && !ItemIn(p, p.scroll)) { p.scroll.scrollOffset += new Vector2(0f, dir * 90f); return; }
            if (p.items.Count == 0) return;
            int n = p.items.Count;
            // Skip a row the page hides (Surrender while observing): focused, Enter still ran it.
            int to = p.focus;
            for (int step = 0; step < n; step++)
            {
                to = (to + dir + n) % n;
                var el = p.items[to].el;
                // The inline style is the page's own hide (RefreshPause), there at once; the resolved one
                // catches a class's, but only after the next style pass.
                if (el == null || (el.style.display.value != DisplayStyle.None && el.resolvedStyle.display != DisplayStyle.None)) break;
            }
            SetFocus(p, to, true);
            Sound(AudioDirector.MenuCue.Hover);
        }

        static bool ItemIn(Page p, VisualElement container)
        {
            foreach (var n in p.items) if (container.Contains(n.el)) return true;
            return false;
        }

        void Adjust(int dir)
        {
            var p = Top;
            if (p == null || p.items.Count == 0) return;
            var nav = p.items[p.focus];
            if (nav.adjust != null) { nav.adjust(dir); Sound(AudioDirector.MenuCue.Hover); }
        }

        void Activate(Page page, Nav nav)
        {
            if (modalOpen || deploying || Top != page) return;
            int idx = page.items.IndexOf(nav);
            if (idx >= 0 && idx != page.focus) SetFocus(page, idx, false);
            if (nav.activate != null) { Sound(AudioDirector.MenuCue.Select); nav.activate(); }
            else if (nav.adjust != null) { Sound(AudioDirector.MenuCue.Hover); nav.adjust(1); }
        }

        void Back()
        {
            if (modalOpen) { CloseModal(false); return; }
            var state = bootstrap != null ? bootstrap.State : MatchState.Menu;
            if (state == MatchState.Ended && stack.Count <= 1) { SetViewField(!viewingField); return; }
            if (stack.Count > 1) { Pop(); return; }
            if (state == MatchState.Menu && Top == main) AskQuit();
        }

        /// <summary>PlayerController asks this when Esc is pressed while paused.</summary>
        bool OnPausedEscape()
        {
            if (deploying) return true;   // a reload is under way: nothing resumes under the veil
            if (modalOpen) { CloseModal(false); return true; }
            if (stack.Count > 1) { Pop(); return true; }
            return false;
        }

        // ------------------------------------------------------------ per frame
        void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            RunTimers(now);
            if (veil.ClassListContains("m-veil--shown"))
            {
                veilT += dt;
                veilFill.style.width = Length.Percent(Mathf.Repeat(veilT * 0.9f, 1f) * 100f);
                Drift(veilArt, veilT / 20f, 0.04f);
            }
            AnimatePortraits(now, dt);
            UpdateAttract(Mathf.Min(dt, 0.1f));
            if (Top == credits) ScrollCredits(dt);
            if (intro != null)
            {
                intro.Tick(dt);
                HushFor(intro);
                if (intro.Done) intro = null;
            }
            if (splash.ClassListContains("m-splash--visible") && !splashOut)
            {
                splashBlinkT += dt;
                splashArtT += dt;
                Drift(splashArt, splashArtT / 40f, 0.08f);
                splashPrompt.style.opacity = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(splashBlinkT * 3.2f));
            }
            HandleKeys(now);
        }

        void HandleKeys(float now)
        {
            var kb = Keyboard.current;
            if (kb == null || deploying) return;
            var state = bootstrap != null ? bootstrap.State : MatchState.Menu;

            if (intro != null)
            {
                if (kb.anyKey.wasPressedThisFrame) intro.Skip();   // the same key does not also leave the title
                return;
            }
            if (splash.ClassListContains("m-splash--visible") && !splashOut)
            {
                if (kb.anyKey.wasPressedThisFrame) DismissSplash();
                return;
            }
            if (Top == null && !modalOpen) return;

            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
            bool down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool submit = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            bool esc = kb.escapeKey.wasPressedThisFrame;
            int lr = Held(kb.leftArrowKey, kb.aKey, kb.rightArrowKey, kb.dKey, now);
            bool lrPressed = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool shift = kb.shiftKey.isPressed;
            int tab = kb.tabKey.wasPressedThisFrame ? (shift ? -1 : 1) : kb.qKey.wasPressedThisFrame ? -1 : kb.eKey.wasPressedThisFrame ? 1 : 0;

            if (modalOpen)
            {
                if (esc && state != MatchState.Paused) CloseModal(false);
                else if (lrPressed || up || down || tab != 0) { SetModalFocus(1 - modalFocus); Sound(AudioDirector.MenuCue.Hover); }
                else if (submit) CloseModal(modalFocus == 1);
                return;
            }
            if (esc && state != MatchState.Paused) { Back(); return; }
            if (viewingField) return;
            if (tab != 0 && Top.tab != null) { Top.tab(tab); Sound(AudioDirector.MenuCue.Hover); return; }
            if (up) Move(-1);
            else if (down) Move(1);
            else if (lr != 0) Adjust(lr);
            else if (submit && Top.items.Count > 0) Activate(Top, Top.items[Top.focus]);
        }

        /// <summary>Left/right with key repeat (a held arrow steps a slider).</summary>
        int Held(ButtonControl l1, ButtonControl l2, ButtonControl r1, ButtonControl r2, float now)
        {
            int dir = (r1.isPressed || r2.isPressed ? 1 : 0) - (l1.isPressed || l2.isPressed ? 1 : 0);
            bool pressed = l1.wasPressedThisFrame || l2.wasPressedThisFrame || r1.wasPressedThisFrame || r2.wasPressedThisFrame;
            if (dir == 0) { lastDir = 0; return 0; }
            if (pressed || dir != lastDir) { lastDir = dir; holdT = now; repeatT = now + 0.38f; return dir; }
            if (now >= repeatT) { repeatT = now + 0.07f; return dir; }
            return 0;
        }

        void After(float seconds, Action act) =>
            timers.Add(new Timer { at = Time.unscaledTime + seconds, frame = Time.frameCount, act = act });

        void RunTimers(float now)
        {
            int frame = Time.frameCount;
            for (int i = 0; i < timers.Count; i++)
            {
                var t = timers[i];
                if (t.frame >= frame || now < t.at) continue;
                timers.RemoveAt(i--);
                t.act();
            }
        }

        void Sound(AudioDirector.MenuCue cue)
        {
            if (audioDir != null) audioDir.Menu(cue, GameSettings.InterfaceVolume);
        }

        void UpdateHints()
        {
            footerHints.Clear();
            var top = modalOpen ? null : Top;
            if (modalOpen) { Hint("←→", "CHOOSE"); Hint("ENTER", "CONFIRM"); Hint("ESC", "CANCEL"); return; }
            if (top == null) return;
            if (top.hints != null)
                for (int i = 0; i + 1 < top.hints.Length; i += 2) Hint(top.hints[i], top.hints[i + 1]);
        }

        void Hint(string key, string label)
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore };
            h.AddToClassList("m-hint");
            h.Add(Key(key));
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("m-hint-label");
            h.Add(l);
            footerHints.Add(h);
        }

        static Label Key(string text)
        {
            var k = new Label(text) { pickingMode = PickingMode.Ignore };
            k.AddToClassList("m-key");
            return k;
        }

        static Label Text(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in cls.Split(' ')) if (c.Length > 0) l.AddToClassList(c);
            return l;
        }

        // ------------------------------------------------------------ building blocks
        /// <summary>A large menu entry: index, title and (when focused) a line of description.</summary>
        Nav NavItem(Page page, VisualElement parent, string index, string title, string desc, Action activate, string name = null, bool danger = false)
        {
            var b = new Button { name = name, focusable = false };
            b.AddToClassList("m-nav-item");
            if (danger) b.AddToClassList("m-nav-item--danger");
            var band = new SFShade { From = new Color(0.31f, 0.82f, 1f, 0.17f), To = new Color(0.31f, 0.82f, 1f, 0f), Direction = SFShade.Mode.Horizontal, Power = 0.9f };
            band.AddToClassList("m-nav-band");
            b.Add(band);
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("m-nav-bar");
            b.Add(bar);
            b.Add(Text(index, "m-nav-index"));
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.AddToClassList("m-nav-text");
            col.Add(Text(title, "m-nav-title"));
            if (!string.IsNullOrEmpty(desc)) col.Add(Text(desc, "m-nav-desc"));
            b.Add(col);
            parent.Add(b);
            var nav = Register(page, new Nav { el = b, activate = activate, title = title, desc = desc });
            b.clicked += () => Activate(page, nav);
            return nav;
        }

        /// <summary>A plain button already in the UXML, made a nav target.</summary>
        Nav ButtonNav(Page page, string name, Action activate)
        {
            var b = root.Q<Button>(name);
            b.focusable = false;
            var nav = Register(page, new Nav { el = b, activate = activate, title = b.text });
            b.clicked += () => Activate(page, nav);
            return nav;
        }

        VisualElement Row(string label, out VisualElement control)
        {
            var row = new VisualElement();
            row.AddToClassList("m-setrow");
            row.Add(Text(label, "m-setrow-label"));
            control = new VisualElement { pickingMode = PickingMode.Ignore };
            control.AddToClassList("m-setrow-control");
            row.Add(control);
            return row;
        }

        /// <summary>A setting chosen from a short list: ‹ VALUE › with a pip per option.</summary>
        Nav SelectorRow(Page page, VisualElement parent, string label, string desc, string[] options,
                        Func<int> get, Action<int> set, string name = null)
        {
            var row = Row(label, out var control);
            if (name != null) row.name = name;
            var sel = new VisualElement { pickingMode = PickingMode.Ignore };
            sel.AddToClassList("m-selector");
            var left = Text("‹", "m-arrow");
            var right = Text("›", "m-arrow");
            left.pickingMode = right.pickingMode = PickingMode.Position;
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.AddToClassList("m-value-col");
            var value = Text("", "m-value");
            var pips = new VisualElement { pickingMode = PickingMode.Ignore };
            pips.AddToClassList("m-pips");
            var pipEls = new VisualElement[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                pipEls[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                pipEls[i].AddToClassList("m-pip");
                pips.Add(pipEls[i]);
            }
            col.Add(value);
            col.Add(pips);
            sel.Add(left);
            sel.Add(col);
            sel.Add(right);
            control.Add(sel);
            parent.Add(row);

            void Show()
            {
                int v = Mathf.Clamp(get(), 0, options.Length - 1);
                value.text = options[v];
                for (int i = 0; i < pipEls.Length; i++) pipEls[i].EnableInClassList("m-pip--on", i == v);
            }
            void Step(int dir)
            {
                int n = options.Length;
                set((Mathf.Clamp(get(), 0, n - 1) + dir + n) % n);
                Show();
            }
            Show();
            var nav = Register(page, new Nav { el = row, adjust = Step, title = label, desc = desc });
            row.RegisterCallback<ClickEvent>(_ => Activate(page, nav));
            left.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); if (!modalOpen && Top == page) { SetFocus(page, page.items.IndexOf(nav), false); nav.adjust(-1); Sound(AudioDirector.MenuCue.Hover); } });
            right.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); if (!modalOpen && Top == page) { SetFocus(page, page.items.IndexOf(nav), false); nav.adjust(1); Sound(AudioDirector.MenuCue.Hover); } });
            page.refresh.Add(Show);
            return nav;
        }

        /// <summary>A 0-1 setting on a segmented slider; the arrows step it by a twentieth.</summary>
        Nav SliderRow(Page page, VisualElement parent, string label, string desc, Func<float> get, Action<float> set,
                      Func<float, string> format)
        {
            var row = Row(label, out var control);
            var slider = new SFSlider();
            slider.AddToClassList("m-slider");
            slider.pickingMode = PickingMode.Position;
            var val = Text("", "m-slider-value");
            control.Add(slider);
            control.Add(val);
            parent.Add(row);
            void Show() { float v = get(); slider.SetWithoutNotify(v); val.text = format(v); }
            slider.Changed += v => { set(v); val.text = format(v); };
            Show();
            var nav = Register(page, new Nav
            {
                el = row, title = label, desc = desc,
                adjust = dir => { slider.Value = Mathf.Round((slider.Value + dir * 0.05f) * 20f) / 20f; },
            });
            page.refresh.Add(Show);
            return nav;
        }

        /// <summary>A row that does something when chosen (New map, Forget me).</summary>
        Nav ActionRow(Page page, VisualElement parent, string label, string action, string desc, Action act)
        {
            var row = Row(label, out var control);
            control.Add(Text(action, "m-rowaction"));
            parent.Add(row);
            var nav = Register(page, new Nav { el = row, activate = act, title = label, desc = desc });
            row.RegisterCallback<ClickEvent>(_ => Activate(page, nav));
            return nav;
        }

        VisualElement StatRow(VisualElement parent, string label, float share, string value, bool amber = false)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("m-statrow");
            row.Add(Text(label, "m-statrow-label"));
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("m-statbar");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("m-statbar-fill");
            if (amber) fill.AddToClassList("m-statbar-fill--amber");
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100f);
            bar.Add(fill);
            row.Add(bar);
            row.Add(Text(value, "m-statrow-value"));
            parent.Add(row);
            return row;
        }

        /// <summary>A row of tabs over a page; Q/E and Tab step them.</summary>
        Label[] Tabs(Page page, string container, string[] names, Action<int> select)
        {
            var bar = root.Q(container);
            bar.Clear();
            bar.Add(Key("Q"));
            bar[0].AddToClassList("m-tab-key");
            var tabs = new Label[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                tabs[i] = new Label(names[i]);
                tabs[i].AddToClassList("m-tab");
                tabs[i].RegisterCallback<ClickEvent>(_ => { if (!modalOpen) { select(idx); Sound(AudioDirector.MenuCue.Hover); } });
                bar.Add(tabs[i]);
            }
            var e = Key("E");
            e.AddToClassList("m-tab-key");
            bar.Add(e);
            return tabs;
        }

        void ResetItems(Page page)
        {
            page.items.Clear();
            page.refresh.Clear();
            page.focus = 0;
        }

        // ------------------------------------------------------------ main
        Label profileRecord, profileOpponent, lastEngagement, intelTip;

        void BuildMain()
        {
            main = NewPage("pageMain", true, "↑↓", "SELECT", "ENTER", "CONFIRM", "ESC", "QUIT");
            var nav = root.Q("mainNav");
            NavItem(main, nav, "01", "SKIRMISH", "Deploy against the adaptive AI. It scouts, reads how you play and remembers you.", () => OpenSetup(false), "skirmishButton");
            NavItem(main, nav, "02", "OBSERVE", "Watch two adaptive commanders fight it out.", () => OpenSetup(true), "spectateButton");
            NavItem(main, nav, "03", "FIELD MANUAL", "Every unit and structure, the controls, the opponent, and the world itself.", () => Push(manual), "manualButton");
            NavItem(main, nav, "04", "SERVICE RECORD", "Your victories, defeats and time in command.", () => Push(record), "recordButton");
            NavItem(main, nav, "05", "SETTINGS", "Camera, graphics and audio.", () => Push(settings), "settingsButton");
            NavItem(main, nav, "06", "CREDITS", "The people and the free assets behind StarForge.", () => Push(credits), "creditsButton");
            NavItem(main, nav, "07", "QUIT", "Return to the desktop.", AskQuit, "quitButton", true);

            profileRecord = root.Q<Label>("profileRecord");
            profileOpponent = root.Q<Label>("profileOpponent");
            lastEngagement = root.Q<Label>("lastEngagement");
            intelTip = root.Q<Label>("intelTip");
            main.onShow = RefreshMain;
        }

        void RefreshMain()
        {
            var rec = ServiceRecord.Load();
            profileRecord.text = rec.matches == 0 ? "NO ENGAGEMENTS" : $"{rec.wins} W  ·  {rec.losses} L  ·  {rec.WinRate * 100f:0}%";
            profileOpponent.text = $"OPPONENT  {DifficultyName(MatchSettings.difficulty)}";
            if (rec.recent.Count == 0)
                lastEngagement.text = "No engagements on record yet. Deploy from Skirmish to start your service record.";
            else
            {
                var e = rec.recent[0];
                lastEngagement.text = $"{ResultName(e.result)} against {DifficultyName((AIDifficulty)e.difficulty)} in {ServiceRecord.Clock(e.seconds)}.\n{e.when}";
            }
            intelTip.text = Tip();
        }

        // ------------------------------------------------------------ operation setup
        Label setupTitle, setupCrumb, oppKicker, oppName, oppBlurb, memoryStatus, mapName, mapStats, oppPortraitTag;
        VisualElement oppBars, oppPortrait;
        SFMapPreview mapPreview;
        Texture2D mapTex;
        Button deployButton;
        bool setupSpectate;

        void BuildSetup()
        {
            setup = NewPage("pageSetup", true, "↑↓", "SELECT", "←→", "CHANGE", "ENTER", "CONFIRM", "ESC", "BACK");
            setupTitle = root.Q<Label>("setupTitle");
            setupCrumb = root.Q<Label>("setupCrumb");
            oppKicker = root.Q<Label>("oppKicker");
            oppName = root.Q<Label>("oppName");
            oppBlurb = root.Q<Label>("oppBlurb");
            oppBars = root.Q("oppBars");
            oppPortrait = root.Q("oppPortrait");
            oppPortraitTag = root.Q<Label>("oppPortraitTag");
            AddPortrait(oppPortrait, setup);
            memoryStatus = root.Q<Label>("memoryStatus");
            mapName = root.Q<Label>("mapName");
            mapStats = root.Q<Label>("mapStats");
            mapPreview = root.Q<SFMapPreview>("mapPreview");
            deployButton = root.Q<Button>("deployButton");

            var rows = root.Q("setupRows");
            SelectorRow(setup, rows, "OPPONENT", null, new[] { "RECRUIT", "VETERAN", "COMMANDER" },
                () => (int)MatchSettings.difficulty,
                v => { MatchSettings.difficulty = (AIDifficulty)v; GameSettings.SavedDifficulty = v; RefreshOpponent(); });
            SelectorRow(setup, rows, "GAME SPEED", null, GameSettings.SpeedNames,
                () => GameSettings.StartSpeed, v => GameSettings.StartSpeed = v);
            ActionRow(setup, rows, "BATTLEFIELD", "NEW MAP  ›", null,
                () => { MatchSettings.mapSeed = 0; Reload(() => bootstrap.ReturnToMenu(), "SURVEYING A NEW SECTOR", "setup"); });
            if (MatchSettings.debugAI)
            {
                SelectorRow(setup, rows, "AI MEMORY (DEBUG)", null, new[] { "OFF", "ON" },
                    () => MatchSettings.aiMemory ? 1 : 0, v => { MatchSettings.aiMemory = v == 1; RefreshOpponent(); });
                ActionRow(setup, rows, "FORGET ME (DEBUG)", "RESET  ›", null, () => { AIMemory.Reset(); RefreshOpponent(); });
            }
            ButtonNav(setup, "deployButton", Deploy);
            ButtonNav(setup, "setupBack", Pop);
            setup.onShow += () => { RefreshOpponent(); RefreshMap(); };
        }

        void OpenSetup(bool spectate)
        {
            setupSpectate = spectate;
            setupTitle.text = spectate ? "OBSERVER SETUP" : "OPERATION SETUP";
            setupCrumb.text = spectate ? "MAIN MENU  /  OBSERVE" : "MAIN MENU  /  SKIRMISH";
            deployButton.text = spectate ? "OBSERVE" : "DEPLOY";
            // Focus the button that starts it, the most likely next step.
            setup.focus = setup.items.FindIndex(n => n.el == deployButton);
            Push(setup);
        }

        void RefreshOpponent()
        {
            var d = MatchSettings.difficulty;
            oppKicker.text = setupSpectate ? "BOTH COMMANDERS" : "OPPONENT PROFILE";
            oppName.text = DifficultyName(d);
            SetTier(oppName, oppPortrait, (int)d);
            // Headings use the world's names (lore section 13): the staff across the field, by its grade.
            oppPortraitTag.text = setupSpectate ? $"BOTH STAFFS  ·  {DifficultyName(d)}" : $"OPPOSING STAFF  ·  {DifficultyName(d)}";
            oppBlurb.text = DifficultyBlurb(d) + (setupSpectate ? "\nTwo of them, one a side. Spectated matches teach the AI nothing about you." : "");
            oppBars.Clear();
            float apm = Apm(d), react = Reaction(d);
            StatRow(oppBars, "ACTIONS / MINUTE", apm / 330f, $"{apm:0}");
            StatRow(oppBars, "REACTION", Mathf.InverseLerp(1.1f, 0.15f, react), $"{react:0.00} s");
            if (MatchSettings.debugAI)
            {
                var mem = AIMemory.Load();
                memoryStatus.text = !MatchSettings.aiMemory ? "Memory off: every match starts from its default doctrine."
                    : mem.games == 0 ? "It has never played you."
                    : $"It has played you {mem.games} time{(mem.games == 1 ? "" : "s")} (won {mem.aiWins}). Last read: {mem.lastRead}.";
            }
            else memoryStatus.text = setupSpectate ? "" : "It plays by your rules: it sees only what its units see, gives the orders you can, and has a limited number of actions a minute.";
        }

        void RefreshMap()
        {
            var map = MapInfo.Instance;
            if (map == null) return;
            if (mapTex == null) mapTex = RenderMap(map, 512);
            if (mapTex != null) mapPreview.style.backgroundImage = Background.FromTexture2D(mapTex);
            float size = map.mapSize;
            Vector2 a = map.StartPos(0) / size, b = map.StartPos(1) / size;
            mapPreview.BaseA = a;
            mapPreview.BaseB = b;
            mapPreview.Ore.Clear();
            int seams = 0;
            if (map.oreRoot != null)
                foreach (Transform t in map.oreRoot)
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    seams++;
                    mapPreview.Ore.Add(new Vector2(t.position.x / size, t.position.z / size));
                }
            mapPreview.MarkDirtyRepaint();
            mapPreview.Animate = true;
            uint s = map.seed;
            mapName.text = $"VESPER · SECTOR {(char)('A' + s % 26)}{(char)('A' + s / 26 % 26)}-{s / 676 % 10000:0000}";
            mapStats.text = $"{size:0} × {size:0} M  ·  {seams} ORE SEAMS  ·  BASES {Vector2.Distance(map.StartPos(0), map.StartPos(1)):0} M APART";
        }

        /// <summary>The battlefield from above, once, before any unit exists (as the HUD's minimap).</summary>
        static Texture2D RenderMap(MapInfo map, int res)
        {
            float size = map.mapSize;
            var go = new GameObject("MenuMapCapture");
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
            var rt = RenderTexture.GetTemporary(res, res, 24, RenderTextureFormat.ARGB32);
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            RenderSettings.fog = fog;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MenuMap" };
            tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);
            return tex;
        }

        void Deploy()
        {
            if (deploying || bootstrap == null) return;
            deploying = true;
            bool spectate = setupSpectate;
            Sound(AudioDirector.MenuCue.Confirm);
            ShowVeil(spectate ? "OBSERVATION" : "DEPLOYING", false);
            After(0.45f, () =>
            {
                try
                {
                    MatchSettings.spectate = spectate;
                    MatchFromMenu = !spectate;
                    bootstrap.StartMatch();
                    bootstrap.SetSpeed(GameSettings.Speeds[GameSettings.StartSpeed]);
                }
                finally
                {
                    deploying = false;
                    HideVeil(0.3f);
                }
            });
        }

        /// <summary>Covers the screen, then reloads the scene through <paramref name="act"/>; the
        /// next scene's controller lifts the veil (and opens <paramref name="open"/>).</summary>
        void Reload(Action act, string title, string open = null)
        {
            if (deploying) return;
            deploying = true;
            CloseModal(false);
            ShowVeil(title, false);
            veilOnLoad = true;
            veilOnLoadTitle = title;
            openOnLoad = open;
            openSpectate = setupSpectate;
            After(0.4f, () => { deploying = false; act(); });
        }

        // ------------------------------------------------------------ settings
        ScrollView settingsRows;
        Label settingsDescTitle, settingsDescText;
        VisualElement settingsDesc;
        Label[] settingsTabs;
        int settingsTab;

        void BuildSettings()
        {
            settings = NewPage("pageSettings", true, "↑↓", "SELECT", "←→", "CHANGE", "Q E", "TAB", "ESC", "BACK");
            settingsRows = root.Q<ScrollView>("settingsRows");
            settingsRows.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            settingsRows.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            settingsDesc = root.Q("settingsDesc");
            settingsDescTitle = root.Q<Label>("settingsDescTitle");
            settingsDescText = root.Q<Label>("settingsDescText");
            settings.scroll = settingsRows;
            settingsTabs = Tabs(settings, "settingsTabs", new[] { "GAMEPLAY", "GRAPHICS", "AUDIO", "CONTROLS" }, SetSettingsTab);
            settings.tab = dir => SetSettingsTab((settingsTab + dir + 4) % 4);
            settings.onFocus = nav =>
            {
                settingsDescTitle.text = nav.title ?? "";
                settingsDescText.text = nav.desc ?? "";
            };
            settings.onShow = () => SetSettingsTab(settingsTab);
            settingsReset = ButtonNav(settings, "settingsReset", () => Ask("RESET SETTINGS",
                "Put every setting back to how the game came: camera, readout, volumes and the High preset?", () =>
                {
                    GameSettings.ResetAll();
                    QualityController.Current = SFQuality.High;
                    ApplySettings();
                    SetSettingsTab(settingsTab);
                }));
            settingsReset.title = "RESET TO DEFAULTS";
            settingsReset.desc = "Every setting back to how the game came: camera, readout, volumes and the High preset.";
            settingsBack = ButtonNav(settings, "settingsBack", Pop);
            settingsBack.title = "BACK";
            settingsBack.desc = "Settings are saved as you change them.";
        }

        Nav settingsReset, settingsBack;

        void SetSettingsTab(int tab)
        {
            settingsTab = tab;
            for (int i = 0; i < settingsTabs.Length; i++) settingsTabs[i].EnableInClassList("m-tab--active", i == tab);
            settingsRows.Clear();
            ResetItems(settings);
            var p = settings;
            var c = settingsRows.contentContainer;
            switch (tab)
            {
                case 0:
                    SelectorRow(p, c, "EDGE PANNING", "Push the pointer against an edge of the screen to move the camera. Panels stop it except in the last few points at the very edge.",
                        new[] { "OFF", "ON" }, () => GameSettings.EdgePan ? 1 : 0, v => { GameSettings.EdgePan = v == 1; ApplySettings(); });
                    SliderRow(p, c, "CAMERA PAN SPEED", "How fast WASD, the arrows and the screen edge move the camera, at any zoom.",
                        () => Mathf.InverseLerp(0.5f, 2f, GameSettings.PanSpeed), v => { GameSettings.PanSpeed = Mathf.Lerp(0.5f, 2f, v); ApplySettings(); },
                        v => $"{Mathf.Lerp(0.5f, 2f, v) * 100f:0}%");
                    SliderRow(p, c, "CAMERA TURN SPEED", "How fast Q and E turn the camera.",
                        () => Mathf.InverseLerp(0.5f, 2f, GameSettings.RotateSpeed), v => { GameSettings.RotateSpeed = Mathf.Lerp(0.5f, 2f, v); ApplySettings(); },
                        v => $"{Mathf.Lerp(0.5f, 2f, v) * 100f:0}%");
                    SelectorRow(p, c, "STARTING GAME SPEED", "The speed a match begins at. ',' and '.' change it while you play, and a pause keeps it.",
                        GameSettings.SpeedNames, () => GameSettings.StartSpeed, v => GameSettings.StartSpeed = v);
                    SelectorRow(p, c, "PERFORMANCE READOUT", "Frames a second and the render scale, in the top bar during a match.",
                        new[] { "HIDDEN", "SHOWN" }, () => GameSettings.ShowPerformance ? 1 : 0, v => { GameSettings.ShowPerformance = v == 1; ApplySettings(); });
                    break;
                case 1:
                    var q = SelectorRow(p, c, "QUALITY", QualityController.Blurb(QualityController.Current), new[] { "HIGH", "BALANCED", "BATTERY" },
                        () => (int)QualityController.Current, v => { QualityController.Current = (SFQuality)v; });
                    q.adjust += _ => { q.desc = QualityController.Blurb(QualityController.Current); settings.onFocus(q); };
                    SelectorRow(p, c, "DISPLAY", "Full screen fills the display at its own shape; a window comes back at its last size.",
                        new[] { "WINDOW", "FULL SCREEN" }, () => DisplayModes.IsFullScreen ? 1 : 0, v => DisplayModes.Set(v == 1));
                    break;
                case 2:
                    SliderRow(p, c, "MASTER VOLUME", "Everything the game plays.",
                        () => GameSettings.MasterVolume, v => { GameSettings.MasterVolume = v; ApplySettings(); }, v => $"{v * 100f:0}%");
                    SliderRow(p, c, "MUSIC", "The score, under the master volume. Calm while you build, tense when the enemy is near, louder in a fight.",
                        () => GameSettings.MusicVolume, v => { GameSettings.MusicVolume = v; ApplySettings(); }, v => $"{v * 100f:0}%");
                    SliderRow(p, c, "INTERFACE", "The menus' clicks and sweeps.",
                        () => GameSettings.InterfaceVolume, v => GameSettings.InterfaceVolume = v, v => $"{v * 100f:0}%");
                    break;
                default:
                    BuildKeys(c);
                    break;
            }
            settingsDesc.style.display = tab == 3 ? DisplayStyle.None : DisplayStyle.Flex;
            settings.items.Add(settingsReset);
            settings.items.Add(settingsBack);
            SetFocus(settings, 0, true);
            settingsRows.scrollOffset = Vector2.zero;
        }

        // ------------------------------------------------------------ field manual
        ScrollView unitList, opponentScroll, manualKeys, chronicleScroll, ordersScroll;
        VisualElement manualUnits, manualOpponent, manualControls, manualChronicle, manualOrders, unitStats, unitTags;
        Label unitKicker, unitTitle, unitRole, unitBlurb;
        Image unitIcon;
        Label[] manualTabs;
        int manualTab;
        UnitDef[] catalog;
        VisualElement selectedUnit;

        static readonly string[] CategoryNames = { "STRUCTURES", "WORKERS", "INFANTRY", "VEHICLES", "AIRCRAFT", "CREATURES", "THE MECH" };

        static int Category(UnitDef d)
        {
            if (d.type == UnitType.Mech) return 6;
            if (d.building) return 0;
            if (d.harvester || d.builder) return 1;
            if (d.organic) return 5;
            if (d.flying) return 4;
            if (d.upgradeClass == UnitClass.Infantry) return 2;
            return 3;
        }

        void BuildManual()
        {
            manual = NewPage("pageManual", true, "↑↓", "SELECT", "Q E", "TAB", "ESC", "BACK");
            unitList = root.Q<ScrollView>("unitList");
            opponentScroll = root.Q<ScrollView>("opponentScroll");
            manualKeys = root.Q<ScrollView>("manualKeys");
            chronicleScroll = root.Q<ScrollView>("chronicleScroll");
            ordersScroll = root.Q<ScrollView>("ordersScroll");
            for (int i = 0; i < 3; i++)
            {
                var d = (AIDifficulty)i;
                var tag = root.Q<Label>("rosterTag" + i);
                tag.text = $"{DifficultyName(d)} · {Apm(d):0} APM · {Reaction(d):0.00} S";
                AddPortrait(tag.parent, manual).tab = 1;
            }
            foreach (var sv in new[] { unitList, opponentScroll, manualKeys, chronicleScroll, ordersScroll })
            {
                sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            manualUnits = root.Q("manualUnits");
            manualOpponent = root.Q("manualOpponent");
            manualControls = root.Q("manualControls");
            manualChronicle = root.Q("manualChronicle");
            manualOrders = root.Q("manualOrders");
            unitStats = root.Q("unitStatRows");   // "unitStats" is the HUD's selection label
            unitTags = root.Q("unitTags");
            unitKicker = root.Q<Label>("unitKicker");
            unitTitle = root.Q<Label>("unitTitle");
            unitRole = root.Q<Label>("unitRole");
            unitBlurb = root.Q<Label>("unitBlurb");
            unitIcon = root.Q<Image>("unitIcon");
            unitIcon.scaleMode = ScaleMode.ScaleToFit;
            manualTabs = Tabs(manual, "manualTabs", new[] { "UNITS", "THE OPPONENT", "CONTROLS", "CHRONICLE", "THE ORDERS" }, SetManualTab);
            manual.tab = dir => SetManualTab((manualTab + dir + manualTabs.Length) % manualTabs.Length);
            manual.onShow = () => SetManualTab(manualTab);
            manualBack = ButtonNav(manual, "manualBack", Pop);

            // The opponent, as the player is told it (HUDController.OpponentHelp): each
            // paragraph's first sentence is its heading.
            Prose(opponentScroll, HUDController.OpponentHelp);
            // The world, as the player is told it (LoreText; canon in docs/lore/starforge-lore.md).
            Prose(chronicleScroll, LoreText.Chronicle, LoreText.PlateFor);
            BuildOrders(ordersScroll);
            BuildKeys(manualKeys.contentContainer);
        }

        /// <summary>The Field Manual's page on the eight knightly orders (lore section 7's table):
        /// each one's standard, its motto in the Old Language with the gloss, its teaching and
        /// its repute. The standards are the Mechs' own (<c>OrderStandards</c>).</summary>
        static void BuildOrders(ScrollView into)
        {
            into.Add(Text(LoreText.OrdersHead, "m-prose-head"));
            into.Add(Text(LoreText.OrdersIntro, "m-prose-text"));
            foreach (var o in LoreText.Orders)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("m-prose-entry");
                int index = OrderStandards.IndexOf(o.name);
                var sprite = index >= 0 ? OrderStandards.Icon(index) : null;
                if (sprite != null)
                {
                    // The sprite is the banner's square, squeezed in the atlas: stretched back, as the HUD draws it.
                    var img = new Image { sprite = sprite, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                    img.style.width = 112; img.style.height = 112; img.style.flexShrink = 0;
                    img.style.marginLeft = 12; img.style.marginRight = 6;
                    row.Add(img);
                }
                var col = new VisualElement { pickingMode = PickingMode.Ignore };
                col.AddToClassList("m-prose-col");
                col.Add(Text(o.name.ToUpperInvariant(), "m-prose-head"));
                string motto = LoreText.Motto(o.name);
                if (motto != null) col.Add(Text(motto, "m-prose-text"));
                bool glossed = motto != null && motto.Contains(o.teaching);   // the gloss already says it
                col.Add(Text(glossed ? $"Standard: {o.standard}." : $"Standard: {o.standard}. {o.teaching}", "m-prose-text"));
                col.Add(Text($"By repute: {o.repute}", "m-prose-text"));
                row.Add(col);
                into.Add(row);
            }
            into.Add(Text(LoreText.OrdersFoot, "m-prose-text"));
        }

        /// <summary>Paragraphs split by a blank line, each one's first sentence its heading.</summary>
        /// <param name="plate">Null, or which entries have a picture: the heading to its USS class.</param>
        static void Prose(ScrollView into, string text, Func<string, string> plate = null)
        {
            foreach (var para in text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int dot = para.IndexOf(". ", StringComparison.Ordinal);
                string head = (dot > 0 ? para.Substring(0, dot + 1) : para).TrimEnd('.').ToUpperInvariant();
                string body = dot > 0 ? para.Substring(dot + 2) : "";
                string picture = plate?.Invoke(head);
                VisualElement col = into;
                if (picture != null)
                {
                    // The picture at the left, its entry beside it.
                    var row = new VisualElement { pickingMode = PickingMode.Ignore };
                    row.AddToClassList("m-prose-entry");
                    var img = new VisualElement { pickingMode = PickingMode.Ignore };
                    img.AddToClassList("m-plate");
                    img.AddToClassList(picture);
                    row.Add(img);
                    col = new VisualElement { pickingMode = PickingMode.Ignore };
                    col.AddToClassList("m-prose-col");
                    row.Add(col);
                    into.Add(row);
                }
                col.Add(Text(head, "m-prose-head"));
                if (body.Length > 0) col.Add(Text(body, "m-prose-text"));
            }
        }

        void SetManualTab(int tab)
        {
            manualTab = tab;
            for (int i = 0; i < manualTabs.Length; i++) manualTabs[i].EnableInClassList("m-tab--active", i == tab);
            manualUnits.style.display = tab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            manualOpponent.style.display = tab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            manualControls.style.display = tab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            manualChronicle.style.display = tab == 3 ? DisplayStyle.Flex : DisplayStyle.None;
            manualOrders.style.display = tab == 4 ? DisplayStyle.Flex : DisplayStyle.None;
            ResetItems(manual);
            manual.onFocus = null;
            manual.scroll = tab == 0 ? unitList : tab == 1 ? opponentScroll : tab == 2 ? manualKeys : tab == 3 ? chronicleScroll : ordersScroll;
            if (tab == 0) BuildUnitList();
            manual.items.Add(manualBack);
            if (tab != 0) manual.scroll.scrollOffset = Vector2.zero;
            SetFocus(manual, 0, true);
        }

        Nav manualBack;

        void BuildUnitList()
        {
            unitList.Clear();
            if (catalog == null)
            {
                var cat = UnitCatalog.Load();
                var list = new List<UnitDef>();
                if (cat != null && cat.defs != null)
                    // The numbers the game plays with: Defs hands out copies with TacticalRules.Apply's
                    // ranges, damage, speeds and hulls; the assets keep the old ones on purpose.
                    foreach (var d in cat.defs) if (d != null && !d.neutral) list.Add(Defs.Get(d.type) ?? d);
                list.Sort((a, b) =>
                {
                    int ca = Category(a), cb = Category(b);
                    return ca != cb ? ca.CompareTo(cb) : a.cost != b.cost ? a.cost.CompareTo(b.cost) : string.CompareOrdinal(a.displayName, b.displayName);
                });
                catalog = list.ToArray();
            }
            int lastCat = -1;
            foreach (var d in catalog)
            {
                int c = Category(d);
                if (c != lastCat) { lastCat = c; unitList.Add(Text(CategoryNames[c], "m-list-head")); }
                var item = new VisualElement();
                item.AddToClassList("m-unit-item");
                var icon = new Image { image = d.icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("m-unit-item-icon");
                item.Add(icon);
                item.Add(Text(d.displayName.ToUpperInvariant(), "m-unit-item-name"));
                if (d.cost > 0) item.Add(Text($"{d.cost}", "m-unit-item-cost"));
                unitList.Add(item);
                var def = d;
                var nav = Register(manual, new Nav { el = item, title = d.displayName });
                nav.activate = () => ShowUnit(def, item);
                item.RegisterCallback<ClickEvent>(_ => Activate(manual, nav));
            }
            manual.onFocus = nav =>
            {
                for (int i = 0; i < catalog.Length && i < manual.items.Count; i++)
                    if (manual.items[i] == nav) { ShowUnit(catalog[i], nav.el); return; }
            };
        }

        void ShowUnit(UnitDef d, VisualElement item)
        {
            if (selectedUnit != null) selectedUnit.RemoveFromClassList("m-unit-item--selected");
            selectedUnit = item;
            item.AddToClassList("m-unit-item--selected");
            unitIcon.image = d.icon;
            unitKicker.text = CategoryNames[Category(d)];
            unitTitle.text = d.displayName.ToUpperInvariant();
            var role = new StringBuilder();
            if (d.producer != UnitType.None) role.Append($"FROM {NameOf(d.producer)}  ·  ");
            if (d.cost > 0) role.Append($"{d.cost} ORE  ·  ");
            if (d.supplyCost > 0) role.Append($"{d.supplyCost} SUPPLY  ·  ");
            if (d.buildTime > 0f) role.Append($"{d.buildTime:0} S  ·  ");
            if (d.hotkey != '\0' && d.hotkey != ' ') role.Append($"KEY {char.ToUpperInvariant(d.hotkey)}  ·  ");
            unitRole.text = role.Length > 5 ? role.ToString(0, role.Length - 5) : "";
            unitBlurb.text = string.IsNullOrEmpty(d.blurb) ? "No field notes." : d.blurb;

            float maxHp = 1f, maxDps = 1f, maxRange = 1f, maxSpeed = 1f, maxSight = 1f;
            foreach (var o in catalog)
            {
                if (o.type == UnitType.Mech) continue;
                maxHp = Mathf.Max(maxHp, o.hp);
                maxDps = Mathf.Max(maxDps, Dps(o));
                maxRange = Mathf.Max(maxRange, o.range);
                maxSpeed = Mathf.Max(maxSpeed, o.speed);
                maxSight = Mathf.Max(maxSight, o.sight);
            }
            unitStats.Clear();
            StatRow(unitStats, "HIT POINTS", d.hp / maxHp, $"{d.hp:0}");
            float dps = Dps(d);
            StatRow(unitStats, "DAMAGE / SECOND", dps / maxDps, d.Armed ? $"{dps:0.#}" : "—");
            StatRow(unitStats, "RANGE", d.range / maxRange, d.Armed ? $"{d.range:0} M" : "—");
            if (!d.building) StatRow(unitStats, "SPEED", d.speed / maxSpeed, $"{d.speed:0.#} M/S");
            StatRow(unitStats, "SIGHT", d.sight / maxSight, $"{d.sight:0} M");

            unitTags.Clear();
            void Tag(string t, bool warn = false) { var l = Text(t, "m-tag"); if (warn) l.AddToClassList("m-tag--warn"); unitTags.Add(l); }
            if (d.Armed) Tag(d.hitsAir && d.hitsGround ? "GROUND + AIR" : d.hitsAir ? "ANTI-AIR ONLY" : "GROUND TARGETS");
            if (d.flying) Tag("FLIES");
            if (d.heavyArmour) Tag("ARMOURED");
            if (d.splash > 0f) Tag("SPLASH");
            if (d.minRange > 0f) Tag($"MIN RANGE {d.minRange:0} M");
            if (d.harvester) Tag("MINES ORE");
            if (d.builder) Tag("BUILDS");
            if (d.organic) Tag("ORGANIC");
            if (d.supplyGive > 0) Tag($"+{d.supplyGive} SUPPLY");
            if (d.ignite > 0f) Tag("SETS FIRES");
            if (d.requires != UnitType.None) Tag($"NEEDS {NameOf(d.requires)}", true);
        }

        /// <summary>Sustained damage a second: a magazine's rounds over their cooldowns and the reload
        /// (Apply's numbers), not the burst rate between two rounds.</summary>
        static float Dps(UnitDef d)
        {
            if (!d.Armed || d.cooldown <= 0f) return 0f;
            if (d.magazine > 1) return d.damage * d.magazine / (d.magazine * d.cooldown + d.reloadTime);
            if (d.magazine == 1) return d.damage / (d.cooldown + d.reloadTime);
            return d.damage / d.cooldown;
        }

        string NameOf(UnitType t)
        {
            if (catalog != null) foreach (var d in catalog) if (d.type == t) return d.displayName.ToUpperInvariant();
            return t.ToString().ToUpperInvariant();
        }

        /// <summary>The key reference (PlayerController's keys; the README's Controls table says the same).</summary>
        void BuildKeys(VisualElement into)
        {
            void Head(string t) => into.Add(Text(t, "m-keys-head"));
            void K(string text, params string[] keys)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("m-keyrow");
                var ks = new VisualElement { pickingMode = PickingMode.Ignore };
                ks.AddToClassList("m-keyrow-keys");
                foreach (var k in keys) ks.Add(Key(k));
                row.Add(ks);
                row.Add(Text(text, "m-keyrow-text"));
                into.Add(row);
            }
            Head("CAMERA");
            K("Pan", "W", "A", "S", "D", "ARROWS", "SCREEN EDGE");
            K("Turn", "Q", "E");
            K("Zoom", "WHEEL");
            K("Grab the ground", "MIDDLE DRAG");
            K("Centre on the selection", "SPACE");
            K("Save the view to a camera spot / go back to it", "CTRL + OPTION + 1-4", "OPTION + 1-4");
            K("Go to a camera spot as in StarCraft II (fn on a MacBook; macOS may keep Ctrl + F5-F8 for itself)", "F5-F8", "CTRL + F5-F8");
            K("To the latest alert; again for the ones before", "TAB");
            K("Select the next Foundry and look at it", "BACKSPACE");
            Head("SELECTION");
            K("Select, or drag a box", "CLICK", "DRAG");
            K("Every unit of that type on screen", "DOUBLE-CLICK", "CTRL + CLICK");
            K("Add to the selection", "SHIFT");
            K("Make a group / add the selection to it / recall it", "CTRL + 1-0", "SHIFT + 1-0", "1-0");
            K("Go to a group", "1-0 TWICE");
            K("Groups on the bar: click selects, double-click goes there, Shift-click adds", "GROUP BAR");
            K("Next idle Digger / every idle Digger (fn on a MacBook; or the IDLE button, Ctrl-click for all)", "F1", "SHIFT + F1");
            K("The whole army", "F2");
            Head("ORDERS");
            K("Move, attack or mine", "RIGHT-CLICK");
            K("Hurry: no cover, straight there", "DOUBLE RIGHT-CLICK");
            K("Attack-move", "R");
            K("Stop / hold position", "C", "H");
            Head("BUILDING AND TRAINING");
            K("Digger: basic structures (Foundry F, Bunkhouse B, Garrison G, Sentinel N, Workshop V)", "B");
            K("Digger: advanced structures (Armory U, Airfield L, Biolab O, Mech Bay Y)", "V");
            K("Back out of a build card", "X", "ESC");
            K("Foundry trains", "U", "J", "K");
            K("Garrison trains", "T", "K", "F", "M", "N", "B", "G");
            K("Workshop trains", "M", "L", "F", "T", "K");
            K("Airfield trains", "F", "G");
            K("Biolab trains", "T", "G", "V", "Z");
            K("Cancel the last in the queue", "X");
            Head("THE MECH");
            K("Mech Bay upgrades: servos, armour, weapons, hardpoint, targeting", "V", "B", "N", "M", "K");
            K("Find your Mech", "CLICK THE MECH STRIP");
            Head("THE GAME");
            K("Pause", "P");
            K("Slower / faster", ",", ".");
            K("Menu", "ESC");
            if (MatchSettings.debugAI) K("AI inspector (debug)", "I");
        }

        // ------------------------------------------------------------ service record
        void BuildRecord()
        {
            record = NewPage("pageRecord", true, "↑↓", "SELECT", "ENTER", "CONFIRM", "ESC", "BACK");
            ButtonNav(record, "recordBack", Pop);
            ButtonNav(record, "recordErase", () => Ask("ERASE SERVICE RECORD", "Every victory, defeat and engagement on record will be gone for good. The AI's own memory of you is not touched.", () =>
            {
                ServiceRecord.Erase();
                RefreshRecord();
            }));
            record.onShow = RefreshRecord;
        }

        void RefreshRecord()
        {
            var r = ServiceRecord.Load();
            var tiles = root.Q("recordTiles");
            tiles.Clear();
            VisualElement Tile(string label, string value, string sub, bool last = false)
            {
                var t = new SFFrame { Tab = false, pickingMode = PickingMode.Ignore };
                t.AddToClassList("m-tile");
                if (last) t.AddToClassList("m-tile--last");
                t.Add(Text(label, "m-tile-label"));
                t.Add(Text(value, "m-tile-value"));
                if (sub != null) t.Add(Text(sub, "m-tile-sub"));
                tiles.Add(t);
                return t;
            }
            Tile("ENGAGEMENTS", $"{r.matches}", r.draws > 0 ? $"{r.draws} drawn" : null);
            Tile("VICTORIES", $"{r.wins}", r.fastestWin > 0f ? $"fastest {ServiceRecord.Clock(r.fastestWin)}" : null);
            Tile("DEFEATS", $"{r.losses}", null);
            var rate = new SFFrame { Tab = false, pickingMode = PickingMode.Ignore };
            rate.AddToClassList("m-tile");
            var rateRow = new VisualElement { pickingMode = PickingMode.Ignore };
            rateRow.AddToClassList("m-tile-row");
            var ring = new SFRing { Value = r.WinRate, pickingMode = PickingMode.Ignore };
            ring.AddToClassList("m-ring");
            rateRow.Add(ring);
            var rateText = new VisualElement { pickingMode = PickingMode.Ignore };
            rateText.Add(Text("WIN RATE", "m-tile-label"));
            rateText.Add(Text(r.matches > 0 ? $"{r.WinRate * 100f:0}%" : "—", "m-tile-value"));
            rateRow.Add(rateText);
            rate.Add(rateRow);
            tiles.Add(rate);
            Tile("TIME IN COMMAND", ServiceRecord.Hours(r.seconds), $"{r.ore:N0} ore mined", true);

            var by = root.Q("recordByOpponent");
            by.Clear();
            for (int d = 0; d < 3; d++)
            {
                int w = r.winsBy[d], l = r.lossesBy[d];
                StatRow(by, DifficultyName((AIDifficulty)d), w + l > 0 ? (float)w / (w + l) : 0f, $"{w} – {l}");
            }
            by.Add(Text("", "m-text"));
            StatRow(by, "UNITS FIELDED", 1f, $"{r.produced:N0}", true);
            StatRow(by, "STRUCTURES RAISED", 1f, $"{r.built:N0}", true);
            StatRow(by, "ENEMY LOSSES", r.killed + r.lost > 0 ? (float)r.killed / (r.killed + r.lost) : 0f, $"{r.killed:N0}");
            StatRow(by, "OWN LOSSES", r.killed + r.lost > 0 ? (float)r.lost / (r.killed + r.lost) : 0f, $"{r.lost:N0}");

            var recent = root.Q("recordRecent");
            recent.Clear();
            if (r.recent.Count == 0)
                recent.Add(Text("No engagements yet. Matches you deploy from Skirmish are recorded here; spectated and cheated ones are not.", "m-text m-text--muted"));
            foreach (var e in r.recent)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("m-recent-row");
                var badge = Text(ResultName(e.result), "m-badge");
                badge.AddToClassList(e.result == 1 ? "m-badge--win" : e.result == 0 ? "m-badge--loss" : "m-badge--draw");
                row.Add(badge);
                row.Add(Text($"{DifficultyName((AIDifficulty)e.difficulty)}  ·  {ServiceRecord.Clock(e.seconds)}", "m-recent-text"));
                row.Add(Text(e.when, "m-recent-when"));
                recent.Add(row);
            }
        }

        // ------------------------------------------------------------ credits
        VisualElement creditsRoll, creditsViewport;

        void BuildCredits()
        {
            credits = NewPage("pageCredits", true, "ESC", "BACK");
            creditsRoll = root.Q("creditsRoll");
            creditsViewport = root.Q("creditsViewport");
            ButtonNav(credits, "creditsBack", Pop);
            void Head(string t) => creditsRoll.Add(Text(t, "m-credit-head"));
            void Name(string t) => creditsRoll.Add(Text(t, "m-credit-name"));
            void Line(string t) => creditsRoll.Add(Text(t, "m-credit-text"));
            Head("A GAME BY");
            Name("Simon Gergely");
            Line("Design, code and art. Rebuilt in Unity from the original C++ and Metal StarForge.");
            Head("ILLUSTRATIONS");
            Name("Simon Gergely, with Google Gemini");
            Line("The title art and the Chronicle's plates · made with an image model for StarForge");
            Line("The opponent holograms and the knightly orders' standards · likewise");
            Line("The intro · the title art set in motion by a video model");
            Head("ENGINE");
            Name("Unity 6 · Universal Render Pipeline");
            Head("TYPEFACES");
            Name("Inter");
            Line("The Inter Project Authors (Rasmus Andersson) · SIL Open Font License 1.1");
            Name("Roboto Mono");
            Line("The Roboto Mono Project Authors (Christian Robertson, Google) · Apache License 2.0");
            Name("Cinzel");
            Line("The Cinzel Project Authors (Natanael Gama) · SIL Open Font License 1.1");
            Head("TERRAIN, ROCK AND PLANT SCANS");
            Name("Poly Haven");
            Name("ambientCG");
            Line("CC0");
            Head("CHARACTER ANIMATION");
            Name("Kay Lousberg");
            Line("KayKit Character Animations · CC0");
            Head("ANIMALS AND CREATURES");
            Name("Quaternius");
            Line("Animals Pack, Animal Pack Vol. 2, Easy Animated Enemy Pack, Farm Animals Pack · CC0");
            Head("FIRE AND EXPLOSIONS");
            Name("JangaFX");
            Line("EmberGen free VDB animations, rendered into flipbooks · CC0");
            Head("SOUND");
            Name("Kenney");
            Line("Sci-Fi Sounds, Impact Sounds, Interface Sounds · CC0");
            Name("Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney");
            Line("The Free Firearm Sound Library · CC0");
            Name("kheetor · AntumDeluge · Department64 · rubberduck");
            Line("Falling trees, creaks, wood and metal · CC0, via OpenGameArt");
            Head("MUSIC");
            Name("wolfgang · yoiyami · Joth · Samza · cynicmusic");
            Name("Juhani Junkala (SubspaceAudio) · yd · Emma_MA");
            Line("At Home · First Light Particles · Contemplation · Peaceful Forest · Calm Ambient 1 · A Place I Call Home · Insistent · Pressure · Menace · Battle Theme A · Determined Pursuit · Epic Boss Battle · QaziJamJam · CC0, via OpenGameArt");
            Head("EVERY ASSET AND ITS LICENCE");
            Line("THIRD-PARTY.md, beside the game's source on GitHub (SiGe11/StarForge2). The game's own code and art are MIT.");
            Head("");
            Name("Thank you for playing.");
        }

        void ScrollCredits(float dt)
        {
            float view = creditsViewport.resolvedStyle.height, h = creditsRoll.resolvedStyle.height;
            if (float.IsNaN(view) || float.IsNaN(h) || h < 1f) return;
            creditsY += dt * 38f;
            if (creditsY > h + 40f) creditsY = -view;
            creditsRoll.style.translate = new Translate(0f, view - creditsY);
        }

        // ------------------------------------------------------------ pause
        Label pauseInfo;
        VisualElement sitrepRows;

        void BuildPause()
        {
            pause = NewPage("pagePause", false, "↑↓", "SELECT", "ENTER", "CONFIRM", "ESC", "RESUME");
            pauseInfo = root.Q<Label>("pauseInfo");
            sitrepRows = root.Q("sitrepRows");
            var nav = root.Q("pauseNav");
            NavItem(pause, nav, "", "RESUME", null, () => bootstrap.TogglePause(), "resumeButton");
            NavItem(pause, nav, "", "SETTINGS", null, () => Push(settings), "pauseSettingsButton");
            NavItem(pause, nav, "", "FIELD MANUAL", null, () => Push(manual), "pauseManualButton");
            NavItem(pause, nav, "", "RESTART", null, () => Ask("RESTART", "Start the match again from the beginning, on a new battlefield? This one will not count.",
                () => Rematch()), "restartButton");
            NavItem(pause, nav, "", "SURRENDER", null, () => Ask("SURRENDER", "Concede the match? It counts as a defeat.",
                () => { if (!MatchSettings.spectate) bootstrap.Surrender(); }), "surrenderButton", true);
            NavItem(pause, nav, "", "MAIN MENU", null, () => Ask("LEAVE THE MATCH", "Return to the main menu? The match will be abandoned and not recorded.",
                () => Reload(() => bootstrap.ReturnToMenu(), "RETURNING TO COMMAND")), "menuReturnButton");
            NavItem(pause, nav, "", "QUIT TO DESKTOP", null, AskQuit, "quitButton2", true);
            pause.onShow = RefreshPause;
        }

        void RefreshPause()
        {
            if (world == null || bootstrap == null) return;
            int t = Mathf.FloorToInt(world.time);
            string opp = bootstrap.AI != null ? DifficultyName(bootstrap.AI.Difficulty) : "";
            pauseInfo.text = $"{t / 60}:{t % 60:00}  ·  {(MatchSettings.spectate ? "OBSERVING" : "OPPONENT")} {opp}  ·  SPEED x{bootstrap.Speed:0.##}";
            var surrender = root.Q("surrenderButton");
            if (surrender != null) surrender.style.display = MatchSettings.spectate ? DisplayStyle.None : DisplayStyle.Flex;

            int me = player != null ? player.team : 0;
            int army = 0, structures = 0, workers = 0;
            foreach (var u in world.units)
            {
                if (!Unit.Live(u) || u.team != me || u.deserted) continue;
                if (u.def.building) structures++;
                else if (u.def.harvester) workers++;
                else if (!u.def.neutral) army++;
            }
            var F = world.factions[me];
            sitrepRows.Clear();
            void R(string label, string value)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("m-sitrep-row");
                row.Add(Text(label, "m-sitrep-label"));
                row.Add(Text(value, "m-sitrep-value"));
                sitrepRows.Add(row);
            }
            R("ORE", $"{F.ore}");
            R("SUPPLY", $"{F.supplyUsed} / {world.SupplyCap(me)}");
            R("ARMY", $"{army}");
            R("WORKERS", $"{workers}");
            R("STRUCTURES", $"{structures}");
            R("ORE MINED", $"{F.oreMined}");
            R("ENEMY LOSSES", $"{F.killed}");
            R("OWN LOSSES", $"{F.lost}");
        }

        void Rematch()
        {
            speedOnLoad = true;
            MatchSettings.mapSeed = 0;   // "on a new battlefield": a seed a trial left pinned in the Editor would give the same map again
            Reload(() => bootstrap.Restart(), MatchSettings.spectate ? "OBSERVATION" : "REDEPLOYING");
        }

        // ------------------------------------------------------------ after-action report
        Label endTitle, endSubtitle, endDossier, endRecord;
        VisualElement endBars, debriefPanel, debriefShade, endOppColumn, endPortrait;
        Label endOppName, endOppLine, endPortraitTag;
        Portrait endPortraitP;
        Button showReport;
        BarChartElement endStyleChart;
        Nav rematchNav;
        bool viewingField;
        static readonly string[] BeliefLabels = { "Rushing", "Macro", "Turtling", "Harassing", "Expanding", "Teching" };

        void BuildDebrief()
        {
            debrief = NewPage("pageDebrief", false, "←→", "SELECT", "ENTER", "CONFIRM", "ESC", "VIEW FIELD");
            endTitle = root.Q<Label>("endTitle");
            endSubtitle = root.Q<Label>("endSubtitle");
            endDossier = root.Q<Label>("endDossier");
            endRecord = root.Q<Label>("endRecord");
            endBars = root.Q("endBars");
            endOppColumn = root.Q("endOpponentColumn");
            endPortrait = root.Q("endPortrait");
            endPortraitTag = root.Q<Label>("endPortraitTag");
            endOppName = root.Q<Label>("endOppName");
            endOppLine = root.Q<Label>("endOppLine");
            endPortraitP = AddPortrait(endPortrait, debrief);
            endStyleChart = root.Q<BarChartElement>("endStyleChart");
            debriefPanel = root.Q("debriefPanel");
            debriefShade = root.Q("debriefShade");
            showReport = root.Q<Button>("showReportButton");
            showReport.clicked += () => SetViewField(false);
            var nav = root.Q("debriefNav");
            Button B(string name, string text, string cls)
            {
                var b = new Button { name = name, text = text, focusable = false };
                b.AddToClassList("m-btn");
                b.AddToClassList(cls);
                nav.Add(b);
                return b;
            }
            B("rematchButton", "REMATCH", "m-btn--primary");
            B("viewFieldButton", "VIEW BATTLEFIELD", "m-btn--ghost");
            B("endMenuButton", "MAIN MENU", "m-btn--ghost");
            rematchNav = ButtonNav(debrief, "rematchButton", Rematch);
            ButtonNav(debrief, "viewFieldButton", () => SetViewField(true));
            ButtonNav(debrief, "endMenuButton", () => Reload(() => bootstrap.ReturnToMenu(), "RETURNING TO COMMAND"));
            root.Q("rematchButton").style.marginLeft = 0;
            root.Q("rematchButton").style.marginRight = 14;
            root.Q("viewFieldButton").style.marginRight = 14;
            root.Q("endDossierColumn").style.display = MatchSettings.debugAI ? DisplayStyle.Flex : DisplayStyle.None;
            // In a row the arrows are left and right.
            foreach (var n in debrief.items) { var me = n; me.adjust = dir => { SetFocus(debrief, (debrief.focus + dir + debrief.items.Count) % debrief.items.Count, false); }; }
        }

        void SetViewField(bool view)
        {
            viewingField = view;
            // Looking at the field, the page lets the pointer through (edge panning); its button does not.
            debrief.el.pickingMode = view ? PickingMode.Ignore : PickingMode.Position;
            debriefPanel.EnableInClassList("m-debrief--hidden", view);
            debriefShade.style.display = view ? DisplayStyle.None : DisplayStyle.Flex;
            showReport.EnableInClassList("m-show-report--visible", view && bootstrap != null && bootstrap.State == MatchState.Ended);
            UpdateChrome();
        }

        void OnMatchEnded(int winner)
        {
            int me = player != null ? player.team : 0;
            bool won = winner == me;
            int result = winner > 1 ? 2 : won ? 1 : 0;
            endTitle.RemoveFromClassList("m-end-title--defeat");
            endTitle.RemoveFromClassList("m-end-title--draw");
            if (MatchSettings.spectate)
            {
                endTitle.text = winner > 1 ? "DRAW" : winner == 0 ? "BLUE WINS" : "RED WINS";
                endTitle.EnableInClassList("m-end-title--defeat", winner == 1);
            }
            else
            {
                endTitle.text = winner > 1 ? "DRAW" : won ? "VICTORY" : "DEFEAT";   // both sides gone in one tick
                endTitle.EnableInClassList("m-end-title--defeat", !won && winner <= 1);
            }
            endTitle.EnableInClassList("m-end-title--draw", winner > 1);
            int t = Mathf.FloorToInt(world.time);
            endSubtitle.text = $"{t / 60}:{t % 60:00} · opponent {bootstrap.AI.Difficulty}";
            if (world.ReckoningTeam == me) endSubtitle.text += " · cheat mode: the animals had the last word";
            else if (!MatchSettings.spectate && result == 0) endSubtitle.text += " · <i>Lighttheinto gangen</i> — gone into the light";
            ((Button)rematchNav.el).text = MatchSettings.spectate ? "WATCH AGAIN" : "REMATCH";
            root.Q<Label>("vsLeft").text = MatchSettings.spectate ? "BLUE" : "YOU";
            root.Q<Label>("vsRight").text = MatchSettings.spectate ? "RED" : "OPPONENT";
            // The opponent's hologram beside the summary (not when watching two AIs, nor beside the debug dossier).
            // Both columns read debugAI here, at the match's end, so a mid-session toggle shows one of them, never both.
            var diff = bootstrap.AI.Difficulty;
            root.Q("endDossierColumn").style.display = MatchSettings.debugAI ? DisplayStyle.Flex : DisplayStyle.None;
            endOppColumn.style.display = !MatchSettings.debugAI && !MatchSettings.spectate ? DisplayStyle.Flex : DisplayStyle.None;
            SetTier(endOppName, endPortrait, (int)diff);
            endOppName.text = DifficultyName(diff);
            endPortraitP.dim = result == 1;
            endPortraitTag.text = result == 1 ? "SIGNAL LOST" : $"OPPOSING STAFF  ·  {DifficultyName(diff)}";
            endOppLine.text = (result == 1 ? "Outfought. " : result == 0 ? "It held the field. " : "Neither side stands. ")
                + $"{Apm(diff):0} actions a minute, {Reaction(diff):0.00} s reactions.";

            var F = world.factions[me];
            var E = world.factions[1 - me];
            endBars.Clear();
            VsRow("ORE MINED", F.oreMined, E.oreMined);
            VsRow("UNITS PRODUCED", F.unitsProduced, E.unitsProduced);
            VsRow("STRUCTURES BUILT", F.structuresBuilt, E.structuresBuilt);
            VsRow("UNITS DESTROYED", F.killed, E.killed);
            VsRow("UNITS LOST", F.lost, E.lost);

            // The service record takes only matches the menu launched, never spectated or cheated ones.
            if (MatchFromMenu && !MatchSettings.spectate && !world.cheated)
            {
                var rec = ServiceRecord.Load();
                rec.Add(result, bootstrap.AI.Difficulty, world.time, true, F.oreMined, F.unitsProduced, F.structuresBuilt, F.killed, F.lost);
                rec.Save();
                endRecord.text = $"Service record updated: {Count(rec.wins, "victory", "victories")}, {Count(rec.losses, "defeat", "defeats")} " +
                                 $"in {Count(rec.matches, "engagement", "engagements")} ({rec.WinRate * 100f:0}% won).";
            }
            else endRecord.text = MatchSettings.spectate ? "Spectated matches are not recorded."
                : world.cheated ? "A match played with the cheat is not recorded." : "";

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

        void VsRow(string label, int you, int them)
        {
            float max = Mathf.Max(1, Mathf.Max(you, them));
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("m-vsrow");
            row.Add(Text($"{you}", "m-vs-val"));
            var lb = new VisualElement { pickingMode = PickingMode.Ignore };
            lb.AddToClassList("m-vs-bar");
            lb.AddToClassList("m-vs-bar--left");
            var lf = new VisualElement { pickingMode = PickingMode.Ignore };
            lf.AddToClassList("m-vs-fill");
            lf.AddToClassList("m-vs-fill--you");
            lf.style.width = Length.Percent(you / max * 100f);
            lb.Add(lf);
            row.Add(lb);
            row.Add(Text(label, "m-vs-label"));
            var rb = new VisualElement { pickingMode = PickingMode.Ignore };
            rb.AddToClassList("m-vs-bar");
            var rf = new VisualElement { pickingMode = PickingMode.Ignore };
            rf.AddToClassList("m-vs-fill");
            rf.AddToClassList("m-vs-fill--them");
            rf.style.width = Length.Percent(them / max * 100f);
            rb.Add(rf);
            row.Add(rb);
            var r = Text($"{them}", "m-vs-val");
            r.AddToClassList("m-vs-val--right");
            row.Add(r);
            endBars.Add(row);
        }

        // ------------------------------------------------------------ dialog
        void Ask(string title, string body, Action ok)
        {
            confirmTitle.text = title;
            confirmBody.text = body;
            confirmAction = ok;
            modalOpen = true;
            modal.AddToClassList("m-modal--visible");
            SetModalFocus(0);   // Cancel first: nothing is lost by a stray Enter
            Sound(AudioDirector.MenuCue.Open);
            UpdateHints();
        }

        void SetModalFocus(int i)
        {
            modalFocus = i;
            confirmCancel.EnableInClassList("m-focus", i == 0);
            confirmOk.EnableInClassList("m-focus", i == 1);
        }

        void CloseModal(bool ok)
        {
            if (!modalOpen) return;
            modalOpen = false;
            modal.RemoveFromClassList("m-modal--visible");
            var act = confirmAction;
            confirmAction = null;
            Sound(ok ? AudioDirector.MenuCue.Select : AudioDirector.MenuCue.Back);
            UpdateHints();
            if (ok) act?.Invoke();
        }

        void AskQuit() => Ask("QUIT", "Leave StarForge and return to the desktop?", Quit);

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------ splash and veil
        // The intro clip over the splash, while it plays (IntroClip: the built player's first title only).
        IntroClip intro;

        void ShowSplash()
        {
            splashOut = false;
            splashArtT = 0f;
            splash.AddToClassList("m-splash--visible");
            splash.RemoveFromClassList("m-splash--out");
            splashEmblem.Animate = true;
            // The splash is shown again on every Menu state until a key dismisses it: a later call must not
            // drop the clip the first one started (TryStart answers null from the second time on).
            if (intro == null) intro = IntroClip.TryStart(root, GameSettings.MasterVolume);
            if (intro != null) HushFor(intro);
        }

        // The clip has its own sound and a spoken line: the game's music and ambience (all of AudioDirector) stay
        // silent under it and come up as it fades. ApplySettings would undo this, but no page opens while it plays.
        void HushFor(IntroClip clip)
        {
            if (audioDir != null) audioDir.masterVolume = GameSettings.MasterVolume * (1f - clip.Level);
        }

        void DismissSplash()
        {
            if (splashOut || !splash.ClassListContains("m-splash--visible")) return;
            splashSeen = true;
            splashOut = true;
            Sound(AudioDirector.MenuCue.Select);
            splash.AddToClassList("m-splash--out");
            After(0.5f, () => HideSplash(true));
        }

        void HideSplash(bool now)
        {
            splash.RemoveFromClassList("m-splash--visible");
            splash.RemoveFromClassList("m-splash--out");
            splashEmblem.Animate = false;
            splashOut = false;
        }

        void ShowVeil(string title, bool instant)
        {
            veilTitle.text = title;
            veilTip.text = Tip();
            veilT = 0f;
            veil.pickingMode = PickingMode.Position;
            veil.AddToClassList("m-veil--shown");
            if (instant) veil.AddToClassList("m-veil--in");
            else After(0f, () => veil.AddToClassList("m-veil--in"));
            veilEmblem.Animate = true;
        }

        void HideVeil(float delay)
        {
            After(delay, () =>
            {
                veil.RemoveFromClassList("m-veil--in");
                veil.pickingMode = PickingMode.Ignore;
                After(0.36f, () =>
                {
                    if (veil.ClassListContains("m-veil--in")) return;
                    veil.RemoveFromClassList("m-veil--shown");
                    veilEmblem.Animate = false;
                });
            });
        }

        // ------------------------------------------------------------ the title's camera
        // Slow shots over the battlefield behind the title, cut through black. It drives
        // the camera only while nothing else does: a tool or trial that sets `scripted`, or
        // moves the camera itself, stops it for the rest of the visit to the menu.
        readonly List<Vector2> sights = new List<Vector2>();
        bool attractOn, attractDrove, dipping;
        int attractMoves, attractWait, lastSight = -1;
        float shotT, shotLen;
        Vector2 shotAt, shotDrift;
        float shotYaw, shotYawRate, shotDist0, shotDist1;
        System.Random attractRng;

        void StartAttract()
        {
            if (rig == null || world == null || world.Map == null) return;
            attractOn = true;
            attractDrove = false;
            attractWait = 3;   // let the camera's own Start place it first
            attractRng ??= new System.Random(Environment.TickCount);
        }

        void UpdateAttract(float dt)
        {
            if (!attractOn || rig == null) return;
            if (bootstrap == null || bootstrap.State != MatchState.Menu) { attractOn = false; return; }
            if (attractWait > 0)
            {
                if (--attractWait == 0)
                {
                    if (rig.scripted) { attractOn = false; return; }   // a tool already has the camera
                    attractMoves = rig.Moves;
                    NextShot();
                }
                return;
            }
            if (rig.scripted || rig.Moves != attractMoves)
            {
                attractOn = attractDrove = false;
                dip.RemoveFromClassList("m-dip--on");
                return;
            }
            shotT += dt;
            if (!dipping && shotT > shotLen - 0.6f) { dipping = true; dip.AddToClassList("m-dip--on"); }
            if (shotT >= shotLen) { NextShot(); return; }
            float k = shotT / shotLen;
            rig.Glide(shotAt + shotDrift * k, shotYaw + shotYawRate * shotT, Mathf.Lerp(shotDist0, shotDist1, k * k * (3f - 2f * k)));
        }

        float R(float a, float b) => a + (float)attractRng.NextDouble() * (b - a);

        void NextShot()
        {
            var map = world.Map;
            float size = map.mapSize;
            if (sights.Count == 0)
            {
                sights.Add(map.StartPos(0));
                sights.Add(map.StartPos(1));
                sights.Add(new Vector2(size * 0.5f, size * 0.5f));
                if (map.oreRoot != null)
                    for (int i = 0; i < map.oreRoot.childCount && sights.Count < 8; i += Mathf.Max(1, map.oreRoot.childCount / 5))
                    {
                        var p = map.oreRoot.GetChild(i).position;
                        sights.Add(new Vector2(p.x, p.z));
                    }
                for (int tries = 0; tries < 80 && sights.Count < 14; tries++)
                {
                    var p = new Vector2(R(30f, size - 30f), R(30f, size - 30f));
                    if (map.HeightAt(p) > map.waterLevel + 0.8f) sights.Add(p);
                }
            }
            int pick;
            do pick = attractRng.Next(sights.Count); while (pick == lastSight && sights.Count > 1);
            lastSight = pick;
            shotLen = R(13f, 18f);
            shotT = 0f;
            float driftAngle = R(0f, Mathf.PI * 2f);
            shotDrift = new Vector2(Mathf.Cos(driftAngle), Mathf.Sin(driftAngle)) * R(5f, 14f);
            shotAt = sights[pick];
            shotAt.x = Mathf.Clamp(shotAt.x, 20f, size - 20f - Mathf.Abs(shotDrift.x));
            shotAt.y = Mathf.Clamp(shotAt.y, 20f, size - 20f - Mathf.Abs(shotDrift.y));
            shotYaw = R(0f, 360f);
            shotYawRate = R(1.2f, 2.8f) * (attractRng.Next(2) == 0 ? -1f : 1f);
            shotDist0 = R(42f, 62f);
            shotDist1 = shotDist0 + R(-14f, -4f);
            rig.Snap(shotAt, shotYaw, shotDist0);
            attractDrove = true;
            dipping = false;
            dip.RemoveFromClassList("m-dip--on");
        }

        // ------------------------------------------------------------ illustrations
        // The key art (Art/keyart.jpg) drifts in on the splash and the veil; the opponent holograms
        // (Art/opponent_*.jpg, class m-portrait--<tier>) flicker while their page is on top and fade in when
        // the tier changes. Scale and tint only, so no layout work, and nothing runs during a match.
        sealed class Portrait { public VisualElement el; public Page page; public int tab = -1; public float since = 9f; public bool dim; }
        readonly List<Portrait> portraits = new List<Portrait>();

        Portrait AddPortrait(VisualElement el, Page page)
        {
            var p = new Portrait { el = el, page = page };
            portraits.Add(p);
            return p;
        }

        void SetTier(Label name, VisualElement portrait, int tier)
        {
            if (name == null || portrait == null) return;
            for (int i = 0; i < 3; i++)
            {
                name.EnableInClassList("m-tier--" + i, i == tier);
                if (i == tier && !portrait.ClassListContains("m-portrait--" + i))
                {
                    // A new face starts dark and fades in (AnimatePortraits may already have run this frame).
                    foreach (var p in portraits) if (p.el == portrait) p.since = 0f;
                    portrait.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, 0f);
                }
                portrait.EnableInClassList("m-portrait--" + i, i == tier);
            }
        }

        void AnimatePortraits(float now, float dt)
        {
            var top = Top;
            for (int i = 0; i < portraits.Count; i++)
            {
                var p = portraits[i];
                if (p.el == null || p.page != top || (p.tab >= 0 && manualTab != p.tab)) continue;
                p.since += dt;
                float h = i * 0.37f;
                float a = 0.9f + 0.07f * Mathf.Sin(now * 5.3f + h * 9f) * Mathf.Sin(now * 1.7f + h * 4f);
                if (Mathf.Repeat(now * 0.23f + h, 1f) < 0.012f) a *= 0.55f;   // a ~50 ms drop-out every ~4 s
                a *= Mathf.SmoothStep(0f, 1f, p.since / 0.45f);
                if (p.dim) a *= 0.5f;
                p.el.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, a);
            }
        }

        static void Drift(VisualElement art, float t, float amount)
        {
            if (art == null) return;
            float s = 1f + amount * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            art.style.scale = new Scale(new Vector3(s, s, 1f));
        }

        // Commander.Init: 1.5 / 3 / 5.5 actions a second; reactions 0.9 / 0.45 / 0.22 s.
        static float Apm(AIDifficulty d) => d == AIDifficulty.Recruit ? 90f : d == AIDifficulty.Veteran ? 180f : 330f;
        static float Reaction(AIDifficulty d) => d == AIDifficulty.Recruit ? 0.9f : d == AIDifficulty.Veteran ? 0.45f : 0.22f;

        // ------------------------------------------------------------ words
        static string DifficultyName(AIDifficulty d) =>
            d == AIDifficulty.Recruit ? "RECRUIT" : d == AIDifficulty.Veteran ? "VETERAN" : "COMMANDER";

        static string DifficultyBlurb(AIDifficulty d) =>
            d == AIDifficulty.Recruit ? "90 actions per minute, slow reactions. Still scouts, still reads you."
            : d == AIDifficulty.Veteran ? "180 actions a minute with half-second reactions. A seasoned field officer."
            : "330 actions a minute with 0.22 s reactions: as fast as the best officers alive.";

        static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

        static string ResultName(int r) => r == 1 ? "VICTORY" : r == 0 ? "DEFEAT" : "DRAW";

        static readonly string[] Tips =
        {
            "The AI sees only what its own units see. Kill its scout, or hide your army, and it has to guess.",
            "It reads how you play and picks its plan against that. Change your style and it will notice.",
            "Between matches it remembers you, and starts the next one from what worked against you.",
            "A Mech lands seven minutes after its bay is finished. Defend the bay until then.",
            "Your Mech is not under your command. Click the Mech strip to find it, and listen to what it tells you.",
            "A Digger with no Foundry to bring ore to waits with its load. Keep one standing.",
            "P pauses the match; ',' and '.' change the game speed, and a pause keeps it.",
            "Fire runs downwind through dry grass and groves. Scorchers set it on purpose.",
            "Maulers and Thumpers break towers; Lancers and Rocketeers break armour.",
            "Aircraft outlast anything that cannot shoot up. Bring Aegis tanks, Swifts, Spitters or Marksmen.",
            "Double right-click hurries an order: no cover, no turns, straight there.",
            "A soldier hit too hard, too far from friends, can break and run. Deserters camp behind their base.",
        };

        // The play tips and the lore's (LoreText.Tips) in one rotation.
        string Tip()
        {
            int i = UnityEngine.Random.Range(0, Tips.Length + LoreText.Tips.Length);
            return i < Tips.Length ? Tips[i] : LoreText.Tips[i - Tips.Length];
        }
    }
}
