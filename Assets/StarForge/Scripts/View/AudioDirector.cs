// AudioDirector.cs — every sound in a match: effects, ambience and music.
//
// Effects are recordings (Tools/make_audio.py builds Audio/Sfx, SceneAssembler
// hands them over in a SoundBank), several takes per sound picked at random and
// pitched a little, so a volley is not one sample repeated. Explosions, impacts
// and the interface come from Kenney's CC0 packs; the guns are real firearms
// recorded from beside the shooter and again at a distance and mixed into one
// shot (the Mauler's is a 12-gauge pitched down an octave), and a felled tree
// cracks, groans and crashes with a CC0 recording of a tree coming down.
// Anything missing from the bank falls back to the sounds this class
// synthesises at startup, as it did before there were recordings.
//
// RTS mixing is relative to the camera's focus, not a 3D listener: loudness
// falls off with distance from the point being looked at, pan follows screen x,
// and only what the player can see makes a sound.
//
// Ambience is looped beds whose levels follow the view: wind (stronger zoomed
// out), water when the view holds a lake, a fire's roar and crackle when plants
// burn nearby, engines under moving Maulers and Skimmers, the knock of Diggers
// at work, and birdsong over green ground that falls silent for a while after
// an explosion nearby.
//
// Music is recorded CC0 tracks from OpenGameArt (Audio/Music, copied there by
// make_audio.py), one mood at a time: calm (piano and strings) when nothing is
// happening, tension (a dark, quiet loop) when armies gather or the enemy is in
// sight, combat (an orchestral battle theme) while fighting is on screen. A mood
// has to hold for a while before the music follows it, and moods crossfade
// slowly; every track plays at one loudness (music.json), well under the effects.
using System;
using System.Collections.Generic;
using UnityEngine;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;
using Random = UnityEngine.Random;

namespace StarForge.View
{
    [Serializable]
    public sealed class SoundBank
    {
        public AudioClip[] rifle, cannon, pulse, bolt, boom, bigBoom, rumble, thud, hitMetal, crystal, rock, mine, stomp, shield, build;
        [Tooltip("A tree splitting and groaning as it goes over, its crash as it lands, and a bush flattened under a Mauler.")]
        public AudioClip[] treeFall, treeCrash, crush;
        public AudioClip engineHeavy, engineHover;
        public AudioClip uiSelect, uiClick, uiMove, uiAttack, uiError, uiDone, uiNotice, uiAlert, uiPromote, uiPlace;
        public AudioClip wind, water, fire;
        public AudioClip[] birds;
        public AudioClip[] musicCalm, musicTension, musicCombat;
        [Tooltip("Each music clip's RMS loudness (music.json), in the same order, so all play at one level.")]
        public float[] rmsCalm, rmsTension, rmsCombat;
    }

    public sealed class AudioDirector : MonoBehaviour
    {
        public GameWorld world;
        public PlayerController player;
        public RTSCamera rig;
        public SoundBank bank = new SoundBank();
        [Range(0f, 1f)] public float masterVolume = 0.7f;
        [Range(0f, 0.2f), Tooltip("The RMS level every music track is played at (before the master volume): low, under the effects.")]
        public float musicLoudness = 0.05f;

        const int SR = 44100;
        AudioClip rifle, cannon, pulse, bolt, boom, bigBoom, crystal, click, warn, good, promote, wind;
        readonly List<AudioSource> voices = new List<AudioSource>();
        readonly Dictionary<object, float> lastPlayed = new Dictionary<object, float>();
        AudioSource ui, windSrc, waterSrc, fireSrc, heavySrc, hoverSrc, birdSrc;
        // Music: two sources crossfading; the mood playing and the one asked for.
        readonly AudioSource[] music = new AudioSource[2];
        readonly float[] musicGain = new float[2];
        readonly int[] musicMood = { -1, -1 };
        int front = -1, mood = -1, wantMood, lastCalm = -1;
        float moodSince, fade;
        readonly float[] moodLevel = new float[3];
        int next;
        float underAttackT = -99f;
        uint noiseState = 0x12345678;
        float combatHeat, birdsQuietUntil, nextBird, nextMine, nextScan;
        float waterNear, fireNear, heavyMoving, hoverMoving, enemiesInSight;

