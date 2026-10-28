// InfantryCape.cs — the cape a soldier may earn: at each promotion a soldier without one
// gets one 81% of the time (a hash of the unit and its new rank, never the match's random
// stream), in a deep shade of the side's colour with a hem band that marks the rank. A
// knight's cape: hung from the shoulders under the pauldrons, the pack worn over it, falling
// to the ankles in deep folds and flaring wide. It is Unity Cloth near the camera -- the
// shoulders and the stretch under the pack held, the skirt free, thrown back by the soldier's
// own movement and blown by the match's wind. Away from the camera, above 3.5x, through a
// hitch or past the budget of simulated capes it is the same mesh at rest, leant back on a
// spring as the soldier moves. Presentation only: UnitView makes and ticks it and the
// simulation never knows it is there.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using StarForge.Sim;

namespace StarForge.View
{
    public sealed class InfantryCape : MonoBehaviour
    {
        /// <summary>The chance a promotion brings a cape to a soldier without one.</summary>
        public const float Chance = 0.81f;
        const int Cols = 13, Rows = 14;          // cloth vertices across and down
        const int MaxSimulated = 24;             // cloth capes at once, the nearest the camera's focus
        const float SimNear = 42f, SimFar = 50f; // metres from the focus a cape starts / stops simulating
        const float MaxStep = 0.1f;              // a longer frame (a hitch) is not simulated
        const float Resume = 1f;                 // seconds of good frames after a hitch before cloth comes back
        const float Incumbent = 6f;              // metres a cape already cloth is ranked nearer (no flicking at the 24th)
        const float MaxLean = 20f;               // degrees the rigid skirt swings back at a run
        const float FabricGain = 6.4f;           // 1 / the fabric texture's linear average over its weave
        const float Crumple = 0.6f;              // seconds a dead soldier's cape takes to bunch up on his back
        static readonly Vector3 Crumpled = new Vector3(0.55f, 0.24f, 0.4f);   // its size bunched (of the hung cape)
        const float Reach = 3f;                  // the hem rests ~1.4 from the shoulders: only NaN/Inf gets past this
        // Body space (the model faces +Z, 2 units tall): the shoulder line it hangs from, where
        // it comes out from under the pack, and the hem.
        const float ShoulderY = 1.50f, ShoulderZ = -0.20f, HalfTop = 0.52f;
        const float FreeY = 1.02f, HemY = 0.22f, HalfHem = 0.95f;

        static readonly bool Off = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sfnocapes") >= 0;
        /// <summary>For the fps A/B at the full budget: <c>-sfallcapes</c> on the player gives every
        /// soldier a cape from the moment he is trained (so up to 24 are cloth near the camera).</summary>
        public static readonly bool All = !Off && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sfallcapes") >= 0;
        static readonly List<InfantryCape> all = new List<InfantryCape>();
        static readonly List<InfantryCape> ranked = new List<InfantryCape>();
        static readonly ByDistance byDistance = new ByDistance();
        static int rankedFrame = -1;
        static float hitchUntil, nextFind;
        static Material capeMat;
        static RTSCamera rig;

        sealed class ByDistance : IComparer<InfantryCape>
        {
            public int Compare(InfantryCape a, InfantryCape b) => a.focusDistance.CompareTo(b.focusDistance);
        }

        /// <summary>Statics outlive a play session (no domain reload): start each one clean.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
            ranked.Clear();
            rankedFrame = -1;
            hitchUntil = nextFind = 0f;
            rig = null;
            RestOnly = false;
            Simulating = Worn = GaveUp = Hitches = 0;
        }

        /// <summary>For trials (the cost of the cloth): every cape at rest while set. Whoever sets it clears it.</summary>
        public static bool RestOnly;
        /// <summary>Capes in the scene (wrecks' included), those on living soldiers, and how many of
        /// them may be cloth this frame (for trials and the benchmark).</summary>
        public static int Live => all.Count;
        public static int Worn { get; private set; }
        public static int Simulating { get; private set; }
        /// <summary>Capes whose cloth went bad and were left at rest for good.</summary>
        public static int GaveUp { get; private set; }
        /// <summary>Hitches that sent every cape to rest for a second (for trials).</summary>
        public static int Hitches { get; private set; }

        Mesh mesh;
        SkinnedMeshRenderer smr;
        Cloth cloth;
        int team, rank, id;
        bool shown = true, frozen, gaveUp, simulating, wanted;
        float focusDistance, lean, leanVel, nextCheck, crumple;
        Quaternion restRot;
        Vector3 restPos;
        /// <summary>Times this cape went from cloth to rest and back (for trials: flicker).</summary>
        public int Switches { get; private set; }

