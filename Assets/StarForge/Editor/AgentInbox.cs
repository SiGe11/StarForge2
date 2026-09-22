// AgentInbox.cs — lets command-line tooling drive the open Editor through files,
// for when no live connection (the Unity MCP bridge) is available.
//
// Drop a text file into Temp/AgentInbox/ named <id>.cmd holding one command:
//
//     menu StarForge/Build All          run a menu item
//     refresh                           AssetDatabase.Refresh
//     play | stop                       enter or leave play mode
//     call Namespace.Type.Method        invoke a public static method (no
//                                       arguments); a string it returns is reported
//
// The Editor answers with <id>.out beside it: "ok" or "error", then whatever the
// command returned and every console message logged while it ran. Commands are
// taken one per Editor update, in name order.
//
// Outside play mode the inbox also refreshes the asset database by itself when a
// file under Assets/ changes, so edited scripts compile without the Editor
// window being brought to the front (macOS only checks on focus otherwise).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.World;

namespace StarForge.EditorTools
{
    [InitializeOnLoad]
    public static class AgentInbox
    {
        static readonly string Dir = Path.GetFullPath("Temp/AgentInbox");
        static double nextPoll, nextScan;
        static DateTime lastChange;

        static AgentInbox()
        {
            Directory.CreateDirectory(Dir);
            lastChange = LatestWrite();
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + 0.3;

            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && now > nextScan)
            {
                nextScan = now + 2.0;
                var latest = LatestWrite();
                if (latest > lastChange)
                {
                    lastChange = latest;
                    AssetDatabase.Refresh();
                }
            }

            string next = Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.cmd").OrderBy(f => f).FirstOrDefault() : null;
            if (next == null) return;
            string command;
            try { command = File.ReadAllText(next).Trim(); File.Delete(next); }
            catch (IOException) { return; }   // still being written

            var log = new StringBuilder();
            void Capture(string msg, string stack, LogType type)
            {
                log.Append('[').Append(type).Append("] ").AppendLine(msg);
                if (type == LogType.Exception || type == LogType.Error) log.AppendLine(stack);
            }
            Application.logMessageReceived += Capture;
            string status = "ok", result = "";
            try { result = Run(command) ?? ""; }
            catch (Exception e)
            {
                status = "error";
                result = (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e).ToString();
            }
            finally { Application.logMessageReceived -= Capture; }
            File.WriteAllText(Path.ChangeExtension(next, ".out"), $"{status}\n{result}\n{log}");
        }

