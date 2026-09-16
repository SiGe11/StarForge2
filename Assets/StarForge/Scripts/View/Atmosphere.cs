// Atmosphere.cs — height fog, aerial haze, sun scattering and drifting cloud shadows.
//
// This replaces RenderSettings fog, which is a linear fade by distance only.
// The full-screen fog-of-war pass already reconstructs every pixel's world
// position, so it also integrates exponential height fog along the view ray
// (mist pools over the lakes and in the low ground, plateaus stay clear) and
// in-scatters the sun: warm toward it, cool away from it. Cloud shadows are the
// sun's light cookie, scrolled with the wind.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StarForge.View
{
    [ExecuteAlways]
    public sealed class Atmosphere : MonoBehaviour
    {
        [Header("Height fog")]
        public Color fogColor = new Color(0.60f, 0.67f, 0.76f);
        [Tooltip("Extinction per metre at the base height.")]
        public float density = 0.02f;
        [Tooltip("World height the fog is densest at (the waterline).")]
        public float baseHeight = 2.4f;
        [Tooltip("Per metre: how quickly the fog thins with height.")]
        public float heightFalloff = 0.22f;

        [Header("Distance haze")]
        public float hazeStart = 130f;
        public float hazeDensity = 0.0032f;

        [Header("Sun scattering")]
        public Color sunScatter = new Color(1f, 0.74f, 0.46f);
        [Range(0f, 2f)] public float sunScatterStrength = 0.7f;

        [Header("Cloud shadows")]
        public Light sun;
        [Tooltip("Metres per second the cloud shadows drift.")]
        public Vector2 wind = new Vector2(1.4f, 0.8f);

        static readonly int ColorId = Shader.PropertyToID("_SF_AtmoColor");
        static readonly int ParamsId = Shader.PropertyToID("_SF_AtmoParams");
        static readonly int HazeId = Shader.PropertyToID("_SF_AtmoHaze");
        static readonly int SunId = Shader.PropertyToID("_SF_AtmoSun");
        static readonly int SunDirId = Shader.PropertyToID("_SF_AtmoSunDir");

        UniversalAdditionalLightData sunData;
        Vector2 cookieOrigin;

        void OnEnable()
        {
            if (sun != null && Application.isPlaying)
            {
                sunData = sun.GetComponent<UniversalAdditionalLightData>();
                if (sunData != null) cookieOrigin = sunData.lightCookieOffset;
            }
            Apply();
        }

        void OnDisable()
        {
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            // The offset is serialized on the light; leave the scene as it was.
            if (sunData != null) sunData.lightCookieOffset = cookieOrigin;
        }

        void OnValidate() => Apply();

        void Update()
        {
            Apply();
            if (sunData != null) sunData.lightCookieOffset = cookieOrigin + wind * Time.time;
        }

        void Apply()
        {
            Color f = fogColor.linear, s = sunScatter.linear;
            Shader.SetGlobalVector(ColorId, new Vector4(f.r, f.g, f.b, 1f));
            Shader.SetGlobalVector(ParamsId, new Vector4(density, heightFalloff, baseHeight, isActiveAndEnabled ? 1f : 0f));
            Shader.SetGlobalVector(HazeId, new Vector4(hazeStart, hazeDensity, 0f, 0f));
            Shader.SetGlobalVector(SunId, new Vector4(s.r, s.g, s.b, sunScatterStrength));
            var light = sun != null ? sun : RenderSettings.sun;
            if (light != null) Shader.SetGlobalVector(SunDirId, -light.transform.forward);
        }
    }
}