        public int Rank => rank;
        public bool IsCloth => simulating;
        /// <summary>For trials: the cape at rest (its own space), the cloth's vertices while it
        /// simulates (allocates; null otherwise), whether it is drawn.</summary>
        public Mesh Rest => mesh;
        public Vector3[] ClothVertices() => simulating && cloth != null ? cloth.vertices : null;
        public bool Drawn => smr != null && smr.enabled;

        /// <summary>Soldiers on foot earn capes; workers (the Prospector's hopper is on its back),
        /// creatures and machines do not.</summary>
        public static bool Eligible(UnitDef d) =>
            d != null && d.upgradeClass == UnitClass.Infantry && !d.harvester && !d.organic && !d.flying && !d.building;

        /// <summary>Whether the promotion of unit <paramref name="unitId"/> to <paramref name="newRank"/>
        /// brings a cape: a hash of the two, so it is the same however the match is watched.</summary>
        public static bool Earned(int unitId, int newRank)
        {
            if (All) return true;
            uint h = (uint)unitId * 0x9E3779B1u ^ (uint)newRank * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f < Chance;
        }

        /// <summary>Hangs a cape on <paramref name="body"/>. <paramref name="backZ"/>, how far back the
        /// torso and pack reach, sets how far the skirt stands off below a deep pack.</summary>
        public static InfantryCape Make(Transform body, float backZ, int team, int rank, int id)
        {
            var fx = FXDirector.Current;
            if (Off || body == null || fx == null || fx.debrisMaterial == null) return null;
            if (capeMat == null)
            {
                // The banner's material (URP Particles Simple Lit), both sides drawn, white
                // under the vertex colours that paint the cape: no new shader or variant.
                capeMat = new Material(fx.debrisMaterial) { name = "SF_Cape" };
                capeMat.SetColor("_BaseColor", Color.white);
                capeMat.SetFloat("_Cull", 0f);
                if (fx.capeFabric != null)
                {
                    // The woven cloth with its embroidered hem band along the bottom (the hem rows'
                    // v), brightened by its own average (0.156 linear over the weave) so the cape keeps
                    // the shade its vertex colours give it.
                    capeMat.SetTexture("_BaseMap", fx.capeFabric);
                    // SetColor takes gamma values and makes them linear in this linear-space project:
                    // given 6.4 it applied ~59 and the cape came out pale cyan, its hem white.
                    capeMat.SetColor("_BaseColor", new Color(FabricGain, FabricGain, FabricGain, 1f).gamma);
                }
            }
            var go = new GameObject("Cape");
            go.transform.SetParent(body, false);
            go.transform.localPosition = new Vector3(0f, ShoulderY, ShoulderZ);   // the shoulder line: the pivot
            var cape = go.AddComponent<InfantryCape>();
            cape.Build(backZ, team, rank, id);
            return cape;
        }

        void Build(float backZ, int team, int rank, int id)
        {
            this.team = team;
            this.rank = rank;
            this.id = id;
            restRot = transform.localRotation;
            restPos = transform.localPosition;
            mesh = CapeMesh(backZ, team, rank);
            smr = gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterial = capeMat;
            smr.shadowCastingMode = ShadowCastingMode.Off;   // Particles/Simple Lit has no shadow pass
            smr.updateWhenOffscreen = false;
            cloth = gameObject.AddComponent<Cloth>();
            cloth.useGravity = true;
            cloth.damping = 0.2f;
            cloth.stretchingStiffness = 0.95f;
            cloth.bendingStiffness = 0.45f;
            cloth.worldVelocityScale = 0.55f;
            cloth.worldAccelerationScale = 0.5f;
            cloth.sleepThreshold = 0.05f;
            // The shoulders and the stretch under the pack are held where they lie; below, the
            // skirt may swing as far as it hangs below the pack, and never in through the soldier
            // (the backstop at its rest surface, whose normals face out behind him).
            var coeff = cloth.coefficients;
            var verts = cloth.vertices;
            for (int i = 0; i < coeff.Length && i < verts.Length; i++)
            {
                float below = FreeY - (ShoulderY + verts[i].y);
                coeff[i].maxDistance = below <= 0f ? 0f : Mathf.Min(0.55f, 0.06f + below * 0.65f);
                coeff[i].collisionSphereDistance = 0f;
            }
            cloth.coefficients = coeff;
            cloth.enabled = false;
            simulating = false;
            nextCheck = Time.time + 1f + (id % 17) * 0.1f;
            enabled = false;   // Update only folds a dead soldier's cape (UnitView ticks the living)
            all.Add(this);
        }

        /// <summary>A further promotion: the hem band takes the new rank's colour.</summary>
        public void SetRank(int newRank)
        {
            if (newRank == rank || mesh == null) return;
            rank = newRank;
            mesh.colors = Colours(team, rank);
        }

