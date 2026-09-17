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
                case SFQuality.High: return "Full shadow distance and ground cover. Best plugged in.";
                case SFQuality.Balanced: return "Shorter shadows, thinner grass. Holds 60 Hz with more headroom.";
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
            float shadows, minScale, maxScale, density, clutterDistance;
            int samples;
            switch (Current)
            {
                case SFQuality.Balanced:
                    shadows = 120f; samples = 2; minScale = 0.6f; maxScale = 1f; density = 0.7f; clutterDistance = 150f; break;
                case SFQuality.Battery:
                    shadows = 90f; samples = 1; minScale = 0.5f; maxScale = 0.85f; density = 0.45f; clutterDistance = 110f; break;
                default:
                    shadows = 150f; samples = 2; minScale = 0.7f; maxScale = 1f; density = 1f; clutterDistance = 185f; break;
            }
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
            }
            // Grass is grown once, in GroundScatter.Start; this component runs first,
            // so a preset chosen before the match decides how much there is.
            var scatter = FindAnyObjectByType<GroundScatter>();
            if (scatter != null)
            {
                scatter.density = density;
                scatter.drawDistance = clutterDistance;
            }
        }
    }
}
