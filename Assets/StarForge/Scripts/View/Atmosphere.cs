// Atmosphere.cs — height fog, aerial haze, sun scattering and drifting cloud shadows.
//
// This replaces RenderSettings fog, which is a linear fade by distance only.
// The full-screen fog-of-war pass already reconstructs every pixel's world
// position, so it also integrates exponential height fog along the view ray
// (mist pools over the lakes and in the low ground, plateaus stay clear) and
// in-scatters the sun: warm toward it, cool away from it. Cloud shadows are the
// sun's light cookie, scrolled with the wind. It also uploads the match's wind
// (World/Wind) as _SF_Wind, which the trees and the grass sway to.
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
        [Tooltip("Metres per second the cloud shadows drift at the wind's full strength.")]
        public float cloudDrift = 14f;

        static readonly int ColorId = Shader.PropertyToID("_SF_AtmoColor");
        static readonly int ParamsId = Shader.PropertyToID("_SF_AtmoParams");
        static readonly int HazeId = Shader.PropertyToID("_SF_AtmoHaze");
        static readonly int SunId = Shader.PropertyToID("_SF_AtmoSun");
        static readonly int SunDirId = Shader.PropertyToID("_SF_AtmoSunDir");
        // The match's wind, for everything that sways in it (SF_Tree, SF_Grass):
        // xy the heading, z how hard it is blowing, w the gust's travelling phase.
        static readonly int WindId = Shader.PropertyToID("_SF_Wind");

        UniversalAdditionalLightData sunData;
        Vector2 cookieOrigin, cloudOffset;
        float gustPhase;

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
            float dt = Time.deltaTime;
            var w = StarForge.World.Wind.Direction(Time.time);
            float speed = StarForge.World.Wind.Speed(Time.time);
            // The gust rolls across the map faster the harder it blows, so a lull
            // and a gust are told apart by more than the amount of sway.
            gustPhase += dt * (0.5f + 1.4f * speed);
            Shader.SetGlobalVector(WindId, new Vector4(w.x, w.y, speed, gustPhase));
            // The clouds go with it.
            cloudOffset += w * (cloudDrift * speed * dt);
            if (sunData != null) sunData.lightCookieOffset = cookieOrigin + cloudOffset;
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
