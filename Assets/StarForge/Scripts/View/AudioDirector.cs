// AudioDirector.cs — sound for every gameplay event, synthesized at startup.
//
// The original game had no audio at all. Rather than ship sample files, each
// sound is built from noise, sine sweeps and envelopes when the scene loads
// (well under a megabyte of PCM), then played from a small voice pool. RTS
// mixing is relative to the camera's focus, not a 3D listener: loudness falls
// off with distance from the point being looked at, and pan follows screen x.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed class AudioDirector : MonoBehaviour
    {
        public GameWorld world;
        public PlayerController player;
        public RTSCamera rig;
        [Range(0f, 1f)] public float masterVolume = 0.7f;

        const int SR = 44100;
        AudioClip rifle, cannon, pulse, bolt, boom, bigBoom, crystal, click, warn, good, promote, wind;
        readonly List<AudioSource> voices = new List<AudioSource>();
        readonly Dictionary<AudioClip, float> lastPlayed = new Dictionary<AudioClip, float>();
        AudioSource ui, ambience;
        int next;
        float underAttackT = -99f;
        uint noiseState = 0x12345678;

        void Awake()
        {
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (rig == null) rig = FindAnyObjectByType<RTSCamera>();
            Synthesize();
            for (int i = 0; i < 20; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                voices.Add(s);
            }
            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = wind;
            ambience.loop = true;
            ambience.volume = 0.12f * masterVolume;
            ambience.Play();
        }

        void OnEnable()
        {
            if (world != null) world.Event += OnEvent;
            if (player != null)
            {
                player.OrderMarker += OnOrder;
                player.SelectionChanged += OnSelect;
            }
        }

        void OnDisable()
        {
            if (world != null) world.Event -= OnEvent;
            if (player != null)
            {
                player.OrderMarker -= OnOrder;
                player.SelectionChanged -= OnSelect;
            }
        }

        // ------------------------------------------------------------ playback
        void OnSelect() => PlayUI(click, 0.25f, 1.25f);
        void OnOrder(Vector3 pos, int kind) => PlayUI(click, 0.35f, kind == 1 ? 0.8f : 1f);

        void OnEvent(GameEvent e)
        {
            int me = player != null ? player.team : 0;
            switch (e.kind)
            {
                case GameEventKind.Fire:
                    if (!Seen(e.pos)) return;
                    switch (e.projectileKind)
                    {
                        case 1: PlayAt(e.pos, cannon, 0.8f, 0.06f); break;
                        case 2: PlayAt(e.pos, pulse, 0.35f, 0.03f); break;
                        case 3: PlayAt(e.pos, bolt, 0.45f, 0.04f); break;
                        default: PlayAt(e.pos, rifle, e.type == UnitType.Worker ? 0.12f : 0.25f, 0.025f); break;
                    }
                    break;
                case GameEventKind.Impact:
                    if (e.scale > 1f && Seen(e.pos)) PlayAt(e.pos, boom, 0.55f, 0.05f);
                    break;
                case GameEventKind.Death:
                    if (!Seen(e.pos)) return;
                    if (e.type == UnitType.Ore) PlayAt(e.pos, crystal, 0.6f, 0.1f);
                    else if (e.unit != null && e.unit.def.building) PlayAt(e.pos, bigBoom, 1f, 0.2f);
                    else PlayAt(e.pos, boom, 0.75f, 0.05f);
                    break;
                case GameEventKind.UnderAttack when e.team == me:
                    if (Time.unscaledTime - underAttackT > 10f) { underAttackT = Time.unscaledTime; PlayUI(warn, 0.6f, 1f); }
                    break;
                case GameEventKind.StructureComplete when e.team == me:
                    PlayUI(good, 0.5f, 1f);
                    break;
                case GameEventKind.Promoted when e.team == me:
                    PlayUI(promote, 0.5f, 1f);
                    break;
                case GameEventKind.Refused when e.team == me:
                    PlayUI(warn, 0.35f, 1.4f);
                    break;
            }
        }

        bool Seen(Vector3 p) => MatchSettings.spectate || world.Visible(player != null ? player.team : 0, new Vector2(p.x, p.z));

        void PlayAt(Vector3 pos, AudioClip clip, float volume, float minGap)
        {
            if (clip == null || rig == null || rig.cam == null) return;
            if (lastPlayed.TryGetValue(clip, out float t) && Time.unscaledTime - t < minGap) return;
            Vector2 focus = rig.Focus;
            float dist = Vector2.Distance(focus, new Vector2(pos.x, pos.z));
            float att = Mathf.Clamp01(1f - dist / 160f);
            att *= att;
            if (att < 0.02f) return;
            Vector3 vp = rig.cam.WorldToViewportPoint(pos);
            lastPlayed[clip] = Time.unscaledTime;
            var s = voices[next];
            next = (next + 1) % voices.Count;
            s.clip = clip;
            s.volume = volume * att * masterVolume;
            s.pitch = Random.Range(0.92f, 1.08f);
            s.panStereo = Mathf.Clamp((vp.x - 0.5f) * 1.4f, -0.9f, 0.9f);
            s.Play();
        }

        void PlayUI(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            ui.pitch = pitch;
            ui.PlayOneShot(clip, volume * masterVolume);
        }

        // ------------------------------------------------------------ synthesis
        float Noise()
        {
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            return (noiseState & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        static AudioClip Clip(string name, float[] data)
        {
            float peak = 1e-4f;
            foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < data.Length; i++) data[i] = data[i] / peak * 0.9f;
            var c = AudioClip.Create(name, data.Length, 1, SR, false);
            c.SetData(data, 0);
            return c;
        }

        void Synthesize()
        {
            rifle = Clip("rifle", Burst(0.14f, 42f, 0.35f, 2600f, 1500f));
            pulse = Clip("pulse", Sweep(0.2f, 1500f, 420f, 18f, 0.15f));
            bolt = Clip("bolt", Sweep(0.26f, 520f, 160f, 12f, 0.3f));
            cannon = Clip("cannon", Thump(0.9f, 90f, 38f, 5.5f, 0.75f, 700f));
            boom = Clip("boom", Thump(1.5f, 60f, 30f, 3.2f, 1f, 420f));
            bigBoom = Clip("bigboom", Thump(3f, 45f, 22f, 1.6f, 1f, 260f));
            crystal = Clip("crystal", Chime(0.8f, new[] { 1760f, 2637f, 3520f, 4186f }, 6f));
            click = Clip("click", Sweep(0.05f, 1400f, 900f, 60f, 0f));
            warn = Clip("warn", Tones(0.42f, new[] { 520f, 390f }, 9f));
            good = Clip("good", Tones(0.36f, new[] { 523f, 784f }, 10f));
            promote = Clip("promote", Tones(0.5f, new[] { 523f, 659f, 1046f }, 8f));
            wind = Clip("wind", Wind(8f));
        }

        float[] Burst(float seconds, float decay, float click, float lowpass, float body)
        {
            int n = (int)(SR * seconds);
            var d = new float[n];
            float lp = 0f, a = Mathf.Exp(-2f * Mathf.PI * lowpass / SR);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                lp = Mathf.Lerp(Noise(), lp, a);
                float env = Mathf.Exp(-t * decay);
                d[i] = lp * env + click * Mathf.Sin(2f * Mathf.PI * body * t) * Mathf.Exp(-t * 90f);
            }
            return d;
        }

        static float[] Sweep(float seconds, float f0, float f1, float decay, float grit)
        {
            int n = (int)(SR * seconds);
            var d = new float[n];
            float phase = 0f;
            uint s = 99;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = Mathf.Lerp(f0, f1, Mathf.Sqrt(t / seconds));
                phase += 2f * Mathf.PI * f / SR;
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                float nz = (s & 0xFFFF) / 32768f - 1f;
                d[i] = (Mathf.Sin(phase) + Mathf.Sign(Mathf.Sin(phase)) * 0.25f + nz * grit) * Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 800f);
            }
            return d;
        }

        float[] Thump(float seconds, float f0, float f1, float decay, float noiseAmt, float lowpass)
        {
            int n = (int)(SR * seconds);
            var d = new float[n];
            float phase = 0f, lp = 0f, lp2 = 0f;
            float a = Mathf.Exp(-2f * Mathf.PI * lowpass / SR);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = Mathf.Lerp(f1, f0, Mathf.Exp(-t * 6f));
                phase += 2f * Mathf.PI * f / SR;
                lp = Mathf.Lerp(Noise(), lp, a);
                lp2 = Mathf.Lerp(lp, lp2, a);
                float crackle = Noise() * Mathf.Exp(-t * 30f) * 0.35f;
                d[i] = (Mathf.Sin(phase) * Mathf.Exp(-t * decay * 1.8f) * 0.9f + lp2 * 2.5f * noiseAmt * Mathf.Exp(-t * decay) + crackle)
                       * Mathf.Min(1f, t * 400f);
            }
            return d;
        }

        static float[] Chime(float seconds, float[] freqs, float decay)
        {
            int n = (int)(SR * seconds);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float v = 0f;
                for (int k = 0; k < freqs.Length; k++)
                    v += Mathf.Sin(2f * Mathf.PI * freqs[k] * t) * Mathf.Exp(-t * decay * (1f + k * 0.6f)) / (1f + k);
                d[i] = v * Mathf.Min(1f, t * 600f);
            }
            return d;
        }

        static float[] Tones(float seconds, float[] freqs, float decay)
        {
            int n = (int)(SR * seconds);
            var d = new float[n];
            float step = seconds / freqs.Length;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                int k = Mathf.Min(freqs.Length - 1, (int)(t / step));
                float lt = t - k * step;
                float env = Mathf.Exp(-lt * decay) * Mathf.Min(1f, lt * 300f);
                d[i] = (Mathf.Sin(2f * Mathf.PI * freqs[k] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * freqs[k] * t)) * env;
            }
            return d;
        }

        float[] Wind(float seconds)
        {
            int n = (int)(SR * seconds);
            var d = new float[n];
            float lp = 0f, lp2 = 0f;
            float a = Mathf.Exp(-2f * Mathf.PI * 300f / SR);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                lp = Mathf.Lerp(Noise(), lp, a);
                lp2 = Mathf.Lerp(lp, lp2, a);
                float gust = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * t / seconds * 2f) * Mathf.Sin(2f * Mathf.PI * t / seconds * 3f + 1f);
                d[i] = lp2 * gust;
            }
            // Crossfade the ends so the loop has no seam.
            int fade = SR / 2;
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                d[i] = d[i] * k + d[n - fade + i] * (1f - k);
            }
            System.Array.Resize(ref d, n - fade);
            return d;
        }
    }
}
