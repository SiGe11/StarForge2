// FaunaTrials.cs — play-mode checks for the animals (View/Fauna.cs), read from what is
// drawn: the birds' transforms and their Animation states, not Fauna's own fields.
//
//   Tools/editor.sh play
//   Tools/editor.sh call StarForge.EditorTools.FaunaTrials.Birds
//   Tools/editor.sh wait StarForge.EditorTools.FaunaTrials.BirdsReport
//
// At the menu (no match: nothing is fogged and nothing walks up to frighten them), the
// title screen hidden for the pictures. Writes Temp/bird_*.png.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class FaunaTrials
    {
        /// <summary>The last run's report (kept here: the watcher removes itself when done).</summary>
        public static string last = "not run";

        /// <summary>Eagles: which way they lean in their turn and how much of the time
        /// their wings are held; then one frightened. Songbirds: the largest turn in a
        /// frame while feeding, and how the wings sit on a standing bird.</summary>
        public static string Birds()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<BirdWatcher>();
            if (old != null) Object.Destroy(old.gameObject);
            last = "watching";
            new GameObject("BirdWatch").AddComponent<BirdWatcher>();
            return "watching; call BirdsReport in about a minute";
        }

        public static string BirdsReport()
        {
            return last;
        }
    }

    public sealed class BirdWatcher : MonoBehaviour
    {
        string report { get => FaunaTrials.last; set => FaunaTrials.last = value; }

        sealed class Bird
        {
            public Transform t;
            public Animation anim;
            public AnimationState fly;
            public float yaw, into, intoMin = 1e9f, intoMax = -1e9f, pitchMax, stepMax;
            public Vector3 pos;
            public int frames, outward, gliding, bursts;
            public bool wasGliding;
        }

        float savedScale = 1f;
        readonly List<UIDocument> hidden = new List<UIDocument>();
        StarForge.View.RTSCamera rig;
        bool wasScripted;

        void OnDestroy()
        {
            // Stopped part-way: put back what the watch changed.
            Time.timeScale = savedScale;
            foreach (var d in hidden) if (d != null && d.rootVisualElement != null) d.rootVisualElement.style.display = DisplayStyle.Flex;
            if (rig != null) rig.scripted = wasScripted;
            if (closeCam != null) Destroy(closeCam.gameObject);
        }

        static List<Bird> Collect(StarForge.View.Fauna fauna, string name)
        {
            var list = new List<Bird>();
            foreach (Transform t in fauna.transform)
            {
                if (t.name != name) continue;
                var b = new Bird { t = t, anim = t.GetComponentInChildren<Animation>(), yaw = t.eulerAngles.y, pos = t.position };
                if (b.anim != null)
                    foreach (AnimationState st in b.anim)
                        if (st.name.ToLowerInvariant().Contains("fly")) b.fly = st;
                list.Add(b);
            }
            return list;
        }

        // The ground point the camera must look at for a bird in the air to sit on its view ray.
        void LookAt(Vector3 p)
        {
            var f = rig.cam.transform.forward;
            float ground = MapInfo.Instance != null ? MapInfo.Instance.HeightAt(new Vector2(p.x, p.z)) : 0f;
            float along = f.y < -0.05f ? (p.y - ground) / -f.y : 0f;
            var q = p + f * along;
            rig.JumpTo(new Vector2(q.x, q.z));
        }

        Camera closeCam;
        Transform closeOn;

        // After Fauna has placed the birds this frame.
        void LateUpdate()
        {
            if (closeCam == null || closeOn == null || rig == null) return;
            var dir = Quaternion.Euler(0f, rig.yaw, 0f) * new Vector3(0f, 0.6f, -1f);
            closeCam.transform.position = closeOn.position + dir.normalized * 13f;
            closeCam.transform.rotation = Quaternion.LookRotation(closeOn.position - closeCam.transform.position);
        }

        IEnumerator Start()
        {
            var fauna = Object.FindAnyObjectByType<StarForge.View.Fauna>();
            rig = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var world = StarForge.World.GameWorld.Instance;
            if (fauna == null || rig == null || world == null) { report = "no fauna/camera/world"; yield break; }
            if (world.running) { report = "a match is running: run it from the menu"; yield break; }
            savedScale = Time.timeScale;
            Time.timeScale = 1f;
            wasScripted = rig.scripted;
            rig.scripted = true;
            foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (d.rootVisualElement != null && d.rootVisualElement.style.display != DisplayStyle.None)
                {
                    d.rootVisualElement.style.display = DisplayStyle.None;
                    hidden.Add(d);
                }
            var sb = new StringBuilder();

            // ---- eagles: 30 s soaring, a picture gliding and one beating if it happens
            var eagles = Collect(fauna, "Eagle");
            if (eagles.Count == 0) sb.Append("no eagles; ");
            else
            {
                rig.ZoomTo(90f);
                yield return null;
                // A camera of its own for the pictures, riding 13 m off the first eagle: the game's
                // camera looks at the ground, and a bird 34 m over it sits at the picture's edge.
                var eagleCam = new GameObject("EagleCamera").AddComponent<Camera>();
                eagleCam.CopyFrom(rig.cam);
                eagleCam.depth = rig.cam.depth + 10f;
                eagleCam.fieldOfView = 30f;
                closeCam = eagleCam;
                closeOn = eagles[0].t;
                bool shotGlide = false, shotBeat = false;
                float until = Time.unscaledTime + 30f, nextLook = 0f;
                yield return null;   // a frame, so the first yaw rate is a real one
                foreach (var b in eagles) { b.yaw = b.t.eulerAngles.y; b.pos = b.t.position; }
                while (Time.unscaledTime < until)
                {
                    yield return null;
                    float dt = Time.deltaTime;
                    if (dt < 1e-4f) continue;
                    Sample(eagles, dt);
                    if (Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + 0.25f; LookAt(eagles[0].t.position); }
                    bool g = eagles[0].wasGliding;
                    bool shot = false;
                    if (g && !shotGlide && Time.unscaledTime > until - 27f) { shotGlide = shot = true; yield return Shot("Temp/bird_eagle_glide.png"); }
                    if (!g && !shotBeat && Time.unscaledTime > until - 27f) { shotBeat = shot = true; yield return Shot("Temp/bird_eagle_beat.png"); }
                    // A picture takes frames nothing was sampled in: start the next step from here,
                    // or the birds' "fastest" reads three frames' travel as one (52 m/s on an 8 m/s circle).
                    if (shot) foreach (var b in eagles) { b.yaw = b.t.eulerAngles.y; b.pos = b.t.position; }
                }
                sb.Append("EAGLES soaring 30 s: ").Append(Describe(eagles));

                // ---- one frightened: a blast under it
                foreach (var b in eagles) { Reset(b); b.yaw = b.t.eulerAngles.y; b.pos = b.t.position; }
                var e0 = eagles[0].t.position;
                world.Raise(new StarForge.World.GameEvent { kind = StarForge.World.GameEventKind.Impact, team = 1, pos = new Vector3(e0.x, 0f, e0.z), scale = 0.9f });   // under 1: no scorch left on the ground
                until = Time.unscaledTime + 6f;
                while (Time.unscaledTime < until)
                {
                    yield return null;
                    float dt = Time.deltaTime;
                    if (dt < 1e-4f) continue;
                    Sample(eagles, dt);
                    if (Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + 0.25f; LookAt(eagles[0].t.position); }
                }
                yield return Shot("Temp/bird_eagle_flee.png");
                closeOn = null;
                closeCam = null;
                Destroy(eagleCam.gameObject);
                yield return null;
                sb.Append(" | FRIGHTENED 6 s (the first): ").Append(Describe(eagles));
            }

            // ---- songbirds on the ground: a close picture, and the largest turn in a frame
            if (fauna.TryGetSongbirds(out var p, out bool aloft) && !aloft)
            {
                var birds = Collect(fauna, "Songbird");
                // Only the flock looked at, and only while it is on its feet.
                birds.RemoveAll(b => (new Vector2(b.t.position.x, b.t.position.z) - new Vector2(p.x, p.z)).sqrMagnitude > 12f * 12f);
                rig.JumpTo(new Vector2(p.x, p.z));
                rig.ZoomTo(14f);
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Shot("Temp/bird_song_feed.png");
                // Closer than the game's camera goes (26 m), to see how a standing bird holds its wings.
                if (birds.Count > 0)
                {
                    var mid = Vector3.zero;
                    foreach (var b in birds) mid += b.t.position;
                    mid /= birds.Count;
                    var go = new GameObject("BirdCloseCamera");
                    var cam = go.AddComponent<Camera>();
                    cam.CopyFrom(rig.cam);
                    cam.depth = rig.cam.depth + 10f;
                    cam.fieldOfView = 30f;
                    var dir = Quaternion.Euler(0f, rig.yaw, 0f) * new Vector3(0f, 0.75f, -1f);
                    go.transform.position = mid + dir.normalized * 9f;
                    go.transform.rotation = Quaternion.LookRotation(mid + Vector3.up * 0.3f - go.transform.position);
                    yield return null;
                    yield return Shot("Temp/bird_song_close.png");
                    Destroy(go);
                    yield return null;
                }
                foreach (var b in birds) b.yaw = b.t.eulerAngles.y;
                float turnMax = 0f, tiltMax = 0f;
                int frames = 0, turns = 0;
                float until = Time.unscaledTime + 12f;
                while (Time.unscaledTime < until)
                {
                    yield return null;
                    if (Time.deltaTime < 1e-4f) continue;
                    var map = MapInfo.Instance;
                    foreach (var b in birds)
                    {
                        float y = b.t.eulerAngles.y;
                        float d = Mathf.Abs(Mathf.DeltaAngle(b.yaw, y));
                        b.yaw = y;
                        float ground = map != null ? map.HeightAt(new Vector2(b.t.position.x, b.t.position.z)) : b.t.position.y;
                        if (b.t.position.y - ground > 0.4f) continue;     // in the air: not this check
                        frames++;
                        if (d > 0.01f) turns++;
                        turnMax = Mathf.Max(turnMax, d);
                        tiltMax = Mathf.Max(tiltMax, Vector3.Angle(b.t.up, Vector3.up));
                    }
                }
                sb.Append($" | SONGBIRDS feeding 12 s ({birds.Count} birds, {frames} bird-frames): largest turn in a frame {turnMax:0.0} deg, " +
                          $"turning in {turns} frames, largest tilt {tiltMax:0.0} deg");
                fauna.FlushSongbirds();
                yield return new WaitForSecondsRealtime(1.6f);
                if (fauna.TryGetSongbirds(out p, out _)) { rig.JumpTo(new Vector2(p.x, p.z)); rig.ZoomTo(30f); }
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("Temp/bird_song_up.png");
            }
            else sb.Append(" | no songbird flock on the ground");

            report = sb.ToString();
            Destroy(gameObject, 0.5f);    // OnDestroy puts the speed, the camera and the screens back
        }

        static IEnumerator Shot(string path)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path, 2);
            yield return null;
        }

        static void Reset(Bird b)
        {
            b.frames = b.outward = b.gliding = b.bursts = 0;
            b.into = b.pitchMax = b.stepMax = 0f;
            b.intoMin = 1e9f; b.intoMax = -1e9f;
        }

        static void Sample(List<Bird> birds, float dt)
        {
            foreach (var b in birds)
            {
                float yaw = b.t.eulerAngles.y;
                float rate = Mathf.DeltaAngle(b.yaw, yaw) / dt;       // + turning right
                b.yaw = yaw;
                // The right wing's rise over level, and so the lean into the turn (+) or out of it (-).
                float roll = Mathf.Asin(Mathf.Clamp(b.t.right.y, -1f, 1f)) * Mathf.Rad2Deg;
                float into = Mathf.Abs(rate) > 0.5f ? -Mathf.Sign(rate) * roll : 0f;
                b.frames++;
                b.into += into;
                b.intoMin = Mathf.Min(b.intoMin, into);
                b.intoMax = Mathf.Max(b.intoMax, into);
                if (into < -0.5f) b.outward++;
                b.pitchMax = Mathf.Max(b.pitchMax, Mathf.Abs(Mathf.Asin(Mathf.Clamp(b.t.forward.y, -1f, 1f)) * Mathf.Rad2Deg));
                b.stepMax = Mathf.Max(b.stepMax, (b.t.position - b.pos).magnitude / dt);
                b.pos = b.t.position;
                bool gliding = b.fly != null && b.fly.enabled && b.fly.speed == 0f;
                if (gliding) b.gliding++;
                if (b.wasGliding && !gliding) b.bursts++;
                b.wasGliding = gliding;
            }
        }

        static string Describe(List<Bird> birds)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < birds.Count; i++)
            {
                var b = birds[i];
                int n = Mathf.Max(1, b.frames);
                sb.Append($"[{i}: lean into the turn mean {b.into / n:0.0} deg ({b.intoMin:0.0}..{b.intoMax:0.0}), leaning outward in {b.outward} of {b.frames} frames, " +
                          $"wings held {100f * b.gliding / n:0}% of the time, {b.bursts} bursts of beats, nose at most {b.pitchMax:0.0} deg off level, fastest {b.stepMax:0.0} m/s] ");
            }
            return sb.ToString();
        }
    }
}
