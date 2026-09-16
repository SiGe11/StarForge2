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

        void Set(float s)
        {
            scale = Mathf.Clamp(Mathf.Round(s * 100f) / 100f, minScale, maxScale);
            urp.renderScale = scale;
        }
    }
}
