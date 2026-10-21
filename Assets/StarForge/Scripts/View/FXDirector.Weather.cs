// FXDirector.Weather.cs — the day's weather around the camera. View only: nothing here
// touches the simulation, and what a match gets is drawn from the map seed (not the
// match's random stream), so a seed replays.
//
//  * Gusts lift dust off dry, open ground in low sheets that run along with the wind
//    (ash off burnt grass), and on a windy day a dust devil now and then spins up on
//    bare ground and wanders downwind before it falls apart.
//  * About three days in ten bring passing showers: rain slanting with the wind, rings
//    on the lakes, splashes on the ground; the ground, the grass and the machines
//    darken and take a sheen as they wet (`_SF_Rain`, read by SF_Terrain, SF_Unit and
//    SF_Grass) and dry off slowly after; the sun dims and the air thickens while it
//    rains (Atmosphere).
//
// `-sfweather rain` or `-sfweather clear` forces the showers on or off (seed 1000, the
// benchmark's, is a clear day).
using System.Collections.Generic;
using UnityEngine;
using StarForge.World;

namespace StarForge.View
{
    public sealed partial class FXDirector
    {
        static readonly int RainId = Shader.PropertyToID("_SF_Rain");
        // The shower now (SF_Water roughens the lake with it; _SF_Rain is how wet the
        // ground is, which lags and outlasts it).
        static readonly int ShowerId = Shader.PropertyToID("_SF_Shower");

        struct Devil
        {
            public Vector3 pos;
            public Vector2 drift;
            public float born, life, radius, spin;
        }

        bool weatherReady, rainDay;
        float showerOffset, nextDevil;
        float rainNow, wetNow;   // the shower now, 0..1; how wet the ground is, 0..1
        readonly List<Devil> devils = new List<Devil>(4);
        ParticleSystem rain;
        Atmosphere atmo;
        Light sunLight;
        float sunBase, fogBase, hazeBase, scatterBase;
        Color fogColorBase;

        /// <summary>The shower now, 0..1 (for checks and the HUD).</summary>
        public float Rain => rainNow;
        /// <summary>How wet the ground is now, 0..1.</summary>
        public float Wetness => wetNow;
        public bool RainDay => rainDay;
        public int DevilCount => devils.Count;

        /// <summary>For checks: start a dust devil next frame at <paramref name="at"/>.</summary>
        public void SpinUpDevil(Vector3 at) => devilAt = at;
        Vector3? devilAt;

        void InitWeather()
        {
            weatherReady = true;
            uint seed = world.Map != null ? world.Map.seed : 1000u;
            uint h = Hash(seed);
            rainDay = h % 1000u < 300u;
            showerOffset = (h >> 10) % 1000u * 0.37f;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-sfweather");
            if (i >= 0 && i + 1 < args.Length)
            {
                if (args[i + 1] == "rain") rainDay = true;
                else if (args[i + 1] == "clear") rainDay = false;
            }
            if (ForceRain.HasValue) rainDay = ForceRain.Value;
            nextDevil = Time.time + 20f + (h >> 20) % 30u;   // devils run on Time.time

            atmo = FindAnyObjectByType<Atmosphere>();
            if (atmo != null)
            {
                fogBase = atmo.density;
                hazeBase = atmo.hazeDensity;
                scatterBase = atmo.sunScatterStrength;
                fogColorBase = atmo.fogColor;
                sunLight = atmo.sun != null ? atmo.sun : RenderSettings.sun;
            }
            else sunLight = RenderSettings.sun;
            if (sunLight != null) sunBase = sunLight.intensity;

            if (rainDay)
            {
                // Streaks: the droplets' material, stretched along the fall.
                rain = MakeSystem("Rain", dropletMaterial != null ? dropletMaterial : smokeMaterial, 8000, false, 0f, 1f, 1f, true, 0f);
                // The whole round puff, not one tile of MakeSystem's 4x4 sheet (a cropped
                // tile drew each streak as a faint speck in its corner), as the droplets do.
                SmokeStreams(rain);
                var r = rain.GetComponent<ParticleSystemRenderer>();
                r.velocityScale = 0.06f;
                r.lengthScale = 1f;
                var col = rain.colorOverLifetime;
                col.color = new ParticleSystem.MinMaxGradient(Fade(0.08f, 0.85f));
            }
        }