        void Awake()
        {
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (rig == null) rig = FindAnyObjectByType<RTSCamera>();
            Synthesize();
            for (int i = 0; i < 24; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                voices.Add(s);
            }
            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            windSrc = Loop(bank.wind != null ? bank.wind : wind);
            waterSrc = Loop(bank.water);
            fireSrc = Loop(bank.fire);
            heavySrc = Loop(bank.engineHeavy);
            hoverSrc = Loop(bank.engineHover);
            birdSrc = gameObject.AddComponent<AudioSource>();
            birdSrc.playOnAwake = false;
            StartMusic();
        }

        AudioSource Loop(AudioClip clip)
        {
            if (clip == null) return null;
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            s.playOnAwake = false;
            // Start each bed somewhere different in its loop, so they never line up.
            s.timeSamples = Random.Range(0, Mathf.Max(1, clip.samples - 1));
            s.Play();
            return s;
        }

        void StartMusic()
        {
            for (int i = 0; i < 2; i++)
            {
                var src = music[i] = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.volume = 0f;
            }
        }

        /// <summary>How much each mood is playing now (calm, tension, combat), 0..1; for the benchmark report.</summary>
        public Vector3 MusicLevels => new Vector3(moodLevel[0], moodLevel[1], moodLevel[2]);

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

        // ------------------------------------------------------------ events
        void OnSelect() => PlayUI(bank.uiSelect != null ? bank.uiSelect : click, 0.3f, bank.uiSelect != null ? 1f : 1.25f);

        void OnOrder(Vector3 pos, int kind)
        {
            if (kind == 1) PlayUI(bank.uiAttack != null ? bank.uiAttack : click, 0.35f, bank.uiAttack != null ? 1f : 0.8f);
            else if (kind == 3) PlayUI(bank.uiPlace != null ? bank.uiPlace : click, 0.4f, 1f);
            else PlayUI(bank.uiMove != null ? bank.uiMove : click, 0.35f, 1f);
        }

