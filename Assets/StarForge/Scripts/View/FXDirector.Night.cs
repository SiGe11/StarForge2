// FXDirector.Night.cs -- how a night map looks (World/GameWorld.Night has which maps and when).
// Presentation only: nothing here writes to the world or a Unit. Values it moves are saved when the
// night begins and put back when the match or the director stops (ClearNight): the sun as a moon,
// the ambient, a clone of the sky, the reflection, the Atmosphere's colours (the weather's own write
// takes the night's gains: FXDirector.Weather), a runtime Volume blended by the night's level, the
// fog of war's remembered ground (_SF_Night), the lamps at each structure and their lights, the
// works' bright glows in the Quenching (_SF_LampsOut, SF_Unit), and the field celebrants. No new
// shader keyword: two plain globals and a colour grading the LUT already bakes.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed partial class FXDirector
    {
        static readonly int NightId = Shader.PropertyToID("_SF_Night");
        static readonly int LampsOutId = Shader.PropertyToID("_SF_LampsOut");

        // The look at full night (k = 1); tuned on the Editor's and the player's look shots.
        static readonly Color MoonColor = new Color(0.62f, 0.72f, 1.0f);
        const float NightSunGain = 0.38f;
        static readonly Color NightSky = new Color(0.20f, 0.26f, 0.42f), NightEquator = new Color(0.12f, 0.13f, 0.19f), NightGround = new Color(0.05f, 0.05f, 0.06f);
        const float NightAmbientGain = 0.55f;
        const float NightPostExposure = 1.3f;
        /// <summary>For NightTrials.Look's sweep: EV added to <see cref="NightPostExposure"/>. ClearNight
        /// puts it back to 0 (so does the trial).</summary>
        public static float NightExposureNudge;
        static readonly Color NightFog = new Color(0.08f, 0.10f, 0.16f), MoonScatter = new Color(0.55f, 0.65f, 0.95f);
        const float NightReflection = 0.25f, NightSkyExposure = 0.10f;
        static readonly Color NightSkyTint = new Color(0.10f, 0.14f, 0.26f);
        /// <summary>Lamp lights: the nearest this many lit lamps to the camera's focus.</summary>
        const int LampLightCount = 6;
        const int MaxLamps = 240;   // structures' lamps, the soldiers' mast lights, the celebrants' hand-lamps

        /// <summary>The night's level now, 0..1: read by the weather's write of the sky (0 by day,
        /// which leaves every value it writes exactly as before).</summary>
        float nightK;
        bool nightReady;
        NightPlan nightPlan;
        Light nightSun;
        Color sunColorBase, ambSkyBase, ambEqBase, ambGroundBase, scatterColorBase;
        float reflectionBase;
        Material skyBase, skyNight;
        float skyExposureBase;
        Color skyTintBase;
        GameObject nightVolumeGo;
        Volume nightVolume;
        VolumeProfile nightProfile;
        ColorAdjustments nightCA;
        readonly List<(Vector2 at, int team)> hallsNow = new List<(Vector2, int)>(8);
        ParticleSystem lamps;
        ParticleSystem.Particle[] lampBuf;
        readonly Light[] lampLights = new Light[LampLightCount];
        readonly Dictionary<int, float> lampLitAt = new Dictionary<int, float>(64);
        readonly List<(Vector3 at, float lit, float radius)> lampsNow = new List<(Vector3, float, float)>(MaxLamps);
        readonly GameObject[] celebrants = new GameObject[2];
        GameObject celebrantPrefab, celebrantHold;
        bool celebrantLooked;

        /// <summary>For checks: the night's level now and the lamps drawn last frame.</summary>
        public float NightLevel => nightK;
        public int LampsLit { get; private set; }
        /// <summary>For checks: structures' lamps drawn (of them the Foundries', the hall-lamps), soldiers
        /// showing mast lights near the focus, deserters near the focus (their masts dark).</summary>
        public int StructureLamps { get; private set; }
        public int HallLamps { get; private set; }
        /// <summary>For checks: what the Lamps system holds now (what it draws), against what was set.</summary>
        public int LampParticles => lamps != null ? lamps.particleCount : -1;
        public int MastsLit { get; private set; }
        public int DesertersDark { get; private set; }
        public int CelebrantsShown { get; private set; }

        void NightEffects(float dt)
        {
            var plan = world.Night;
            if (nightReady && (plan.night != nightPlan.night || plan.quenching != nightPlan.quenching || plan.darkeningAt != nightPlan.darkeningAt))
                ClearNight();
            if (!plan.night) { nightK = 0f; return; }
            if (!nightReady) InitNight(plan);
            float t = world.time;
            nightK = plan.Level(t);
            float k = nightK;

            if (nightSun != null) nightSun.color = Color.Lerp(sunColorBase, MoonColor, k);
            float amb = Mathf.Lerp(1f, NightAmbientGain, k);
            RenderSettings.ambientSkyColor = Color.Lerp(ambSkyBase, NightSky, k) * amb;
            RenderSettings.ambientEquatorColor = Color.Lerp(ambEqBase, NightEquator, k) * amb;
            RenderSettings.ambientGroundColor = Color.Lerp(ambGroundBase, NightGround, k) * amb;
            RenderSettings.reflectionIntensity = Mathf.Lerp(reflectionBase, NightReflection, k);
            if (skyNight != null)
            {
                skyNight.SetFloat("_Exposure", Mathf.Lerp(skyExposureBase, NightSkyExposure, k));
                skyNight.SetColor("_SkyTint", Color.Lerp(skyTintBase, NightSkyTint, k));
            }
            var atmoNow = FindAtmosphere();
            if (atmoNow != null) atmoNow.sunScatter = Color.Lerp(scatterColorBase, MoonScatter, k);
            if (nightVolume != null) nightVolume.weight = k;
            float ev = NightPostExposure + NightExposureNudge;
            if (nightCA != null && nightCA.postExposure.value != ev) nightCA.postExposure.value = ev;
            Shader.SetGlobalFloat(NightId, k);
            Shader.SetGlobalFloat(LampsOutId, plan.LampsOut(t));

            Lamps(plan, t, k);
            Celebrants(plan, t);
        }

        Atmosphere nightAtmo;
        Atmosphere FindAtmosphere()
        {
            if (nightAtmo == null) nightAtmo = FindAnyObjectByType<Atmosphere>();
            return nightAtmo;
        }

        void InitNight(NightPlan plan)
        {
            nightReady = true;
            nightPlan = plan;
            var atmoNow = FindAtmosphere();
            nightSun = atmoNow != null && atmoNow.sun != null ? atmoNow.sun : RenderSettings.sun;
            if (nightSun != null) sunColorBase = nightSun.color;
            ambSkyBase = RenderSettings.ambientSkyColor;
            ambEqBase = RenderSettings.ambientEquatorColor;
            ambGroundBase = RenderSettings.ambientGroundColor;
            reflectionBase = RenderSettings.reflectionIntensity;
            if (atmoNow != null) scatterColorBase = atmoNow.sunScatter;
            // A clone of the sky: editing the scene's material in play would write the asset.
            skyBase = RenderSettings.skybox;
            if (skyBase != null && skyBase.HasProperty("_Exposure"))
            {
                skyNight = new Material(skyBase) { name = skyBase.name + " (night)" };
                skyExposureBase = skyBase.GetFloat("_Exposure");
                skyTintBase = skyBase.HasProperty("_SkyTint") ? skyBase.GetColor("_SkyTint") : Color.grey;
                RenderSettings.skybox = skyNight;
            }
            // The grade on top of the scene's (its Volume's profile is an asset: this one is made here).
            nightProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            // Add<T>(false): only what is set here is overridden (true would override every parameter at
            // Unity's default: bloom's clamp, the lens dirt, the vignette's smoothness).
            var ca = nightProfile.Add<ColorAdjustments>(false);
            nightCA = ca;
            ca.postExposure.Override(NightPostExposure + NightExposureNudge);
            ca.contrast.Override(18f);
            ca.saturation.Override(-28f);
            ca.colorFilter.Override(new Color(0.80f, 0.87f, 1.0f));
            nightProfile.Add<WhiteBalance>(false).temperature.Override(-18f);
            nightProfile.Add<Bloom>(false).intensity.Override(1.15f);
            nightProfile.Add<Vignette>(false).intensity.Override(0.32f);
            nightVolumeGo = new GameObject("NightVolume");
            nightVolumeGo.transform.SetParent(transform, false);
            var vol = nightVolume = nightVolumeGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.weight = 0f;
            vol.sharedProfile = nightProfile;

            if (lamps == null)
            {
                // The same set-up as the glow system's, a system of its own whose particles are set each frame.
                lamps = MakeSystem("Lamps", glowMaterial, MaxLamps, false, 4f / 16f, 1f, 0.4f, false, 0f);
                lampBuf = new ParticleSystem.Particle[MaxLamps];
            }
            // MakeSystem plays it (emission 0); kept playing here in case anything ever stops it.
            if (!lamps.isPlaying) lamps.Play();
            for (int i = 0; i < lampLights.Length; i++)
            {
                if (lampLights[i] != null) continue;
                var go = new GameObject("LampLight" + i);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = new Color(1f, 0.62f, 0.32f);
                l.enabled = false;
                lampLights[i] = l;
            }
            lampLitAt.Clear();
        }

        /// <summary>The Taking of the Light (lore section 4): every lamp but the hall-lamps (the Foundries')
        /// goes out one by one over about 23 s; the field waits in the dark; from 40 s the flame is passed out
        /// lamp to lamp, nearest the hall-lamp first, the last about 80 s in. Presentation only.</summary>
        static float LiturgyLit(float at, float t, int id, float fromHall)
        {
            float o = LampOrder(id);
            float outAt = at + 22f * o;
            float relight = at + 40f + Mathf.Min(36f, fromHall * 0.6f) + 2f * o;
            if (t < outAt) return 1f;
            if (t < relight) return 1f - Mathf.Clamp01((t - outAt) / 1.2f);
            return Mathf.Clamp01((t - relight) / 1.2f);
        }

        /// <summary>A structure's lamp: lit one by one after vespers (a structure finished later, 3 s
        /// after it stands), out one by one in the Quenching by the same order, the Foundries last.</summary>
        static float LampOrder(int id) => (NightRules.Hash((uint)id ^ 0x1A3Bu) % 1000u) / 1000f;

        void Lamps(NightPlan plan, float t, float k)
        {
            lampsNow.Clear();
            int n = 0, hall = 0;
            bool taking = plan.liturgy && t >= plan.liturgyAt && t < plan.liturgyAt + 90f;
            hallsNow.Clear();
            if (taking)
                foreach (var u in world.units)
                    if (u != null && !u.dying && u.Type == UnitType.Foundry && u.Complete && u.team <= 1) hallsNow.Add((u.pos, u.team));
            if (t >= NightPlan.VespersAt)
                foreach (var u in world.units)
                {
                    if (u == null || u.dying || !u.def.building || !u.Complete || u.team > 1 || u.def.neutral) continue;
                    if (!lampLitAt.TryGetValue(u.id, out float litAt))
                    {
                        // One by one after vespers; standing when a night comes on late (a forced one) or
                        // finished later, within 3 s (late, every one waited 3 s, and Look read "lamps 0").
                        litAt = t <= NightPlan.VespersAt + 55f ? Mathf.Max(t + 3f, NightPlan.VespersAt + 1f + 50f * LampOrder(u.id))
                                                               : t + 3f * LampOrder(u.id);
                        lampLitAt[u.id] = litAt;
                    }
                    float lit = Mathf.Clamp01((t - litAt) / 1.5f);
                    if (plan.quenching && t >= plan.darkeningAt)
                    {
                        float outAt = plan.darkeningAt + (u.Type == UnitType.Foundry ? 52f + 7f * LampOrder(u.id) : 50f * LampOrder(u.id));
                        lit *= 1f - Mathf.Clamp01((t - outAt) / 1.2f);
                    }
                    if (taking && u.Type != UnitType.Foundry)
                    {
                        float near = float.MaxValue;
                        foreach (var (hAt, hTeam) in hallsNow)
                            if (hTeam == u.team) near = Mathf.Min(near, (hAt - u.pos).magnitude);
                        lit *= LiturgyLit(plan.liturgyAt, t, u.id, near);
                    }
                    if (lit <= 0.001f || k < 0.05f) continue;
                    if (!(u.team == (player != null ? player.team : 0) || u.visibleToPlayer || MatchSettings.spectate)) continue;
                    var fwd = new Vector3(Mathf.Sin(u.yaw), 0f, Mathf.Cos(u.yaw));
                    Vector3 at = u.Ground + fwd * (u.def.radius + 0.35f) + Vector3.up * 1.6f;
                    float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 3f + u.id * 0.37f, u.id * 0.11f);
                    float shown = lit * k * flicker;
                    lampsNow.Add((at, shown, u.def.radius));
                    if (n < MaxLamps)
                    {
                        if (u.Type == UnitType.Foundry) hall++;
                        ref var p = ref lampBuf[n++];
                        p.position = at;
                        p.startSize = 1.2f;
                        p.startColor = new Color(1f, 0.6f, 0.28f, 0.55f * shown);
                        p.startLifetime = 2f;
                        p.remainingLifetime = 1f;
                        p.randomSeed = (uint)u.id;
                        p.rotation = 0f;
                        p.velocity = Vector3.zero;
                    }
                }
            StructureLamps = n;
            HallLamps = hall;
            // The field net's masts (lore section 6: two on the iron's back): on a night map a soldier near the
            // camera shows two faint lights at their tips; a deserter cut their leads first ("gone quiet") and
            // shows none. Presentation only.
            MastsLit = DesertersDark = 0;
            if (k > 0.3f)
            {
                Vector2 f2 = rig != null ? rig.Focus : Vector2.zero;
                int me = player != null ? player.team : 0;
                foreach (var u in world.units)
                {
                    if (n >= MaxLamps - 4) break;
                    if (u == null || u.dying || u.def.bodyRadius <= 0f || u.team > 1) continue;
                    if ((u.pos - f2).sqrMagnitude > 45f * 45f) continue;
                    if (!(u.team == me || u.visibleToPlayer || MatchSettings.spectate)) continue;
                    if (u.deserted) { DesertersDark++; continue; }
                    var fwd = new Vector3(Mathf.Sin(u.yaw), 0f, Mathf.Cos(u.yaw));
                    var side = new Vector3(fwd.z, 0f, -fwd.x);
                    float h = u.posture == Posture.Prone ? 0.55f : u.posture == Posture.Crouch ? 1.45f : 1.95f;
                    Vector3 back = u.Ground - fwd * 0.30f + Vector3.up * h;
                    for (int s2 = 0; s2 < 2; s2++)
                    {
                        ref var p = ref lampBuf[n++];
                        p.position = back + side * (s2 == 0 ? -0.12f : 0.12f);
                        p.startSize = 0.16f;
                        p.startColor = new Color(0.45f, 1f, 0.75f, 0.5f * k);
                        p.startLifetime = 2f;
                        p.remainingLifetime = 1f;
                        p.randomSeed = (uint)(u.id * 2 + s2);
                        p.rotation = 0f;
                        p.velocity = Vector3.zero;
                    }
                    MastsLit++;
                }
            }
            // The celebrant's hand-lamp: out near the end of the darkening, lit again by no one until lauds.
            for (int s = 0; s < 2; s++)
            {
                var c = celebrants[s];
                if (c == null || !c.activeSelf || n >= MaxLamps) continue;
                float lit = plan.quenching && t >= plan.darkeningAt ? 1f - Mathf.Clamp01((t - plan.darkeningAt - 56f) / 1.2f) : 1f;
                if (lit <= 0.001f) continue;
                Vector3 at = c.transform.position + c.transform.forward * 0.31f + Vector3.up * 1.39f;   // the flame of the lamp held before her chest (build_celebrant.py)
                ref var p = ref lampBuf[n++];
                p.position = at;
                p.startSize = 0.6f;
                p.startColor = new Color(1f, 0.66f, 0.32f, 0.6f * lit * k);
                p.startLifetime = 2f;
                p.remainingLifetime = 1f;
                p.randomSeed = 77u + (uint)s;
                p.velocity = Vector3.zero;
            }
            LampsLit = n;
            if (lamps != null) lamps.SetParticles(lampBuf, n);

            Vector2 focus = rig != null ? rig.Focus : Vector2.zero;
            for (int i = 0; i < lampLights.Length; i++)
            {
                int best = -1;
                float bestScore = 0f;
                for (int j = 0; j < lampsNow.Count; j++)
                {
                    var (at, shown, _) = lampsNow[j];
                    if (shown <= 0f) continue;
                    float d2 = (at.x - focus.x) * (at.x - focus.x) + (at.z - focus.y) * (at.z - focus.y);
                    float score = shown / (1f + d2 / 900f);
                    if (score > bestScore) { bestScore = score; best = j; }
                }
                var l = lampLights[i];
                if (l == null) continue;
                if (best < 0) { l.enabled = false; continue; }
                var (pos, sh, r) = lampsNow[best];
                lampsNow[best] = (pos, -1f, r);
                l.transform.position = pos;
                l.intensity = 2.2f * sh;
                l.range = 7f + r;
                l.enabled = true;
            }
        }

        /// <summary>The field celebrants (lore section 4: a white alb, a gold cord, a veil to the waist,
        /// no iron and no weapon): one at each base from vespers, gone at lauds after a Quenching. A
        /// GameObject of the view's, no Unit: nothing can target or count her. Shown where the player
        /// sees the ground. Without a model in Resources/Celebrant nothing is drawn.</summary>
        void Celebrants(NightPlan plan, float t)
        {
            if (!celebrantLooked) { celebrantLooked = true; celebrantPrefab = Resources.Load<GameObject>("Celebrant"); }
            CelebrantsShown = 0;
            if (celebrantPrefab == null) return;
            bool on = t >= NightPlan.VespersAt - 2f && !(plan.quenching && t >= plan.LaudsAt);
            for (int s = 0; s < 2; s++)
            {
                if (!on) { if (celebrants[s] != null) celebrants[s].SetActive(false); continue; }
                if (celebrants[s] == null)
                {
                    Vector2 home = world.Map.StartPos(s);
                    Vector2 mid = new Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
                    Vector2 dir = (mid - home).sqrMagnitude > 1f ? (mid - home).normalized : Vector2.up;
                    Vector2 p2 = home + dir * 9f;
                    Vector3 p = new Vector3(p2.x, 0f, p2.y);
                    var terrain = Terrain.activeTerrain;
                    if (terrain != null) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                    if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) p = hit.position;
                    // Made under an inactive holder, so nothing on her wakes before it is stripped: no collider,
                    // obstacle or agent (she is no unit: nothing of the sim's).
                    if (celebrantHold == null) { celebrantHold = new GameObject("CelebrantHold"); celebrantHold.transform.SetParent(transform, false); celebrantHold.SetActive(false); }
                    var go = Instantiate(celebrantPrefab, p, Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)), celebrantHold.transform);
                    go.name = "Celebrant" + s;
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                    foreach (var c in go.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true)) DestroyImmediate(c);
                    foreach (var c in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) DestroyImmediate(c);
                    go.transform.SetParent(transform, true);
                    celebrants[s] = go;
                }
                bool seen = Seen(celebrants[s].transform.position);
                if (celebrants[s].activeSelf != seen) celebrants[s].SetActive(seen);
                if (seen) CelebrantsShown++;
            }
        }

        /// <summary>Day again: every value put back as it was, the made things destroyed.</summary>
        void ClearNight()
        {
            nightK = 0f;
            NightExposureNudge = 0f;
            Shader.SetGlobalFloat(NightId, 0f);
            Shader.SetGlobalFloat(LampsOutId, 0f);
            if (!nightReady) return;
            nightReady = false;
            if (nightSun != null) nightSun.color = sunColorBase;
            RenderSettings.ambientSkyColor = ambSkyBase;
            RenderSettings.ambientEquatorColor = ambEqBase;
            RenderSettings.ambientGroundColor = ambGroundBase;
            RenderSettings.reflectionIntensity = reflectionBase;
            if (skyNight != null)
            {
                if (RenderSettings.skybox == skyNight) RenderSettings.skybox = skyBase;
                Destroy(skyNight);
                skyNight = null;
            }
            var atmoNow = FindAtmosphere();
            if (atmoNow != null) atmoNow.sunScatter = scatterColorBase;
            if (nightVolumeGo != null) Destroy(nightVolumeGo);
            nightVolumeGo = null;
            nightVolume = null;
            if (nightProfile != null)
            {
                foreach (var c in nightProfile.components) if (c != null) Destroy(c);   // made with the profile: not freed with it
                Destroy(nightProfile);
            }
            nightProfile = null;
            if (lamps != null) lamps.Clear();
            foreach (var l in lampLights) if (l != null) l.enabled = false;
            for (int s = 0; s < 2; s++) { if (celebrants[s] != null) Destroy(celebrants[s]); celebrants[s] = null; }
            lampLitAt.Clear();
            LampsLit = CelebrantsShown = 0;
        }
    }
}