        /// <summary>The soldier is down: the cape stays on the body into the wreck, rigid, and bunches up
        /// toward his shoulders as he falls. Left hung and flared it stood off a body on its side or
        /// face down like a sail; bunched it stays within him whichever way he lands.</summary>
        public void Freeze()
        {
            frozen = true;
            SetCloth(false);
            enabled = true;
        }

        void Update()
        {
            if (!frozen) return;
            crumple = Mathf.Min(1f, crumple + Time.deltaTime / Crumple);
            float k = Mathf.SmoothStep(0f, 1f, crumple);
            lean = Mathf.Lerp(lean, 0f, k);
            Lean(lean);
            transform.localScale = Vector3.Lerp(Vector3.one, Crumpled, k);
            if (crumple >= 1f) enabled = false;
        }

        /// <summary>One call a frame from the soldier's UnitView: whether he is on show, his
        /// velocity in the world, and the way he faces.</summary>
        public void Tick(bool visible, Vector3 velocity, Vector3 forward)
        {
            if (visible != shown)
            {
                shown = visible;
                smr.enabled = visible;
            }
            if (rankedFrame != Time.frameCount) ChooseSimulated();
            bool sim = wanted && shown && !frozen && !gaveUp;
            if (sim != simulating) SetCloth(sim);
            if (!shown || frozen) return;

            if (simulating)
            {
                Vector2 wind = StarForge.World.Wind.At(Time.time);
                float gust = 0.8f + 0.35f * Mathf.PerlinNoise(Time.time * 0.9f, id * 0.37f);
                float flutter = 1f + 4f * wind.magnitude;
                cloth.externalAcceleration = new Vector3(wind.x, 0f, wind.y) * 7f * gust;
                cloth.randomAcceleration = new Vector3(flutter, flutter * 0.4f, flutter);
                // A hitch can leave a cloth NaN: now and then read it back (Cloth.vertices
                // allocates, so a few capes a second, staggered) and leave a bad one at rest.
                if (Time.time >= nextCheck)
                {
                    nextCheck = Time.time + 2f;
                    var v = cloth.vertices;
                    if (v.Length > 0 && !(v[v.Length - 1].magnitude < Reach))
                    {
                        gaveUp = true;
                        GaveUp++;
                        SetCloth(false);
                    }
                }
                return;
            }
            // At rest: the skirt swings back with the soldier's pace on a damped spring, about where
            // it comes out from under the pack (about the shoulders the stretch under the pack went
            // back with it, through the pack's back).
            float pace = Mathf.Max(0f, Vector3.Dot(velocity, forward));
            float target = Mathf.Clamp(pace * 5f, 0f, MaxLean);
            float dt = Mathf.Min(Time.deltaTime, MaxStep);
            leanVel += ((target - lean) * 40f - leanVel * 9f) * dt;
            lean += leanVel * dt;
            Lean(lean);
        }

        /// <summary>Turn the whole cape about the line under the pack: +X takes the hem back.</summary>
        void Lean(float degrees)
        {
            var pivot = new Vector3(0f, FreeY - ShoulderY, -0.26f - ShoulderZ);
            var r = Quaternion.Euler(degrees, 0f, 0f);
            transform.localRotation = restRot * r;
            transform.localPosition = restPos + restRot * (pivot - r * pivot);
        }

        void SetCloth(bool on)
        {
            if (on == simulating || cloth == null) return;
            simulating = on;
            Switches++;
            if (on)
            {
                lean = leanVel = 0f;
                Lean(0f);
                cloth.enabled = true;
                cloth.ClearTransformMotion();
            }
            else cloth.enabled = false;
        }

        /// <summary>Once a frame: which capes may be cloth -- shown, near the camera's focus, at most
        /// MaxSimulated of them, nearest first. Held against flicker: a cape already cloth keeps it
        /// to 50 m (taken up at 42) and ranks Incumbent metres nearer, and after a hitch none is
        /// cloth again until a second of good frames has passed.</summary>
        static void ChooseSimulated()
        {
            rankedFrame = Time.frameCount;
            if (rig == null && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                rig = Object.FindAnyObjectByType<RTSCamera>();
            }
            if (Time.deltaTime > MaxStep)
            {
                if (Time.unscaledTime >= hitchUntil) Hitches++;
                hitchUntil = Time.unscaledTime + Resume;
            }
            bool canRun = !RestOnly && Time.timeScale <= 3.5f && Time.unscaledTime >= hitchUntil && rig != null;
            Vector2 focus = rig != null ? rig.Focus : Vector2.zero;
            ranked.Clear();
            int sim = 0, worn = 0;
            foreach (var c in all)
            {
                c.wanted = false;
                if (c.frozen) continue;
                worn++;
                if (!canRun || !c.shown || c.gaveUp) continue;
                var p = c.transform.position;
                float d = Vector2.Distance(focus, new Vector2(p.x, p.z));
                if (d >= (c.simulating ? SimFar : SimNear)) continue;
                c.focusDistance = c.simulating ? d - Incumbent : d;
                ranked.Add(c);
            }
            if (ranked.Count > MaxSimulated) ranked.Sort(byDistance);
            for (int i = 0; i < ranked.Count && i < MaxSimulated; i++) { ranked[i].wanted = true; sim++; }
            Simulating = sim;
            Worn = worn;
        }