        void OnEvent(GameEvent e)
        {
            int me = player != null ? player.team : 0;
            switch (e.kind)
            {
                case GameEventKind.Fire:
                    if (!Seen(e.pos)) return;
                    Heat(e.pos, 0.06f);
                    switch (e.projectileKind)
                    {
                        case 1:
                            PlayAt(e.pos, bank.cannon, 0.75f, 0.06f, cannon);
                            PlayAt(e.pos, bank.thud, 0.3f, 0.06f);
                            break;
                        case 2: PlayAt(e.pos, bank.pulse, 0.3f, 0.03f, pulse); break;
                        case 3: PlayAt(e.pos, bank.bolt, 0.4f, 0.04f, bolt); break;
                        default: PlayAt(e.pos, bank.rifle, e.type == UnitType.Worker ? 0.1f : 0.22f, 0.025f, rifle); break;
                    }
                    break;
                case GameEventKind.Impact:
                    if (!Seen(e.pos)) return;
                    Heat(e.pos, e.scale > 1f ? 0.25f : 0.03f);
                    if (e.scale > 1f)
                    {
                        PlayAt(e.pos, bank.boom, 0.5f, 0.05f, boom);
                        QuietBirds(e.pos);
                    }
                    else PlayAt(e.pos, bank.hitMetal, 0.09f, 0.07f);
                    break;
                case GameEventKind.Death:
                    if (!Seen(e.pos)) return;
                    Heat(e.pos, 0.4f);
                    QuietBirds(e.pos);
                    if (e.type == UnitType.Ore) { PlayAt(e.pos, bank.crystal, 0.6f, 0.1f); PlayAt(e.pos, crystal, 0.35f, 0.1f); }
                    else if (e.type == UnitType.Boulder) { PlayAt(e.pos, bank.rock, 0.55f, 0.05f, boom); PlayAt(e.pos, bank.stomp, 0.3f, 0.05f); }
                    else if (e.unit != null && e.unit.def.building)
                    {
                        PlayAt(e.pos, bank.bigBoom, 1f, 0.2f, bigBoom);
                        PlayAt(e.pos, bank.rumble, 0.7f, 0.2f);
                    }
                    else
                    {
                        PlayAt(e.pos, bank.boom, 0.65f, 0.05f, boom);
                        if (e.type == UnitType.Mauler) PlayAt(e.pos, bank.thud, 0.6f, 0.1f);
                    }
                    break;
                case GameEventKind.PlantFelled:
                {
                    // The trunk splitting and the tree groaning as it goes over -- or,
                    // for a bush under a Mauler, just the rush of leaves.
                    if (!Seen(e.pos)) return;
                    var veg = world.Plants;
                    bool bush = veg != null && e.index >= 0 && e.index < veg.plants.Length && veg.KindOf(e.index).bush;
                    if (bush) PlayAt(e.pos, bank.crush, 0.3f, 0.1f);
                    else PlayAt(e.pos, bank.treeFall, 0.45f * Mathf.Clamp(e.scale, 0.7f, 1.3f), 0.12f);
                    break;
                }
                case GameEventKind.PlantLanded:
                    // And the crash as it lands, quieter than any explosion.
                    if (Seen(e.pos)) PlayAt(e.pos, bank.treeCrash, 0.5f * Mathf.Clamp(e.scale, 0.7f, 1.3f), 0.15f, boom);
                    break;
                case GameEventKind.StructurePlaced when e.team == me:
                    PlayAt(e.pos, bank.build, 0.25f, 0.5f);
                    break;
                case GameEventKind.UnderAttack when e.team == me:
                    if (Time.unscaledTime - underAttackT > 10f) { underAttackT = Time.unscaledTime; PlayUI(bank.uiAlert != null ? bank.uiAlert : warn, 0.6f, 1f); }
                    break;
                case GameEventKind.StructureComplete when e.team == me:
                    PlayUI(bank.uiDone != null ? bank.uiDone : good, 0.5f, 1f);
                    break;
                case GameEventKind.Notice when e.team == me:
                    PlayUI(bank.uiNotice != null ? bank.uiNotice : good, 0.45f, 1f);
                    break;
                case GameEventKind.Promoted when e.team == me:
                    PlayUI(bank.uiPromote != null ? bank.uiPromote : promote, 0.5f, 1f);
                    break;
                case GameEventKind.Refused when e.team == me:
                    PlayUI(bank.uiError != null ? bank.uiError : warn, 0.4f, bank.uiError != null ? 1f : 1.4f);
                    break;
            }
        }

        bool Seen(Vector3 p) => MatchSettings.spectate || world.Visible(player != null ? player.team : 0, new Vector2(p.x, p.z));

        float Attenuation(Vector3 pos)
        {
            if (rig == null) return 0f;
            float dist = Vector2.Distance(rig.Focus, new Vector2(pos.x, pos.z));
            float att = Mathf.Clamp01(1f - dist / 160f);
            return att * att;
        }

        /// <summary>Fighting the player can hear warms the music; far from the view it counts less.</summary>
        void Heat(Vector3 pos, float amount) => combatHeat = Mathf.Min(3f, combatHeat + amount * (0.35f + 0.65f * Attenuation(pos)));

        void QuietBirds(Vector3 pos)
        {
            if (Attenuation(pos) > 0.15f) birdsQuietUntil = Time.unscaledTime + Random.Range(18f, 30f);
        }

        void PlayAt(Vector3 pos, AudioClip[] takes, float volume, float minGap, AudioClip fallback = null)
        {
            if (takes != null && takes.Length > 0) PlayAt(pos, takes[Random.Range(0, takes.Length)], volume, minGap, (object)takes);
            else if (fallback != null) PlayAt(pos, fallback, volume, minGap);
        }

        void PlayAt(Vector3 pos, AudioClip clip, float volume, float minGap) => PlayAt(pos, clip, volume, minGap, (object)clip);

