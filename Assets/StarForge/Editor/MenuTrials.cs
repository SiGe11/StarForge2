// MenuTrials.cs — walks the menus in play mode (Tools/editor.sh play, then
// `editor.sh call StarForge.EditorTools.MenuTrials.Walk` and `wait ...WalkReport`):
// every page and tab photographed to Temp/menu_*.png, each checked for elements past
// the panel's edge and one-line labels wider than their box; then Deploy, pause (Esc
// closes a page before it resumes), surrender, the after-action report and the service
// record, and whether the illustrations (UI/Art) resolved. Every pref it touches is put back.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.Game;
using StarForge.Sim;
using StarForge.UI;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class MenuTrials
    {
        /// <summary>Stage the walk at the menu (a fresh Play). About 40 s.</summary>
        public static string Walk()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<MenuWalkTrial>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("MenuWalkTrial").AddComponent<MenuWalkTrial>();
            return "staging; call MenuTrials.WalkReport in about 40 s";
        }

        public static string WalkReport()
        {
            var t = Object.FindAnyObjectByType<MenuWalkTrial>();
            return t == null ? "no trial running" : t.report;
        }

        // ---- the scene-reload paths, one `editor.sh call` each so nothing has to survive the
        // load: SaveRecord, Dismiss, DeployPlayer, Surrender, EscOnReport, Rematch, PauseToMenu,
        // NewMap, DeployObserve, then State after each (a second or three later) and RestoreRecord.
        static string savedRecord;
        static bool recordSaved;

        static MenuController Menu => Object.FindAnyObjectByType<MenuController>();

        static string Invoke(string method, params object[] args)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var m = Menu;
            if (m == null) return "no MenuController";
            var mi = typeof(MenuController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi == null) return "no method " + method;
            mi.Invoke(m, args);
            return "ok " + method;
        }

        static object Get(MenuController m, string field) =>
            typeof(MenuController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(m);

        public static string SaveRecord()
        {
            if (!Application.isPlaying) return "not playing";
            if (recordSaved) return "already saved (a run cut short keeps its first snapshot)";
            savedRecord = PlayerPrefs.HasKey("sf_service_record") ? PlayerPrefs.GetString("sf_service_record") : null;
            recordSaved = true;
            return $"saved ({(savedRecord == null ? "none" : savedRecord.Length + " chars")})";
        }

        /// <summary>The reload paths' saved record goes back when play stops if RestoreRecord never came:
        /// a sequence cut short left its surrender and rematch in the Editor's real service record.</summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void RestoreRecordOnStop()
        {
            UnityEditor.EditorApplication.playModeStateChanged += change =>
            {
                if (change == UnityEditor.PlayModeStateChange.EnteredEditMode && recordSaved) Debug.Log("[MenuTrials] record put back after a stop: " + RestoreRecord());
            };
        }

        public static string RestoreRecord()
        {
            if (!recordSaved) return "nothing saved";
            if (savedRecord == null) PlayerPrefs.DeleteKey("sf_service_record");
            else PlayerPrefs.SetString("sf_service_record", savedRecord);
            PlayerPrefs.Save();
            recordSaved = false;
            return "restored";
        }

        public static string Dismiss() => Invoke("DismissSplash");
        public static string DeployPlayer() { var s = Invoke("OpenSetup", false); return s.StartsWith("ok") ? Invoke("Deploy") : s; }
        public static string DeployObserve() { var s = Invoke("OpenSetup", true); return s.StartsWith("ok") ? Invoke("Deploy") : s; }
        public static string Surrender() { GameBootstrap.Instance?.Surrender(); return "surrendered"; }
        public static string EscOnReport() => Invoke("Back");
        public static string Rematch() => Invoke("Rematch");
        public static string PauseToMenu()
        {
            var b = GameBootstrap.Instance;
            if (b == null) return "no bootstrap";
            if (b.State == MatchState.Playing) b.TogglePause();
            return Invoke("Reload", (System.Action)(() => b.ReturnToMenu()), "RETURNING TO COMMAND", null);
        }
        public static string NewMap()
        {
            var s = Invoke("OpenSetup", false);
            if (!s.StartsWith("ok")) return s;
            var b = GameBootstrap.Instance;
            return Invoke("Reload", (System.Action)(() => b.ReturnToMenu()), "SURVEYING A NEW SECTOR", "setup");
        }

        /// <summary>Where the menus stand: state, the page on top, the veil, the record.</summary>
        public static string State()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var m = Menu;
            var b = GameBootstrap.Instance;
            if (m == null || b == null) return "no MenuController or GameBootstrap";
            var root = m.GetComponent<UIDocument>().rootVisualElement;
            var veil = root.Q("menuVeil");
            var splash = root.Q("menuSplash");
            var stack = Get(m, "stack") as System.Collections.IList;
            string top = stack != null && stack.Count > 0 ? stack[stack.Count - 1].GetType().GetField("name").GetValue(stack[stack.Count - 1]) as string : "none";
            var rec = ServiceRecord.Load();
            var map = MapInfo.Instance;
            return $"state {b.State}, top {top} (stack {stack?.Count}), splash {splash.ClassListContains("m-splash--visible")}, " +
                   $"veil [{string.Join(" ", veil.GetClasses())}] picking {veil.pickingMode}, deploying {Get(m, "deploying")}, viewingField {Get(m, "viewingField")}, " +
                   $"MatchFromMenu {MenuController.MatchFromMenu}, spectate {MatchSettings.spectate}, speed x{b.Speed}, timeScale {Time.timeScale}, " +
                   $"record {rec.matches} ({rec.wins} W {rec.losses} L), map seed {map?.seed}, inputLocked {Object.FindAnyObjectByType<RTSCamera>()?.inputLocked}";
        }

        /// <summary>Import settings for the menus' illustrations (UI/Art), kept in their .meta files: own size (no
        /// power-of-two rescale, up to 4096), CompressedHQ (BC7 expected in the player; the Editor shows RGBA32; dark skies band under DXT1), mipmaps and
        /// trilinear filtering (the key art is drawn smaller than its 2752 px and slowly scaled). Run once after
        /// adding or replacing a picture there; not a postprocessor, whose arrival would reimport every texture.</summary>
        public static string ImportArt()
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/StarForge/UI/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 4096;
                ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                sb.Append($"{Path.GetFileName(path)} {t?.width}x{t?.height} {t?.format}; ");
            }
            return sb.Length == 0 ? "no textures in Assets/StarForge/UI/Art" : sb.ToString();
        }

        /// <summary>Re-imports the menus' UXML and USS (edit mode): a UXML naming custom elements
        /// that was imported before their scripts compiled is read again with them.</summary>
        public static string Reimport()
        {
            if (EditorApplication.isPlaying) return "stop play first";
            string[] paths =
            {
                "Assets/StarForge/UI/StarForgeMenu.uss", "Assets/StarForge/UI/StarForgeHUD.uss",
                "Assets/StarForge/UI/StarForgeMenu.uxml", "Assets/StarForge/UI/StarForgeHUD.uxml",
            };
            foreach (var p in paths) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/StarForge/UI/StarForgeHUD.uxml");
            if (tree == null) return "HUD UXML did not load";
            var probe = tree.Instantiate();
            int shades = probe.Query<SFShade>().ToList().Count, frames = probe.Query<SFFrame>().ToList().Count;
            return $"reimported; the HUD tree holds menuRoot {(probe.Q("menuRoot") != null)}, {shades} shades, {frames} frames, " +
                   $"endTitle {(probe.Q<Label>("endTitle") != null)}, cheatLabel last {(probe.Q("root")?.Children() is IEnumerable<VisualElement> c && Last(c)?.name == "cheatLabel")}";
        }

        static VisualElement Last(IEnumerable<VisualElement> list)
        {
            VisualElement last = null;
            foreach (var e in list) last = e;
            return last;
        }
    }

    public sealed class MenuWalkTrial : MonoBehaviour
    {
        public string report = "staging";
        readonly StringBuilder sb = new StringBuilder();
        MenuController menu;
        GameBootstrap boot;
        VisualElement root;
        int shots;

        static readonly string[] Keys =
        {
            "sf_service_record", "sf_difficulty", "sf_game_speed", "sf_edge_pan", "sf_pan_speed",
            "sf_rotate_speed", "sf_show_perf", "sf_vol_ui", AudioDirector.MasterKey, AudioDirector.MusicKey,
        };
        readonly Dictionary<string, string> saved = new Dictionary<string, string>();

        object Call(string method, params object[] args)
        {
            var m = typeof(MenuController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) { sb.AppendLine($"  !! no method {method}"); return null; }
            return m.Invoke(menu, args);
        }

        object Field(string name) =>
            typeof(MenuController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(menu);

        void SavePrefs()
        {
            foreach (var k in Keys)
                saved[k] = !PlayerPrefs.HasKey(k) ? null
                    : k == "sf_service_record" ? "s:" + PlayerPrefs.GetString(k)
                    : k == "sf_difficulty" || k == "sf_game_speed" || k == "sf_edge_pan" || k == "sf_show_perf" ? "i:" + PlayerPrefs.GetInt(k)
                    : "f:" + PlayerPrefs.GetFloat(k).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        bool restored;

        /// <summary>Once: at the walk's end, or from OnDestroy when play stops part-way (a second
        /// time would put back the walk-start values over anything changed since).</summary>
        void RestorePrefs()
        {
            if (restored) return;
            restored = true;
            foreach (var kv in saved)
            {
                if (kv.Value == null) { PlayerPrefs.DeleteKey(kv.Key); continue; }
                string v = kv.Value.Substring(2);
                if (kv.Value[0] == 's') PlayerPrefs.SetString(kv.Key, v);
                else if (kv.Value[0] == 'i') PlayerPrefs.SetInt(kv.Key, int.Parse(v));
                else PlayerPrefs.SetFloat(kv.Key, float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
            }
            PlayerPrefs.Save();
        }

        IEnumerator Start()
        {
            SavePrefs();
            yield return Run();
        }

        void OnDestroy() => RestorePrefs();

        IEnumerator Run()
        {
            float wait = 0f;
            while ((menu = FindAnyObjectByType<MenuController>()) == null && wait < 10f) { wait += Time.unscaledDeltaTime; yield return null; }
            boot = GameBootstrap.Instance;
            if (menu == null || boot == null) { report = "done: no MenuController or GameBootstrap"; yield break; }
            if (boot.State != MatchState.Menu) { report = "done: not at the menu (start from a fresh Play)"; yield break; }
            root = menu.GetComponent<UIDocument>().rootVisualElement;
            var rig = FindAnyObjectByType<RTSCamera>();
            Directory.CreateDirectory("Temp");
            sb.AppendLine($"panel {root.worldBound.width:0}x{root.worldBound.height:0}, screen {Screen.width}x{Screen.height}");

            yield return Wait(1.2f);
            yield return Shot("splash");
            Call("DismissSplash");
            yield return Wait(1.2f);
            sb.AppendLine($"camera: inputLocked {rig?.inputLocked}, attract {(bool)Field("attractOn")}, focus {rig?.Focus}");
            yield return Shot("main");

            Call("OpenSetup", false);
            yield return Wait(1.4f);
            yield return Shot("setup");
            sb.AppendLine($"art: splash {Tex("splashArt")}; veil {Tex("veilArt")}; opponent {Tex("oppPortrait")}; roster {Tex("rosterTag0", true)}, {Tex("rosterTag1", true)}, {Tex("rosterTag2", true)}");
            Call("Pop");
            Call("OpenSetup", true);
            yield return Wait(0.8f);
            yield return Shot("observe");
            Call("Pop");

            var settings = Field("settings");
            Call("Push", settings);
            for (int t = 0; t < 4; t++)
            {
                Call("SetSettingsTab", t);
                yield return Wait(0.7f);
                yield return Shot($"settings_{t}");
            }
            for (int round = 0; round < 3; round++) for (int t = 0; t < 4; t++) Call("SetSettingsTab", t);
            var refresh = settings.GetType().GetField("refresh")?.GetValue(settings) as System.Collections.ICollection;
            sb.AppendLine($"settings: refreshers after 12 more tab switches {refresh?.Count} (the controls tab has none; target 0)");
            Call("Pop");

            var manual = Field("manual");
            Call("Push", manual);
            for (int t = 0; t < 3; t++)
            {
                Call("SetManualTab", t);
                if (t == 0) for (int i = 0; i < 9; i++) Call("Move", 1);
                yield return Wait(0.7f);
                yield return Shot($"manual_{t}");
            }
            Call("Pop");

            Call("Push", Field("record"));
            yield return Wait(0.8f);
            yield return Shot("record");
            Call("Pop");
            Call("Push", Field("credits"));
            yield return Wait(4f);
            yield return Shot("credits");
            Call("Pop");
            Call("AskQuit");
            yield return Wait(0.5f);
            yield return Shot("quit_dialog");
            Call("CloseModal", false);
            yield return Wait(0.4f);

            // ---- the match
            var before = ServiceRecord.Load();
            Call("OpenSetup", false);
            yield return Wait(0.6f);
            Call("Deploy");
            wait = 0f;
            while (boot.State != MatchState.Playing && wait < 6f) { wait += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(1.2f);
            var world = FindAnyObjectByType<GameWorld>();
            var player = FindAnyObjectByType<PlayerController>();
            Vector2 start = world.Map.StartPos(player != null ? player.team : 0);
            sb.AppendLine($"deploy: state {boot.State}, MatchFromMenu {MenuController.MatchFromMenu}, inputLocked {rig?.inputLocked}, camera {Vector2.Distance(rig.Focus, start):0.0} m from base, yaw {rig.yaw:0}, speed x{boot.Speed}");
            yield return Shot("playing");

            boot.TogglePause();
            yield return Wait(1.0f);
            yield return Shot("pause");
            Call("Push", settings);
            yield return Wait(0.5f);
            bool closed = (bool)Call("OnPausedEscape");
            yield return Wait(0.4f);
            bool resumes = !(bool)Call("OnPausedEscape");
            sb.AppendLine($"pause: Esc closed settings {closed}, then asks to resume {resumes}, state {boot.State}");
            boot.TogglePause();
            yield return Wait(0.6f);
            sb.AppendLine($"resumed: state {boot.State}, timeScale {Time.timeScale}, inputLocked {rig?.inputLocked}");

            boot.Surrender();
            yield return Wait(1.6f);
            var title = root.Q<Label>("endTitle");
            var after = ServiceRecord.Load();
            sb.AppendLine($"end: title \"{title?.text}\", subtitle \"{root.Q<Label>("endSubtitle")?.text}\", record {before.matches}->{after.matches} matches, losses {before.losses}->{after.losses}");
            yield return Shot("debrief");
            sb.AppendLine($"debrief opponent: column {root.Q("endOpponentColumn")?.resolvedStyle.display}, {Tex("endPortrait")}, \"{root.Q<Label>("endPortraitTag")?.text}\"");
            Call("SetViewField", true);
            yield return Wait(0.6f);
            sb.AppendLine($"view field: inputLocked {rig?.inputLocked}");
            yield return Shot("field");
            Call("SetViewField", false);
            yield return Wait(0.4f);

            RestorePrefs();
            report = $"done: {shots} shots in Temp/menu_*.png\n" + sb;
        }

        static IEnumerator Wait(float s)
        {
            float t = 0f;
            while (t < s) { t += Time.unscaledDeltaTime; yield return null; }
        }

        // The image an element's style resolved to (parent: the element is a label inside the picture).
        string Tex(string name, bool parent = false)
        {
            var e = root.Q(name);
            if (e != null && parent) e = e.parent;
            if (e == null) return $"{name} missing";
            var t = e.resolvedStyle.backgroundImage.texture;
            return t == null ? $"{name} no image" : $"{t.name} {t.width}x{t.height} {t.format}";
        }

        IEnumerator Shot(string name)
        {
            CheckLayout(name);
            ScreenCapture.CaptureScreenshot($"Temp/menu_{shots:00}_{name}.png", 2);
            shots++;
            yield return null;
            yield return null;
        }

        /// <summary>Elements past the panel's edge, and one-line labels wider than their box.</summary>
        void CheckLayout(string page)
        {
            var panel = root.worldBound;
            int outside = 0, clipped = 0;
            var notes = new List<string>();
            root.Query<VisualElement>().ForEach(e =>
            {
                if (e.resolvedStyle.display == DisplayStyle.None || !Visible(e)) return;
                var r = e.worldBound;
                if (float.IsNaN(r.x) || r.width < 1f || r.height < 1f) return;
                if (InScroll(e)) return;
                if (r.xMin < panel.xMin - 1f || r.yMin < panel.yMin - 1f || r.xMax > panel.xMax + 1f || r.yMax > panel.yMax + 1f)
                {
                    if (e.name == "creditsRoll" || e.ClassListContains("m-credit-head") || e.ClassListContains("m-credit-name") || e.ClassListContains("m-credit-text")) return;
                    outside++;
                    if (notes.Count < 6) notes.Add($"outside {Describe(e)} {r}");
                }
                if (e is Label l && !string.IsNullOrEmpty(l.text) && l.resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
                {
                    var size = l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    if (size.x > l.contentRect.width + 2f)
                    {
                        clipped++;
                        if (notes.Count < 10) notes.Add($"label wider than its box {Describe(l)} \"{l.text}\" {size.x:0} > {l.contentRect.width:0}");
                    }
                }
            });
            sb.AppendLine($"[{page}] outside {outside}, labels too wide {clipped}");
            foreach (var n in notes) sb.AppendLine("    " + n);
        }

        static bool Visible(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.opacity < 0.01f) return false;
            return true;
        }

        static bool InScroll(VisualElement e)
        {
            for (var p = e.parent; p != null; p = p.parent) if (p is ScrollView) return true;
            return false;
        }

        static string Describe(VisualElement e) =>
            $"{e.GetType().Name}{(string.IsNullOrEmpty(e.name) ? "" : "#" + e.name)}.{string.Join(".", e.GetClasses())}";
    }
}
