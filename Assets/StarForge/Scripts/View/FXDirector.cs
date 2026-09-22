// FXDirector.cs — turns game events into effects, and draws the instanced
// overlays (selection rings, placement footprint, order markers, scorch marks,
// vehicle tracks, the glow under ore, health bars, projectile streaks). Nothing
// here changes the game.
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
        [Tooltip("StarForge/Particle on the generated flame flipbook (Tools/make_flame_sheet.py).")]
        public Material flameMaterial;
        [Tooltip("StarForge/Smoke: smoke, dust and mist.")] public Material smokeMaterial;
        [Tooltip("StarForge/Smoke without billowing: water drops.")] public Material dropletMaterial;
        public Material glowMaterial;

        [Header("Instanced overlay materials")]
        public Material ringMaterial;
        public Material scorchMaterial;
        public Material barMaterial;
        public Material streakMaterial;

        [Header("Debris")]
        public Material debrisMaterial;
        public Material trailMaterial;

        [Header("Water")]
        public Material rippleMaterial;

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
        struct TrackMark { public Vector3 pos; public float yaw, length, width, born, band, halfBand, pitch; }
        struct Marker { public Vector3 pos; public float born; public int kind; }
        struct FlashSlot { public Light light; public float t, life, intensity; }
        struct Shockwave { public Vector3 pos; public float born, size; }
        struct Blast { public Vector3 pos; public float t, scale; }

        ParticleSystem fire, flames, smoke, sparks, glow, embers, debris, trail, droplets;
        Mesh cube, quad;
        readonly Batch rings = new Batch(), scorches = new Batch(), tracks = new Batch(), bars = new Batch(), streaks = new Batch(), ripples = new Batch();
        readonly List<Scorch> scorchList = new List<Scorch>();
        // Tracks are a ring buffer: the oldest mark gives way once the batch is full.
        const int MaxTracks = 1000;
        const float TrackLife = 40f;
        readonly TrackMark[] trackMarks = new TrackMark[MaxTracks];
        int trackHead, trackCount;
        readonly Dictionary<int, Vector3> lastTrack = new Dictionary<int, Vector3>();
        readonly List<int> trackGone = new List<int>();
        readonly List<Marker> markers = new List<Marker>();
        readonly List<Shockwave> shockwaves = new List<Shockwave>();
        readonly List<Blast> blasts = new List<Blast>();

        // Muzzle blasts and other flashes too short and too directional for a
        // sprite: shaped quads along a direction, drawn with the streaks.
        struct Flare { public Vector3 pos, dir; public float born, life, length, width; public Color color; }
        readonly List<Flare> flares = new List<Flare>(128);

        // Emits held back a moment, so a fireball is not hidden behind the smoke
        // that should follow it.
        struct Delayed { public float at, size, life, rotation; public int system; public Vector3 pos, vel; public Color color; }
        readonly List<Delayed> delayed = new List<Delayed>(256);

        // Rings and foam on the water: wakes, splashes, shells landing in it.
        struct Ripple { public Vector3 pos; public float born, life, size, seed; public int kind; }
        readonly List<Ripple> rippleList = new List<Ripple>(256);
        // Per unit: in the water last frame, and where it last dropped a wake ring.
        readonly Dictionary<int, (bool wet, Vector2 last)> waterState = new Dictionary<int, (bool, Vector2)>();
        readonly List<int> waterGone = new List<int>();

        // Firelight: a few flickering lights on the burning trees nearest the camera.
        readonly Light[] fireLights = new Light[4];

        // Where each shell in flight last dropped a puff of its smoke trail.
        readonly Dictionary<Projectile, (Vector3 last, float age)> trailState = new Dictionary<Projectile, (Vector3, float)>();
        readonly List<Projectile> trailGone = new List<Projectile>();
        const float TrailSpacing = 0.9f;
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

            fire = MakeSystem("Fire", fireMaterial, 1400, true, 0f, 0.65f, 1.3f, false, 0f);
            flames = MakeFlames();
            smoke = MakeSystem("Smoke", smokeMaterial, 900, false, 0f, 0.45f, 1.6f, false, -0.02f);
            SmokeStreams(smoke);
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

            // Shell smoke trails, gun blast smoke and blast dust: puffs that start
            // large and swell less, with more drag, than the rising smoke above.
            trail = MakeSystem("Trail", smokeMaterial, 2000, false, 0f, 0.55f, 1.9f, false, -0.01f);
            SmokeStreams(trail);
            var tlv = trail.limitVelocityOverLifetime;
            tlv.enabled = true;
            tlv.drag = 1.2f;

            // Water thrown up by splashes: small bright drops, stretched along their
            // flight, falling back.
            droplets = MakeSystem("Droplets", dropletMaterial != null ? dropletMaterial : smokeMaterial, 900, false, 0f, 1f, 0.5f, true, 1.7f);
            SmokeStreams(droplets);
            var dr = droplets.GetComponent<ParticleSystemRenderer>();
            dr.velocityScale = 0.06f;
            dr.lengthScale = 1.1f;

            for (int i = 0; i < fireLights.Length; i++)
            {
                var go = new GameObject("FireLight" + i);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = new Color(1f, 0.5f, 0.2f);
                l.range = 12f;
                l.enabled = false;
                fireLights[i] = l;
            }

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

        /// <summary>Burning plants: tongues of flame from the flipbook, upright (they
        /// stay vertical whatever the camera does) and taller than wide, playing the
        /// sheet once over a short life so each flickers on its own.</summary>
        ParticleSystem MakeFlames()
        {
            var ps = MakeSystem("Flames", flameMaterial != null ? flameMaterial : fireMaterial, 1400, true, 0f, 0.75f, 0.45f, false, -0.05f);
            var main = ps.main;
            main.startSize3D = true;
            var sol = ps.sizeOverLifetime;
            sol.separateAxes = false;
            // Flare up, then thin out and die as the tongue lifts off.
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.35f)));
            var col = ps.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(Fade(0.12f, 0.45f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            r.maxParticleSize = 2f;
            return ps;
        }

        static void EmitFlame(ParticleSystem ps, Vector3 pos, Vector3 vel, float width, float height, float life, Color color)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize3D = new Vector3(width, height, width),
                startLifetime = life,
                startColor = color,
                applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        /// <summary>The per-particle data StarForge/Smoke picks, lights and erodes a puff
        /// with: a stable random seed and its age. Smoke has no animated sheet.</summary>
        static void SmokeStreams(ParticleSystem ps)
        {
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = false;
            ps.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent
            });
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

        /// <summary>The ore's glow colour and its violet rim (SFMaterialLibrary's crystal).</summary>
        static readonly Color OreLight = new Color(0.08f, 0.95f, 0.78f), OreRim = new Color(0.55f, 0.35f, 1f);

        /// <summary>A seam's slow breath, 0..1: the same phase and period as the
        /// crystal glow in SF_Unit, so the light on the ground swells with it.</summary>
        static float OreBreath(Vector2 p, float t) =>
            0.5f + 0.5f * Mathf.Sin(t * 1.25f + (p.x * 0.37f + p.y * 0.23f) * 3f);

        bool Seen(Vector3 p) => MatchSettings.spectate || world.Visible(player != null ? player.team : 0, new Vector2(p.x, p.z));

        static Color TeamColor(int team) => team == 0 ? PlayerColor : team == 1 ? EnemyColor : new Color(0.55f, 0.95f, 1f);

        /// <summary>A camera-facing quad along <paramref name="dir"/> ending at <paramref name="head"/>.</summary>
        void AddStreak(Vector3 head, Vector3 dir, float length, float width, Color color, float kind)
        {
            Vector3 center = head - dir * length * 0.5f;
            var mat = new Matrix4x4(
                new Vector4(dir.x * length, dir.y * length, dir.z * length, 0f),
                new Vector4(0f, width, 0f, 0f),
                new Vector4(0f, 0f, 1f, 0f),
                new Vector4(center.x, center.y, center.z, 1f));
            streaks.Add(mat, color, new Vector4(kind, 0f, 0f, 0f));
        }

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
                    if (e.type == UnitType.Boulder) RockSmash(e.pos, e.scale, e.dir);
                    else if (e.type == UnitType.Ore) CrystalShatter(e.pos);
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
                case GameEventKind.PlantFelled:
                    if (Seen(e.pos)) PlantFelled(e);
                    break;
                case GameEventKind.PlantLanded:
                    if (Seen(e.pos)) PlantLanded(e);
                    break;
                case GameEventKind.PlantIgnited:
                    if (Seen(e.pos)) PlantIgnited(e);
                    break;
            }
        }

        // ------------------------------------------------------------ vegetation
        Vegetation Plants => world != null ? world.Plants : null;

        /// <summary>A point in a plant's crown (or along its trunk, once the crown has
        /// burned away), where it stands, falls or lies now.</summary>
        Vector3 CrownPoint(Vegetation veg, int i)
        {
            var k = veg.KindOf(i);
            var pose = veg.Pose(i);
            float lost = veg.live[i].foliageLost;
            Vector3 local;
            if (k.HasCrown && Random.value > lost * 0.8f)
            {
                Vector3 r = Random.insideUnitSphere;
                float shrink = 1f - lost * 0.6f;
                local = k.crownCenter + Vector3.Scale(r, k.crownRadii) * shrink * 0.85f;
            }
            else local = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.3f, 0.85f) * k.height, Random.Range(-0.2f, 0.2f));
            return pose.MultiplyPoint3x4(local);
        }

        void PlantFelled(GameEvent e)
        {
            var veg = Plants;
            if (veg == null) return;
            var k = veg.KindOf(e.index);
            float size = e.scale;
            bool leafy = k.HasCrown && veg.live[e.index].foliageLost < 0.7f;
            Color leaf = Color.Lerp(k.leaf, k.leaf2, Random.value) * 2.4f;
            leaf.a = 1f;
            if (debris != null)
            {
                // Leaves and twigs shaken out of the crown, splinters from the trunk.
                int nLeaves = leafy ? Mathf.RoundToInt((k.bush ? 10 : 18) * size) : 0;
                for (int i = 0; i < nLeaves; i++)
                {
                    Vector3 at = CrownPoint(veg, e.index);
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = at,
                        velocity = Random.insideUnitSphere * 3f + e.dir * 2f + Vector3.up * Random.Range(0.5f, 2.5f),
                        startSize = Random.Range(0.06f, 0.14f),
                        startLifetime = Random.Range(1.6f, 2.8f),
                        startColor = leaf * Random.Range(0.7f, 1.1f),
                        rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                        applyShapeToPosition = false
                    }, 1);
                }
                if (!k.bush)
                    for (int i = 0; i < 7; i++)
                    {
                        Vector3 v = Random.insideUnitSphere * 3.5f - e.dir * 1.5f;
                        v.y = Mathf.Abs(v.y) + Random.Range(2f, 4.5f);
                        float g = Random.Range(0.9f, 1.3f);
                        debris.Emit(new ParticleSystem.EmitParams
                        {
                            position = e.pos + Vector3.up * Random.Range(0.3f, 1.2f),
                            velocity = v,
                            startSize = Random.Range(0.06f, 0.16f),
                            startLifetime = Random.Range(2f, 3f),
                            startColor = new Color(g * 0.62f, g * 0.48f, g * 0.34f),
                            rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                            applyShapeToPosition = false
                        }, 1);
                    }
            }
            // A puff of leaf litter and dust at the foot.
            for (int i = 0; i < (k.bush ? 4 : 6); i++)
                Emit(trail, e.pos + Random.insideUnitSphere * 0.8f + Vector3.up * 0.4f,
                     Random.insideUnitSphere * 1.6f + Vector3.up * 0.5f + e.dir * 0.8f, Random.Range(1.4f, 2.2f) * Mathf.Sqrt(size),
                     Random.Range(1.0f, 1.6f), new Color(0.48f, 0.42f, 0.33f, 0.45f), Random.Range(0f, 360f));
        }

        void PlantLanded(GameEvent e)
        {
            var veg = Plants;
            if (veg == null) return;
            var k = veg.KindOf(e.index);
            float length = k.height * e.scale;
            bool leafy = k.HasCrown && veg.live[e.index].foliageLost < 0.7f;
            // How hard it came down (the crown's speed): a tree that topples from a
            // blast hits far harder than one that settles off a neighbour's boughs,
            // and everything here follows from it.
            float force = Mathf.Clamp(e.speed / 14f, 0.25f, 1.6f);
            // Dust thrown up all along the trunk where it hits, thickest at the crown.
            int n = Mathf.RoundToInt(length * 1.2f * force);
            for (int i = 0; i < n; i++)
            {
                float t = (i + Random.value) / n;
                Vector3 at = e.pos + e.dir * (t * length * 0.9f) + Vector3.up * 0.3f;
                at.y = world.Map.HeightAt(new Vector2(at.x, at.z)) + 0.3f;
                Vector3 side = Vector3.Cross(Vector3.up, e.dir) * (Random.value < 0.5f ? -1f : 1f);
                Emit(trail, at, side * Random.Range(1.5f, 3.5f) * force + Vector3.up * Random.Range(0.4f, 1.2f) * force,
                     Random.Range(1.8f, 2.8f) * Mathf.Lerp(0.8f, 1.4f, t) * Mathf.Lerp(0.7f, 1.2f, force), Random.Range(1.2f, 2f),
                     leafy && t > 0.5f ? new Color(0.42f, 0.44f, 0.30f, 0.45f) : new Color(0.50f, 0.44f, 0.36f, 0.5f), Random.Range(0f, 360f));
            }
            // Leaves and splinters knocked loose where the crown struck.
            if (debris != null && force > 0.5f)
            {
                Color leaf = Color.Lerp(k.leaf, k.leaf2, Random.value) * 2.4f;
                leaf.a = 1f;
                int bits = Mathf.RoundToInt((leafy ? 14 : 6) * force * e.scale);
                for (int i = 0; i < bits; i++)
                {
                    Vector3 at = e.pos + e.dir * (Random.Range(0.5f, 1f) * length * 0.9f);
                    at.y = world.Map.HeightAt(new Vector2(at.x, at.z)) + 0.2f;
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = at,
                        velocity = Random.insideUnitSphere * 2.5f + Vector3.up * Random.Range(1.5f, 4f) * force,
                        startSize = Random.Range(0.06f, 0.16f),
                        startLifetime = Random.Range(1.2f, 2.4f),
                        startColor = (leafy && Random.value < 0.7f ? leaf : new Color(0.34f, 0.26f, 0.18f, 1f)) * Random.Range(0.7f, 1.1f),
                        rotation3D = Random.insideUnitSphere * 180f,
                        angularVelocity3D = Random.insideUnitSphere * 360f
                    }, 1);
                }
            }
            if (rig != null && rig.cam != null)
                rig.Shake(Mathf.Clamp01(1f - Vector3.Distance(rig.cam.transform.position, e.pos) / 90f) * 0.06f * force * Mathf.Min(1f, length / 6f));
        }

        void PlantIgnited(GameEvent e)
        {
            var veg = Plants;
            if (veg == null) return;
            for (int i = 0; i < 3; i++)
                Emit(fire, CrownPoint(veg, e.index), Vector3.up * 1.5f, Random.Range(1.6f, 2.4f) * veg.plants[e.index].scale,
                     Random.Range(0.6f, 0.9f), Color.white, Random.Range(0f, 360f));
            Emit(glow, CrownPoint(veg, e.index), Vector3.zero, 4f, 0.3f, new Color(1f, 0.55f, 0.2f));
        }

        void BurningPlants(float dt)
        {
            var veg = Plants;
            int lights = 0;
            Vector3 camFocus = rig != null ? new Vector3(rig.Focus.x, 0f, rig.Focus.y) : Vector3.zero;
            // The drift the wind gives smoke and embers (the same heading as the trees' sway).
            var w2 = StarForge.World.Wind.At(Time.time);
            var wind = new Vector3(w2.x, 0f, w2.y);
            if (veg != null)
            {
                // Fires that went out since last frame start to smoulder.
                foreach (int i in wasBurning)
                    if (!veg.live[i].burning && !smoulder.ContainsKey(i)) smoulder[i] = Time.time;
                wasBurning.Clear();

                // Nearest fires to the view get the lights.
                fireOrder.Clear();
                foreach (int i in veg.burning)
                {
                    var p = veg.plants[i].pos;
                    wasBurning.Add(i);
                    if (!Seen(p)) continue;
                    fireOrder.Add((new Vector2(p.x - camFocus.x, p.z - camFocus.z).sqrMagnitude, i));
                    ref var s = ref veg.live[i];
                    var k = veg.KindOf(i);
                    float size = veg.plants[i].scale * (k.bush ? 0.55f : 1f) * Mathf.Lerp(0.45f, 1f, s.fire);
                    // Tongues of flame licking up through the crown, or along the bare
                    // limbs once it has gone; a hot, low fire at the foot.
                    // The flame fills about half its quad (the sheet leaves room for the
                    // tongues to sway), hence the sizes.
                    float rate = s.fire * (k.bush ? 16f : 38f);
                    for (int n = Mathf.FloorToInt(rate * dt + Random.value); n > 0; n--)
                    {
                        float w = Random.Range(2.0f, 3.3f) * size;
                        EmitFlame(flames, CrownPoint(veg, i) - Vector3.up * w * 0.4f,
                                  Vector3.up * Random.Range(0.8f, 1.8f) + wind * 0.3f + Random.insideUnitSphere * 0.25f,
                                  w, w * Random.Range(1.6f, 2.2f), Random.Range(0.55f, 0.95f), Color.white);
                    }
                    if (!k.bush && Random.value < dt * s.fire * 5f)
                    {
                        float w = Random.Range(2.0f, 3.0f) * size;
                        EmitFlame(flames, veg.plants[i].pos + Random.insideUnitSphere * 0.4f + Vector3.up * 0.2f,
                                  Vector3.up * 0.6f, w, w * 1.4f, Random.Range(0.6f, 0.9f), new Color(1f, 0.85f, 0.7f));
                    }
                    // A rolling billow of flame now and then, at the height of the blaze.
                    if (s.fire > 0.6f && Random.value < dt * 1.5f)
                        Emit(fire, CrownPoint(veg, i), Vector3.up * Random.Range(1.5f, 2.5f), Random.Range(2.0f, 3.0f) * size,
                             Random.Range(0.6f, 0.9f), new Color(1f, 0.8f, 0.6f), Random.Range(0f, 360f));
                    if (Random.value < dt * s.fire * 3f)
                        Emit(glow, CrownPoint(veg, i), Vector3.up * 0.5f, Random.Range(4.5f, 7f) * size, Random.Range(0.4f, 0.7f), new Color(0.95f, 0.42f, 0.12f));
                    // A column of smoke leaning with the wind: dark and thick while the
                    // leaves burn, thinner and greyer once only wood is left.
                    if (Random.value < dt * s.fire * 6f)
                    {
                        bool leaves = k.HasCrown && s.foliageLost < 0.85f;
                        float shade = leaves ? Random.Range(0.12f, 0.2f) : Random.Range(0.25f, 0.34f);
                        var top = CrownPoint(veg, i) + Vector3.up * (1.5f + size);
                        Emit(smoke, top, Vector3.up * Random.Range(2.2f, 3.4f) + wind * Random.Range(0.8f, 1.4f) + Random.insideUnitSphere * 0.3f,
                             Random.Range(2.8f, 4.4f) * Mathf.Max(0.6f, size), Random.Range(4f, 6f),
                             new Color(shade, shade * 0.97f, shade * 0.93f, leaves ? 0.7f : 0.5f), Random.Range(0f, 360f));
                    }
                    if (Random.value < dt * s.fire * 10f)
                        Emit(embers, CrownPoint(veg, i), Random.insideUnitSphere * 1.0f + Vector3.up * Random.Range(1.8f, 3.8f) + wind * 0.8f,
                             Random.Range(0.07f, 0.14f), Random.Range(1.8f, 3.2f), new Color(1f, Random.Range(0.45f, 0.7f), 0.2f));
                }
                // Fires that have gone out smoulder: a thin, pale wisp of smoke from the
                // black wood for a while, and the odd ember.
                smoulderDone.Clear();
                foreach (var kv in smoulder)
                {
                    float age = Time.time - kv.Value;
                    if (age > 25f || veg.live[kv.Key].state == PlantState.Gone) { smoulderDone.Add(kv.Key); continue; }
                    var p = veg.plants[kv.Key].pos;
                    if (!Seen(p)) continue;
                    float strength = 1f - age / 25f;
                    if (Random.value < dt * 1.6f * strength)
                    {
                        float shade = Random.Range(0.42f, 0.52f);
                        Emit(smoke, p + Vector3.up * Random.Range(0.4f, 2.5f), Vector3.up * Random.Range(0.9f, 1.4f) + wind * 0.6f,
                             Random.Range(1.2f, 2.0f), Random.Range(3.5f, 5f), new Color(shade, shade, shade * 0.98f, 0.35f * strength), Random.Range(0f, 360f));
                    }
                    if (Random.value < dt * 0.8f * strength)
                        Emit(embers, p + Vector3.up * Random.Range(0.3f, 2f), Vector3.up * 1.2f + wind * 0.5f,
                             0.08f, Random.Range(1f, 2f), new Color(1f, 0.45f, 0.15f));
                }
                foreach (int i in smoulderDone) smoulder.Remove(i);

                fireOrder.Sort((a, b) => a.d.CompareTo(b.d));
                for (; lights < fireLights.Length && lights < fireOrder.Count; lights++)
                {
                    int i = fireOrder[lights].i;
                    var l = fireLights[lights];
                    var k = veg.KindOf(i);
                    l.transform.position = veg.Pose(i).MultiplyPoint3x4(k.HasCrown ? k.crownCenter * 0.8f : Vector3.up * k.height * 0.5f);
                    // Two rates of flicker, so the light breathes and gutters like flame.
                    float flicker = 0.7f + 0.2f * Mathf.PerlinNoise(Time.time * 6f, i * 0.37f) + 0.1f * Mathf.PerlinNoise(Time.time * 17f, i * 1.3f);
                    l.intensity = 5f * veg.live[i].fire * flicker;
                    l.range = 10f + 4f * veg.plants[i].scale;
                    l.color = Color.Lerp(new Color(1f, 0.45f, 0.15f), new Color(1f, 0.62f, 0.3f), flicker - 0.6f);
                    l.enabled = true;
                }
            }
            for (int i = lights; i < fireLights.Length; i++) fireLights[i].enabled = false;
            BurningGrass(veg, dt, camFocus, wind);
            WindBlown(veg, dt, camFocus, wind);
        }

        /// <summary>Leaves torn out of the crowns when it blows hard and carried downwind,
        /// so a gust is something you see in the air and not only in the sway.</summary>
        void WindBlown(StarForge.World.Vegetation veg, float dt, Vector3 camFocus, Vector3 wind)
        {
            if (veg == null || debris == null || veg.plants.Length == 0) return;
            float blowing = StarForge.World.Wind.Speed(Time.time);
            if (blowing < 0.55f) return;
            float rate = (blowing - 0.5f) * 14f;
            for (int n = Mathf.FloorToInt(rate * dt + Random.value); n > 0; n--)
            {
                int i = Random.Range(0, veg.plants.Length);
                ref var s = ref veg.live[i];
                var k = veg.KindOf(i);
                if (s.state != StarForge.World.PlantState.Standing || !k.HasCrown || s.foliageLost > 0.6f) continue;
                var p = veg.plants[i].pos;
                if (new Vector2(p.x - camFocus.x, p.z - camFocus.z).sqrMagnitude > 120f * 120f || !Seen(p)) continue;
                Color leaf = Color.Lerp(k.leaf, k.leaf2, Random.value) * 2.2f;
                leaf.a = 1f;
                debris.Emit(new ParticleSystem.EmitParams
                {
                    position = CrownPoint(veg, i),
                    velocity = wind * Random.Range(2.5f, 4.5f) + Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(0.2f, 1.6f),
                    startSize = Random.Range(0.05f, 0.11f),
                    startLifetime = Random.Range(2.5f, 4.5f),
                    startColor = leaf * Random.Range(0.7f, 1.1f),
                    rotation3D = Random.insideUnitSphere * 180f,
                    angularVelocity3D = Random.insideUnitSphere * 540f
                }, 1);
            }
        }

        /// <summary>A grass fire: a low line of flame creeping over the ground with thin
        /// smoke off it. Only the cells near the view are drawn -- a running fire can
        /// have a hundred of them alight.</summary>
        void BurningGrass(StarForge.World.Vegetation veg, float dt, Vector3 camFocus, Vector3 wind)
        {
            if (veg == null || veg.burningGrass.Count == 0) return;
            float now = Time.time;
            float cell = StarForge.World.Vegetation.GrassCell;
            // A running grass fire can have a hundred cells alight; drawing them all
            // would spend the flame budget the burning trees need. Only so many a
            // frame, starting somewhere different each time so none is left out.
            int budget = 60;
            int alight = veg.burningGrass.Count;
            grassFxCursor = (grassFxCursor + 17) % alight;
            for (int k = 0; k < alight && budget > 0; k++)
            {
                int c = veg.burningGrass[(grassFxCursor + k) % alight];
                var at = veg.GrassCentre(c);
                if (!Seen(at)) continue;
                float d2 = new Vector2(at.x - camFocus.x, at.z - camFocus.z).sqrMagnitude;
                if (d2 > 150f * 150f) continue;
                float fire = veg.GrassFire(c, now);
                if (fire <= 0.02f) continue;
                budget--;
                // A few low tongues along the cell, leaning downwind.
                float rate = fire * 7f;
                for (int n = Mathf.FloorToInt(rate * dt + Random.value); n > 0; n--)
                {
                    var p = at + new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f)) * cell;
                    p.y = at.y + 0.05f;
                    EmitFlame(flames, p, wind * Random.Range(0.3f, 0.9f) + Vector3.up * Random.Range(0.6f, 1.2f),
                              Random.Range(0.8f, 1.5f), Random.Range(0.9f, 1.8f) * (0.5f + fire),
                              Random.Range(0.35f, 0.6f), new Color(1f, 0.72f, 0.34f, 1f));
                }
                if (Random.value < 3f * dt * fire)
                    Emit(smoke, at + Vector3.up * 0.4f, Vector3.up * Random.Range(0.8f, 1.6f) + wind * Random.Range(0.8f, 1.6f),
                         Random.Range(1.4f, 2.6f), Random.Range(1.6f, 2.8f), new Color(0.26f, 0.24f, 0.22f, 0.45f), Random.Range(0f, 360f));
            }
        }

        int grassFxCursor;
        readonly HashSet<int> wasBurning = new HashSet<int>();
        readonly Dictionary<int, float> smoulder = new Dictionary<int, float>();
        readonly List<int> smoulderDone = new List<int>();
        readonly List<(float d, int i)> fireOrder = new List<(float, int)>(64);

        void OnMarker(Vector3 pos, int kind) => markers.Add(new Marker { pos = pos, born = Time.time, kind = kind });

        void AddFlare(Vector3 pos, Vector3 dir, float length, float width, Color color, float life)
        {
            if (flares.Count < 256)
                flares.Add(new Flare { pos = pos, dir = dir, length = length, width = width, color = color, life = life, born = Time.time });
        }

        ParticleSystem SystemAt(int i) => i switch { 0 => fire, 1 => smoke, 2 => sparks, 3 => glow, 4 => embers, _ => trail };

        void EmitLater(float delay, int system, Vector3 pos, Vector3 vel, float size, float life, Color color, float rotation = 0f)
        {
            if (delayed.Count < 512)
                delayed.Add(new Delayed { at = Time.time + delay, system = system, pos = pos, vel = vel, size = size, life = life, color = color, rotation = rotation });
        }

        void MuzzleFlash(GameEvent e)
        {
            Color c = TeamColor(e.team);
            Vector3 fwd = e.dir.sqrMagnitude > 1e-4f ? e.dir.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
            switch (e.projectileKind)
            {
                case 1:
                {
                    // The Mauler's gun: a white-hot core inside a long orange blast, jets
                    // out of the muzzle brake either side, a cone of smoke thrown
                    // forward and dragged to a stop, and dust kicked off the ground.
                    // The blast smoke uses the trail system's still frame: the
                    // animated smoke sheet opens on tiny puffs, and a gun's smoke is
                    // there the instant it fires.
                    AddFlare(e.pos, fwd, 4.2f, 2.6f, new Color(3.4f, 1.9f, 0.7f), 0.11f);
                    AddFlare(e.pos, fwd, 2.2f, 1.4f, new Color(4.2f, 3.6f, 2.6f), 0.07f);
                    AddFlare(e.pos - fwd * 0.3f, side, 1.8f, 1.0f, new Color(3.0f, 1.5f, 0.5f), 0.09f);
                    AddFlare(e.pos - fwd * 0.3f, -side, 1.8f, 1.0f, new Color(3.0f, 1.5f, 0.5f), 0.09f);
                    Emit(glow, e.pos + fwd * 0.8f, fwd * 1.5f, 4.2f, 0.16f, new Color(1f, 0.62f, 0.28f));
                    for (int i = 0; i < 9; i++)
                        Emit(trail, e.pos + fwd * Random.Range(0.3f, 1.6f),
                             fwd * Random.Range(4f, 13f) + Random.insideUnitSphere * 1.2f + Vector3.up * 0.5f,
                             Random.Range(2.2f, 3.2f), Random.Range(1.6f, 2.6f), new Color(0.82f, 0.79f, 0.75f, 0.75f), Random.Range(0f, 360f));
                    for (int k = -1; k <= 1; k += 2)
                        for (int i = 0; i < 2; i++)
                            Emit(trail, e.pos - fwd * 0.3f, side * k * Random.Range(3f, 6f) + Vector3.up * 0.6f,
                                 Random.Range(1.6f, 2.3f), Random.Range(1.1f, 1.6f), new Color(0.82f, 0.79f, 0.75f, 0.6f), Random.Range(0f, 360f));
                    for (int i = 0; i < 6; i++)
                        Emit(sparks, e.pos + fwd * 0.5f, fwd * Random.Range(14f, 24f) + Random.insideUnitSphere * 3f, 0.16f, Random.Range(0.15f, 0.3f), new Color(1f, 0.72f, 0.35f));
                    float gy = world.Map.HeightAt(new Vector2(e.pos.x, e.pos.z));
                    if (e.pos.y - gy < 2.6f)
                    {
                        Vector3 under = new Vector3(e.pos.x, gy, e.pos.z) + fwd * 1.8f;
                        for (int i = 0; i < 7; i++)
                        {
                            float a = i / 7f * Mathf.PI * 2f;
                            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                            Emit(trail, under + d * 0.8f + Vector3.up * 0.3f, (d + fwd * 0.8f) * Random.Range(2.5f, 4.5f) + Vector3.up * 0.3f,
                                 Random.Range(2.6f, 3.6f), Random.Range(1.2f, 1.8f), new Color(0.56f, 0.49f, 0.40f, 0.36f), Random.Range(0f, 360f));
                        }
                    }
                    Flash(e.pos + fwd * 1.5f, new Color(1f, 0.62f, 0.3f), 6f, 12f, 0.14f);
                    if (rig != null && rig.cam != null)
                        rig.Shake(Mathf.Clamp01(1f - Vector3.Distance(rig.cam.transform.position, e.pos) / 90f) * 0.07f);
                    break;
                }
                case 3:
                    // Sentinel: a hard amber bolt and a puff from the muzzle.
                    AddFlare(e.pos, fwd, 2.4f, 0.9f, new Color(3.2f, 2.0f, 0.8f), 0.07f);
                    Emit(glow, e.pos, fwd, 1.8f, 0.1f, new Color(1f, 0.75f, 0.35f));
                    Emit(smoke, e.pos + fwd * 0.4f, fwd * 2.5f + Vector3.up * 0.4f, 0.7f, 0.8f, new Color(0.62f, 0.6f, 0.58f, 0.35f));
                    break;
                case 2:
                    // Skimmer: a pulse of plasma in the team's colour.
                    AddFlare(e.pos, fwd, 1.5f, 0.7f, c * 2.6f + new Color(0.6f, 0.6f, 0.6f), 0.06f);
                    Emit(glow, e.pos, fwd, 1.2f, 0.08f, Color.Lerp(Color.white, c, 0.6f));
                    break;
                default:
                    // Rifles: a quick star-shaped flash.
                    AddFlare(e.pos, fwd, 0.9f, 0.38f, Color.Lerp(new Color(3f, 2.4f, 1.4f), c * 2f, 0.3f), 0.045f);
                    Emit(glow, e.pos, fwd, 0.7f, 0.05f, Color.Lerp(Color.white, c, 0.4f));
                    break;
            }
        }

        void ImpactSparks(Vector3 at, int team, int kind)
        {
            Color c = kind == 3 ? new Color(1f, 0.7f, 0.3f) : Color.Lerp(new Color(1f, 0.75f, 0.4f), TeamColor(team), 0.35f);
            for (int i = 0; i < 6; i++)
                Emit(sparks, at, Random.insideUnitSphere * 7f + Vector3.up * 2f, 0.18f, Random.Range(0.15f, 0.35f), c);
            Emit(glow, at, Vector3.zero, 0.9f, 0.08f, c);
            // Rounds striking the water ring it; the ground, a puff of dust.
            if (at.y - world.Map.HeightAt(new Vector2(at.x, at.z)) < 0.4f && world.Map.WaterDepth(new Vector2(at.x, at.z)) > 0.15f)
            {
                AddRipple(new Vector3(at.x, world.Map.waterLevel + 0.03f, at.z), 1.8f, 1.2f, 0);
                for (int i = 0; i < 4; i++)
                    Emit(droplets, new Vector3(at.x, world.Map.waterLevel, at.z), Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(2f, 4f),
                         Random.Range(0.1f, 0.18f), Random.Range(0.4f, 0.6f), new Color(0.86f, 0.94f, 1f, 0.75f));
            }
            else if (at.y - world.Map.HeightAt(new Vector2(at.x, at.z)) < 0.4f)
                Emit(smoke, at + Vector3.up * 0.2f, Vector3.up * Random.Range(0.6f, 1.2f), Random.Range(0.6f, 0.9f), Random.Range(0.5f, 0.8f),
                     new Color(0.5f, 0.45f, 0.38f, 0.35f), Random.Range(0f, 360f));
        }

        void CrystalShatter(Vector3 at)
        {
            // The seam gives up its light: a burst of shards, and motes that hang in
            // the air a while before they fade.
            for (int i = 0; i < 24; i++)
                Emit(sparks, at, Random.insideUnitSphere * 8f + Vector3.up * 4f, 0.3f, Random.Range(0.4f, 1.0f), OreLight * 1.4f);
            for (int i = 0; i < 14; i++)
                Emit(glow, at + Random.insideUnitSphere * 1.2f + Vector3.up, Random.insideUnitSphere * 0.4f + Vector3.up * 0.3f,
                     Random.Range(0.2f, 0.35f), Random.Range(2.5f, 4f), Color.Lerp(OreLight, OreRim, Random.value * 0.5f) * 1.3f);
            Emit(glow, at, Vector3.zero, 5f, 0.6f, OreLight);
        }

        /// <summary>A Mauler driving through a boulder: no fire, just broken rock
        /// thrown forward and a cloud of dust, leaving a crater.</summary>
        void RockSmash(Vector3 at, float radius, Vector3 forward)
        {
            float t = Time.time;
            if (debris != null)
                for (int i = 0; i < 26; i++)
                {
                    Vector3 v = Random.insideUnitSphere * 5f + forward * 3.5f;
                    v.y = Mathf.Abs(v.y) + Random.Range(2.5f, 6f);
                    float g = Random.Range(0.32f, 0.5f);
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = at + Random.insideUnitSphere * radius * 0.6f,
                        velocity = v,
                        startSize = Random.Range(0.15f, 0.45f) * radius,
                        startLifetime = Random.Range(2.5f, 4f),
                        startColor = new Color(g, g * 0.95f, g * 0.9f),
                        rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                        applyShapeToPosition = false
                    }, 1);
                }
            for (int i = 0; i < 14; i++)
            {
                float a = (i + Random.value * 0.5f) / 14f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Emit(trail, at + dir * radius * 0.6f, dir * Random.Range(2.5f, 4.5f) + Vector3.up * Random.Range(0.6f, 1.6f),
                     Random.Range(2.6f, 3.8f) * radius, Random.Range(1.4f, 2.2f), new Color(0.54f, 0.48f, 0.41f, 0.5f), Random.Range(0f, 360f));
            }
            Vector3 ground = new Vector3(at.x, world.Map.HeightAt(new Vector2(at.x, at.z)), at.z);
            if (scorchList.Count >= 96) scorchList.RemoveAt(0);
            scorchList.Add(new Scorch { pos = ground, rot = Random.Range(0f, 6.283f), size = radius * 1.6f, born = t, frame = Random.Range(0, 4) });
            if (rig != null && rig.cam != null)
                rig.Shake(Mathf.Clamp01(1.2f - Vector3.Distance(rig.cam.transform.position, at) / 140f) * 0.12f);
        }

        void Explosion(Vector3 at, float scale, bool structure, bool secondary = false)
        {
            float t = Time.time;
            float root = Mathf.Sqrt(scale);
            float gy = world.Map.HeightAt(new Vector2(at.x, at.z));
            bool grounded = at.y - gy < 3.5f * scale;
            // Over water, the ground burst is a burst of water instead.
            bool inWater = grounded && world.Map.WaterDepth(new Vector2(at.x, at.z)) > 0.25f;
            if (inWater)
            {
                WaterBurst(at, scale);
                grounded = false;
            }

            // 1. The flash: a white-hot core for a tenth of a second.
            Emit(glow, at + Vector3.up * 0.6f * scale, Vector3.zero, 6.5f * scale, 0.12f, new Color(1f, 0.9f, 0.7f));
            Emit(glow, at + Vector3.up * scale, Vector3.zero, 9f * scale, 0.3f, new Color(1f, 0.5f, 0.18f));

            // 2. The fireball: several flipbook puffs of different sizes and
            // rotations billowing up and out, tinted so the sheet's detail survives
            // instead of blowing out to one flat yellow.
            int puffs = structure ? (secondary ? 3 : 7) : 4;
            for (int i = 0; i < puffs; i++)
            {
                Vector3 off = i == 0 ? Vector3.zero : Random.insideUnitSphere * scale * (structure ? 1.3f : 0.8f);
                off.y = Mathf.Abs(off.y) * 0.6f;
                Vector3 vel = Vector3.up * Random.Range(1.0f, 2.2f) * root + new Vector3(off.x, 0f, off.z) * 0.8f;
                float size = Random.Range(2.4f, 3.6f) * scale * (i == 0 ? 1.2f : 0.85f);
                Color tint = Color.Lerp(new Color(1f, 0.86f, 0.7f), new Color(1f, 0.7f, 0.5f), Random.value);
                if (i < 2) Emit(fire, at + off + Vector3.up * 0.5f * scale, vel, size, Random.Range(0.75f, 1.0f) * root, tint, Random.Range(0f, 360f));
                else EmitLater(Random.Range(0.03f, 0.12f), 0, at + off + Vector3.up * 0.5f * scale, vel, size, Random.Range(0.7f, 0.95f) * root, tint, Random.Range(0f, 360f));
            }

            // 3. Smoke: dark billows already full-sized while the fire burns (the
            // still frame), then a column from the animated sheet that takes over
            // as the fire dies, rising and spreading for seconds.
            int billows = structure ? (secondary ? 2 : 6) : 4;
            for (int i = 0; i < billows; i++)
            {
                float shade = Random.Range(0.17f, 0.27f);
                EmitLater(Random.Range(0.02f, 0.12f), 5, at + Random.insideUnitSphere * scale * 0.8f + Vector3.up * scale * 0.6f,
                          new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(1.4f, 2.6f) * root, Random.Range(-0.8f, 0.8f)),
                          Random.Range(3.6f, 5.2f) * scale, Random.Range(2.2f, 3.2f) * root, new Color(shade, shade * 0.95f, shade * 0.9f, 0.85f),
                          Random.Range(0f, 360f));
            }
            int plumes = structure ? (secondary ? 2 : 6) : (scale > 1.2f ? 4 : 3);
            for (int i = 0; i < plumes; i++)
            {
                float shade = Random.Range(0.24f, 0.36f);
                EmitLater(Random.Range(0.08f, 0.3f), 1, at + Random.insideUnitSphere * scale * 0.7f + Vector3.up * scale * 0.8f,
                          new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(1.6f, 3.0f) * root, Random.Range(-0.6f, 0.6f)),
                          Random.Range(3.2f, 5f) * scale, Random.Range(2.6f, 4f) * root, new Color(shade, shade * 0.96f, shade * 0.92f, 0.78f),
                          Random.Range(0f, 360f));
            }

            // 4. Sparks: short, hot and quick to die, rather than long sticks.
            int nSparks = Mathf.RoundToInt(10 * scale);
            for (int i = 0; i < nSparks; i++)
            {
                Vector3 v = Random.insideUnitSphere * 12f * root;
                v.y = Mathf.Abs(v.y) + 4f;
                Emit(sparks, at, v, 0.22f * root, Random.Range(0.25f, 0.6f), new Color(1f, Random.Range(0.6f, 0.8f), 0.3f));
            }

            // 5. Debris: small, dark, charred chunks thrown out on arcs, the hot ones
            // trailing fire, bouncing off the terrain and settling.
            if (debris != null)
            {
                int n = structure ? (secondary ? 8 : 30) : Mathf.RoundToInt(8f * scale);
                float sz = root * (structure ? 1.3f : 1f);
                for (int i = 0; i < n; i++)
                {
                    Vector3 v = Random.insideUnitSphere * 8f * scale;
                    v.y = Mathf.Abs(v.y) * 1.3f + Random.Range(4f, 10f) * root;
                    float g = Random.Range(0.3f, 0.65f);
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = at + Random.insideUnitSphere * 0.6f * scale + Vector3.up * 0.4f,
                        velocity = v,
                        startSize = Random.Range(0.05f, 0.17f) * sz,
                        startLifetime = Random.Range(2.5f, 4.5f),
                        startColor = new Color(g, g * 0.97f, g * 0.95f),
                        rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                        applyShapeToPosition = false
                    }, 1);
                }
            }

            // Embers drift up out of the fireball.
            int nEmbers = Mathf.RoundToInt((structure ? 26 : 8) * scale);
            for (int i = 0; i < nEmbers; i++)
                Emit(embers, at + Random.insideUnitSphere * scale + Vector3.up * 0.5f * scale,
                     Random.insideUnitSphere * 2.5f + Vector3.up * Random.Range(1.5f, 4f),
                     Random.Range(0.08f, 0.18f), Random.Range(1.4f, 2.8f), new Color(1f, Random.Range(0.45f, 0.7f), 0.2f));

            if (grounded)
            {
                Vector3 ground = new Vector3(at.x, gy, at.z);
                // 6. A ground burst throws a fountain of earth: dark clods on arcs
                // and a brown spray column that slows and hangs.
                if (debris != null)
                    for (int i = 0; i < Mathf.RoundToInt(12 * scale); i++)
                    {
                        Vector3 v = Random.insideUnitSphere * 4f * root;
                        v.y = Random.Range(6f, 13f) * root;
                        // Tinted by the debris material's grey, so these read as soil.
                        float g = Random.Range(0.6f, 0.95f);
                        debris.Emit(new ParticleSystem.EmitParams
                        {
                            position = ground + Random.insideUnitSphere * 0.5f * scale + Vector3.up * 0.2f,
                            velocity = v,
                            startSize = Random.Range(0.05f, 0.13f) * root,
                            startLifetime = Random.Range(1.8f, 3f),
                            startColor = new Color(g * 1.15f, g * 0.95f, g * 0.75f),
                            rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                            applyShapeToPosition = false
                        }, 1);
                    }
                for (int i = 0; i < (structure ? 8 : 5); i++)
                    Emit(trail, ground + Random.insideUnitSphere * 0.6f * scale + Vector3.up * 0.3f,
                         new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(6f, 12f) * root, Random.Range(-1.5f, 1.5f)),
                         Random.Range(2.2f, 3.2f) * scale, Random.Range(1.1f, 1.7f), new Color(0.44f, 0.37f, 0.29f, 0.62f), Random.Range(0f, 360f));

                // 7. A ring of dust rolling out over the ground behind a shockwave.
                int nDust = structure ? 20 : 12;
                for (int i = 0; i < nDust; i++)
                {
                    float a = (i + Random.value * 0.5f) / nDust * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Emit(trail, ground + dir * 0.8f * scale + Vector3.up * 0.4f,
                         dir * Random.Range(6f, 10f) * root + Vector3.up * 0.4f,
                         Random.Range(2.4f, 3.6f) * scale, Random.Range(1.3f, 2.1f), new Color(0.50f, 0.44f, 0.37f, 0.45f), Random.Range(0f, 360f));
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
            Flash(at + Vector3.up * 2f, new Color(1f, 0.55f, 0.22f), 9f * scale, 10f * scale, 0.4f);

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

        // ------------------------------------------------------------ water
        void AddRipple(Vector3 at, float size, float life, int kind)
        {
            if (rippleList.Count >= 400) rippleList.RemoveAt(0);
            rippleList.Add(new Ripple { pos = at, size = size, life = life, kind = kind, born = Time.time, seed = Random.value });
        }

        /// <summary>A body breaking the surface: rings racing out, foam, a burst of
        /// drops and a little mist. <paramref name="size"/> is about the body's radius.</summary>
        void Splash(Vector3 at, float size, Vector3 push)
        {
            float w = world.Map.waterLevel + 0.03f;
            Vector3 s = new Vector3(at.x, w, at.z);
            AddRipple(s, size * 3.2f, 1.1f, 0);
            AddRipple(s, size * 5.5f, 1.8f, 0);
            AddRipple(s, size * 2.6f, 2.4f, 1);
            int drops = Mathf.RoundToInt(10 + size * 10);
            for (int i = 0; i < drops; i++)
            {
                Vector3 v = Random.insideUnitSphere * 2.2f * size + push * 0.6f;
                v.y = Random.Range(2.5f, 5.5f) * Mathf.Sqrt(size);
                Emit(droplets, s + Random.insideUnitSphere * size * 0.5f + Vector3.up * 0.1f, v,
                     Random.Range(0.14f, 0.3f), Random.Range(0.6f, 1.0f), new Color(0.86f, 0.94f, 1f, 0.8f));
            }
            for (int i = 0; i < 3; i++)
                Emit(trail, s + Random.insideUnitSphere * size * 0.6f + Vector3.up * 0.3f,
                     Random.insideUnitSphere * 0.8f + Vector3.up * 0.8f + push * 0.3f, Random.Range(1.2f, 2f) * size,
                     Random.Range(0.8f, 1.3f), new Color(0.88f, 0.93f, 0.95f, 0.35f), Random.Range(0f, 360f));
        }

        /// <summary>A shell bursting in the water: a white column and a spreading ring
        /// instead of a fountain of earth.</summary>
        void WaterBurst(Vector3 at, float scale)
        {
            float w = world.Map.waterLevel + 0.03f;
            Vector3 s = new Vector3(at.x, w, at.z);
            float root = Mathf.Sqrt(scale);
            AddRipple(s, 9f * scale, 1.6f, 0);
            AddRipple(s, 16f * scale, 2.6f, 0);
            AddRipple(s, 6f * scale, 3.5f, 1);
            for (int i = 0; i < Mathf.RoundToInt(40 * scale); i++)
            {
                Vector3 v = Random.insideUnitSphere * 2.5f * root;
                v.y = Random.Range(7f, 15f) * root;
                Emit(droplets, s + Random.insideUnitSphere * 0.6f * scale, v, Random.Range(0.2f, 0.45f), Random.Range(0.9f, 1.6f),
                     new Color(0.9f, 0.96f, 1f, 0.85f));
            }
            for (int i = 0; i < 7; i++)
                Emit(trail, s + Random.insideUnitSphere * 0.7f * scale + Vector3.up * 0.5f,
                     new Vector3(Random.Range(-1f, 1f), Random.Range(5f, 10f) * root, Random.Range(-1f, 1f)),
                     Random.Range(2f, 3f) * scale, Random.Range(1.2f, 1.9f), new Color(0.9f, 0.94f, 0.96f, 0.55f), Random.Range(0f, 360f));
        }

        /// <summary>Wakes behind units moving through water, and a splash where one goes
        /// in or comes out.</summary>
        void WaterEffects(float dt)
        {
            var map = world.Map;
            float level = map.waterLevel;
            foreach (var u in world.units)
            {
                if (u == null || u.dying || !u.def.IsMobile) continue;
                float depth = map.WaterDepth(u.pos);
                bool skimmer = u.Type == UnitType.Skimmer;
                bool wet = depth > (skimmer ? 0.05f : 0.18f);
                bool had = waterState.TryGetValue(u.id, out var st);
                if (!had) st = (wet, u.pos);
                bool seen = u.visibleToPlayer || MatchSettings.spectate;
                Vector3 vel = u.agent != null && u.agent.enabled ? u.agent.velocity : Vector3.zero;
                float speed = new Vector2(vel.x, vel.z).magnitude;
                float r = u.def.radius;

                if (had && wet != st.wet && seen && speed > 0.8f)
                    Splash(new Vector3(u.pos.x, level, u.pos.y), r * (skimmer ? 0.8f : 1f), vel.normalized * Mathf.Min(speed, 6f));

                if (wet && seen && speed > 0.6f)
                {
                    float spacing = skimmer ? 1.6f : Mathf.Max(0.7f, r * 0.9f);
                    if ((u.pos - st.last).sqrMagnitude > spacing * spacing)
                    {
                        st.last = u.pos;
                        Vector3 at = new Vector3(u.pos.x, level + 0.03f, u.pos.y);
                        // Deeper wading pushes a bigger bow wave.
                        float bulk = Mathf.Lerp(0.8f, 1.25f, Mathf.Clamp01(depth / 0.9f));
                        AddRipple(at, r * (skimmer ? 3.4f : 2.6f) * bulk, skimmer ? 2.4f : 2.0f, 0);
                        AddRipple(at - vel.normalized * r * 0.5f, r * (skimmer ? 2.4f : 1.8f), skimmer ? 1.6f : 2.2f, 1);
                        if (Random.value < 0.6f)
                            Emit(droplets, at + Random.insideUnitSphere * r * 0.6f + vel.normalized * r * 0.7f,
                                 vel * 0.4f + Vector3.up * Random.Range(1.5f, 3f) + Random.insideUnitSphere,
                                 Random.Range(0.1f, 0.2f), Random.Range(0.4f, 0.7f), new Color(0.86f, 0.94f, 1f, 0.7f));
                    }
                }
                else if (!wet) st.last = u.pos;
                st.wet = wet;
                waterState[u.id] = st;
            }
            if (Time.frameCount % 150 == 0 && waterState.Count > 0)
            {
                waterGone.Clear();
                foreach (var id in waterState.Keys)
                {
                    bool alive = false;
                    foreach (var u in world.units) if (u != null && !u.dying && u.id == id) { alive = true; break; }
                    if (!alive) waterGone.Add(id);
                }
                foreach (var id in waterGone) waterState.Remove(id);
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

            for (int i = delayed.Count - 1; i >= 0; i--)
            {
                var d = delayed[i];
                if (now < d.at) continue;
                delayed.RemoveAt(i);
                Emit(SystemAt(d.system), d.pos, d.vel, d.size, d.life, d.color, d.rotation);
            }
            for (int i = flares.Count - 1; i >= 0; i--)
                if (now - flares[i].born > flares[i].life) flares.RemoveAt(i);

            AmbientEffects(dt);
            LayTracks(now);
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

        /// <summary>Tracked vehicles leave tread marks: a segment each time one has
        /// moved about a metre, laid only while the player can see it and not in
        /// the water. Marks fade over TrackLife seconds.</summary>
        void LayTracks(float now)
        {
            var map = world.Map;
            foreach (var u in world.units)
            {
                if (u == null || u.dying || (u.Type != UnitType.Mauler && u.Type != UnitType.Worker)) continue;
                Vector3 p = u.Ground;
                if (!lastTrack.TryGetValue(u.id, out var last)) { lastTrack[u.id] = p; continue; }
                Vector3 d = p - last;
                d.y = 0f;
                float moved = d.magnitude;
                bool mauler = u.Type == UnitType.Mauler;
                if (moved < (mauler ? 1.0f : 0.8f)) continue;
                lastTrack[u.id] = p;
                if (moved > 6f || !(u.visibleToPlayer || MatchSettings.spectate)) continue;
                Vector3 mid = (p + last) * 0.5f;
                mid.y = map.HeightAt(new Vector2(mid.x, mid.z));
                if (mid.y < map.waterLevel + 0.05f) continue;
                // Mauler tracks sit 1.30 m either side of the centre line, 0.66 m wide;
                // a Digger's 0.60 m out, 0.32 m wide. Width is the box across both.
                float width = mauler ? 3.4f : 1.6f;
                trackMarks[trackHead] = new TrackMark
                {
                    pos = mid, yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, length = moved * 1.15f, width = width,
                    born = now, band = (mauler ? 1.30f : 0.60f) / (width * 0.5f), halfBand = (mauler ? 0.33f : 0.17f) / (width * 0.5f),
                    pitch = mauler ? 3.2f : 4.5f
                };
                trackHead = (trackHead + 1) % MaxTracks;
                trackCount = Mathf.Min(trackCount + 1, MaxTracks);
            }
            if (lastTrack.Count > 256 || Time.frameCount % 120 == 0)
            {
                trackGone.Clear();
                foreach (var id in lastTrack.Keys)
                {
                    bool alive = false;
                    foreach (var u in world.units) if (u != null && !u.dying && u.id == id) { alive = true; break; }
                    if (!alive) trackGone.Add(id);
                }
                foreach (var id in trackGone) lastTrack.Remove(id);
            }
        }

        void AmbientEffects(float dt)
        {
            float water = world.Map.waterLevel;
            WaterEffects(dt);
            BurningPlants(dt);
            foreach (var u in world.units)
            {
                if (u == null || u.dying) continue;
                // Motes of light rise off the ore the player can see, slowly, circling
                // the seam as they go, more of them as it breathes in; and now and then
                // a faint wisp of pale light drifts up the tallest shard.
                if (u.Type == UnitType.Ore && Seen(new Vector3(u.pos.x, 0f, u.pos.y)))
                {
                    float breathe = OreBreath(u.pos, Time.time);
                    if (Random.value < dt * (0.8f + 1.6f * breathe))
                    {
                        float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(0.4f, 1.2f);
                        var off = new Vector3(Mathf.Cos(a) * r, Random.Range(0.2f, 1.6f), Mathf.Sin(a) * r);
                        var swirl = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Random.Range(0.12f, 0.3f);
                        Emit(glow, u.Ground + off, swirl + Vector3.up * Random.Range(0.25f, 0.6f),
                             Random.Range(0.14f, 0.28f), Random.Range(2.4f, 3.8f), Color.Lerp(OreLight, OreRim, Random.value * 0.35f) * 1.3f);
                    }
                    if (Random.value < dt * 0.25f * breathe)
                        Emit(glow, u.Ground + Vector3.up * Random.Range(0.8f, 1.6f), Vector3.up * 0.35f,
                             Random.Range(1.2f, 1.8f), Random.Range(2.5f, 3.5f), OreLight * 0.35f);
                }
                if (!u.visibleToPlayer) continue;
                // Wading and skimming over water throw up spray.
                if (u.agent != null && u.agent.enabled && u.def.IsMobile)
                {
                    float depth = world.Map.WaterDepth(u.pos);
                    float v2 = u.agent.velocity.sqrMagnitude;
                    if (depth > 0.12f && v2 > 1f && Random.value < dt * (u.Type == UnitType.Skimmer ? 26f : 12f))
                    {
                        Vector3 at = new Vector3(u.pos.x, water + 0.05f, u.pos.y);
                        Vector3 back = -u.agent.velocity.normalized;
                        Emit(trail, at + back * u.def.radius * 0.6f + Random.insideUnitSphere * u.def.radius * 0.4f,
                             back * Random.Range(0.4f, 1.2f) + Vector3.up * Random.Range(0.4f, 1.1f),
                             Random.Range(0.6f, 1.1f) * (0.6f + u.def.radius * 0.4f), Random.Range(0.6f, 1.0f),
                             new Color(0.86f, 0.92f, 0.94f, 0.3f), Random.Range(0f, 360f));
                    }
                }
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
                             u.def.radius * 1.4f, Random.Range(2.6f, 3.8f), new Color(0.32f, 0.31f, 0.3f, 0.6f));
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

            // Shells leave a smoke trail: a puff every TrailSpacing metres along the
            // path, so it reads as a continuous arc whatever the frame rate.
            foreach (var p in world.projectiles)
            {
                if (p.kind != 1) continue;
                if (!trailState.TryGetValue(p, out var st) || p.age < st.age)
                    st = (p.prevPos, p.age);
                Vector3 seg = p.pos - st.last;
                float len = seg.magnitude;
                if (len >= TrailSpacing && Seen(p.pos))
                {
                    Vector3 dir = seg / len;
                    int n = Mathf.Min(24, Mathf.FloorToInt(len / TrailSpacing));
                    for (int k = 1; k <= n; k++)
                        Emit(trail, st.last + dir * (k * TrailSpacing) + Random.insideUnitSphere * 0.08f,
                             Random.insideUnitSphere * 0.25f + Vector3.up * 0.15f, Random.Range(0.45f, 0.6f), Random.Range(0.8f, 1.2f),
                             new Color(0.8f, 0.78f, 0.75f, 0.34f), Random.Range(0f, 360f));
                    st.last += dir * (n * TrailSpacing);
                }
                else if (len >= TrailSpacing) st.last = p.pos;
                st.age = p.age;
                trailState[p] = st;
            }
            if (trailState.Count > 0)
            {
                trailGone.Clear();
                foreach (var kv in trailState) if (!kv.Key.alive) trailGone.Add(kv.Key);
                foreach (var gone in trailGone) trailState.Remove(gone);
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

            for (int k = 0; k < trackCount; k++)
            {
                ref var m = ref trackMarks[(trackHead - trackCount + k + MaxTracks) % MaxTracks];
                float age = now - m.born;
                if (age > TrackLife) continue;
                if (!Seen(m.pos) && !world.Explored(me, new Vector2(m.pos.x, m.pos.z))) continue;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, age / TrackLife);
                tracks.Add(Matrix4x4.TRS(m.pos, Quaternion.Euler(0f, m.yaw, 0f), new Vector3(m.width, 2.5f, m.length)),
                           new Color(0.13f, 0.105f, 0.08f, 0.34f * fade), new Vector4(6f, fade, m.band, m.halfBand),
                           new Vector4(m.pitch, 0f, 0f, 0f));
            }
            tracks.Draw(cube, scorchMaterial);

            // Ore light pools on the ground round each seam the player knows about,
            // breathing slowly, and the glow a loaded Digger spills from its hopper.
            foreach (var u in world.units)
            {
                if (u == null || u.dying) continue;
                if (u.Type == UnitType.Ore)
                {
                    if (!Seen(new Vector3(u.pos.x, 0f, u.pos.y)) && !world.Explored(me, u.pos)) continue;
                    float left = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(u.oreLeft / (float)Unit.NodeCapacity));
                    // In step with the crystals' own breath (SF_Unit's crystal path uses
                    // the same phase and period), deep and slow.
                    float breathe = OreBreath(u.pos, now);
                    float size = 8.5f * left * (0.94f + 0.06f * breathe);
                    rings.Add(Matrix4x4.TRS(u.Ground, Quaternion.identity, new Vector3(size, 3f, size)),
                              OreLight * (left * Mathf.Lerp(0.3f, 0.85f, breathe) * 0.6f), new Vector4(7f, 0f, 0f, 0f));
                }
                else if (u.Type == UnitType.Worker && (u.visibleToPlayer || MatchSettings.spectate) && u.view != null && u.view.OreGlow > 0.01f)
                {
                    float g = u.view.OreGlow;
                    rings.Add(Matrix4x4.TRS(u.Ground, Quaternion.identity, new Vector3(4.2f + g * 1.4f, 4f, 4.2f + g * 1.4f)),
                              OreLight * (g * (0.9f + 0.1f * Mathf.Sin(now * 1.6f + u.id))), new Vector4(7f, 0f, 0f, 0f));
                }
            }

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
                if (!Seen(p.pos)) continue;
                Vector3 seg = p.pos - p.prevPos;
                float len = seg.magnitude;
                if (len < 0.01f) continue;
                Vector3 dir = seg / len;
                float streak, width;
                Color c, halo;
                switch (p.kind)
                {
                    // Shell: a glowing slug inside a wide orange glow.
                    case 1: streak = Mathf.Clamp(len * 1.2f, 1.2f, 2.6f); width = 0.42f; c = new Color(3.4f, 2.0f, 0.8f); halo = new Color(0.9f, 0.4f, 0.12f); break;
                    case 2: streak = 1.8f; width = 0.4f; c = p.team == 0 ? new Color(0.6f, 1.8f, 2.8f) : new Color(2.8f, 0.9f, 0.45f); halo = c * 0.3f; break;
                    case 3: streak = 4.5f; width = 0.3f; c = new Color(2.8f, 1.7f, 0.65f); halo = new Color(0.7f, 0.4f, 0.12f); break;
                    default: streak = Mathf.Min(len * 1.5f, 3.5f); width = 0.12f; c = p.team == 0 ? new Color(0.9f, 1.8f, 2.6f) : new Color(2.6f, 1.2f, 0.5f); halo = Color.clear; break;
                }
                AddStreak(p.pos, dir, streak, width, c, 1f);
                if (halo.maxColorComponent > 0f) AddStreak(p.pos, dir, streak * 1.4f, width * 3f, halo, 1f);
            }
            foreach (var f in flares)
            {
                float k = Mathf.Clamp01((now - f.born) / Mathf.Max(0.01f, f.life));
                float fade = (1f - k) * (1f - k);
                // A flare grows a little as it fades; uv 0 sits on the muzzle.
                float length = f.length * (0.75f + 0.35f * k);
                AddStreak(f.pos + f.dir * length, f.dir, length, f.width * (0.85f + 0.3f * k), f.color * fade, 2f);
            }
            streaks.Draw(quad, streakMaterial);

            // Wakes, splashes and foam, lying flat on the water.
            for (int i = rippleList.Count - 1; i >= 0; i--)
            {
                var r = rippleList[i];
                float t = (now - r.born) / r.life;
                if (t >= 1f) { rippleList.RemoveAt(i); continue; }
                ripples.Add(Matrix4x4.TRS(r.pos, Quaternion.Euler(90f, r.seed * 360f, 0f), new Vector3(r.size, r.size, 1f)),
                            new Color(0.9f, 0.95f, 0.97f, r.kind == 0 ? 0.9f : 0.75f), new Vector4(r.kind, t, r.seed, 0f));
            }
            ripples.Draw(quad, rippleMaterial);
        }
    }
}