        void PlayAt(Vector3 pos, AudioClip clip, float volume, float minGap, object key)
        {
            if (clip == null || rig == null || rig.cam == null) return;
            if (lastPlayed.TryGetValue(key, out float t) && Time.unscaledTime - t < minGap) return;
            float att = Attenuation(pos);
            if (att < 0.02f) return;
            Vector3 vp = rig.cam.WorldToViewportPoint(pos);
            lastPlayed[key] = Time.unscaledTime;
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

        // ------------------------------------------------------------ ambience and music
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool paused = Time.timeScale < 0.01f;
            combatHeat = Mathf.Max(0f, combatHeat * Mathf.Exp(-dt / 7f) - dt * 0.01f);
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.25f; ScanView(); }

            float zoom = rig != null ? Mathf.InverseLerp(rig.minDistance, rig.maxDistance, rig.distance) : 0.5f;
            Fade(windSrc, (0.1f + 0.14f * zoom) * masterVolume, dt, 1.5f);
            Fade(waterSrc, 0.3f * waterNear * (1f - 0.5f * zoom) * masterVolume, dt, 1.5f);
            Fade(fireSrc, 0.55f * Mathf.Clamp01(fireNear) * masterVolume, dt, 2.5f);
            Fade(heavySrc, (paused ? 0f : 0.2f * Mathf.Clamp01(heavyMoving)) * masterVolume, dt, 3f);
            Fade(hoverSrc, (paused ? 0f : 0.12f * Mathf.Clamp01(hoverMoving)) * masterVolume, dt, 3f);
            Birds();
            Mining();
            Music(dt);
        }

        static void Fade(AudioSource s, float target, float dt, float rate)
        {
            if (s != null) s.volume = Mathf.MoveTowards(s.volume, target, dt * rate * Mathf.Max(0.05f, Mathf.Abs(target - s.volume) + 0.05f));
        }

        void ScanView()
        {
            if (world == null || rig == null) return;
            var map = world.Map;
            Vector2 f = rig.Focus;
            // Water: the share of a ring of points round the view that is lake.
            int wet = 0, n = 0;
            for (int r = 1; r <= 3; r++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    var p = f + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 9f;
                    if (!map.InBounds(p)) continue;
                    n++;
                    if (map.WaterDepth(p) > 0.05f) wet++;
                }
            waterNear = n > 0 ? Mathf.Clamp01(wet / (float)n * 2.5f) : 0f;

            fireNear = 0f;
            var veg = world.Plants;
            if (veg != null)
                foreach (int i in veg.burning)
                {
                    var p = veg.plants[i].pos;
                    if (!Seen(p)) continue;
                    fireNear += veg.live[i].fire * Attenuation(p) * 0.6f;
                }

            heavyMoving = hoverMoving = enemiesInSight = 0f;
            int me = player != null ? player.team : 0;
            foreach (var u in world.units)
            {
                if (u == null || u.dying || !u.def.IsMobile) continue;
                bool seen = u.team == me || MatchSettings.spectate || u.visibleToPlayer;
                if (!seen) continue;
                if (u.team != me && u.def.IsArmy) enemiesInSight += 1f;
                if (u.agent == null || !u.agent.enabled || u.agent.velocity.sqrMagnitude < 0.5f) continue;
                float att = Attenuation(u.Ground);
                if (u.Type == UnitType.Mauler) heavyMoving += att * 0.5f;
                else if (u.Type == UnitType.Skimmer) hoverMoving += att * 0.4f;
            }
            if (MatchSettings.spectate) enemiesInSight *= 0.5f;
        }

        void Birds()
        {
            var clips = bank.birds;
            if (clips == null || clips.Length == 0 || birdSrc == null || rig == null) return;
            float now = Time.unscaledTime;
            if (now < nextBird) return;
            nextBird = now + Random.Range(1.8f, 6.5f);
            if (now < birdsQuietUntil || combatHeat > 0.6f || Time.timeScale < 0.01f) return;
            // Over green ground, and less the higher the camera is.
            float zoom = Mathf.InverseLerp(rig.minDistance, rig.maxDistance, rig.distance);
            if (Random.value < zoom * 0.7f) return;
            birdSrc.pitch = Random.Range(0.88f, 1.15f);
            birdSrc.panStereo = Random.Range(-0.8f, 0.8f);
            birdSrc.PlayOneShot(clips[Random.Range(0, clips.Length)], Random.Range(0.08f, 0.18f) * masterVolume * (1f - waterNear * 0.3f));
        }