        void OnDestroy()
        {
            all.Remove(this);
            if (mesh != null) Destroy(mesh);
        }

        // ------------------------------------------------------------ mesh

        /// <summary>Body-space depth of the cape's middle at height y: under the pack against the
        /// back, then standing off behind the legs and flaring back to the hem. A deeper pack
        /// (the Scorcher's tanks) pushes the skirt a little further out.</summary>
        static float MiddleZ(float y, float backZ)
        {
            float off = Mathf.Clamp(-0.49f - backZ, 0f, 0.3f) * 0.4f;
            if (y >= FreeY) return Mathf.Lerp(ShoulderZ, -0.26f, Mathf.InverseLerp(ShoulderY, FreeY, y));
            float f = Mathf.InverseLerp(FreeY, HemY, y);
            return -0.26f - off - (0.46f + off) * Mathf.Sqrt(f);   // out fast below the pack, then steadier
        }

        /// <summary>Rows from the shoulders to the hem; across, from wrapped round the left side to
        /// the right, gathered into folds that start at the shoulders and deepen to the hem.</summary>
        static Mesh CapeMesh(float backZ, int team, int rank)
        {
            var v = new Vector3[Cols * Rows];
            var uv = new Vector2[v.Length];
            for (int y = 0; y < Rows; y++)
            {
                float fy = y / (Rows - 1f);
                float by = Mathf.Lerp(ShoulderY, HemY, fy);           // body-space height
                float down = fy;
                float half = Mathf.Lerp(HalfTop, HalfHem, Mathf.Pow(down, 0.8f));
                float mid = MiddleZ(by, backZ);
                for (int x = 0; x < Cols; x++)
                {
                    float fx = x / (Cols - 1f), s = 2f * fx - 1f;
                    // The edges come round toward the front (the cape wraps the soldier a little),
                    // more toward the hem; the shoulders curve round under the pauldrons.
                    float wrap = s * s * (0.06f + 0.30f * down);
                    // Folds: alternate columns in and out, none at the edges.
                    float fold = (x == 0 || x == Cols - 1) ? 0f : (x % 2 == 0 ? 1f : -1f) * (0.012f + 0.075f * Mathf.Pow(down, 1.2f));
                    v[y * Cols + x] = new Vector3(s * half, by - ShoulderY, mid + wrap - fold - ShoulderZ);
                    uv[y * Cols + x] = new Vector2(fx, 1f - fy);
                }
            }
            // Wound so the normals face out behind the soldier: the backstop keeps the cloth on that side.
            var tris = new List<int>((Cols - 1) * (Rows - 1) * 6);
            for (int y = 0; y < Rows - 1; y++)
                for (int x = 0; x < Cols - 1; x++)
                {
                    int a = y * Cols + x, b = a + 1, c = a + Cols, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var m = new Mesh { name = "SF_Cape" };
            m.vertices = v;
            m.uv = uv;
            m.colors = Colours(team, rank);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A deep shade of the side's colour, a little darker in the folds' hollows and
        /// toward the hem, with a band along the hem: dark for the first stripe, bronze for the
        /// second, gold for the third.</summary>
        static Color[] Colours(int team, int rank)
        {
            // Authored as the eye sees them; vertex colours reach the shader unconverted, so they
            // are made linear here or come out pale.
            Color side = team == 0 ? FXDirector.PlayerColor : FXDirector.EnemyColor;
            Color main = Color.Lerp(side, new Color(0.08f, 0.08f, 0.1f), 0.32f);
            Color band = rank >= 3 ? new Color(0.95f, 0.74f, 0.30f) : rank == 2 ? new Color(0.70f, 0.44f, 0.22f) : main * 0.45f;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var c = new Color[Cols * Rows];
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    float down = y / (Rows - 1f);
                    bool hollow = x != 0 && x != Cols - 1 && x % 2 == 1;
                    Color col = main * (1f - 0.18f * down) * (hollow ? 0.82f : 1f);
                    if (y >= Rows - 2) col = y == Rows - 1 ? band : Color.Lerp(band, col, 0.45f);
                    if (linear) col = col.linear;
                    col.a = 1f;
                    c[y * Cols + x] = col;
                }
            return c;
        }
    }
}