        /// <summary>For checks only: showers on or off whatever the seed says, and how hard
        /// it is raining now whatever the clock says. Statics that outlive the play
        /// session: whoever sets them clears them (and calls RedrawWeather).</summary>
        public static bool? ForceRain;
        public static float? ForceShower;

        /// <summary>For checks: drop this match's weather; the next frame draws it again
        /// (with ForceRain, if set).</summary>
        public void RedrawWeather() => ClearWeather();

        /// <summary>For checks: rain streaks alive now, and dust sheets lifted so far.</summary>
        public int RainParticles => rain != null ? rain.particleCount : 0;
        public int DustSheets { get; private set; }

        static uint Hash(uint seed)
        {
            uint x = seed * 2654435761u;
            x ^= x >> 15; x *= 0x2c1b3c6du;
            x ^= x >> 12; x *= 0x297a2d39u;
            x ^= x >> 15;
            return x;
        }

        /// <summary>How hard it is raining at match time <paramref name="t"/>: showers of
        /// a minute or three come and go on a rainy day, drawn from the seed.</summary>
        float ShowerAt(float t)
        {
            if (!rainDay) return 0f;
            float s = Mathf.PerlinNoise(t / 150f + showerOffset, 0.37f);
            // At 0.42-0.62 a rain day rained more than half the time: showers, not a wet day.
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.54f, 0.66f, s));
        }

        /// <summary>For checks: the share of the first <paramref name="seconds"/> of a match
        /// it would be raining on this seed if it were a rain day.</summary>
        public float ShowerShare(float seconds)
        {
            int wet = 0, n = 0;
            for (float t = 0f; t < seconds; t += 1f, n++)
                if (Mathf.InverseLerp(0.54f, 0.66f, Mathf.PerlinNoise(t / 150f + showerOffset, 0.37f)) > 0.05f) wet++;
            return n > 0 ? (float)wet / n : 0f;
        }

        void WeatherEffects(float dt, Vector3 focus, Vector3 wind)
        {
            if (!weatherReady) InitWeather();
            float t = world.time;

            // Showers come and go; the ground wets in about twenty seconds of rain and
            // takes a minute and a half to dry.
            rainNow = ForceShower.HasValue ? (rainDay ? ForceShower.Value : 0f) : ShowerAt(t);
            wetNow = Mathf.MoveTowards(wetNow, rainNow, dt * (rainNow > wetNow ? 0.05f : 0.011f));
            Shader.SetGlobalFloat(RainId, wetNow);
            Shader.SetGlobalFloat(ShowerId, rainNow);
            if (atmo != null)
            {
                atmo.density = fogBase * (1f + 1.3f * rainNow);
                atmo.hazeDensity = hazeBase * (1f + 1.6f * rainNow);
                atmo.sunScatterStrength = scatterBase * (1f - 0.6f * rainNow);
                atmo.fogColor = Color.Lerp(fogColorBase, new Color(0.56f, 0.59f, 0.63f), 0.6f * rainNow);
            }
            if (sunLight != null) sunLight.intensity = sunBase * (1f - 0.42f * rainNow);

            float reach = rig != null ? Mathf.Clamp(rig.distance * 0.9f + 18f, 30f, 90f) : 45f;
            if (rain != null && rainNow > 0.01f) Showers(dt, focus, wind, reach);
            BlownDust(dt, focus, wind, reach);
            DustDevils(dt, focus, wind, reach);
        }

        void Showers(float dt, Vector3 focus, Vector3 wind, float reach)
        {
            var map = world.Map;
            // Streaks falling at 11 m/s, slanting with the wind, born far enough up to
            // cross the view and dying where they reach the ground. Never more than the
            // system has room for, and a long frame (8x) does not ask for thousands.
            dt = Mathf.Min(dt, 0.1f);
            float area = reach * reach / (45f * 45f);
            int room = rain.main.maxParticles - rain.particleCount;
            for (int n = Mathf.Min(room, Mathf.FloorToInt(rainNow * 3000f * area * dt + Random.value)); n > 0; n--)
            {
                var d = Random.insideUnitCircle * reach;
                var p = new Vector2(focus.x + d.x, focus.z + d.y);
                float ground = Mathf.Max(map.HeightAt(p), map.waterLevel);
                float up = Random.Range(14f, 30f);
                var v = new Vector3(wind.x * 2.2f, -11f, wind.z * 2.2f);
                Emit(rain, new Vector3(p.x, ground + up, p.y), v, Random.Range(0.035f, 0.055f), up / 11f,
                     new Color(0.74f, 0.79f, 0.85f, 0.2f));
            }
            // Where it lands: rings on the water, a few drops thrown up off the ground.
            for (int n = Mathf.FloorToInt(rainNow * 260f * area * dt + Random.value); n > 0; n--)
            {
                var d = Random.insideUnitCircle * reach * 0.8f;
                var p = new Vector2(focus.x + d.x, focus.z + d.y);
                if (map.WaterDepth(p) > 0.02f)
                {
                    // Rings only near the camera: further out a ring is a few pixels and
                    // the lake turned to a speckle (SF_Water dulls it there instead); and
                    // leave the ring pool room for shell splashes and wakes.
                    if (d.sqrMagnitude < 30f * 30f && rippleList.Count < 300)
                        AddRipple(new Vector3(p.x, map.waterLevel + 0.03f, p.y), Random.Range(0.5f, 1f), Random.Range(0.6f, 0.9f), 0);
                }
                else if (Random.value < 0.5f)
                    Emit(droplets, map.Ground(p) + Vector3.up * 0.05f, Random.insideUnitSphere * 0.5f + Vector3.up * Random.Range(0.8f, 1.5f),
                         Random.Range(0.05f, 0.09f), Random.Range(0.18f, 0.3f), new Color(0.82f, 0.88f, 0.94f, 0.5f));
            }
        }

        /// <summary>In a gust, low sheets of dust run along over dry, open ground.</summary>
        void BlownDust(float dt, Vector3 focus, Vector3 wind, float reach)
        {
            float speed = wind.magnitude;
            float lift = Mathf.Clamp01((speed - 0.55f) / 0.6f) * (1f - wetNow);
            if (lift <= 0.01f) return;
            Vector3 dir = wind / Mathf.Max(speed, 1e-3f);
            for (int n = Mathf.FloorToInt(lift * 22f * dt + Random.value); n > 0; n--)
            {
                var d = Random.insideUnitCircle * reach;
                var at = world.Map.Ground(new Vector2(focus.x + d.x, focus.z + d.y));
                if (world.Map.WaterDepth(new Vector2(at.x, at.z)) > -0.3f) continue;
                var gr = GroundAt(at);
                if (gr.dustAmount < 0.8f) continue;   // meadows and rock hold their dust
                DustSheets++;
                Emit(trail, at + Vector3.up * Random.Range(0.3f, 0.9f), dir * Random.Range(3f, 6f) * speed + Vector3.up * Random.Range(0f, 0.3f),
                     Random.Range(3f, 5.5f), Random.Range(2f, 3.2f),
                     new Color(gr.dust.r, gr.dust.g, gr.dust.b, Random.Range(0.08f, 0.15f) * Mathf.Min(1.3f, gr.dustAmount)), Random.Range(0f, 360f));
            }
        }

        /// <summary>A dust devil on a windy, dry day: a whirling column wandering
        /// downwind over bare ground for a few seconds before it falls apart.</summary>
        void DustDevils(float dt, Vector3 focus, Vector3 wind, float reach)
        {
            float t = Time.time;
            bool forced = devilAt.HasValue;
            if (forced || (Wind.Weather > 0.55f && wetNow < 0.2f && t > nextDevil && devils.Count < 2))
            {
                nextDevil = t + Random.Range(35f, 80f);
                Vector3 at = forced ? devilAt.Value : world.Map.Ground(new Vector2(focus.x, focus.z) + Random.insideUnitCircle * reach * 0.7f);
                devilAt = null;
                if (forced || (world.Map.WaterDepth(new Vector2(at.x, at.z)) < -0.5f && GroundAt(at).dustAmount >= 0.7f))
                    devils.Add(new Devil
                    {
                        pos = at, born = t, life = Random.Range(8f, 14f), radius = Random.Range(1.1f, 1.8f),
                        spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(5f, 8f),
                        drift = new Vector2(wind.x, wind.z) * 2.2f + Random.insideUnitCircle * 0.6f,
                    });
            }
            for (int i = devils.Count - 1; i >= 0; i--)
            {
                var dv = devils[i];
                float age = (t - dv.born) / dv.life;
                if (age >= 1f) { devils.RemoveAt(i); continue; }
                dv.pos += new Vector3(dv.drift.x, 0f, dv.drift.y) * dt;
                dv.pos.y = world.Map.HeightAt(new Vector2(dv.pos.x, dv.pos.z));
                var gr = GroundAt(dv.pos);
                // Dies early over wet ground, a meadow or water: nothing loose to lift.
                if (gr.dustAmount < 0.5f || world.Map.WaterDepth(new Vector2(dv.pos.x, dv.pos.z)) > -0.2f) dv.life = Mathf.Min(dv.life, t - dv.born + 1f);
                devils[i] = dv;
                // Spins up, holds, and falls apart.
                float strength = Mathf.Clamp01(age * 5f) * Mathf.Clamp01((1f - age) * 3f);
                for (int n = Mathf.FloorToInt(strength * 26f * dt + Random.value); n > 0; n--)
                {
                    float h = Random.value * Random.value * 7f;
                    float r = dv.radius * (0.45f + 0.12f * h);
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    var off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var tangent = new Vector3(-off.z, 0f, off.x) * dv.spin * r * 0.35f;
                    Emit(trail, dv.pos + off * r + Vector3.up * (0.2f + h),
                         tangent + Vector3.up * Random.Range(1.2f, 2.4f) + new Vector3(dv.drift.x, 0f, dv.drift.y),
                         Random.Range(0.9f, 1.6f) * (0.7f + 0.1f * h), Random.Range(0.8f, 1.3f),
                         new Color(gr.dust.r, gr.dust.g, gr.dust.b, 0.22f * strength), Random.Range(0f, 360f));
                }
                // Grit and bits of dry grass carried round and up.
                if (debris != null && Random.value < dt * 6f * strength)
                {
                    var c = gr.clod;
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    var off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = dv.pos + off * dv.radius * 0.5f + Vector3.up * 0.3f,
                        velocity = new Vector3(-off.z, 0f, off.x) * dv.spin * 0.6f + Vector3.up * Random.Range(3f, 5f),
                        startSize = Random.Range(0.03f, 0.07f), startLifetime = Random.Range(1f, 1.6f),
                        startColor = new Color(c.r, c.g, c.b, 1f), rotation3D = Random.insideUnitSphere * 180f, applyShapeToPosition = false
                    }, 1);
                }
            }
        }

        /// <summary>The match is over or the director is going: put the sky and the
        /// ground back as they were. The next match draws its own weather.</summary>
        void ClearWeather()
        {
            if (!weatherReady) return;
            weatherReady = false;
            Shader.SetGlobalFloat(RainId, 0f);
            Shader.SetGlobalFloat(ShowerId, 0f);
            if (atmo != null)
            {
                atmo.density = fogBase;
                atmo.hazeDensity = hazeBase;
                atmo.sunScatterStrength = scatterBase;
                atmo.fogColor = fogColorBase;
            }
            if (sunLight != null && sunBase > 0f) sunLight.intensity = sunBase;
            atmo = null;
            sunLight = null;
            if (rain != null) Destroy(rain.gameObject);
            rain = null;
            devils.Clear();
            devilAt = null;
            rainNow = wetNow = 0f;
        }
    }
}
