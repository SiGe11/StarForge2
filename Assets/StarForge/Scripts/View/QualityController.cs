// QualityController.cs — three graphics presets, picked on the title screen and
// remembered between runs.
//
// The MacBook Neo is fanless: the same settings that hold 60 Hz plugged in run
// hot and drain the battery on a long match. Rather than a page of sliders,
// this trades the three things that actually cost: how far shadows are drawn,
// how much ground clutter is grown, and how far the adaptive resolution
// controller is allowed to drop before it gives frames back.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarForge.View
{
    public enum SFQuality { High, Balanced, Battery }

    [DefaultExecutionOrder(-60)]
    public sealed class QualityController : MonoBehaviour
    {
        const string Key = "sf_quality";

        public static QualityController Instance { get; private set; }

        /// <summary>A preset for this run only (benchmark -sfquality); never saved.</summary>
        public static SFQuality? Override;
        /// <summary>Benchmark experiments only (-sfshadows, -sfdensity): replace the
        /// preset's shadow distance or ground-cover density for this run.</summary>
        public static float ShadowOverride = -1f, DensityOverride = -1f, MinScaleOverride = -1f;

        public static SFQuality Current
        {
            get => Override ?? (SFQuality)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)SFQuality.High), 0, 2);
            set
            {
                Override = null;
                PlayerPrefs.SetInt(Key, (int)value);
                PlayerPrefs.Save();
                if (Instance != null) Instance.Apply();
            }
        }

        public static string Blurb(SFQuality q)
        {
            switch (q)
            {
                case SFQuality.High: return "Full shadow distance and ground cover at the sharpest resolution it can hold above 45 fps, even in the heaviest Mech fights. Best plugged in.";
                case SFQuality.Balanced: return "Shorter shadows and thinner grass: a few frames more than High in a big fight, and a little cooler.";
                default: return "Shadows close in, clutter thins out, resolution drops sooner. Coolest and longest on battery.";
            }
        }

        UniversalRenderPipelineAsset urp;
        float shadowDistance;
        int msaa;

        void Awake()
        {
            Instance = this;
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                shadowDistance = urp.shadowDistance;
                msaa = urp.msaaSampleCount;
            }
            Apply();
        }

        void OnDestroy()
        {
            // The pipeline asset is shared with the editor; leave it as we found it.
            if (urp != null)
            {
                urp.shadowDistance = shadowDistance;
                urp.msaaSampleCount = msaa;
            }
            if (Instance == this) Instance = null;
        }

        public void Apply()
        {
            // targetMs is the frame time adaptive resolution holds. It has to be one display
            // refresh (60 Hz here), not a frame rate in between: a match runs with vsync on,
            // so a frame that misses 16.7 ms waits for 33.3. High once aimed at 21.5 ms
            // ("about 46 fps", measured uncapped); in play that pinned the render scale at its
            // floor with every other frame at 30 fps, averaging 39. Missing a refresh now
            // lowers the resolution instead.
            float shadows, minScale, maxScale, density, clutterDistance, targetMs;
            // How many frames may miss a refresh before the resolution comes down: High
            // lets it go to ~28% (about 47 fps -- 45 is enough here, the user's call) and
            // spends the rest on pixels, shadow and grass; Balanced holds ~52 fps.
            float missHigh = 0.15f, missLow = 0.04f;
            int samples;
            switch (Current)
            {
                case SFQuality.Balanced:
                    shadows = 95f; samples = 2; minScale = 0.6f; maxScale = 1f; density = 0.6f; clutterDistance = 140f; targetMs = 16.9f; break;
                case SFQuality.Battery:
                    shadows = 90f; samples = 1; minScale = 0.5f; maxScale = 0.85f; density = 0.45f; clutterDistance = 110f; targetMs = 16.9f; break;
                default:
                    // The shadow and grass High had before the Mechs (150 m, full), with the
                    // resolution allowed down to 0.55 in the heaviest fights and let miss up to
                    // ~28% of refreshes (about 47 fps) before it drops. The floor was 0.60 until
                    // the Mechs were fitted out for the fight at the drop: rotary cannons and
                    // missile racks against the benchmark's armies brought the frames with a Mech
                    // in view to 43.4-46.0 fps, with the render scale at that floor.
                    shadows = 150f; samples = 2; minScale = 0.55f; maxScale = 1f; density = 1f; clutterDistance = 185f; targetMs = 16.9f;
                    missHigh = 0.28f; missLow = 0.10f; break;
            }
            if (ShadowOverride > 0f) shadows = ShadowOverride;
            if (DensityOverride > 0f) density = DensityOverride;
            if (MinScaleOverride > 0f) minScale = MinScaleOverride;
            if (urp != null)
            {
                urp.shadowDistance = shadows;
                urp.msaaSampleCount = samples;
            }
            var adaptive = FindAnyObjectByType<AdaptiveResolution>();
            if (adaptive != null)
            {
                adaptive.minScale = minScale;
                adaptive.maxScale = maxScale;
                adaptive.targetMs = targetMs;
                adaptive.missHigh = missHigh;
                adaptive.missLow = missLow;
            }
            // Grass is grown in GroundScatter.Start, after this component's Awake has set
            // the saved preset's density. A preset picked on the title screen comes later,
            // so the cover is grown again: set and left, the new density waited for the
            // next scene load, and the match played on the old preset's grass.
            var scatter = FindAnyObjectByType<GroundScatter>();
            if (scatter != null)
            {
                bool regrow = !Mathf.Approximately(scatter.density, density);
                scatter.density = density;
                scatter.drawDistance = clutterDistance;
                if (regrow) scatter.Regrow();
            }
        }
    }
}