        static string Run(string command)
        {
            int sp = command.IndexOf(' ');
            string verb = sp < 0 ? command : command.Substring(0, sp);
            string arg = sp < 0 ? "" : command.Substring(sp + 1).Trim();
            switch (verb)
            {
                case "menu":
                    if (!EditorApplication.ExecuteMenuItem(arg)) throw new ArgumentException("no menu item " + arg);
                    return "ran " + arg;
                case "refresh":
                    AssetDatabase.Refresh();
                    return "refreshed";
                case "play":
                    EditorApplication.EnterPlaymode();
                    return "entering play mode";
                case "stop":
                    EditorApplication.ExitPlaymode();
                    return "leaving play mode";
                case "call":
                {
                    int dot = arg.LastIndexOf('.');
                    string typeName = arg.Substring(0, dot), method = arg.Substring(dot + 1);
                    var type = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null)
                        ?? throw new ArgumentException("no type " + typeName);
                    var m = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null)
                        ?? throw new ArgumentException($"no static {method}() on {typeName}");
                    return m.Invoke(null, null)?.ToString() ?? "called " + arg;
                }
                case "state":
                    return $"playing={EditorApplication.isPlaying} compiling={EditorApplication.isCompiling} version={Application.unityVersion}";
                default:
                    throw new ArgumentException("unknown command " + verb);
            }
        }

        static DateTime LatestWrite()
        {
            var latest = DateTime.MinValue;
            try
            {
                foreach (var f in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".meta", StringComparison.Ordinal)) continue;
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t > latest) latest = t;
                }
            }
            catch (IOException) { }
            return latest;
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>Helpers the inbox can call while the Editor plays.</summary>
    public static class AgentPlay
    {
        /// <summary>Start an AI-vs-AI match at 4x speed (for recording shader variants).</summary>
        public static string SpectateFast()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            StarForge.Game.MatchSettings.spectate = true;
            StarForge.Game.MatchSettings.aiMemory = false;
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (boot == null) return "no GameBootstrap";
            boot.StartMatch();
            Time.timeScale = 4f;
            return "match started";
        }

        /// <summary>Where the animals are and how big they draw (debugging Fauna).</summary>
        public static string FaunaReport()
        {
            var f = UnityEngine.Object.FindAnyObjectByType<StarForge.View.Fauna>();
            if (f == null) return "no Fauna";
            var sb = new StringBuilder();
            int n = 0;
            foreach (Transform t in f.transform)
            {
                if (n++ > 12) break;
                var rs = t.GetComponentsInChildren<Renderer>();
                var anim = t.GetComponentInChildren<Animation>();
                string b = rs.Length > 0 ? rs[0].bounds.ToString() : "no renderer";
                sb.AppendLine($"{t.name} at {t.position} rot {t.eulerAngles.y:0} renderers {rs.Length} enabled {(rs.Length > 0 && rs[0].enabled)} bounds {b} anim {(anim != null ? anim.clip?.name + " playing " + anim.isPlaying + " clips " + anim.GetClipCount() : "none")}");
            }
            return sb.ToString();
        }

        /// <summary>Each animal prefab placed in a row and photographed from the side,
        /// with the posed mesh's real size (debugging the fauna prefabs).</summary>
        public static string FaunaPhoto()
        {
            var sb = new StringBuilder();
            var names = new[] { "Wolf", "Fox", "Eagle", "Songbird" };
            var root = new GameObject("FaunaPhoto");
            float x = 0f;
            foreach (var n in names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/StarForge/Prefabs/Fauna_{n}.prefab");
                if (prefab == null) { sb.AppendLine(n + ": no prefab"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(x, 500f, 0f);
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                var lo = Vector3.one * 1e9f; var hi = -lo;
                foreach (var bone in smr.bones) if (bone != null) { lo = Vector3.Min(lo, bone.position); hi = Vector3.Max(hi, bone.position); }
                sb.AppendLine($"{n}: skeleton {hi - lo}, lowest bone {lo.y - 500f:0.00}, renderer bounds {smr.bounds.size}");
                x += 4f;
            }
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.position = new Vector3(x * 0.5f - 2f + 6f, 504f, -7f);
            cam.transform.LookAt(new Vector3(x * 0.5f - 2f, 500.5f, 0f));
            cam.fieldOfView = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
            var rt = new RenderTexture(1600, 600, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 600, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 600), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes("Temp/fauna_photo.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            UnityEngine.Object.DestroyImmediate(root);
            rt.Release();
            return sb.ToString();
        }

        /// <summary>The songbird as the game shows it: birds on a patch of ground and one
        /// in the air, seen from the RTS camera's angle at close and mid zoom.</summary>
        public static string SongbirdPhoto()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StarForge/Prefabs/Fauna_Songbird.prefab");
            if (prefab == null) return "no prefab";
            var root = new GameObject("SongbirdPhoto");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(0f, 500f, 0f);
            ground.transform.localScale = Vector3.one * 4f;
            ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.30f, 0.30f, 0.22f) };
            var sb = new StringBuilder();
            var rng = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                bool air = i >= 6;
                float x = (float)(rng.NextDouble() * 4f - 2f), z = (float)(rng.NextDouble() * 4f - 2f);
                go.transform.position = new Vector3(x, 500f + (air ? 2.2f + i * 0.3f : 0f), z + (air ? 3f : 0f));
                go.transform.rotation = Quaternion.Euler(air ? -8f : 0f, (float)(rng.NextDouble() * 360f), 0f);
                var anim = go.GetComponentInChildren<Animation>();
                string clip = air ? "Fly" : (i % 3 == 0 ? "Peck" : "Perch");
                if (anim != null && anim.GetClip(clip) != null)
                {
                    var st = anim[clip];
                    anim.Play(clip);
                    st.time = air ? (i - 6) * 0.11f : 0.2f * i;
                    anim.Sample();
                }
                if (i == 0)
                {
                    var r = go.GetComponentInChildren<SkinnedMeshRenderer>();
                    sb.Append($"bird bounds {r.bounds.size}, lowest {r.bounds.min.y - 500f:0.000} m over the ground; clips ");
                    foreach (AnimationState s in anim) sb.Append($"{s.clip.name} {s.length:0.00}s ");
                }
            }
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.4f, 0.45f, 0.5f);
            var light = new GameObject("sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(45f, 30f, 0f);
            foreach (float dist in new[] { 26f, 60f })
            {
                float pitch = StarForge.View.RTSCamera.PitchFor(dist);
                cam.transform.rotation = Quaternion.Euler(pitch, 20f, 0f);
                cam.transform.position = new Vector3(0f, 500f, 3f) - cam.transform.forward * dist;
                var rt = new RenderTexture(1000, 700, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1000, 700, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1000, 700), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes($"Temp/songbird_{dist:0}.png", tex.EncodeToPNG());
                rt.Release();
            }
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            UnityEngine.Object.DestroyImmediate(light.gameObject);
            UnityEngine.Object.DestroyImmediate(root);
            return sb.ToString();
        }

        public static string FaunaBones()
        {
            var sb = new StringBuilder();
            foreach (var n in new[] { "Songbird" })
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/StarForge/Art/Fauna/{n}.fbx");
                var go = (GameObject)UnityEngine.Object.Instantiate(src);
                sb.Append(n + ": ");
                foreach (var t in go.GetComponentsInChildren<Transform>()) sb.Append($"{t.name}@{t.position.x:0.00},{t.position.y:0.00},{t.position.z:0.00} ");
                sb.AppendLine();
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath($"Assets/StarForge/Art/Fauna/{n}.fbx"))
                    if (a is AnimationClip c) sb.Append($"clip {c.name} {c.length:0.00}s legacy {c.legacy}; ");
                    else if (a is Mesh m) sb.Append($"mesh {m.name} sub {m.subMeshCount} verts {m.vertexCount} uv {m.uv.Length}; ");
                sb.AppendLine();
                UnityEngine.Object.DestroyImmediate(go);
            }
            return sb.ToString();
        }

        static Vector3 scareAt;
        static readonly System.Collections.Generic.List<(string, Vector3)> before = new System.Collections.Generic.List<(string, Vector3)>();

        /// <summary>An explosion beside the first wolf and a fire by the first bird
        /// flock; FaunaAfter reports how far each animal moved.</summary>
        public static string FaunaScare()
        {
            var f = UnityEngine.Object.FindAnyObjectByType<StarForge.View.Fauna>();
            var w = StarForge.World.GameWorld.Instance;
            if (f == null || w == null) return "no fauna/world";
            before.Clear();
            foreach (Transform t in f.transform) before.Add((t.name, t.position));
            f.TryGetAnimal(false, out var wolf);
            f.TryGetAnimal(true, out var bird);
            scareAt = wolf + new Vector3(4f, 0f, 3f);
            w.Raise(new StarForge.World.GameEvent { kind = StarForge.World.GameEventKind.Impact, team = 1, pos = scareAt, scale = 1.35f });
            var b = new Vector3(bird.x, 0f, bird.z);
            w.Raise(new StarForge.World.GameEvent { kind = StarForge.World.GameEventKind.Impact, team = 1, pos = b, scale = 1.35f });
            return $"blast at {scareAt} (wolf at {wolf}) and under the birds at {b}; {f.FlightReport()}";
        }

        public static string FaunaAfter()
        {
            var f = UnityEngine.Object.FindAnyObjectByType<StarForge.View.Fauna>();
            var sb = new StringBuilder();
            int i = 0;
            foreach (Transform t in f.transform)
            {
                if (i >= before.Count) break;
                var (n, p0) = before[i++];
                var d0 = new Vector2(p0.x - scareAt.x, p0.z - scareAt.z).magnitude;
                var d1 = new Vector2(t.position.x - scareAt.x, t.position.z - scareAt.z).magnitude;
                sb.Append($"{n}: moved {Vector3.Distance(p0, t.position):0.0} m, from the blast {d0:0} -> {d1:0} m; ");
            }
            sb.Append(f.FlightReport());
            return sb.ToString();
        }

        /// <summary>Force-reimport the animals and the music (after their import rules change).</summary>
        public static string ReimportFaunaAndMusic()
        {
            int n = 0;
            foreach (var folder in new[] { "Assets/StarForge/Art/Fauna", "Assets/StarForge/Audio/Music" })
                foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
                {
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
                    n++;
                }
            return $"reimported {n}";
        }

        /// <summary>Two shots of a grove half a second apart, with the camera held still,
        /// so the sway can be measured (Temp/wind_a.png, wind_b.png).</summary>
        public static string WindShot()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("WindShot");
            go.AddComponent<WindShotter>();
            return "shooting; call WindShotReport shortly";
        }

        public static string WindShotReport()
        {
            var w = UnityEngine.Object.FindAnyObjectByType<WindShotter>();
            return w == null ? "no shot running" : w.report;
        }

        /// <summary>Fell a dozen trees and report how each one came down (and photograph
        /// one mid-fall): how long it took, how fast the crown hit, whether it hung up
        /// in a neighbour. Also prints the match's wind.</summary>
        public static string TreeFallTest()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("TreeFallTest");
            go.AddComponent<TreeFaller>();
            return "felling; call TreeFallReport in half a minute";
        }

        public static string TreeFallReport()
        {
            var t = UnityEngine.Object.FindAnyObjectByType<TreeFaller>();
            return t == null ? "no test running" : t.report;
        }

        /// <summary>Look off the edge of the map (where a flock once landed and the
        /// gallery followed it): what is out there, and a shot of it.</summary>
        public static string EdgeLook()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("EdgeLook");
            go.AddComponent<EdgeLooker>();
            return "looking; call EdgeLookReport shortly";
        }

        public static string EdgeLookReport()
        {
            var e = UnityEngine.Object.FindAnyObjectByType<EdgeLooker>();
            return e == null ? "no looker" : e.report;
        }

        /// <summary>Watch a songbird flock: a shot of it feeding, then put it up and
        /// shoot it on the wing. Writes Temp/sb_*.png.</summary>
        public static string SongbirdWatch()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("SongbirdWatch");
            go.AddComponent<SongbirdWatcher>();
            return "watching; call SongbirdWatchReport in half a minute";
        }

        public static string SongbirdWatchReport()
        {
            var w = UnityEngine.Object.FindAnyObjectByType<SongbirdWatcher>();
            return w == null ? "no watcher" : w.report;
        }

        /// <summary>Light one plant after another and see how far each fire gets: the
        /// spread is meant to die out in a few plants most times and now and then run.
        /// Call FireTrialReport for the answer once it has run.</summary>
        public static string FireTrial()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var world = StarForge.World.GameWorld.Instance;
            if (world == null || world.Plants == null) return "no world";
            var go = new GameObject("FireTrial");
            var run = go.AddComponent<FireTrialRunner>();
            run.world = world;
            return "running 30 fires at 8x speed; call FireTrialReport in a few minutes";
        }

        public static string FireTrialReport()
        {
            var run = UnityEngine.Object.FindAnyObjectByType<FireTrialRunner>();
            return run == null ? "no trial running" : run.report;
        }

        /// <summary>Set a dozen plants across the map alight (fire effects, for the recorder).</summary>
        public static string Burn()
        {
            var world = StarForge.World.GameWorld.Instance;
            var veg = world != null ? world.Plants : null;
            if (veg == null) return "no vegetation";
            int lit = 0;
            for (int i = 0; i < veg.plants.Length && lit < 12; i += 7) { veg.Ignite(world, i, 0); lit++; }
            return $"lit {lit}";
        }
    }

    /// <summary>Lights one plant at a time and counts what each fire takes with it.</summary>
    public sealed class FireTrialRunner : MonoBehaviour
    {
        public StarForge.World.GameWorld world;
        public string report = "running";

        System.Collections.IEnumerator Start()
        {
            var veg = world.Plants;
            float scale = Time.timeScale;
            Time.timeScale = 8f;
            var sizes = new System.Collections.Generic.List<int>();
            var cells = new System.Collections.Generic.List<int>();
            var rng = new System.Random(4242);
            for (int t = 0; t < 30; t++)
            {
                // A plant with company: the middle of a stand, where a fire can go somewhere.
                int start = -1;
                for (int k = 0; k < 400 && start < 0; k++)
                {
                    int i = rng.Next(veg.plants.Length);
                    if (veg.live[i].state != StarForge.World.PlantState.Standing || veg.live[i].charred > 0.2f) continue;
                    int near = 0;
                    var c = veg.Pos2(i);
                    for (int j = 0; j < veg.plants.Length; j++)
                        if (j != i && (veg.Pos2(j) - c).sqrMagnitude < 12f * 12f && veg.live[j].charred < 0.2f) near++;
                    if (near >= 6) start = i;
                }
                if (start < 0) break;
                int before = Lit(veg), beforeCells = Burnt(veg);
                veg.Ignite(world, start, 0);
                float until = Time.time + 300f;
                yield return new WaitForSeconds(2f);
                while ((veg.burning.Count > 0 || veg.burningGrass.Count > 0) && Time.time < until)
                    yield return new WaitForSeconds(0.5f);
                sizes.Add(Lit(veg) - before);
                cells.Add(Burnt(veg) - beforeCells);
                report = $"{sizes.Count}/30 fires so far";
            }
            Time.timeScale = scale;
            var sorted = new System.Collections.Generic.List<int>(sizes);
            sorted.Sort();
            float mean = 0f;
            foreach (int n in sizes) mean += n;
            mean /= Mathf.Max(1, sizes.Count);
            int big = 0;
            foreach (int n in sizes) if (n >= 10) big++;
            int litTotal = Lit(veg);
            report = $"{sizes.Count} fires. Plants each: {string.Join(",", sorted)}; median {(sorted.Count > 0 ? sorted[sorted.Count / 2] : 0)}, mean {mean:0.0}, {big} took 10+. " +
                     $"Grass cells each: {string.Join(",", cells)}. " +
                     $"After them all: {litTotal}/{veg.plants.Length} plants burnt ({100f * litTotal / veg.plants.Length:0}%), grass {100f * veg.Burnt:0}% of the map's fuel. {veg.FireReport()}";
        }

        static int Burnt(StarForge.World.Vegetation veg)
        {
            int n = 0;
            var st = veg.GrassState;
            if (st != null)
                for (int i = 0; i < st.Length; i++) if (st[i] == 2) n++;
            return n;
        }

        static int Lit(StarForge.World.Vegetation veg)
        {
            int n = 0;
            for (int i = 0; i < veg.live.Length; i++) if (veg.live[i].charred > 0.01f || veg.live[i].burning) n++;
            return n;
        }
    }

    /// <summary>Photographs a songbird flock feeding and in flight.</summary>
    public sealed class SongbirdWatcher : MonoBehaviour
    {
        public string report = "watching";

        System.Collections.IEnumerator Start()
        {
            var fauna = UnityEngine.Object.FindAnyObjectByType<StarForge.View.Fauna>();
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            if (fauna == null || rig == null) { report = "no fauna/camera"; yield break; }
            rig.scripted = true;
            float scale = Time.timeScale;
            Time.timeScale = 1f;
            if (!fauna.TryGetSongbirds(out var p, out _)) { report = "no songbirds"; yield break; }
            rig.JumpTo(new Vector2(p.x, p.z));
            rig.ZoomTo(28f);
            yield return new WaitForSecondsRealtime(2.5f);
            ScreenCapture.CaptureScreenshot("Temp/sb_feed.png");
            var sb = new StringBuilder("feeding: " + fauna.SongbirdDetail(new Vector2(p.x, p.z), rig.cam));
            yield return new WaitForSecondsRealtime(1.5f);
            fauna.FlushSongbirds();
            yield return new WaitForSecondsRealtime(1.2f);
            if (fauna.TryGetSongbirds(out p, out _)) { rig.JumpTo(new Vector2(p.x, p.z)); rig.ZoomTo(34f); }
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot("Temp/sb_flight.png");
            sb.Append(" | just up: " + fauna.FlightReport());
            yield return new WaitForSecondsRealtime(2.5f);
            if (fauna.TryGetSongbirds(out p, out _)) rig.JumpTo(new Vector2(p.x, p.z));
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot("Temp/sb_flight2.png");
            sb.Append(" | on the way: " + fauna.FlightReport());
            yield return new WaitForSecondsRealtime(8f);
            if (fauna.TryGetSongbirds(out p, out _)) { rig.JumpTo(new Vector2(p.x, p.z)); rig.ZoomTo(28f); }
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot("Temp/sb_landed.png");
            sb.Append(" | later: " + fauna.FlightReport());
            Time.timeScale = scale;
            report = sb.ToString();
        }
    }

    /// <summary>Points the camera off the map's edge and says what is drawn there.</summary>
    public sealed class EdgeLooker : MonoBehaviour
    {
        public string report = "looking";

        System.Collections.IEnumerator Start()
        {
            var map = StarForge.World.MapInfo.Instance;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            if (map == null || rig == null) { report = "no map/camera"; yield break; }
            rig.scripted = true;
            Time.timeScale = 1f;
            var sb = new StringBuilder($"map {map.mapSize} m, water level {map.waterLevel:0.0}; ");
            float mid = map.mapSize * 0.5f;
            foreach (float x in new[] { -30f, -10f, 2f, 10f, 30f })
                sb.Append($"x={x:0}: ground {map.HeightAt(new Vector2(x, mid)):0.0} water depth {map.WaterDepth(new Vector2(x, mid)):0.00}; ");
            var backdrop = GameObject.Find("Backdrop") ?? GameObject.Find("Map/Backdrop");
            sb.Append(backdrop != null ? $"backdrop {backdrop.name} active {backdrop.activeInHierarchy} " : "no backdrop object found ");
            foreach (Transform t in map.transform)
                sb.Append($"[{t.name} {(t.gameObject.activeSelf ? "on" : "OFF")}] ");
            rig.JumpTo(new Vector2(12f, mid));
            rig.ZoomTo(40f);
            yield return new WaitForSecondsRealtime(2.5f);
            ScreenCapture.CaptureScreenshot("Temp/edge_low.png");
            yield return new WaitForSecondsRealtime(1f);
            rig.ZoomTo(120f);
            yield return new WaitForSecondsRealtime(2.5f);
            ScreenCapture.CaptureScreenshot("Temp/edge_far.png");
            yield return new WaitForSecondsRealtime(1f);
            report = sb.ToString();
        }
    }

    /// <summary>Fells trees and watches them go over.</summary>
    public sealed class TreeFaller : MonoBehaviour
    {
        public string report = "felling";

        System.Collections.IEnumerator Start()
        {
            var world = StarForge.World.GameWorld.Instance;
            var veg = world != null ? world.Plants : null;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            if (veg == null) { report = "no vegetation"; yield break; }
            var sb = new StringBuilder();
            float t0 = Time.time;
            sb.Append($"wind: heading {StarForge.World.Wind.Heading}, weather {StarForge.World.Wind.Weather:0.00}, speed now {StarForge.World.Wind.Speed(t0):0.00}, in 20s {StarForge.World.Wind.Speed(t0 + 20f):0.00}, in 60s {StarForge.World.Wind.Speed(t0 + 60f):0.00}; global {Shader.GetGlobalVector("_SF_Wind")}. ");

            // A dozen standing trees, half of them in company (so some hang up).
            var picked = new System.Collections.Generic.List<int>();
            var rng = new System.Random(11);
            for (int k = 0; k < 4000 && picked.Count < 12; k++)
            {
                int i = rng.Next(veg.plants.Length);
                var kind = veg.KindOf(i);
                if (kind.bush || veg.live[i].state != StarForge.World.PlantState.Standing || picked.Contains(i)) continue;
                picked.Add(i);
            }
            if (picked.Count == 0) { report = "no trees"; yield break; }
            if (rig != null)
            {
                rig.scripted = true;
                var p = veg.plants[picked[0]].pos;
                rig.JumpTo(new Vector2(p.x, p.z));
                rig.ZoomTo(30f);
            }
            Time.timeScale = 1f;
            var started = new float[picked.Count];
            var landed = new float[picked.Count];
            var hit = new float[picked.Count];
            var lodgedAt = new float[picked.Count];
            for (int n = 0; n < picked.Count; n++)
            {
                int i = picked[n];
                var dir = new Vector2(Mathf.Cos(n * 1.7f), Mathf.Sin(n * 1.7f));
                started[n] = Time.time;
                veg.Fell(world, i, dir, n % 2 == 0 ? 0.25f : 1.2f);   // a Mauler's lean, then a blast
                landed[n] = -1f;
                lodgedAt[n] = -1f;
            }
            yield return new WaitForSecondsRealtime(1.2f);
            ScreenCapture.CaptureScreenshot("Temp/fall_mid.png");
            float until = Time.time + 25f;
            while (Time.time < until)
            {
                for (int n = 0; n < picked.Count; n++)
                {
                    int i = picked[n];
                    if (veg.live[i].lodged >= 0 && lodgedAt[n] < 0f) lodgedAt[n] = veg.live[i].fallAngle;
                    if (landed[n] < 0f && veg.live[i].state == StarForge.World.PlantState.Down)
                    {
                        landed[n] = Time.time - started[n];
                        hit[n] = veg.live[i].fallAngle;
                    }
                }
                yield return null;
            }
            ScreenCapture.CaptureScreenshot("Temp/fall_after.png");
            for (int n = 0; n < picked.Count; n++)
            {
                var k = veg.KindOf(picked[n]);
                sb.Append($"[{k.name} h{k.height * veg.plants[picked[n]].scale:0.0}m push {(n % 2 == 0 ? "lean" : "blast")} ");
                sb.Append(landed[n] >= 0f ? $"down in {landed[n]:0.0}s" : $"still going ({veg.live[picked[n]].fallAngle * Mathf.Rad2Deg:0}deg)");
                if (lodgedAt[n] >= 0f) sb.Append($", hung up at {lodgedAt[n] * Mathf.Rad2Deg:0}deg");
                sb.Append("] ");
            }
            report = sb.ToString();
        }
    }

    /// <summary>Photographs a grove twice, to measure the sway.</summary>
    public sealed class WindShotter : MonoBehaviour
    {
        public string report = "shooting";

        System.Collections.IEnumerator Start()
        {
            var world = StarForge.World.GameWorld.Instance;
            var veg = world != null ? world.Plants : null;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            if (veg == null || rig == null) { report = "no vegetation/camera"; yield break; }
            // A tree with company, so the shot is full of crowns.
            int best = -1;
            int bestNear = 0;
            for (int i = 0; i < veg.plants.Length; i += 3)
            {
                if (veg.KindOf(i).bush || veg.live[i].state != StarForge.World.PlantState.Standing) continue;
                var c = veg.Pos2(i);
                if (MapInfo.Instance != null && MapInfo.Instance.WaterDepth(c) > -6f) continue;   // not by the lake
                int near = 0;
                for (int j = 0; j < veg.plants.Length; j++)
                    if (!veg.KindOf(j).bush && veg.KindOf(j).HasCrown && (veg.Pos2(j) - c).sqrMagnitude < 14f * 14f) near++;
                if (near > bestNear) { bestNear = near; best = i; }
            }
            if (best < 0) { report = "no trees"; yield break; }
            rig.scripted = true;
            Time.timeScale = 1f;
            var p = veg.plants[best].pos;
            rig.JumpTo(new Vector2(p.x, p.z));
            rig.ZoomTo(34f);
            yield return new WaitForSecondsRealtime(2.5f);
            float t0 = Time.time;
            ScreenCapture.CaptureScreenshot("Temp/wind_a.png");
            yield return new WaitForSecondsRealtime(0.55f);
            ScreenCapture.CaptureScreenshot("Temp/wind_b.png");
            yield return new WaitForSecondsRealtime(0.5f);
            report = $"grove of {bestNear} at {p}; wind {StarForge.World.Wind.Speed(t0):0.00} heading {StarForge.World.Wind.Direction(t0)}, global {Shader.GetGlobalVector("_SF_Wind")}";
        }
    }
}
