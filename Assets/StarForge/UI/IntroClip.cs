// IntroClip.cs — the ten-second intro before the title (docs/lore/prompts.md, prompt 6): the
// key art come alive. Played once a launch of the built player over the title's splash, whose
// background is the same picture, so it ends by fading into the title; any key or click ends it
// early. It never plays in the Editor (trials photograph the splash) unless a trial asks with
// AskInEditor, never in a test run of the player or with -sfnointro, and when the clip is not in
// the build the splash simply shows. Presentation only: nothing reads it.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace StarForge.UI
{
    public sealed class IntroClip
    {
        /// <summary>What the last attempt did, for trials: "waiting ...", "not started: why", "playing ...", "done ...".</summary>
        public static string Report = "waiting: no splash yet";
        static bool seen;            // once a launch
        static float askedUntil;     // a trial's request for the Editor, by Time.realtimeSinceStartup

        const string Resource = "Intro/intro";   // Assets/StarForge/Resources/Intro/intro.mp4
        // GiveUpAfter runs until the first frame shows (a cold launch prepares the video for a while, behind a black
        // layer), in frames of at most a quarter second: the scene's loading hitch is one long frame (2.9 s in the
        // built player) and is not time spent waiting. Longest counts from the first frame, so a slow start never
        // eats the clip's own time.
        const float FadeOut = 0.7f, GiveUpAfter = 6f, Longest = 13f;
        static readonly string[] Off = { "-sfnointro", "-sfbench", "-sfgallery", "-sfgalleryquick", "-sfburst", "-sfshot", "-sfscreentest" };
        static readonly float[] heard = new float[256];
        // -sfintrolog: the player's log gets the report once a second while the clip plays (frames, its sound's level),
        // the one way to check it in a built player, where nothing reads Report. Start and end are logged anyway.
        static readonly bool Loud = Array.IndexOf(Environment.GetCommandLineArgs(), "-sfintrolog") >= 0;

        readonly VisualElement layer;
        readonly GameObject host;
        readonly VideoPlayer player;
        readonly AudioSource audio;
        readonly RenderTexture frame;
        readonly float volume;
        float age, waited, fading = -1f, nextLog, firstFrameAt = -1f;
        bool skipped;

        public bool Done { get; private set; }
        /// <summary>How much of the clip is up: 1 while it plays, falling to 0 over the fade. The menu holds the game's own sound under it.</summary>
        public float Level => Done ? 0f : fading >= 0f ? Mathf.Clamp01(1f - fading / FadeOut) : 1f;

        // Statics outlive a play session in the Editor (domain reload is off).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void NewLaunch() { seen = false; Report = "waiting: no splash yet"; }

        /// <summary>A trial's switch: a splash in the Editor within <paramref name="seconds"/> starts with the clip.
        /// Used up by that start, and it runs out by itself, so a play that never shows the splash
        /// cannot hand the clip to a later Play of the user's own.</summary>
        public static void AskInEditor(float seconds = 180f) { askedUntil = Time.realtimeSinceStartup + seconds; }

        /// <summary>Starts the clip over <paramref name="root"/>'s other children, or returns null
        /// when it should not or cannot play (<see cref="Report"/> says why).</summary>
        public static IntroClip TryStart(VisualElement root, float volume)
        {
            if (seen) return null;   // Report keeps what the first attempt said
            seen = true;
            var clip = Begin(root, volume);
            if (!Application.isEditor || Loud) Debug.Log("[StarForge] intro: " + Report);
            return clip;
        }

        static IntroClip Begin(VisualElement root, float volume)
        {
            if (root == null) { Report = "not started: no panel"; return null; }
            if (Application.isEditor)
            {
                bool asked = askedUntil > 0f && Time.realtimeSinceStartup <= askedUntil;
                askedUntil = 0f;
                if (!asked) { Report = "not started: the Editor (MenuTrials.IntroNext asks for it)"; return null; }
            }
            else
            {
                var args = Environment.GetCommandLineArgs();
                foreach (var f in Off)
                    if (Array.IndexOf(args, f) >= 0) { Report = "not started: " + f; return null; }
            }
            var clip = Resources.Load<VideoClip>(Resource);
            if (clip == null) { Report = "not started: no clip at Resources/" + Resource; return null; }
            return new IntroClip(root, clip, volume);
        }

        IntroClip(VisualElement root, VideoClip clip, float volume)
        {
            this.volume = Mathf.Clamp01(volume);
            frame = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "SF_Intro" };
            var was = RenderTexture.active;
            RenderTexture.active = frame;
            GL.Clear(true, true, Color.black);   // black until the first frame, never the texture's old memory
            RenderTexture.active = was;

            // The scene owns the host (hidden, not DontSave): a scene load or the end of play takes it,
            // its player and its sound with it. The RenderTexture is not the scene's: Abort releases it.
            host = new GameObject("IntroClip") { hideFlags = HideFlags.HideInHierarchy };
            audio = host.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.volume = this.volume;
            player = host.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.VideoClip;
            player.clip = clip;
            player.isLooping = false;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = frame;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;   // so the listener's volume and a test mute apply
            if (clip.audioTrackCount > 0)
            {
                player.controlledAudioTrackCount = 1;
                player.EnableAudioTrack(0, true);
                player.SetTargetAudioSource(0, audio);
            }
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.loopPointReached += _ => End();
            player.errorReceived += (_, message) => { Debug.LogWarning("[StarForge] intro clip: " + message); Stop("failed: " + message); };

            layer = new VisualElement { name = "introClip", pickingMode = PickingMode.Position };
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            layer.style.backgroundColor = Color.black;
            layer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(frame));
            layer.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);   // as the splash frames its picture
            layer.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            layer.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            layer.RegisterCallback<PointerDownEvent>(e => { skipped = true; End(); e.StopPropagation(); });
            root.Add(layer);   // last child: over the splash
            player.Play();
            Report = "playing 0.0 s";
        }

        /// <summary>Once a frame, by unscaled time.</summary>
        public void Tick(float dt)
        {
            if (Done) return;
            if (host == null) { Stop("its scene went"); return; }   // Unity's null check: the scene took the host
            age += dt;
            if (fading >= 0f)
            {
                fading += dt;
                float k = Mathf.Clamp01(1f - fading / FadeOut);
                layer.style.opacity = k;
                audio.volume = volume * k;
                if (fading >= FadeOut) Stop((skipped ? "skipped" : "ended") + $" after {age:0.0} s, {FirstFrame}");
                return;
            }
            if (firstFrameAt < 0f)
            {
                if (player.frame > 0) firstFrameAt = age;
                else waited += Mathf.Min(dt, 0.25f);
            }
            // The report is a trial's aid (and -sfintrolog's): a played game builds no string a frame for it.
            if (Application.isEditor || Loud)
                Report = $"playing {age:0.0} s, frame {player.frame} of {player.frameCount}, {frame.width}x{frame.height}, first frame {(firstFrameAt < 0f ? "not yet" : firstFrameAt.ToString("0.0") + " s in")} (waited {waited:0.0} s); {Sound()}";
            if (Loud && age >= nextLog) { nextLog = age + 1f; Debug.Log("[StarForge] intro: " + Report); }
            // Never hold the title hostage: a clip that will not start, or will not end, gives way.
            if (firstFrameAt < 0f ? waited > GiveUpAfter : age - firstFrameAt > Longest) End();
        }

        // What the clip's own sound is doing, for the trial (agent runs are muted, so nothing can be listened to):
        // the track taken, the source running, and the level in its output just now.
        string Sound()
        {
            audio.GetOutputData(heard, 0);
            float sum = 0f;
            for (int i = 0; i < heard.Length; i++) sum += heard[i] * heard[i];
            return $"sound: {player.audioTrackCount} track(s), track 0 on {(player.audioTrackCount > 0 && player.IsAudioTrackEnabled(0))}, " +
                   $"source playing {audio.isPlaying} at {audio.volume:0.00}, level {Mathf.Sqrt(sum / heard.Length):0.0000}";
        }

        /// <summary>A key was pressed: fade into the title now.</summary>
        public void Skip() { skipped = true; End(); }

        /// <summary>The menu is going (a scene load, the end of play): stop at once and free what the scene does not own.</summary>
        public void Abort() { Stop("aborted after " + age.ToString("0.0") + " s"); }

        string FirstFrame => firstFrameAt < 0f ? $"no frame shown (waited {waited:0.0} s)" : $"first frame {firstFrameAt:0.0} s in (waited {waited:0.0} s)";

        void End() { if (!Done && fading < 0f) fading = 0f; }

        void Stop(string how)
        {
            if (Done) return;
            Done = true;
            Report = "done: " + how;
            if (!Application.isEditor || Loud) Debug.Log("[StarForge] intro: " + Report);
            layer.RemoveFromHierarchy();
            if (player != null) { player.Stop(); player.targetTexture = null; }
            if (host != null) UnityEngine.Object.Destroy(host);
            if (frame != null) { frame.Release(); UnityEngine.Object.Destroy(frame); }
        }
    }
}
