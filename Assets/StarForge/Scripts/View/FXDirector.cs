// FXDirector.cs — turns game events into effects, and draws the instanced
// overlays (selection rings, placement footprint, order markers, scorch marks,
// health bars, projectile streaks). Nothing here changes the game.
//
// Effects are cut outside the player's vision: an explosion is live
// information, so fog of war has to hide it.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed class FXDirector : MonoBehaviour
    {
        public GameWorld world;
        public PlayerController player;
        public RTSCamera rig;

        [Header("Particle materials")]
        public Material fireMaterial;
        public Material smokeMaterial;
        public Material glowMaterial;

        [Header("Instanced overlay materials")]
        public Material ringMaterial;
        public Material scorchMaterial;
        public Material barMaterial;
        public Material streakMaterial;

        [Header("Debris")]
        public Material debrisMaterial;
        public Material trailMaterial;

        public static readonly Color PlayerColor = new Color(0.22f, 0.68f, 1f);
        public static readonly Color EnemyColor = new Color(1f, 0.28f, 0.16f);

        sealed class Batch
        {
            public readonly Matrix4x4[] m = new Matrix4x4[1023];
            readonly Vector4[] color = new Vector4[1023], param = new Vector4[1023], uv = new Vector4[1023];
            // Created on first draw: a MaterialPropertyBlock may not be constructed
            // while the owning MonoBehaviour is being constructed.
            MaterialPropertyBlock mpb;
            public int n;

            public void Add(Matrix4x4 mat, Color c, Vector4 p, Vector4 rect = default)
            {
                if (n >= m.Length) return;
                m[n] = mat; color[n] = c; param[n] = p; uv[n] = rect;
                n++;
            }

            public void Draw(Mesh mesh, Material mat)
            {
                if (n == 0 || mat == null || mesh == null) { n = 0; return; }
                mpb ??= new MaterialPropertyBlock();
                mpb.SetVectorArray("_Color", color);
                mpb.SetVectorArray("_Params", param);
                mpb.SetVectorArray("_UVRect", uv);
                var rp = new RenderParams(mat)
                {
                    matProps = mpb,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = new Bounds(new Vector3(128f, 20f, 128f), new Vector3(2000f, 400f, 2000f))
                };
                Graphics.RenderMeshInstanced(rp, mesh, 0, m, n);
                n = 0;
            }
        }

        struct Scorch { public Vector3 pos; public float rot, size, born; public int frame; }
        struct Marker { public Vector3 pos; public float born; public int kind; }
        struct FlashSlot { public Light light; public float t, life, intensity; }
        struct Shockwave { public Vector3 pos; public float born, size; }
        struct Blast { public Vector3 pos; public float t, scale; }

        ParticleSystem fire, smoke, sparks, glow, embers, debris;
        Mesh cube, quad;
        readonly Batch rings = new Batch(), scorches = new Batch(), bars = new Batch(), streaks = new Batch();
        readonly List<Scorch> scorchList = new List<Scorch>();
        readonly List<Marker> markers = new List<Marker>();
        readonly List<Shockwave> shockwaves = new List<Shockwave>();
        readonly List<Blast> blasts = new List<Blast>();
        readonly FlashSlot[] flashes = new FlashSlot[8];
        readonly Vector4[] shockUniforms = new Vector4[8];
        float nextFlash;

        static readonly int ShocksId = Shader.PropertyToID("_SF_Shocks");
        static readonly int ShockCountId = Shader.PropertyToID("_SF_ShockCount");

        void Awake()
        {
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (rig == null) rig = FindAnyObjectByType<RTSCamera>();
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            fire = MakeSystem("Fire", fireMaterial, 600, true, 0f, 0.65f, 1.3f, false, 0f);
            smoke = MakeSystem("Smoke", smokeMaterial, 900, true, 0f, 0.45f, 1.6f, false, -0.02f);
            sparks = MakeSystem("Sparks", glowMaterial, 1600, false, 4f / 16f, 1f, 0.25f, true, 2.2f);
            glow = MakeSystem("Glow", glowMaterial, 600, false, 4f / 16f, 1f, 0.4f, false, 0f);
            var lv = smoke.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.drag = 0.9f;
            var fireCol = fire.colorOverLifetime;
            fireCol.color = new ParticleSystem.MinMaxGradient(Fade(0.02f, 0.6f));

            // Embers float up out of fireballs and wander on turbulence.
            embers = MakeSystem("Embers", glowMaterial, 900, false, 4f / 16f, 1f, 0.3f, false, -0.05f);
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 1.2f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var elv = embers.limitVelocityOverLifetime;
            elv.enabled = true;
            elv.drag = 0.6f;
            if (debrisMaterial != null) debris = MakeDebris();

            for (int i = 0; i < flashes.Length; i++)
            {
                var go = new GameObject("Flash" + i);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.intensity = 0f;
                l.enabled = false;
                flashes[i].light = l;
            }
        }

        void OnEnable()
        {
            if (world != null) world.Event += OnEvent;
            if (player != null) player.OrderMarker += OnMarker;
        }

        void OnDisable()
        {
            if (world != null) world.Event -= OnEvent;
            if (player != null) player.OrderMarker -= OnMarker;
        }

        static Gradient Fade(float fadeIn, float holdUntil)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        ParticleSystem MakeSystem(string name, Material mat, int max, bool animated, float frame,
                                  float sizeStart, float sizeEnd, bool stretch, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 1f;
            main.gravityModifier = gravity;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, sizeStart, 1f, sizeEnd));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(0.06f, 0.5f));
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = 4;
            tsa.numTilesY = 4;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = animated
                ? new ParticleSystem.MinMaxCurve(0.9999f, AnimationCurve.Linear(0f, 0f, 1f, 1f))
                : new ParticleSystem.MinMaxCurve(frame);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r.velocityScale = stretch ? 0.045f : 0f;
            r.lengthScale = stretch ? 1.2f : 2f;
            r.maxParticleSize = 4f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>Solid chunks thrown by explosions: lit mesh particles with 3D spin that
        /// bounce off the terrain collider and settle, the hottest trailing fire.</summary>
        ParticleSystem MakeDebris()
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 700;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 3f;
            main.startSize = 1f;
            main.startRotation3D = true;
            main.gravityModifier = 1.6f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
            rot.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
            rot.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.82f, 1f), new Keyframe(1f, 0f)));
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium;
            col.dampen = 0.45f;
            col.bounce = 0.3f;
            col.lifetimeLoss = 0f;
            col.radiusScale = 0.6f;
            col.enableDynamicColliders = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = BuildDebrisMesh();
            r.alignment = ParticleSystemRenderSpace.World;
            r.sharedMaterial = debrisMaterial;
            r.enableGPUInstancing = true;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;

            if (trailMaterial != null)
            {
                var trails = ps.trails;
                trails.enabled = true;
                trails.ratio = 0.6f;
                trails.lifetime = new ParticleSystem.MinMaxCurve(0.28f);
                trails.minVertexDistance = 0.2f;
                trails.dieWithParticles = true;
                trails.sizeAffectsWidth = true;
                trails.inheritParticleColor = false;
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.9f, 1f, 0f));
                var hot = new Gradient();
                hot.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.6f, 0.25f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.1f), 0.35f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 0.35f) });
                trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(hot);
                r.trailMaterial = trailMaterial;
            }
            ps.Play();
            return ps;
        }

        /// <summary>A unit cube with jittered corners: reads as a torn chunk of plating or rock.</summary>
        static Mesh BuildDebrisMesh()
        {
            var rnd = new System.Random(5);
            float J() => ((float)rnd.NextDouble() - 0.5f) * 0.35f;
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.35f : 0.35f, (i & 4) == 0 ? -0.5f : 0.5f)
                       + new Vector3(J(), J(), J());
            int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 } };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in faces)
            {
                Vector3 a = c[f[0]], b = c[f[1]], cc = c[f[2]], d = c[f[3]];
                if (Vector3.Dot(Vector3.Cross(b - a, cc - a), a + b + cc + d) < 0f) (b, d) = (d, b);
                int k = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(cc); verts.Add(d);
                tris.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            var mesh = new Mesh { name = "SF_Debris" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color color, float rotation = 0f)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = rotation,
                applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        bool Seen(Vector3 p) => MatchSettings.spectate || world.Visible(player != null ? player.team : 0, new Vector2(p.x, p.z));

        static Color TeamColor(int team) => team == 0 ? PlayerColor : team == 1 ? EnemyColor : new Color(0.55f, 0.95f, 1f);

        // ------------------------------------------------------------ events
        void OnEvent(GameEvent e)
        {
            switch (e.kind)
            {
                case GameEventKind.Fire:
                    if (!Seen(e.pos)) return;
                    MuzzleFlash(e);
                    break;
                case GameEventKind.Impact:
                    if (!Seen(e.pos)) return;
                    if (e.scale > 1f) Explosion(e.pos, e.scale, false);
                    else ImpactSparks(e.pos, e.team, e.projectileKind);
                    break;
                case GameEventKind.Death:
                    if (!Seen(e.pos) && !(e.unit != null && e.unit.everSeenByPlayer && e.unit.def.building)) return;
                    if (e.type == UnitType.Ore) CrystalShatter(e.pos);
                    else Explosion(e.pos, e.scale, e.unit != null && e.unit.def.building);
                    break;
                case GameEventKind.Promoted:
                    if (!Seen(e.pos)) return;
                    for (int i = 0; i < 18; i++)
                        Emit(sparks, e.pos + Vector3.up * 0.5f, new Vector3(Random.Range(-2f, 2f), Random.Range(4f, 9f), Random.Range(-2f, 2f)),
                             0.35f, Random.Range(0.5f, 0.9f), new Color(1f, 0.85f, 0.35f));
                    Emit(glow, e.pos + Vector3.up * 1.2f, Vector3.up, 4f, 0.6f, new Color(1f, 0.8f, 0.3f));
                    markers.Add(new Marker { pos = e.pos, born = Time.time, kind = 5 });
                    break;
                case GameEventKind.StructureComplete:
                    if (!Seen(e.pos)) return;
                    markers.Add(new Marker { pos = e.pos, born = Time.time, kind = e.team == 0 ? 6 : 7 });
                    break;
            }
        }

        void OnMarker(Vector3 pos, int kind) => markers.Add(new Marker { pos = pos, born = Time.time, kind = kind });

        void MuzzleFlash(GameEvent e)
        {
            Color c = TeamColor(e.team);
            switch (e.projectileKind)
            {
                case 1:
                    Emit(glow, e.pos + e.dir * 0.3f, e.dir * 2f, 2.6f, 0.12f, new Color(1f, 0.7f, 0.35f));
                    for (int i = 0; i < 5; i++)
                        Emit(smoke, e.pos, e.dir * Random.Range(3f, 8f) + Random.insideUnitSphere * 1.5f, 1.2f, Random.Range(0.6f, 1.0f), new Color(0.55f, 0.52f, 0.5f, 0.55f));
                    Flash(e.pos, new Color(1f, 0.65f, 0.3f), 3f, 8f, 0.1f);
                    break;
                case 3:
                    Emit(glow, e.pos, e.dir, 1.6f, 0.1f, new Color(1f, 0.75f, 0.35f));
                    break;
                default:
                    Emit(glow, e.pos, e.dir, e.projectileKind == 2 ? 1.1f : 0.8f, 0.06f, Color.Lerp(Color.white, c, 0.5f));
                    break;
            }
        }

        void ImpactSparks(Vector3 at, int team, int kind)
        {
            Color c = kind == 3 ? new Color(1f, 0.7f, 0.3f) : Color.Lerp(new Color(1f, 0.75f, 0.4f), TeamColor(team), 0.35f);
            for (int i = 0; i < 6; i++)
                Emit(sparks, at, Random.insideUnitSphere * 7f + Vector3.up * 2f, 0.18f, Random.Range(0.15f, 0.35f), c);
            Emit(glow, at, Vector3.zero, 0.9f, 0.08f, c);
        }

        void CrystalShatter(Vector3 at)
        {
            for (int i = 0; i < 24; i++)
                Emit(sparks, at, Random.insideUnitSphere * 8f + Vector3.up * 4f, 0.3f, Random.Range(0.4f, 1.0f), new Color(0.5f, 0.95f, 1f));
            Emit(glow, at, Vector3.zero, 5f, 0.5f, new Color(0.4f, 0.9f, 1f));
        }

        void Explosion(Vector3 at, float scale, bool structure, bool secondary = false)
        {
            float t = Time.time;
            int puffs = structure && !secondary ? 5 : 1;
            for (int i = 0; i < puffs; i++)
            {
                Vector3 off = i == 0 ? Vector3.zero : Random.insideUnitSphere * scale * 1.2f;
                off.y = Mathf.Abs(off.y) * 0.5f;
                Emit(fire, at + off + Vector3.up * 0.6f * scale, Vector3.up * 0.9f * scale,
                     Random.Range(3.6f, 4.6f) * scale * (i == 0 ? 1f : 0.6f), 0.85f * Mathf.Sqrt(scale), Color.white, Random.Range(0f, 360f));
            }
            int nSparks = Mathf.RoundToInt(12 * scale);
            for (int i = 0; i < nSparks; i++)
            {
                Vector3 v = Random.insideUnitSphere * 14f * scale;
                v.y = Mathf.Abs(v.y) + 3f;
                Emit(sparks, at, v, 0.32f * Mathf.Sqrt(scale), Random.Range(0.3f, 0.9f), new Color(1f, 0.62f, 0.25f));
            }
            int plumes = structure ? 4 : (scale > 1.2f ? 2 : 1);
            for (int i = 0; i < plumes; i++)
                Emit(smoke, at + Random.insideUnitSphere * scale * 0.6f,
                     new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(1.4f, 2.6f), Random.Range(-0.5f, 0.5f)),
                     Random.Range(3.5f, 5f) * scale, Random.Range(2.2f, 3.2f), new Color(0.32f, 0.30f, 0.29f, 0.75f));
            Emit(glow, at + Vector3.up * scale, Vector3.zero, 8f * scale, 0.22f, new Color(1f, 0.55f, 0.2f));

            // Debris: dark chunks thrown out on arcs, the hot ones trailing fire,
            // bouncing off the terrain and settling.
            if (debris != null)
            {
                int n = structure ? (secondary ? 8 : 28) : Mathf.RoundToInt(6f * scale);
                float sz = Mathf.Sqrt(scale) * (structure ? 1.5f : 1f);
                for (int i = 0; i < n; i++)
                {
                    Vector3 v = Random.insideUnitSphere * 8f * scale;
                    v.y = Mathf.Abs(v.y) * 1.3f + Random.Range(4f, 9f) * Mathf.Sqrt(scale);
                    float g = Random.Range(0.45f, 1f);
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = at + Random.insideUnitSphere * 0.6f * scale + Vector3.up * 0.4f,
                        velocity = v,
                        startSize = Random.Range(0.08f, 0.3f) * sz,
                        startLifetime = Random.Range(2.5f, 4.5f),
                        startColor = new Color(g, g * 0.96f, g * 0.92f),
                        rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                        applyShapeToPosition = false
                    }, 1);
                }
            }

            // Embers drift up out of the fireball.
            int nEmbers = Mathf.RoundToInt((structure ? 30 : 9) * scale);
            for (int i = 0; i < nEmbers; i++)
                Emit(embers, at + Random.insideUnitSphere * scale + Vector3.up * 0.5f * scale,
                     Random.insideUnitSphere * 2.5f + Vector3.up * Random.Range(1.5f, 4f),
                     Random.Range(0.10f, 0.22f), Random.Range(1.4f, 2.8f), new Color(1f, Random.Range(0.45f, 0.7f), 0.2f));

            float gy = world.Map.HeightAt(new Vector2(at.x, at.z));
            if (at.y - gy < 3.5f * scale)
            {
                Vector3 ground = new Vector3(at.x, gy, at.z);
                // A ring of dust rolling out over the ground behind a shockwave.
                int nDust = structure ? 20 : 12;
                for (int i = 0; i < nDust; i++)
                {
                    float a = (i + Random.value * 0.5f) / nDust * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Emit(smoke, ground + dir * 0.8f * scale + Vector3.up * 0.4f,
                         dir * Random.Range(5f, 8f) * Mathf.Sqrt(scale) + Vector3.up * 0.4f,
                         Random.Range(1.6f, 2.6f) * scale, Random.Range(1.0f, 1.7f), new Color(0.46f, 0.41f, 0.35f, 0.42f));
                }
                if (shockwaves.Count < 32)
                    shockwaves.Add(new Shockwave { pos = ground, born = t, size = (structure ? 26f : 11f) * scale });

                if (scorchList.Count >= 96) scorchList.RemoveAt(0);
                scorchList.Add(new Scorch
                {
                    pos = ground, rot = Random.Range(0f, 6.283f),
                    size = (structure ? 3.2f : 2.1f) * scale, born = t, frame = Random.Range(0, 4)
                });
            }
            Flash(at + Vector3.up * 2f, new Color(1f, 0.55f, 0.22f), 7f * scale, 9f * scale, 0.35f);

            if (rig != null && rig.cam != null)
            {
                float d = Vector3.Distance(rig.cam.transform.position, at);
                rig.Shake(Mathf.Clamp01(1.2f - d / 140f) * 0.25f * scale * (secondary ? 0.5f : 1f));
            }

            // Structures come apart in a short chain of secondary blasts.
            if (structure && !secondary)
                for (int i = 0; i < 3; i++)
                {
                    Vector3 off = Random.insideUnitSphere * scale * 1.6f;
                    off.y = Mathf.Abs(off.y) + 0.5f;
                    blasts.Add(new Blast { t = t + Random.Range(0.18f, 1.1f), pos = at + off, scale = scale * Random.Range(0.45f, 0.65f) });
                }
        }

        void Flash(Vector3 at, Color color, float intensity, float range, float life)
        {
            int slot = -1;
            float oldest = float.MaxValue;
            for (int i = 0; i < flashes.Length; i++)
            {
                if (!flashes[i].light.enabled) { slot = i; break; }
                if (flashes[i].t < oldest) { oldest = flashes[i].t; slot = i; }
            }
            if (slot < 0) return;
            ref var f = ref flashes[slot];
            f.t = Time.time;
            f.life = life;
            f.intensity = intensity;
            f.light.transform.position = at;
            f.light.color = color;
            f.light.range = range;
            f.light.intensity = intensity;
            f.light.enabled = true;
        }

        // ------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (world == null || !world.running) return;
            float now = Time.time;
            float dt = Time.deltaTime;

            for (int i = 0; i < flashes.Length; i++)
            {
                ref var f = ref flashes[i];
                if (!f.light.enabled) continue;
                float k = (now - f.t) / Mathf.Max(0.01f, f.life);
                if (k >= 1f) { f.light.enabled = false; continue; }
                f.light.intensity = f.intensity * (1f - k) * (1f - k);
            }

            for (int i = blasts.Count - 1; i >= 0; i--)
            {
                if (now < blasts[i].t) continue;
                var b = blasts[i];
                blasts.RemoveAt(i);
                Explosion(b.pos, b.scale, true, secondary: true);
            }

            AmbientEffects(dt);
            DrawOverlays(now);
            UploadShockwaves(now);
        }

        /// <summary>Live blast rings, in screen space, for the full-screen pass to
        /// refract the image through. Rings behind the camera or off screen are
        /// dropped, and the strength falls away as the wave passes.</summary>
        void UploadShockwaves(float now)
        {
            int n = 0;
            var cam = rig != null ? rig.cam : Camera.main;
            if (cam != null)
                for (int i = 0; i < shockwaves.Count && n < shockUniforms.Length; i++)
                {
                    var w = shockwaves[i];
                    float t = (now - w.born) / 0.45f;
                    if (t >= 1f) continue;
                    Vector3 vp = cam.WorldToViewportPoint(w.pos);
                    if (vp.z <= 0.5f) continue;
                    float radius = Mathf.Lerp(0.1f, 1f, 1f - (1f - t) * (1f - t)) * w.size * 0.5f;
                    Vector3 edge = cam.WorldToViewportPoint(w.pos + cam.transform.right * radius);
                    float uvRadius = Mathf.Abs(edge.x - vp.x);
                    if (uvRadius < 0.002f || uvRadius > 1.5f) continue;
                    shockUniforms[n++] = new Vector4(vp.x, vp.y, uvRadius, 0.012f * (1f - t) * (1f - t));
                }
            for (int i = n; i < shockUniforms.Length; i++) shockUniforms[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(ShocksId, shockUniforms);
            Shader.SetGlobalFloat(ShockCountId, n);
        }

        void AmbientEffects(float dt)
        {
            foreach (var u in world.units)
            {
                if (u == null || u.dying || !u.visibleToPlayer) continue;
                if (u.working)
                {
                    if (u.order == Order.Harvest && Unit.Live(u.harvestNode) && Random.value < dt * 20f)
                        Emit(sparks, u.harvestNode.Ground + Vector3.up * 1f + Random.insideUnitSphere * 0.6f,
                             new Vector3(Random.Range(-1f, 1f), Random.Range(1.5f, 4f), Random.Range(-1f, 1f)), 0.16f, Random.Range(0.3f, 0.6f),
                             new Color(0.45f, 0.95f, 1f));
                    else if (u.order == Order.Build && Unit.Live(u.buildTarget) && Random.value < dt * 28f)
                    {
                        var b = u.buildTarget;
                        Emit(sparks, b.Ground + Vector3.up * (b.def.visualHeight * b.buildProgress) + Random.insideUnitSphere * b.def.radius * 0.6f,
                             new Vector3(Random.Range(-1f, 1f), Random.Range(2f, 5f), Random.Range(-1f, 1f)), 0.2f, Random.Range(0.25f, 0.5f),
                             new Color(1f, 0.75f, 0.35f));
                    }
                }
                if (u.def.building && u.Complete && u.team < 2)
                {
                    float frac = u.hp / u.MaxHp;
                    // A burning structure is the clearest long-range cue that a base is under attack.
                    if (frac < 0.55f && Random.value < dt / (0.25f + frac * 0.7f))
                    {
                        float r = u.def.radius * 0.55f;
                        Vector3 p = u.Ground + new Vector3(Random.Range(-r, r), u.def.visualHeight * 0.8f, Random.Range(-r, r));
                        Emit(smoke, p, new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(1.6f, 2.6f), Random.Range(-0.4f, 0.4f)),
                             u.def.radius * 1.4f, Random.Range(2.6f, 3.8f), new Color(0.25f, 0.24f, 0.24f, 0.6f));
                        if (frac < 0.3f) Emit(fire, p, Vector3.up, u.def.radius * 0.8f, 0.7f, Color.white, Random.Range(0f, 360f));
                    }
                }
                if (u.Type == UnitType.Skimmer && u.agent != null && u.agent.enabled && u.agent.velocity.sqrMagnitude > 4f && Random.value < dt * 30f)
                {
                    float yaw = u.yaw;
                    Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                    Vector3 side = new Vector3(fwd.z, 0f, -fwd.x) * 0.8f;
                    Vector3 basePos = u.Ground + Vector3.up * 1.05f - fwd * 1.2f;
                    Color c = Color.Lerp(new Color(0.4f, 0.9f, 1f), TeamColor(u.team), 0.3f);
                    Emit(glow, basePos + side, -fwd * 2f, 0.45f, 0.22f, c);
                    Emit(glow, basePos - side, -fwd * 2f, 0.45f, 0.22f, c);
                }
            }

            foreach (var p in world.projectiles)
            {
                if (p.kind != 1 || !Seen(p.pos)) continue;
                Emit(glow, p.pos, Vector3.zero, 0.9f, 0.07f, new Color(1f, 0.7f, 0.35f));
                if (Random.value < dt * 40f)
                    Emit(smoke, p.pos, Random.insideUnitSphere * 0.3f, 0.7f, 0.5f, new Color(0.5f, 0.48f, 0.46f, 0.45f));
            }
        }

        void DrawOverlays(float now)
        {
            int me = player != null ? player.team : 0;

            // Scorch marks outlive everything else a fight produces; they fade over a minute.
            for (int i = scorchList.Count - 1; i >= 0; i--)
            {
                var s = scorchList[i];
                float age = now - s.born;
                if (age > 60f) { scorchList.RemoveAt(i); continue; }
                if (!Seen(s.pos) && !world.Explored(me, new Vector2(s.pos.x, s.pos.z))) continue;
                float a = 0.82f * (1f - Mathf.SmoothStep(0f, 1f, age / 60f));
                var rect = new Vector4((s.frame % 2) * 0.5f, (s.frame / 2) * 0.5f, 0.5f, 0.5f);
                scorches.Add(Matrix4x4.TRS(s.pos, Quaternion.identity, new Vector3(s.size * 2f, 3f, s.size * 2f)),
                             new Color(0.035f, 0.03f, 0.026f, a), new Vector4(1f, 0f, s.rot, 0f), rect);
            }
            scorches.Draw(cube, scorchMaterial);

            if (player != null)
            {
                foreach (var u in player.Selection)
                {
                    if (!Unit.Live(u)) continue;
                    float r = Mathf.Max(u.def.radius * 1.3f, u.def.visualRadius * 0.9f);
                    Color c = TeamColor(u.team) * 2.2f;
                    rings.Add(Matrix4x4.TRS(u.Ground, Quaternion.identity, new Vector3(r * 2f, 4f, r * 2f)), c, new Vector4(0f, 0f, 0f, 0f));
                    if (u.team == me && u.def.building && u.rallySet)
                        rings.Add(Matrix4x4.TRS(world.Map.Ground(u.rally), Quaternion.identity, new Vector3(1.6f, 3f, 1.6f)),
                                  PlayerColor * 1.5f, new Vector4(4f, 0f, 0f, 0f));
                    if (u.def.type == UnitType.Sentinel && u.team == me)
                        rings.Add(Matrix4x4.TRS(u.Ground, Quaternion.identity, new Vector3(u.def.range * 2f, 6f, u.def.range * 2f)),
                                  PlayerColor * 0.9f, new Vector4(4f, 0f, 0f, 0f));
                }
                var h = player.Hovered;
                if (Unit.Live(h) && !player.Selection.Contains(h))
                {
                    float r = Mathf.Max(h.def.radius * 1.3f, h.def.visualRadius * 0.9f);
                    rings.Add(Matrix4x4.TRS(h.Ground, Quaternion.identity, new Vector3(r * 2f, 4f, r * 2f)),
                              TeamColor(h.team) * 0.9f, Vector4.zero);
                }
                if (player.PlacingType != UnitType.None)
                {
                    var d = Defs.Get(player.PlacingType);
                    Vector3 g = world.Map.Ground(player.PlacementPos);
                    Color c = player.PlacementValid ? new Color(0.3f, 1.6f, 0.6f) : new Color(1.8f, 0.3f, 0.25f);
                    rings.Add(Matrix4x4.TRS(g, Quaternion.identity, new Vector3(d.radius * 2f, 6f, d.radius * 2f)), c, new Vector4(2f, 0f, 0f, 0f));
                    if (d.range > 0f)
                        rings.Add(Matrix4x4.TRS(g, Quaternion.identity, new Vector3(d.range * 2f, 8f, d.range * 2f)), c * 0.6f, new Vector4(4f, 0f, 0f, 0f));
                }
            }

            for (int i = markers.Count - 1; i >= 0; i--)
            {
                var m = markers[i];
                float life = m.kind >= 5 ? 1.2f : 0.6f;
                float t = (now - m.born) / life;
                if (t >= 1f) { markers.RemoveAt(i); continue; }
                Color c;
                float size = 3f;
                switch (m.kind)
                {
                    case 1: c = new Color(2f, 0.4f, 0.3f); break;
                    case 2: c = PlayerColor * 1.8f; break;
                    case 3: c = new Color(1.8f, 1.4f, 0.5f); size = 5f; break;
                    case 5: c = new Color(2.2f, 1.7f, 0.5f); size = 5f; break;
                    case 6: c = PlayerColor * 2.5f; size = 14f; break;
                    case 7: c = EnemyColor * 2.5f; size = 14f; break;
                    default: c = new Color(0.4f, 1.8f, 0.8f); break;
                }
                rings.Add(Matrix4x4.TRS(m.pos, Quaternion.identity, new Vector3(size, 4f, size)), c, new Vector4(3f, t, 0f, 0f));
            }
            for (int i = shockwaves.Count - 1; i >= 0; i--)
            {
                var w = shockwaves[i];
                float t = (now - w.born) / 0.45f;
                if (t >= 1f) { shockwaves.RemoveAt(i); continue; }
                rings.Add(Matrix4x4.TRS(w.pos, Quaternion.identity, new Vector3(w.size, 6f, w.size)),
                          new Color(2.4f, 1.5f, 0.7f), new Vector4(5f, t, 0f, 0f));
            }
            rings.Draw(cube, ringMaterial);

            // Health bars: damaged, selected or hovered, and only what the player can see.
            if (rig != null && rig.cam != null)
            {
                Vector3 camPos = rig.cam.transform.position;
                foreach (var u in world.units)
                {
                    if (u == null || u.dying || u.def.neutral) continue;
                    if (u.team == 1 && !u.visibleToPlayer && !MatchSettings.spectate) continue;
                    bool selected = player != null && player.Selection.Contains(u);
                    bool hovered = player != null && player.Hovered == u;
                    float frac = u.hp / u.MaxHp;
                    if (!selected && !hovered && frac > 0.995f && u.Complete) continue;
                    Vector3 top = u.Ground + Vector3.up * (u.def.visualHeight + 0.7f);
                    float dist = Vector3.Distance(camPos, top);
                    float k = Mathf.Lerp(0.8f, 1.9f, Mathf.InverseLerp(30f, 150f, dist));
                    float w = Mathf.Clamp(u.def.visualRadius * 1.5f, 1.3f, 7f) * k;
                    float hgt = 0.26f * k;
                    Color c = frac > 0.6f ? new Color(0.35f, 0.95f, 0.55f) : frac > 0.3f ? new Color(1f, 0.75f, 0.25f) : new Color(1f, 0.3f, 0.25f);
                    if (u.team == 1) c = Color.Lerp(c, EnemyColor, 0.25f);
                    float segments = Mathf.Clamp(u.MaxHp / 50f, 1f, 30f);
                    float secondary = u.Complete ? 0f : u.buildProgress;
                    bars.Add(Matrix4x4.TRS(top, Quaternion.identity, new Vector3(w, hgt, 1f)), c, new Vector4(0f, frac, segments, secondary));
                }
                bars.Draw(quad, barMaterial);
            }

            foreach (var p in world.projectiles)
            {
                if (p.kind == 1 || !Seen(p.pos)) continue;
                Vector3 seg = p.pos - p.prevPos;
                float len = seg.magnitude;
                if (len < 0.01f) continue;
                Vector3 dir = seg / len;
                float streak, width;
                Color c;
                switch (p.kind)
                {
                    case 2: streak = 1.6f; width = 0.32f; c = p.team == 0 ? new Color(0.5f, 1.6f, 2.4f) : new Color(2.4f, 0.8f, 0.4f); break;
                    case 3: streak = 4f; width = 0.26f; c = new Color(2.6f, 1.6f, 0.6f); break;
                    default: streak = Mathf.Min(len * 1.5f, 3.5f); width = 0.12f; c = p.team == 0 ? new Color(0.9f, 1.8f, 2.6f) : new Color(2.6f, 1.2f, 0.5f); break;
                }
                Vector3 center = p.pos - dir * streak * 0.5f;
                var mat = new Matrix4x4(
                    new Vector4(dir.x * streak, dir.y * streak, dir.z * streak, 0f),
                    new Vector4(0f, width, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f),
                    new Vector4(center.x, center.y, center.z, 1f));
                streaks.Add(mat, c, new Vector4(1f, 0f, 0f, 0f));
            }
            streaks.Draw(quad, streakMaterial);
        }
    }
}
