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

        /// <summary>Is the grass fire's fuel where the grass is? Counts the tufts
        /// GroundScatter actually drew in every 3 m fire cell, and compares the cells
        /// that can burn now with the ones the old rule (meadow weight plus a quarter of
        /// the gravel) let burn. A burnable cell with no tuft in it is bare ground on fire.</summary>
        public static string GrassFuelCheck()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var world = StarForge.World.GameWorld.Instance;
            var veg = world != null ? world.Plants : null;
            var scatter = UnityEngine.Object.FindAnyObjectByType<StarForge.View.GroundScatter>();
            var map = MapInfo.Instance;
            if (veg == null || scatter == null || map == null || veg.GrassSide <= 0) return "no vegetation, grass or map yet (let the match tick)";
            int side = veg.GrassSide;
            float cell = StarForge.World.Vegetation.GrassCell;
            int canBurn = 0, bare = 0, one = 0, oldBurn = 0, oldBare = 0, grassNoFuel = 0, tuftsTotal = 0;
            var td = map.terrain.terrainData;
            for (int i = 0; i < side * side; i++)
            {
                int x = i % side, z = i / side;
                var min = new Vector2(x * cell, z * cell);
                int tufts = scatter.TuftsIn(min, min + new Vector2(cell, cell));
                bool burns = veg.GrassFuel(i) >= 25;
                if (burns) { canBurn++; tuftsTotal += tufts; if (tufts == 0) bare++; else if (tufts == 1) one++; }
                else if (tufts >= 4) grassNoFuel++;
                // The rule this replaced.
                var mid = min + new Vector2(cell * 0.5f, cell * 0.5f);
                if (!map.InBounds(mid, 1f)) continue;
                float h = map.HeightAt(mid);
                if (h < map.waterLevel + 0.4f) continue;
                var nrm = td.GetInterpolatedNormal(mid.x / map.mapSize, mid.y / map.mapSize);
                if (nrm.y < 0.86f) continue;
                var w = MapGenerator.SplatAt(mid.x, mid.y, h, nrm);
                float fuel = Mathf.Clamp01(w.x * 1.1f + w.y * 0.25f) * (1f - veg.Wetness(mid) * 0.95f);
                if (fuel * 255f >= 25f) { oldBurn++; if (tufts == 0) oldBare++; }
            }
            // Where the line between burnable and bare could go: cover recomputed by the
            // same rule, against the tufts actually drawn.
            var sweep = new StringBuilder(" SWEEP (expected tufts at full density >= t): ");
            var alpha = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
            int aw = td.alphamapWidth, ah = td.alphamapHeight;
            var covers = new float[side * side];
            var tuftsAt = new int[side * side];
            for (int i = 0; i < side * side; i++)
            {
                int x = i % side, z = i / side;
                var min = new Vector2(x * cell, z * cell);
                tuftsAt[i] = scatter.TuftsIn(min, min + new Vector2(cell, cell));
                var mid = min + new Vector2(cell * 0.5f, cell * 0.5f);
                if (!map.InBounds(mid, 1f) || veg.Wetness(mid) > 0.6f) continue;
                float sum = 0f;
                for (int b = 0; b < 9; b++)
                {
                    float qx = (x + ((b % 3) + 0.5f) / 3f) * cell, qz = (z + ((b / 3) + 0.5f) / 3f) * cell;
                    int ax = Mathf.Clamp((int)(qx / map.mapSize * aw), 0, aw - 1), az = Mathf.Clamp((int)(qz / map.mapSize * ah), 0, ah - 1);
                    var w4 = new Vector4(alpha[az, ax, 0], alpha[az, ax, 1], alpha[az, ax, 2], alpha[az, ax, 3]);
                    MapGenerator.GrassChance(w4, qx, qz, out float lush, out float dry);
                    if (map.HeightAt(new Vector2(qx, qz)) < map.waterLevel + 0.4f) continue;
                    if (td.GetInterpolatedNormal(qx / map.mapSize, qz / map.mapSize).y < MapGenerator.GrassMinUp) continue;
                    sum += Mathf.Min(1f, lush + dry);
                }
                covers[i] = sum * StarForge.World.Vegetation.TuftsPerSquare;
            }
            foreach (float t in new[] { 1.5f, 2f, 2.5f, 3f, 3.5f, 4f })
            {
                int nb = 0, n0 = 0, n1 = 0, lostGrass = 0;
                for (int i = 0; i < covers.Length; i++)
                {
                    if (covers[i] >= t) { nb++; if (tuftsAt[i] == 0) n0++; else if (tuftsAt[i] == 1) n1++; }
                    else if (tuftsAt[i] >= 4) lostGrass++;
                }
                sweep.Append($"[t {t}: {nb} burnable, {n0} bare ({100f * n0 / Mathf.Max(1, nb):0.0}%), {n1} one-tuft, {lostGrass} cells of 4+ tufts left out] ");
            }
            return $"fire cells {side}x{side} of {cell} m, fuel grid built in {veg.GrassBuildMs} ms. " +
                   $"NOW: {canBurn} cells can burn; {bare} of them have no tuft drawn, {one} have one; mean {(canBurn > 0 ? tuftsTotal / (float)canBurn : 0):0.0} tufts a burnable cell. " +
                   $"{grassNoFuel} cells with 4+ tufts cannot burn (wet shores, under buildings and ore). " +
                   $"OLD RULE: {oldBurn} cells could burn, {oldBare} of them bare ground with no tuft." + sweep;
        }

        /// <summary>Stage wildfires against buildings: a Bunkhouse set down in a meadow,
        /// the grass lit a few metres upwind, and wait for the fire to pass. Reports how
        /// many caught (of those the fire reached) and the health they lost, and
        /// photographs the first one that burns (Temp/building_fire_*.png).</summary>
        public static string BuildingFireTrial()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("BuildingFireTrial");
            go.AddComponent<BuildingFireRunner>();
            return "running 24 buildings at 8x speed; call BuildingFireTrialReport in a few minutes";
        }

        /// <summary>Photograph structures burning in the open: the player's Foundry and a
        /// Bunkhouse beside it, set alight directly, at 2, 6 and 12 s
        /// (Temp/building_burn_*.png).</summary>
        public static string BuildingFireLook()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("BuildingFireLook");
            go.AddComponent<BuildingFireLooker>();
            return "photographing; Temp/building_burn_*.png in about 20 s";
        }

        public static string BuildingFireTrialReport()
        {
            var run = UnityEngine.Object.FindAnyObjectByType<BuildingFireRunner>();
            return run == null ? "no trial running" : run.report;
        }

        /// <summary>Stage the Mauler: one of them in a grove, driving, then firing at a
        /// target across the trees. Photographs the drive, the shot, the shell landing
        /// and what the pressure did to the growth. Writes Temp/mauler_*.png.</summary>
        public static string MaulerShow()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("MaulerShow");
            go.AddComponent<MaulerShower>();
            return "staging; call MaulerShowReport in half a minute";
        }

        public static string MaulerShowReport()
        {
            var m = UnityEngine.Object.FindAnyObjectByType<MaulerShower>();
            return m == null ? "no show running" : m.report;
        }

        /// <summary>The blast pressure on its own, at a few strengths, with the front
        /// standing still in the middle of the view: what SF_Wind.hlsl actually does to
        /// the grass and the crowns, with FXDirector held off so nothing else moves.
        /// Writes Temp/pressure_*.png.</summary>
        public static string PressureLook()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("PressureLook");
            go.AddComponent<PressureLooker>();
            return "looking; call PressureLookReport in half a minute";
        }

        public static string PressureLookReport()
        {
            var l = UnityEngine.Object.FindAnyObjectByType<PressureLooker>();
            return l == null ? "no look running" : l.report;
        }

        /// <summary>How many particles each of FXDirector's systems is holding, and what
        /// the Maulers on the field are doing (debugging the effects).</summary>
        public static string FXCounts()
        {
            var fx = UnityEngine.Object.FindAnyObjectByType<StarForge.View.FXDirector>();
            if (fx == null) return "no FXDirector";
            var sb = new StringBuilder();
            sb.Append("enabled ").Append(fx.isActiveAndEnabled).Append("; ");
            foreach (Transform t in fx.transform)
            {
                var ps = t.GetComponent<ParticleSystem>();
                if (ps != null) sb.Append($"{t.name} {ps.particleCount}/{ps.main.maxParticles}  ");
            }
            var world = StarForge.World.GameWorld.Instance;
            if (world != null)
                foreach (var u in world.units)
                    if (u != null && !u.dying && u.Type == StarForge.Sim.UnitType.Mauler)
                        sb.Append($"\n  Mauler {u.id} at {u.Ground} complete {u.Complete} seen {u.visibleToPlayer} " +
                                  $"speed {(u.agent != null && u.agent.enabled ? u.agent.velocity.magnitude : -1f):0.00}");
            return sb.ToString();
        }

        /// <summary>The smoke, close up: a shell burst photographed as its smoke rises
        /// and spreads, a structure going up, and a burning tree's column. Writes
        /// Temp/smoke_*.png. Raises the effect events directly, so nothing is damaged
        /// and the pictures are the same every time on a seed.</summary>
        public static string SmokeLook()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("SmokeLook");
            go.AddComponent<SmokeLooker>();
            return "looking; call SmokeLookReport in half a minute";
        }

        public static string SmokeLookReport()
        {
            var l = UnityEngine.Object.FindAnyObjectByType<SmokeLooker>();
            return l == null ? "no look running" : l.report;
        }

        /// <summary>What the scene's sound bank actually holds: which sounds were found
        /// and which fell back to synthesis (nothing here can be listened to from the
        /// agent's side, so this is the check that make_audio.py's output is wired in).</summary>
        public static string AudioReport()
        {
            var a = UnityEngine.Object.FindAnyObjectByType<StarForge.View.AudioDirector>();
            if (a == null) return "no AudioDirector";
            var b = a.bank;
            var sb = new StringBuilder();
            void Row(string name, AudioClip[] c) =>
                sb.AppendLine($"  {name,-12} {(c == null ? 0 : c.Length)} take(s)" +
                              (c != null && c.Length > 0 ? $"  first {c[0].name} {c[0].length:0.00}s" : "  MISSING"));
            void One(string name, AudioClip c) =>
                sb.AppendLine($"  {name,-12} {(c == null ? "MISSING" : $"{c.name} {c.length:0.00}s {c.frequency} Hz")}");
            Row("rifle", b.rifle); Row("cannon", b.cannon);
            Row("blast", b.blast); Row("blastbig", b.blastBig);
            Row("boom", b.boom); Row("bigboom", b.bigBoom);
            One("engineHeavy", b.engineHeavy); One("engineTracks", b.engineTracks); One("engineHover", b.engineHover);
            return sb.ToString();
        }

        /// <summary>Shell a wood and count what goes over: a burst beside a tree is
        /// meant to be a chance, not a certainty, so the same shot twice is not the
        /// same picture. Reports how many trees each burst felled and how the chance
        /// falls off with distance.</summary>
        public static string FellTrial()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (world != null && !world.running && boot != null)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                boot.StartMatch();
            }
            var veg = world != null ? world.Plants : null;
            if (world == null) return "no GameWorld";
            if (veg == null) return "no vegetation";
            if (!world.running) return "the match did not start; call again in a moment";
            var rng = new System.Random(20240923);
            int bursts = 0, felled = 0, inReach = 0;
            var byBand = new int[4];
            var seenBand = new int[4];
            const float Radius = 3.15f;      // a Mauler's shell: splash 4.5 * 0.7
            float reach = Radius * 1.35f;
            for (int t = 0; t < 60; t++)
            {
                // Aim at a standing tree, offset a little, as a shell would land.
                int aim = -1;
                for (int k = 0; k < 300 && aim < 0; k++)
                {
                    int i = rng.Next(veg.plants.Length);
                    if (veg.live[i].state == StarForge.World.PlantState.Standing && !veg.KindOf(i).bush) aim = i;
                }
                if (aim < 0) break;
                Vector2 at = veg.Pos2(aim) + new Vector2((float)rng.NextDouble() * 6f - 3f, (float)rng.NextDouble() * 6f - 3f);
                var before = new System.Collections.Generic.List<(int i, float d)>();
                for (int i = 0; i < veg.plants.Length; i++)
                {
                    if (veg.live[i].state != StarForge.World.PlantState.Standing) continue;
                    float d = (veg.Pos2(i) - at).magnitude - veg.TrunkRadius(i);
                    if (d < reach) before.Add((i, d));
                }
                if (before.Count == 0) continue;
                bursts++;
                inReach += before.Count;
                veg.Blast(world, new Vector3(at.x, world.Map.HeightAt(at), at.y), Radius, 0f);
                foreach (var (i, d) in before)
                {
                    int band = Mathf.Clamp((int)(d / reach * 4f), 0, 3);
                    seenBand[band]++;
                    if (veg.live[i].state != StarForge.World.PlantState.Standing) { felled++; byBand[band]++; }
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"{bursts} bursts, {inReach} trees within {reach:0.0} m of one, {felled} felled " +
                          $"({(inReach > 0 ? 100f * felled / inReach : 0f):0}% overall)");
            for (int b = 0; b < 4; b++)
                sb.AppendLine($"  {b * reach / 4f:0.0}-{(b + 1) * reach / 4f:0.0} m: {byBand[b]}/{seenBand[b]} felled" +
                              (seenBand[b] > 0 ? $" ({100f * byBand[b] / seenBand[b]:0}%)" : ""));
            return sb.ToString();
        }

        /// <summary>Before entering play: fix the map and the match seed, so two runs of
        /// ShellTrial -- one with the artillery's move-up, one without -- play the same
        /// map and the same two commanders.</summary>
        public static string ShellTrialPrepare()
        {
            if (EditorApplication.isPlaying) return "call this before play";
            StarForge.Game.MatchSettings.mapSeed = StarForge.World.MapGenerator.DefaultSeed;
            StarForge.Game.MatchSettings.fixedSeed = true;
            StarForge.Game.MatchSettings.seed = 1000;
            return $"map seed {StarForge.Game.MatchSettings.mapSeed}, match seed 1000; now play and call ShellTrial or ShellTrialOff";
        }

        /// <summary>AI against AI at 4x for eight game minutes, counting the Maulers'
        /// shells that caught their target against the ones that burst on the ground,
        /// and how often the commanders moved artillery up. Call ShellTrialReport.</summary>
        public static string ShellTrial() => StartShellTrial(false);
        public static string ShellTrialOff() => StartShellTrial(true);

        static string StartShellTrial(bool off)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("ShellTrial");
            go.AddComponent<ShellTrialRunner>().off = off;
            return $"running with the move-up {(off ? "OFF" : "on")}; call ShellTrialReport in about two minutes";
        }

        public static string ShellTrialReport()
        {
            var r = UnityEngine.Object.FindAnyObjectByType<ShellTrialRunner>();
            return r == null ? "no trial running" : r.report;
        }

        /// <summary>A shot the terrain blocks: an AI Mauler put behind a rise from one of
        /// the player's structures, so its shells land on the rise. Reports what it
        /// fired and hit from there, whether its commander moved it, how far, and what
        /// it hit afterwards. Run once normally and once after ShellTrialOffSwitch.</summary>
        public static string BlockedShot()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("BlockedShot");
            go.AddComponent<BlockedShotRunner>();
            return $"staging (move-up {(StarForge.AI.Commander.RepositionOff ? "OFF" : "on")}); call BlockedShotReport in about a minute";
        }

        public static string BlockedShotReport()
        {
            var r = UnityEngine.Object.FindAnyObjectByType<BlockedShotRunner>();
            return r == null ? "no trial running" : r.report;
        }

        /// <summary>Toggle the artillery move-up for the A/B.</summary>
        public static string ShellTrialOffSwitch()
        {
            StarForge.AI.Commander.RepositionOff = !StarForge.AI.Commander.RepositionOff;
            return $"move-up is now {(StarForge.AI.Commander.RepositionOff ? "OFF" : "on")}";
        }

        /// <summary>A shell burst on a lakeshore, photographed as its pressure front goes
        /// out: the water ringing, dust lifting off the dry ground, the grass laid over
        /// and leaves torn from the crowns. The effect event only, so nothing is damaged.
        /// Writes Temp/pressure_shore_*.png.</summary>
        public static string PressureShow()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("PressureShow");
            go.AddComponent<PressureShower>();
            return "staging; call PressureShowReport in about twenty seconds";
        }

        public static string PressureShowReport()
        {
            var r = UnityEngine.Object.FindAnyObjectByType<PressureShower>();
            return r == null ? "no show running" : r.report;
        }

        /// <summary>The Maulers' engine voices: each looping source on the AudioDirector
        /// playing the engine or the track clatter, with its level and pitch now.</summary>
        public static string TankVoices()
        {
            var a = UnityEngine.Object.FindAnyObjectByType<StarForge.View.AudioDirector>();
            if (a == null) return "no AudioDirector";
            var sb = new StringBuilder();
            foreach (var src in a.GetComponents<AudioSource>())
                if (src.clip != null && (src.clip.name == "engine_heavy" || src.clip.name == "engine_tracks"))
                    sb.AppendLine($"  {src.clip.name,-14} playing {src.isPlaying} volume {src.volume:0.000} pitch {src.pitch:0.00} pan {src.panStereo:0.00}");
            return sb.Length == 0 ? "no tank voices" : sb.ToString();
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

    /// <summary>Photographs the smoke on its own: a shell burst, a structure going up
    /// and a tree burning, each caught as the smoke rises so the shape of it can be
    /// judged rather than guessed at.</summary>
    public sealed class SmokeLooker : MonoBehaviour
    {
        public string report = "looking";

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (!world.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            var veg = world.Plants;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var map = MapInfo.Instance;
            if (veg == null || rig == null || map == null) { report = "no vegetation/camera/map"; yield break; }

            // Open, flat, dry ground with a tree in reach, near the player's start so
            // the spot is lit and in vision.
            Vector2 at = map.StartPos(0) + new Vector2(18f, 12f);
            world.Spawn(StarForge.Sim.UnitType.Mauler, 0, at + new Vector2(-8f, -8f));
            rig.scripted = true;
            Time.timeScale = 1f;
            rig.JumpTo(at);
            rig.ZoomTo(26f);
            yield return new WaitForSecondsRealtime(5f);

            var sb = new StringBuilder();
            Vector3 p = map.Ground(at);

            // A shell burst: the effect only, so nothing round it is damaged.
            Time.timeScale = 0.35f;
            world.Raise(new StarForge.World.GameEvent
            {
                kind = StarForge.World.GameEventKind.Impact, team = 0, pos = p + Vector3.up * 0.4f,
                dir = Vector3.forward, scale = 1.35f, projectileKind = 1
            });
            foreach (var (name, wait) in new[] { ("shell_a", 0.15f), ("shell_b", 0.6f), ("shell_c", 1.2f), ("shell_d", 2.5f) })
            {
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot($"Temp/smoke_{name}.png");
            }
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(4f);

            // A structure going up, which is the biggest smoke the game makes.
            world.Raise(new StarForge.World.GameEvent
            {
                kind = StarForge.World.GameEventKind.Death, type = StarForge.Sim.UnitType.Bunkhouse, team = 1,
                pos = p + Vector3.up * 1.2f, scale = 2.6f
            });
            Time.timeScale = 0.35f;
            foreach (var (name, wait) in new[] { ("base_a", 0.4f), ("base_b", 1.5f), ("base_c", 3f) })
            {
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot($"Temp/smoke_{name}.png");
            }
            Time.timeScale = 1f;

            // A tree burning: the long, slow column.
            int tree = -1;
            float bestD = 1e9f;
            for (int i = 0; i < veg.plants.Length; i++)
            {
                if (veg.KindOf(i).bush || veg.live[i].state != StarForge.World.PlantState.Standing) continue;
                float d = (veg.Pos2(i) - at).sqrMagnitude;
                if (d < bestD) { bestD = d; tree = i; }
            }
            if (tree >= 0)
            {
                rig.JumpTo(veg.Pos2(tree));
                veg.Ignite(world, tree, 0, 1f);
                sb.Append($"burning the tree {Mathf.Sqrt(bestD):0} m off; ");
                yield return new WaitForSecondsRealtime(7f);
                ScreenCapture.CaptureScreenshot("Temp/smoke_tree_a.png");
                yield return new WaitForSecondsRealtime(6f);
                ScreenCapture.CaptureScreenshot("Temp/smoke_tree_b.png");
            }
            report = sb + $"shots round {at}";
        }
    }

    /// <summary>Holds a pressure front still in the middle of the view at a few
    /// strengths, so what the shaders do to the growth can be judged without a blast
    /// going off over it. FXDirector is switched off for the duration, or it would
    /// upload the live fronts over these on the next frame.</summary>
    public sealed class PressureLooker : MonoBehaviour
    {
        public string report = "looking";

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (!world.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1f);
            }
            var veg = world.Plants;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var fx = UnityEngine.Object.FindAnyObjectByType<StarForge.View.FXDirector>();
            if (veg == null || rig == null || fx == null) { report = "no vegetation/camera/FX"; yield break; }

            // Open meadow with trees standing round it, so both are in the shot.
            int best = -1, bestNear = 0;
            for (int i = 0; i < veg.plants.Length; i += 3)
            {
                if (veg.KindOf(i).bush || veg.live[i].state != StarForge.World.PlantState.Standing) continue;
                var c = veg.Pos2(i);
                if (MapInfo.Instance != null && MapInfo.Instance.WaterDepth(c) > -6f) continue;
                int near = 0;
                for (int j = 0; j < veg.plants.Length; j++)
                    if (veg.KindOf(j).HasCrown && (veg.Pos2(j) - c).sqrMagnitude < 16f * 16f) near++;
                if (near > bestNear) { bestNear = near; best = i; }
            }
            if (best < 0) { report = "no trees"; yield break; }
            Vector2 at = veg.Pos2(best);

            // Nothing is drawn under the fog of war, so put one of the player's own
            // units there to light the spot.
            world.Spawn(StarForge.Sim.UnitType.Mauler, 0, at + new Vector2(0f, -7f));

            rig.scripted = true;
            Time.timeScale = 1f;
            rig.JumpTo(at);
            rig.ZoomTo(18f);
            // Long enough for the camera to stop easing: it has to be still, or the
            // drift between two shots swamps what the pressure did.
            yield return new WaitForSecondsRealtime(6f);

            fx.enabled = false;
            var gusts = new Vector4[4];
            var shape = new Vector4[4];
            // Each strength against a still frame taken the very next frame, so the
            // only thing between the two is the pressure -- the wind moves a crown
            // further in a second than a shell does.
            Shader.SetGlobalFloat("_SF_GustCount", 0f);
            yield return null;
            ScreenCapture.CaptureScreenshot("Temp/pressure_off.png");
            yield return new WaitForSecondsRealtime(0.5f);
            foreach (float strength in new[] { 0.25f, 0.5f, 1.0f, 2.0f })
            {
                // The front held 7 m out from the middle of the view: the growth just
                // inside it is under the pressure, what is outside is untouched.
                gusts[0] = new Vector4(at.x, at.y, 7f, strength);
                shape[0] = new Vector4(0f, 1f, 0f, 5f);
                Shader.SetGlobalVectorArray("_SF_Gusts", gusts);
                Shader.SetGlobalVectorArray("_SF_GustShape", shape);
                Shader.SetGlobalFloat("_SF_GustCount", 1f);
                yield return null;
                ScreenCapture.CaptureScreenshot($"Temp/pressure_{strength:0.00}.png");
                yield return null;
                Shader.SetGlobalFloat("_SF_GustCount", 0f);
                yield return null;
                ScreenCapture.CaptureScreenshot($"Temp/pressure_{strength:0.00}_off.png");
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Shader.SetGlobalFloat("_SF_GustCount", 0f);
            fx.enabled = true;
            report = $"front held at {at} 7 m out, {bestNear} crowns round it; " +
                     "each of 0.25, 0.5, 1.0 and 2.0 m of shove against the next frame with it off";
        }
    }

    /// <summary>Puts a Mauler in a grove and makes it drive and shoot, so the engine
    /// smoke, the muzzle blast, the shell landing and the pressure on the growth can
    /// all be looked at in one pass.</summary>
    public sealed class MaulerShower : MonoBehaviour
    {
        public string report = "staging";

        /// <summary>One of FXDirector's particle systems by the name it gives the child.</summary>
        static ParticleSystem FxSystem(StarForge.View.FXDirector fx, string name)
        {
            foreach (Transform t in fx.transform) if (t.name == name) return t.GetComponent<ParticleSystem>();
            return null;
        }

        System.Collections.IEnumerator Start()
        {
            // Nothing ticks on the title screen, so start a match first -- but never
            // restart one that is already running, or the units left from it spend the
            // rest of the session throwing from LateUpdate.
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (boot == null) { report = "no GameBootstrap"; yield break; }
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }

            var world = StarForge.World.GameWorld.Instance;
            var veg = world != null ? world.Plants : null;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var fx = UnityEngine.Object.FindAnyObjectByType<StarForge.View.FXDirector>();
            if (world == null || veg == null || rig == null || fx == null || !world.running) { report = "no running world/vegetation/camera/FX"; yield break; }

            // A stand with plenty of crowns round it, on dry and -- this matters, or
            // the tank spawns on a cliff edge and drives out of shot -- flat ground.
            var map = MapInfo.Instance;
            int best = -1, bestNear = 0;
            float bestScore = -1f;
            for (int i = 0; i < veg.plants.Length; i += 3)
            {
                if (veg.KindOf(i).bush || veg.live[i].state != StarForge.World.PlantState.Standing) continue;
                var c = veg.Pos2(i);
                if (map != null && map.WaterDepth(c) > -6f) continue;
                float relief = 0f;
                if (map != null)
                {
                    float lo = 1e9f, hi = -1e9f;
                    for (int a = 0; a < 8; a++)
                    {
                        float h = map.HeightAt(c + new Vector2(Mathf.Cos(a * 0.785f), Mathf.Sin(a * 0.785f)) * 16f);
                        lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                    }
                    relief = hi - lo;
                }
                int near = 0;
                for (int j = 0; j < veg.plants.Length; j++)
                    if (veg.KindOf(j).HasCrown && (veg.Pos2(j) - c).sqrMagnitude < 18f * 18f) near++;
                // Crowns are what the shot wants in it, but flat ground matters more:
                // on a slope the tank drives out of the frame and over a ridge.
                float score = near / (1f + relief * relief * 0.08f);
                if (score > bestScore) { bestScore = score; bestNear = near; best = i; }
            }
            if (best < 0) { report = "no dry stand of trees"; yield break; }
            Vector2 grove = veg.Pos2(best);

            // The tank on one side of the stand, the target it will shell on the
            // other, inside the gun's 24 m so the shell flies over the crowns between
            // them. The target goes in later: a Mauler acquires on its own, and a
            // Trooper standing there would be dead before the camera was ready.
            Vector2 from = grove + new Vector2(-14f, -4f);
            Vector2 mark = grove + new Vector2(4f, 2f);
            var tank = world.Spawn(StarForge.Sim.UnitType.Mauler, 0, from, true, Mathf.Atan2(mark.x - from.x, mark.y - from.y));
            if (tank == null) { report = "could not spawn the tank"; yield break; }
            var order = new System.Collections.Generic.List<StarForge.World.Unit> { tank };

            // The HUD covers the bottom of the screen, so the camera holds the tank
            // itself in the middle rather than the ground between the two.
            rig.scripted = true;
            Time.timeScale = 1f;
            rig.JumpTo(tank.pos);
            rig.ZoomTo(17f);
            yield return new WaitForSecondsRealtime(5f);
            ScreenCapture.CaptureScreenshot("Temp/mauler_idle.png");
            yield return new WaitForSecondsRealtime(0.6f);

            // Driving: the stacks under load and the dust off the tracks. Across the
            // view rather than away from the camera, so both sides of it show, with
            // the camera held on it every frame so it cannot drive out of shot.
            var smokeSys = FxSystem(fx, "Smoke");
            var trailSys = FxSystem(fx, "Trail");
            int idleSmoke = smokeSys != null ? smokeSys.particleCount : -1;
            tank.MoveTo(from + new Vector2(2f, 14f));
            for (float w = 0f; w < 2.0f; w += Time.unscaledDeltaTime) { rig.JumpTo(tank.pos); yield return null; }
            int driveSmoke = smokeSys != null ? smokeSys.particleCount : -1;
            int driveTrail = trailSys != null ? trailSys.particleCount : -1;
            // Which of the trail particles are exhaust: above the deck and within a few
            // metres of the hull, with their size and colour, so a plume that is there
            // but cannot be seen can be told from one that is not there at all.
            string exhaust = "";
            if (trailSys != null)
            {
                var buf = new ParticleSystem.Particle[trailSys.particleCount];
                int n = trailSys.GetParticles(buf);
                Vector3 deck = tank.Ground;
                int above = 0; float sz = 0f, al = 0f, gr = 0f, hi = 0f;
                for (int i = 0; i < n; i++)
                {
                    var q = buf[i];
                    Vector3 d = q.position - deck;
                    if (d.y < 1.9f || new Vector2(d.x, d.z).magnitude > 6f) continue;
                    above++;
                    sz += q.GetCurrentSize(trailSys);
                    var c = q.GetCurrentColor(trailSys);
                    al += c.a / 255f; gr += c.r / 255f; hi += d.y;
                }
                exhaust = above == 0 ? "no particles above the deck"
                    : $"{above} above the deck, mean size {sz / above:0.00} m, alpha {al / above:0.00}, grey {gr / above:0.00}, height {hi / above:0.0} m";
            }
            float driveSpeed = tank.agent != null ? tank.agent.velocity.magnitude : -1f;
            ScreenCapture.CaptureScreenshot("Temp/mauler_drive.png");
            for (float w = 0f; w < 1.8f; w += Time.unscaledDeltaTime) { rig.JumpTo(tank.pos); yield return null; }
            ScreenCapture.CaptureScreenshot("Temp/mauler_trail.png");
            yield return new WaitForSecondsRealtime(0.6f);
            from = tank.pos;

            // The shot. The target is a structure, which stands still and survives
            // the first shell. The tank has to turn onto it and the gun has its own
            // cooldown, so wait for the shot itself, catch the blast, then the field
            // a moment later as the pressure front crosses it.
            rig.JumpTo(Vector2.Lerp(tank.pos, mark, 0.35f));
            rig.ZoomTo(26f);
            yield return new WaitForSecondsRealtime(2.5f);
            var quarry = world.Spawn(StarForge.Sim.UnitType.Bunkhouse, 1, mark);
            if (quarry == null) { report = "could not spawn the target"; yield break; }
            world.CmdAttack(order, quarry);
            float fired = -1f;
            for (int i = 0; i < 900 && fired < 0f; i++)
            {
                float before = tank.cooldown;
                yield return null;
                if (tank.cooldown > before + 0.05f) fired = Time.time;
            }
            if (fired < 0f) { report = $"the gun never fired (target {tank.Dist(quarry):0.0} m off, order {tank.order})"; yield break; }

            // In slow motion from here, so the blast, the front crossing the grass and
            // the shell landing are each caught on a frame of their own.
            Time.timeScale = 0.25f;
            var seen = new StringBuilder();
            var hull = tank.view != null ? tank.view.body : null;
            float rock = 0f;
            foreach (var (name, wait) in new[]
                     { ("fire", 0f), ("blast", 0.3f), ("wave", 0.5f), ("wave2", 0.7f),
                       ("impact", 1.4f), ("settle", 1.6f), ("after", 3.5f) })
            {
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot($"Temp/mauler_{name}.png");
                if (hull != null)
                {
                    float pitch = Mathf.DeltaAngle(0f, hull.localEulerAngles.x);
                    if (Mathf.Abs(pitch) > Mathf.Abs(rock)) rock = pitch;
                }
                var g = Shader.GetGlobalVectorArray("_SF_Gusts");
                seen.Append($"\n  {name}: {Shader.GetGlobalFloat("_SF_GustCount")} gust(s)");
                if (g != null && g.Length > 0) seen.Append($", front {g[0].z:0.0} m at strength {g[0].w:0.000}");
            }
            Time.timeScale = 1f;

            report = $"grove of {bestNear} at {grove}, tank at {from}, target at {mark}, " +
                     $"wind {StarForge.World.Wind.Speed(Time.time):0.00}\n" +
                     $"  engine: {idleSmoke} smoke particles idling, {driveSmoke} driving at {driveSpeed:0.0} m/s, " +
                     $"{driveTrail} in the trail system; exhaust: {exhaust}\n" +
                     $"  hull rocked {rock:0.00} deg at its furthest{seen}";
        }
    }

    /// <summary>Sets off a shell burst on a shore and photographs what its pressure moves.</summary>
    public sealed class PressureShower : MonoBehaviour
    {
        public string report = "staging";

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (!world.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            var map = MapInfo.Instance;
            var veg = world.Plants;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            if (map == null || veg == null || rig == null) { report = "no map/vegetation/camera"; yield break; }

            // A dry spot 5-8 m from open water, with crowns within fifteen metres.
            Vector2 at = Vector2.zero;
            int best = -1;
            for (int k = 0; k < 20000 && best < 0; k++)
            {
                var p = new Vector2(UnityEngine.Random.Range(10f, world.MapSize - 10f), UnityEngine.Random.Range(10f, world.MapSize - 10f));
                if (map.WaterDepth(p) > -0.4f) continue;
                // A beach, not a cliff over the water: gentle ground round the spot.
                float lo = 1e9f, hi = -1e9f;
                for (int a = 0; a < 8; a++)
                {
                    float h = map.HeightAt(p + new Vector2(Mathf.Cos(a * 0.785f), Mathf.Sin(a * 0.785f)) * 5f);
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
                if (hi - lo > 2.6f) continue;
                bool shore = false;
                for (int a = 0; a < 8 && !shore; a++)
                {
                    var q = p + new Vector2(Mathf.Cos(a * 0.785f), Mathf.Sin(a * 0.785f)) * 9f;
                    if (map.InBounds(q) && map.WaterDepth(q) > 0.4f) shore = true;
                }
                if (!shore) continue;
                int crowns = 0;
                for (int i = 0; i < veg.plants.Length; i++)
                    if (veg.KindOf(i).HasCrown && (veg.Pos2(i) - p).sqrMagnitude < 20f * 20f) crowns++;
                if (crowns >= 1) { at = p; best = crowns; }
            }
            if (best < 0) { report = "no shore with trees on this map"; yield break; }

            // Light the spot for the player, and look at it.
            world.Spawn(StarForge.Sim.UnitType.Trooper, 0, at + new Vector2(-9f, -9f));
            rig.scripted = true;
            Time.timeScale = 1f;
            rig.JumpTo(at);
            rig.ZoomTo(26f);
            yield return new WaitForSecondsRealtime(5f);
            ScreenCapture.CaptureScreenshot("Temp/pressure_shore_before.png");
            yield return new WaitForSecondsRealtime(0.5f);

            Time.timeScale = 0.25f;
            world.Raise(new StarForge.World.GameEvent
            {
                kind = StarForge.World.GameEventKind.Impact, team = 1, pos = map.Ground(at) + Vector3.up * 0.4f,
                dir = Vector3.forward, scale = 1.35f, projectileKind = 1
            });
            foreach (var (name, wait) in new[] { ("a", 0.25f), ("b", 0.35f), ("c", 0.5f), ("d", 1.2f) })
            {
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot($"Temp/pressure_shore_{name}.png");
            }
            Time.timeScale = 1f;
            report = $"burst at {at}, {best} crowns within 20 m, water within 9 m";
        }
    }

    /// <summary>Stages an AI Mauler with a rise between it and its target.</summary>
    public sealed class BlockedShotRunner : MonoBehaviour
    {
        public string report = "staging";

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (!world.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            var map = MapInfo.Instance;
            var ai = boot.AI;
            if (map == null || ai == null) { report = "no map or AI"; yield break; }

            // Find a blocked shot: a target on open ground and, 16-22 m from it, a
            // spot whose shell arc hits the ground on the way -- with a spot nearby
            // from which it clears, so moving can fix it. Away from both bases.
            var rng = new System.Random(77);
            StarForge.World.Unit target = null;
            Vector2 from = Vector2.zero;
            for (int tries = 0; tries < 40 && target == null; tries++)
            {
                var b = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                if (!map.InBounds(b, 20f) || map.WaterDepth(b) > -0.3f) continue;
                if ((b - map.StartPos(0)).magnitude < 45f || (b - map.StartPos(1)).magnitude < 45f) continue;
                var bunk = world.Spawn(StarForge.Sim.UnitType.Trooper, 0, b);
                if (bunk == null) continue;
                for (int k = 0; k < 48; k++)
                {
                    float a = k / 48f * Mathf.PI * 2f, r = 16f + (k % 4) * 2f;
                    var a2 = b + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!map.InBounds(a2, 6f) || map.WaterDepth(a2) > 0.3f) continue;
                    // A real ridge: the burst at least 8 m short, well outside the splash.
                    // A proper ridge: under the ground for five metres, a burst well
                    // short of the splash. (A thinner crest the real shell can jump
                    // between one frame and the next when the editor runs slowly.)
                    float shortBy = world.ShellFallsShort(a2, bunk, 5f);
                    if (shortBy < 8f || shortBy > 1000f) continue;      // > 1000: off the map, not a ridge
                    bool fixable = false;
                    for (int j = -3; j <= 3 && !fixable; j++)
                    {
                        var c2 = b + new Vector2(Mathf.Cos(a + j * 0.4f), Mathf.Sin(a + j * 0.4f)) * 16f;
                        if (map.InBounds(c2, 4f) && world.ShellClears(c2, bunk, 4.5f)) fixable = true;
                    }
                    if (!fixable) continue;
                    target = bunk; from = a2;
                    break;
                }
            }
            if (target == null) { report = "no blocked shot found on this map"; yield break; }

            var tank = world.Spawn(StarForge.Sim.UnitType.Mauler, 1, from, true,
                                   Mathf.Atan2(target.pos.x - from.x, target.pos.y - from.y));
            if (tank == null) { report = "could not spawn the tank"; yield break; }
            float predicted = world.ShellFallsShort(tank.pos, target, 5f);
            // Where this tank's shells actually burst, from the effects events.
            var bursts = new System.Collections.Generic.List<float>();
            var tgt = target;
            System.Action<StarForge.World.GameEvent> onEvent = e =>
            {
                if (e.kind == StarForge.World.GameEventKind.Impact && e.projectileKind == 1 && e.team == 1)
                    bursts.Add(Vector2.Distance(new Vector2(e.pos.x, e.pos.z), tgt.pos));
            };
            world.Event += onEvent;
            var f = world.factions[1];
            int h0 = f.shellHits, m0 = f.shellMisses, rep0 = ai.Repositions;
            int hitsBefore = 0, missBefore = 0, hitsAfter = 0, missAfter = 0;
            bool moved = false;
            float movedAt = -1f;
            Vector2 start = tank.pos;
            float t0 = world.time;
            while (world.time - t0 < 45f && StarForge.World.Unit.Live(tank) && StarForge.World.Unit.Live(target))
            {
                int dh = f.shellHits - h0, dm = f.shellMisses - m0;
                if (!moved && ai.Repositions > rep0) { moved = true; movedAt = world.time - t0; hitsBefore = dh; missBefore = dm; }
                if (moved) { hitsAfter = dh - hitsBefore; missAfter = dm - missBefore; }
                else { hitsBefore = dh; missBefore = dm; }
                yield return null;
            }
            world.Event -= onEvent;
            var burstText = new StringBuilder();
            foreach (var b in bursts) burstText.Append($"{b:0.0} ");
            report = $"target {target.def.displayName} at {target.pos}, tank started {Vector2.Distance(start, target.pos):0.0} m off behind a rise " +
                     $"(predicted burst {predicted:0.0} m short)\n  bursts, metres from the target: {burstText}\n" +
                     $"  from there: {hitsBefore} hit, {missBefore} missed\n" +
                     (moved ? $"  moved at {movedAt:0.0} s, {Vector2.Distance(start, tank.pos):0.0} m, now {tank.Dist(target):0.0} m off, " +
                              $"\n  after moving: {hitsAfter} hit, {missAfter} missed"
                            : $"  never moved (move-up {(StarForge.AI.Commander.RepositionOff ? "OFF" : "on")}); tank {Vector2.Distance(start, tank.pos):0.0} m from its start") +
                     $"\n  target hp {target.hp:0}/{target.MaxHp:0}";
        }
    }

    /// <summary>Plays an AI-against-AI match fast and counts the artillery's hits and misses.</summary>
    public sealed class ShellTrialRunner : MonoBehaviour
    {
        public bool off;
        public string report = "running";

        System.Collections.IEnumerator Start()
        {
            StarForge.AI.Commander.RepositionOff = off;
            StarForge.Game.MatchSettings.spectate = true;
            StarForge.Game.MatchSettings.aiMemory = false;
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (!world.running) boot.StartMatch();
            Time.timeScale = 4f;
            const float GameSeconds = 480f;
            while (world.running && world.time < GameSeconds)
            {
                var a = world.factions;
                report = $"t {world.time:0} s: " + Line(a);
                yield return new WaitForSecondsRealtime(2f);
            }
            Time.timeScale = 1f;
            var ai = boot.AI;
            var proxy = boot.PlayerProxy;
            report = $"move-up {(off ? "OFF" : "on")}, map {StarForge.World.MapRuntime.LastSeed}, {world.time:0} game s" +
                     (world.running ? "" : $" (match over, winner {world.winner})") + "\n  " + Line(world.factions) +
                     $"\n  repositions: team 1 {(ai != null ? ai.Repositions : -1)}, team 0 {(proxy != null ? proxy.Repositions : -1)}";
            StarForge.AI.Commander.RepositionOff = false;
        }

        static string Line(StarForge.World.Faction[] f)
        {
            string One(StarForge.World.Faction x)
            {
                int n = x.shellHits + x.shellMisses;
                return $"team {x.team}: {x.shellHits}/{n} shells hit ({(n > 0 ? 100f * x.shellHits / n : 0f):0}%)";
            }
            return One(f[0]) + ", " + One(f[1]);
        }
    }

    /// <summary>Lights one plant at a time and counts what each fire takes with it.</summary>
    public sealed class BuildingFireLooker : MonoBehaviour
    {
        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (boot == null || world == null) yield break;
            if (!world.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            var map = MapInfo.Instance;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            StarForge.World.Unit foundry = null;
            foreach (var u in world.units)
                if (u != null && !u.dying && u.team == 0 && u.Type == StarForge.Sim.UnitType.Foundry) { foundry = u; break; }
            if (foundry == null || rig == null) yield break;
            var home = foundry.pos;
            var side = world.NearestWalkable(home + new Vector2(11f, -7f), 6f);
            var hut = world.Spawn(StarForge.Sim.UnitType.Bunkhouse, 0, side, true);
            Time.timeScale = 1f;
            rig.scripted = true;
            rig.JumpTo((home + side) * 0.5f);
            rig.ZoomTo(34f);
            yield return new WaitForSecondsRealtime(3f);
            world.Ignite(foundry);
            world.Ignite(hut);
            foreach (var (name, wait) in new[] { ("2s", 2f), ("6s", 4f), ("12s", 6f) })
            {
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot($"Temp/building_burn_{name}.png");
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log($"[BuildingFireLook] Foundry {foundry.hp / foundry.MaxHp * 100f:0.0}% health, Bunkhouse {hut.hp / hut.MaxHp * 100f:0.0}% after 12 s of burning");
            rig.scripted = false;
        }
    }

    public sealed class BuildingFireRunner : MonoBehaviour
    {
        public string report = "running";

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = StarForge.World.GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (!world.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            var veg = world.Plants;
            var map = MapInfo.Instance;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            while (veg != null && veg.GrassSide <= 0) yield return null;
            if (veg == null || map == null || rig == null) { report = "no vegetation/map/camera"; yield break; }
            int side = veg.GrassSide;
            float cellSize = StarForge.World.Vegetation.GrassCell;
            var rng = new System.Random(77);
            var used = new System.Collections.Generic.List<Vector2>();
            var lost = new System.Collections.Generic.List<float>();
            var exposures = new System.Collections.Generic.List<float>();
            var heatTotals = new System.Collections.Generic.List<float>();
            int trials = 0, reached = 0, caught = 0, attacked = 0;
            bool photographed = false;
            float keep = Time.timeScale;
            Time.timeScale = 8f;
            for (int attempt = 0; attempt < 20000 && trials < 24; attempt++)
            {
                int cell = rng.Next(side * side);
                if (veg.GrassFuel(cell) < 150 || veg.GrassState[cell] != 0) continue;
                var gc = veg.GrassCentre(cell);
                var c = new Vector2(gc.x, gc.z);
                if (!map.InBounds(c, 20f)) continue;
                if ((c - map.StartPos(0)).magnitude < 45f || (c - map.StartPos(1)).magnitude < 45f) continue;
                bool clash = false;
                foreach (var u in used) if ((u - c).magnitude < 26f) { clash = true; break; }
                if (clash) continue;
                // A meadow round it, so there is something to carry a fire up to the walls.
                int fuelled = 0;
                for (int dz = -3; dz <= 3; dz++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = cell % side + dx, z = cell / side + dz;
                        if (x < 0 || z < 0 || x >= side || z >= side) continue;
                        int k = z * side + x;
                        if (veg.GrassFuel(k) >= 60 && veg.GrassState[k] == 0) fuelled++;
                    }
                if (fuelled < 22) continue;
                used.Add(c);
                var b = world.Spawn(StarForge.Sim.UnitType.Bunkhouse, 0, c, true);
                float hp0 = b.hp, t0 = world.time, exposed = 0f, planned = 0f, heatSeconds = 0f;
                bool reach = false, lit = false;
                var wd = StarForge.World.Wind.Direction(world.time);
                veg.BurnGrass(world, c - wd * (b.def.radius + 4f), 2.5f, 1f);
                while (world.time - t0 < 90f)
                {
                    float before = world.time;
                    yield return new WaitForSeconds(0.25f);
                    float heat = veg.FireNear(c, b.def.radius + 1.5f, world.time);
                    if (heat >= 0.1f) { reach = true; exposed += world.time - before; heatSeconds += heat * (world.time - before); }
                    if (b.OnFire && !lit)
                    {
                        lit = true;
                        planned = b.burnDamageLeft / b.MaxHp * 100f;
                        if (!photographed)
                        {
                            photographed = true;
                            Time.timeScale = 1f;
                            rig.scripted = true;
                            rig.JumpTo(c);
                            rig.ZoomTo(30f);
                            yield return new WaitForSecondsRealtime(3f);
                            ScreenCapture.CaptureScreenshot("Temp/building_fire_a.png");
                            yield return new WaitForSecondsRealtime(4f);
                            ScreenCapture.CaptureScreenshot("Temp/building_fire_b.png");
                            yield return new WaitForSecondsRealtime(0.5f);
                            rig.scripted = false;
                            Time.timeScale = 8f;
                        }
                    }
                    if (world.time - t0 > 4f && heat < 0.02f && !b.OnFire) break;
                }
                trials++;
                if (reach) { reached++; exposures.Add(exposed); heatTotals.Add(heatSeconds); }
                if (b.lastDamagedT > t0) attacked++;
                if (lit) { caught++; lost.Add((hp0 - b.hp) / b.MaxHp * 100f); }
                report = $"{trials}/24 buildings: {caught} caught of {reached} the fire reached";
            }
            Time.timeScale = keep;
            lost.Sort();
            exposures.Sort();
            string L(System.Collections.Generic.List<float> v) => string.Join(", ", v.Select(x => x.ToString("0.0")));
            // The catch is a roll each half second against CatchRate x heat, so what a
            // fire at the walls does is set by its heat-seconds: the expected share
            // caught, from the fires actually measured, is far steadier than a count.
            float expect = 0f;
            foreach (float hs in heatTotals) expect += 1f - Mathf.Exp(-StarForge.World.GameWorld.CatchRate * hs);
            expect /= Mathf.Max(1, heatTotals.Count);
            heatTotals.Sort();
            report = $"Expected share caught at CatchRate {StarForge.World.GameWorld.CatchRate}: {100f * expect:0}% (heat-seconds at the walls: {L(heatTotals)}). " +
                     $"{trials} buildings with the grass lit upwind: the fire reached {reached}, and {caught} of those caught " +
                     $"({(reached > 0 ? 100f * caught / reached : 0f):0}%). Health lost by those that caught (%): {L(lost)}. " +
                     $"Seconds of fire at the walls (game time): {L(exposures)}. {attacked} were also shot at (their loss includes that). " +
                     $"Photos: {(photographed ? "Temp/building_fire_a.png, _b.png" : "none")}.";
        }
    }

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