        void Mining()
        {
            if (bank.mine == null || bank.mine.Length == 0 || world == null || Time.timeScale < 0.01f) return;
            float now = Time.unscaledTime;
            if (now < nextMine) return;
            nextMine = now + Random.Range(0.35f, 0.7f);
            int me = player != null ? player.team : 0;
            foreach (var u in world.units)
            {
                if (u == null || u.dying || u.Type != UnitType.Worker || u.order != Order.Harvest || u.carrying > 0) continue;
                if (u.agent != null && u.agent.enabled && u.agent.velocity.sqrMagnitude > 0.3f) continue;
                if (u.team != me && !MatchSettings.spectate && !u.visibleToPlayer) continue;
                if (Random.value > 0.35f) continue;
                PlayAt(u.Ground, bank.mine, 0.12f, 0.12f);
                break;
            }
        }

        void Music(float dt)
        {
            if (music[0] == null || bank.musicCalm == null || bank.musicCalm.Length == 0) return;
            bool inMatch = world != null && world.running;
            float now = Time.unscaledTime;
            // The mood the game is in. Combat and tension are asked for at once and
            // let go of slowly, so a lull in a fight does not drop to the calm music.
            int asked = 0;
            if (inMatch)
            {
                if (combatHeat > 1.0f) asked = 2;
                else if (combatHeat > 0.35f || enemiesInSight >= 3f || now - underAttackT < 20f) asked = 1;
            }
            if (asked != wantMood) { wantMood = asked; moodSince = now; }
            float hold = wantMood > mood ? 2.5f : wantMood == 1 ? 12f : 15f;
            if (wantMood != mood && now - moodSince > (mood < 0 ? 0f : hold)) Play(wantMood);

            // Crossfade: the front source rises, the other falls, both on a smoothstep.
            fade = Mathf.MoveTowards(fade, 1f, dt / (mood == 2 ? 3f : 6f));
            float k = fade * fade * (3f - 2f * fade);
            for (int i = 0; i < 2; i++)
            {
                var src = music[i];
                if (src.clip == null) continue;
                float level = i == front ? k : (1f - k) * (musicMood[i] >= 0 ? 1f : 0f);
                src.volume = level * musicGain[i] * masterVolume;
                if (i != front && k >= 1f && src.isPlaying) src.Stop();
            }
            for (int m = 0; m < 3; m++) moodLevel[m] = 0f;
            for (int i = 0; i < 2; i++)
                if (musicMood[i] >= 0 && music[i].isPlaying) moodLevel[musicMood[i]] += i == front ? k : 1f - k;
            // A calm track that has ended gives way to the next one.
            if (front >= 0 && mood == 0 && !music[front].isPlaying && bank.musicCalm.Length > 1) Play(0);
        }

        void Play(int m)
        {
            var clips = m == 0 ? bank.musicCalm : m == 1 ? bank.musicTension : bank.musicCombat;
            var rms = m == 0 ? bank.rmsCalm : m == 1 ? bank.rmsTension : bank.rmsCombat;
            mood = m;
            if (clips == null || clips.Length == 0) return;
            int pick = Random.Range(0, clips.Length);
            if (m == 0 && clips.Length > 1 && pick == lastCalm) pick = (pick + 1) % clips.Length;
            if (m == 0) lastCalm = pick;
            int next = front < 0 ? 0 : 1 - front;
            var src = music[next];
            src.Stop();
            src.clip = clips[pick];
            // The calm set plays through its tracks in turn; the others loop.
            src.loop = m != 0 || clips.Length == 1;
            float r = rms != null && pick < rms.Length && rms[pick] > 1e-4f ? rms[pick] : 0.1f;
            musicGain[next] = Mathf.Min(1f, musicLoudness / r);
            musicMood[next] = m;
            src.volume = 0f;
            src.Play();
            front = next;
            fade = 0f;
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
