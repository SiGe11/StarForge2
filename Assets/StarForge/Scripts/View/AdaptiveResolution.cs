// AdaptiveResolution.cs — trades render scale for frame rate under sustained
// load (thermal throttling on a fanless laptop), with FSR upscaling keeping the
// Retina panel sharp.
//
// Two lessons from the original's controller, kept here: a step up multiplies
// the pixel count by (new/old)^2, so the test is whether the *target* scale
// fits the budget; and against something bursty sharing the GPU, a naive
// controller climbs in every quiet spell and drops in every busy one forever,
// so each undone climb doubles the clean run the next one must see.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarForge.View
{
    public sealed class AdaptiveResolution : MonoBehaviour
    {
        public float targetMs = 16.9f;
        public float minScale = 0.7f;
        public float maxScale = 1f;
        public float step = 0.1f;

        UniversalRenderPipelineAsset urp;
        float originalScale = 1f;
        float scale = 1f;
        float avgMs = 16f;
        float overTime, cleanTime;
        float requiredClean = 4f;
        float lastClimbT = -100f;

        public float Scale => scale;
        /// <summary>For the benchmark's report: how often the resolution changed (each one
        /// can be seen) and how long a clean spell the next climb must now wait for.</summary>
        public int Changes { get; private set; }
        public float RequiredClean => requiredClean;

        void OnEnable()
        {
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) { originalScale = urp.renderScale; scale = urp.renderScale; }
        }

        void OnDisable()
        {
            // The pipeline asset is shared with the editor; leave it as we found it.
            if (urp != null) urp.renderScale = originalScale;
        }

        void Update()
        {
            if (urp == null) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > 250f) return;   // hitches (loading, focus changes) are not load
            if (QualitySettings.vSyncCount > 0) { SyncedUpdate(ms); return; }
            avgMs = Mathf.Lerp(avgMs, ms, 0.05f);

            if (avgMs > targetMs + 1.5f)
            {
                overTime += Time.unscaledDeltaTime;
                cleanTime = 0f;
                if (overTime > 1.5f && scale > minScale + 1e-3f)
                {
                    // Dropping soon after a climb means the climb did not fit: back off harder.
                    if (Time.unscaledTime - lastClimbT < 6f) requiredClean = Mathf.Min(120f, requiredClean * 2f);
                    Set(scale - step);
                    overTime = 0f;
                }
            }
            else
            {
                overTime = 0f;
                float next = Mathf.Min(maxScale, scale + step);
                float predicted = avgMs * (next * next) / (scale * scale);
                if (next > scale + 1e-3f && predicted < targetMs - 1f)
                {
                    cleanTime += Time.unscaledDeltaTime;
                    if (cleanTime > requiredClean)
                    {
                        Set(next);
                        lastClimbT = Time.unscaledTime;
                        cleanTime = 0f;
                    }
                }
                else cleanTime = 0f;
            }
        }

        // With vsync (a match always has it) a frame's length
        // cannot fall below 16.7 ms at 60 Hz however light the load, so the rule above
        // could never see room to climb, and one heavy fight left the picture at the
        // floor for the rest of the match. Here the load is how often a frame runs past one
        // and a half refreshes, 25 ms at 60 Hz: past missHigh (at 60 Hz, a share m missed is 60/(1+m) fps, so 60% is 37.5
        // and 71% is 35) the scale comes down; after a clean spell under missLow it tries a
        // step up, and a climb that is undone soon after makes the next one wait twice as
        // long, as above. The share settles well under missHigh (near 44% at 0.60), and the
        // scale can climb in a fight only if missLow is above that fight's share. The preset
        // sets both (QualityController).
        public float missHigh = 0.15f, missLow = 0.04f;
        float missRate;

        void SyncedUpdate(float ms)
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            float refresh = hz > 1.0 ? (float)(1000.0 / hz) : 16.67f;
            float missed = ms > refresh * 1.5f ? 1f : 0f;
            missRate = Mathf.Lerp(missRate, missed, 0.03f);
            if (missRate > missHigh)
            {
                overTime += Time.unscaledDeltaTime;
                cleanTime = 0f;
                if (overTime > 1.5f && scale > minScale + 1e-3f)
                {
                    if (Time.unscaledTime - lastClimbT < 6f) requiredClean = Mathf.Min(120f, requiredClean * 2f);
                    Set(scale - step);
                    overTime = 0f;
                    // Judge the new scale on its own frames. On High this start, 0.30, is under
                    // missLow, so the clean timer runs at once: a real share above missLow
                    // crosses back in about a second, against the 4 s a climb needs.
                    missRate = missHigh * 0.5f;
                }
            }
            else
            {
                overTime = 0f;
                if (missRate < missLow && scale < maxScale - 1e-3f)
                {
                    cleanTime += Time.unscaledDeltaTime;
                    if (cleanTime > requiredClean)
                    {
                        Set(scale + step);
                        lastClimbT = Time.unscaledTime;
                        cleanTime = 0f;
                    }
                }
                else cleanTime = 0f;
            }
        }

        void Set(float s)
        {
            float next = Mathf.Clamp(Mathf.Round(s * 100f) / 100f, minScale, maxScale);
            if (!Mathf.Approximately(next, scale)) Changes++;
            scale = next;
            urp.renderScale = scale;
        }
    }
}
