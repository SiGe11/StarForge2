// InfantryCape.cs — the cape a soldier may earn: at each promotion a soldier without one
// gets one 81% of the time (a hash of the unit and its new rank, never the match's random
// stream), in a deep shade of the side's colour, an embroidered trim down both edges and
// along the hem in the rank's colour. A knight's cape: hung from the shoulders under the
// pauldrons, the pack worn over it, falling nearly straight to the ankles in a few broad
// folds. It is Unity Cloth near the camera -- the shoulders and the stretch under the pack
// held, the skirt free, thrown back by the soldier's own movement and blown by the match's
// wind. Away from the camera, above 3.5x, through a hitch or past the budget of simulated
// capes it is the same mesh at rest, leant back on a spring as the soldier moves.
// Presentation only: UnitView makes and ticks it and the simulation never knows it is there.
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
        const int Cols = 15, Rows = 14;          // cloth vertices across and down; the outer columns and the last row are the trim
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
        // Behind the back plate by 5-6 cm where it shows beside the pack (the plate lies at -0.21..-0.24):
        // at -0.20..-0.26 it wove in and out of the plate, and the seam shimmered.
        const float ShoulderY = 1.50f, ShoulderZ = -0.30f, HalfTop = 0.52f, UnderPackZ = -0.31f;
        const float FreeY = 1.02f, HemY = 0.22f, HalfHem = 0.78f;
        const float EdgeHold = 0.5f;             // how much less an edge particle may swing than the middle
        const float Trim = 0.13f, HemTrim = 0.12f;   // the edge trims' share of the half-width; the hem trim's height
        // The embroidered band across the fabric texture: v at its outer rope and its inner one,
        // its height in u (the texture is square), and the palmette over a crossing that the corners and
        // the middle of the hem show. Its u runs on along the trims, the texture mirrored (wrap U).
        const float BandOut = 0.027f, BandIn = 0.209f, BandU = BandIn - BandOut, Palmette = 0.503f;
        const float Weave = 2f;                  // metres of cape across the fabric texture

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
        int hem;   // the particle the give-up check reads: the lowest at rest (welded particles keep no documented order)
        // The skirt's bone: everything below the pack line hangs from it, so the rigid lean turns the
        // skirt alone and never pushes the held part above into the soldier's back.
        Transform skirt;
        static readonly Vector3 Pivot = new Vector3(0f, FreeY - ShoulderY, UnderPackZ - ShoulderZ);
        /// <summary>Times this cape went from cloth to rest and back (for trials: flicker).</summary>
        public int Switches { get; private set; }

        public int Rank => rank;
        public bool IsCloth => simulating;
        /// <summary>For trials: the cape at rest (its own space), the cloth's vertices while it
        /// simulates (allocates; null otherwise), whether it is drawn.</summary>
        public Mesh Rest => mesh;
        public Vector3[] ClothVertices() => simulating && cloth != null ? cloth.vertices : null;
        /// <summary>For trials: the cloth's particles at rest, in the order <see cref="ClothVertices"/> gives
        /// them (Cloth welds the mesh's seam vertices, so they do not follow the mesh's own order).</summary>
        public Vector3[] RestParticles { get; private set; }
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
            skirt = new GameObject("Skirt").transform;
            skirt.SetParent(transform, false);
            skirt.localPosition = Pivot;
            mesh = CapeMesh(backZ, team, rank, id);
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(-Pivot) };
            smr = gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.bones = new[] { transform, skirt };
            smr.rootBone = transform;
            smr.sharedMesh = mesh;
            // Room for the swing: bounds that only held the hung cape let one swung out past them be
            // culled at the screen's edge.
            smr.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + new Vector3(0.8f, 0.6f, 1.4f));
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
            // (the backstop at its rest surface, whose normals face out behind him). The backstop
            // only stops a particle along its normal, so the edges swing less: free as far as the
            // middle, a hem corner slid 0.5 m sideways and in front of a leg.
            var coeff = cloth.coefficients;
            var verts = cloth.vertices;
            RestParticles = verts;
            for (int i = 1; i < verts.Length; i++) if (verts[i].y < verts[hem].y) hem = i;
            for (int i = 0; i < coeff.Length && i < verts.Length; i++)
            {
                float by = ShoulderY + verts[i].y, below = FreeY - by;
                float half = Mathf.Lerp(HalfTop, HalfHem, Mathf.Pow(Mathf.InverseLerp(ShoulderY, HemY, by), 0.8f));
                float edge = Mathf.Clamp01(Mathf.Abs(verts[i].x) / half);
                coeff[i].maxDistance = below <= 0f ? 0f : Mathf.Min(0.55f, 0.06f + below * 0.65f) * (1f - EdgeHold * edge);
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
                // Taken up from a leaning rest pose, the skirt's bone eases back to hanging, and the
                // cloth with it (switched straight to hanging, every cape jumped at each change).
                if (lean != 0f)
                {
                    lean *= Mathf.Exp(-Mathf.Min(Time.deltaTime, MaxStep) / 0.25f);
                    if (Mathf.Abs(lean) < 0.2f) lean = 0f;
                    Lean(lean);
                }
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
                    if (v.Length > hem && !(v[hem].magnitude < Reach))
                    {
                        gaveUp = true;
                        GaveUp++;
                        SetCloth(false);
                    }
                }
                return;
            }
            // At rest: the skirt swings back with the soldier's pace on a damped spring, about where
            // it comes out from under the pack, on its own bone (about the shoulders the stretch under
            // the pack went back out through it; the whole cape turned, its top went into his back).
            float pace = Mathf.Max(0f, Vector3.Dot(velocity, forward));
            float target = Mathf.Clamp(pace * 5f, 0f, MaxLean);
            float dt = Mathf.Min(Time.deltaTime, MaxStep);
            leanVel += ((target - lean) * 40f - leanVel * 9f) * dt;
            lean += leanVel * dt;
            Lean(lean);
        }

        /// <summary>Turn the skirt about the line under the pack: +X takes the hem back.</summary>
        void Lean(float degrees) => skirt.localRotation = Quaternion.Euler(degrees, 0f, 0f);

        void SetCloth(bool on)
        {
            if (on == simulating || cloth == null) return;
            simulating = on;
            Switches++;
            if (on)
            {
                // From the pose it hangs in: the cloth starts on the leaning bone, which then eases back.
                leanVel = 0f;
                cloth.enabled = true;
                cloth.ClearTransformMotion();
            }
            else
            {
                // The rigid cape takes up the swing the cloth had, so the change does not show.
                lean = LeanOfCloth();
                leanVel = 0f;
                Lean(lean);
                cloth.enabled = false;
            }
        }

        /// <summary>How far back the cloth's hem hangs now, as the skirt's lean that would put it
        /// there (degrees; 0 for a bad cloth). Reads the cloth back (allocates): only at a change.</summary>
        float LeanOfCloth()
        {
            var v = cloth.vertices;
            var rest = RestParticles;
            if (rest == null || v.Length != rest.Length) return 0f;
            float back = 0f;
            int n = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (rest[i].y > HemY - ShoulderY + 0.3f) continue;   // the hem's rows
                back += rest[i].z - v[i].z;
                n++;
            }
            if (n == 0 || float.IsNaN(back) || float.IsInfinity(back)) return 0f;
            float deg = Mathf.Atan2(back / n, FreeY - HemY) * Mathf.Rad2Deg;
            return Mathf.Clamp(deg, -10f, MaxLean * 1.5f);
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

        /// <summary>For trials and look checks: the cape's mesh as <see cref="Make"/> builds it.</summary>
        public static Mesh PreviewMesh(float backZ, int team, int rank, int id) => CapeMesh(backZ, team, rank, id);

        /// <summary>Body-space depth of the cape's middle at height y: under the pack against the
        /// back, out from under its bottom edge, then hanging nearly straight behind the legs. A
        /// deeper pack (the Scorcher's tanks) pushes the skirt a little further out.</summary>
        static float MiddleZ(float y, float backZ)
        {
            float off = Mathf.Clamp(-0.49f - backZ, 0f, 0.3f) * 0.4f;
            if (y >= FreeY) return Mathf.Lerp(ShoulderZ, UnderPackZ, Mathf.InverseLerp(ShoulderY, FreeY, y));
            float f = 1f - Mathf.InverseLerp(FreeY, HemY, y);
            return UnderPackZ - off - (0.44f + off) * (1f - f * f * f);
        }

        /// <summary>A few broad folds across the back, -1..1 (a ridge at 1), placed by the unit so no
        /// two capes hang alike. At least six columns to a fold, or the hem shows its corners.</summary>
        static float Fold(float s, int id)
        {
            float p = (id * 0.618034f) % 1f * 6.2832f;
            return 0.75f * Mathf.Cos(7f * s + p) + 0.25f * Mathf.Cos(3.7f * s - 1.3f * p);
        }

        // The parts a cell belongs to. A grid point on a seam gets a vertex for each part round it
        // (Cloth welds them back into one particle), so colour and texture change at the seam.
        const int Body = 0, EdgeL = 1, EdgeR = 2, Hem = 3, CornerL = 4, CornerR = 5;
        static int Part(int r, int c) =>
            c == 0 ? (r == Rows - 2 ? CornerL : EdgeL) :
            c == Cols - 2 ? (r == Rows - 2 ? CornerR : EdgeR) :
            r == Rows - 2 ? Hem : Body;
        // Per vertex, the same for every cape: whether it is trim, and how far down it hangs (0-1).
        static bool[] vertTrim;
        static float[] vertDown;

        /// <summary>Rows from the shoulders to the hem; across, from the left edge to the right, wrapped
        /// a little round the soldier toward the hem. The outer columns and the last row are the trim:
        /// the band of the fabric texture runs down each edge and along the hem, and the corners show
        /// its palmette.</summary>
        static Mesh CapeMesh(float backZ, int team, int rank, int id)
        {
            int n = Rows * Cols;
            var grid = new Vector3[n];
            var downs = new float[Rows];
            for (int r = 0; r < Rows; r++)
            {
                float by = r < Rows - 1 ? Mathf.Lerp(ShoulderY, HemY + HemTrim, r / (Rows - 2f)) : HemY;
                float down = Mathf.InverseLerp(ShoulderY, HemY, by);
                downs[r] = down;
                float half = Mathf.Lerp(HalfTop, HalfHem, Mathf.Pow(down, 0.8f));
                float mid = MiddleZ(by, backZ);
                float depth = 0.12f * Mathf.Pow(down, 1.3f);
                for (int c = 0; c < Cols; c++)
                {
                    float s = c == 0 ? -1f : c == Cols - 1 ? 1f : Mathf.Lerp(-1f + Trim, 1f - Trim, (c - 1) / (Cols - 3f));
                    // None at the shoulders, growing toward the hem: wrapped forward up there, the
                    // edges met the back plate.
                    float wrap = s * s * 0.24f * down * down;
                    grid[r * Cols + c] = new Vector3(s * half, by - ShoulderY, mid + wrap - Fold(s, id) * depth - ShoulderZ);
                }
            }
            // Smooth normals over the whole grid, the trim seams included; wound so they face out
            // behind the soldier (the backstop keeps the cloth on that side).
            var gridN = new Vector3[n];
            for (int r = 0; r < Rows - 1; r++)
                for (int c = 0; c < Cols - 1; c++)
                {
                    int a = r * Cols + c, b = a + 1, cc = a + Cols, d = cc + 1;
                    var f1 = Vector3.Cross(grid[b] - grid[a], grid[cc] - grid[a]);
                    var f2 = Vector3.Cross(grid[d] - grid[b], grid[cc] - grid[b]);
                    gridN[a] += f1; gridN[b] += f1 + f2; gridN[cc] += f1 + f2; gridN[d] += f2;
                }
            // Texture u along the trims, keeping the band's proportions: down each edge from the
            // corner up, and along the hem out from its middle.
            var edgeU = new float[Rows];
            for (int r = Rows - 3; r >= 0; r--)
            {
                float w = 0.5f * (Vector3.Distance(grid[r * Cols], grid[r * Cols + 1]) + Vector3.Distance(grid[(r + 1) * Cols], grid[(r + 1) * Cols + 1]));
                edgeU[r] = edgeU[r + 1] + Vector3.Distance(grid[r * Cols], grid[(r + 1) * Cols]) * BandU / w;
            }
            var hemU = new float[Cols];
            int midC = Cols / 2, last = (Rows - 1) * Cols;
            hemU[midC] = Palmette;
            for (int c = midC + 1; c < Cols - 1; c++) hemU[c] = hemU[c - 1] + Vector3.Distance(grid[last + c], grid[last + c - 1]) * BandU / HemTrim;
            for (int c = midC - 1; c > 0; c--) hemU[c] = hemU[c + 1] - Vector3.Distance(grid[last + c], grid[last + c + 1]) * BandU / HemTrim;

            var verts = new List<Vector3>(n * 2);
            var norms = new List<Vector3>(n * 2);
            var uvs = new List<Vector2>(n * 2);
            var trim = new List<bool>(n * 2);
            var down01 = new List<float>(n * 2);
            var index = new int[6 * n];
            for (int i = 0; i < index.Length; i++) index[i] = -1;
            int Vertex(int part, int r, int c)
            {
                int key = part * n + r * Cols + c;
                if (index[key] >= 0) return index[key];
                var p = grid[r * Cols + c];
                Vector2 uv;
                bool outer = c == 0 || c == Cols - 1, hem = r == Rows - 1;
                switch (part)
                {
                    case Body: uv = new Vector2(0.5f + p.x / Weave, 1f + p.y / Weave); break;
                    case EdgeL: case EdgeR: uv = new Vector2(edgeU[r], outer ? BandOut : BandIn); break;
                    case Hem: uv = new Vector2(hemU[c], hem ? BandOut : BandIn); break;
                    default:
                        bool left = part == CornerL ? c == 0 : c == Cols - 2;
                        uv = new Vector2(Palmette + (left ? -0.5f : 0.5f) * BandU, hem ? BandOut : BandIn);
                        break;
                }
                index[key] = verts.Count;
                verts.Add(p);
                norms.Add(gridN[r * Cols + c].normalized);
                uvs.Add(uv);
                trim.Add(part != Body);
                down01.Add(downs[r]);
                return index[key];
            }
            var tris = new List<int>((Cols - 1) * (Rows - 1) * 6);
            for (int r = 0; r < Rows - 1; r++)
                for (int c = 0; c < Cols - 1; c++)
                {
                    int part = Part(r, c);
                    int a = Vertex(part, r, c), b = Vertex(part, r, c + 1), cc = Vertex(part, r + 1, c), d = Vertex(part, r + 1, c + 1);
                    tris.Add(a); tris.Add(b); tris.Add(cc);
                    tris.Add(b); tris.Add(d); tris.Add(cc);
                }
            vertTrim = trim.ToArray();
            vertDown = down01.ToArray();
            var m = new Mesh { name = "SF_Cape" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.colors = Colours(team, rank);
            m.SetTriangles(tris, 0);
            // Above the pack line on the cape's own bone (0), below it on the skirt's (1), blended over a row.
            float freeDown = (ShoulderY - FreeY) / (ShoulderY - HemY);
            var weights = new BoneWeight[verts.Count];
            for (int i = 0; i < weights.Length; i++)
            {
                float w = Mathf.Clamp01((down01[i] - freeDown) / 0.1f);
                weights[i] = new BoneWeight { boneIndex0 = 1, weight0 = w, boneIndex1 = 0, weight1 = 1f - w };
            }
            m.boneWeights = weights;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A deep shade of the side's colour, darkening a little toward the hem, and the trim:
        /// dark for the first stripe, bronze for the second, gold for the third. The folds are left
        /// to the light.</summary>
        static Color[] Colours(int team, int rank)
        {
            // Authored as the eye sees them; vertex colours reach the shader unconverted, so they
            // are made linear here or come out pale.
            Color side = team == 0 ? FXDirector.PlayerColor : FXDirector.EnemyColor;
            Color main = Color.Lerp(side, new Color(0.08f, 0.08f, 0.1f), 0.32f);
            Color band = rank >= 3 ? new Color(0.95f, 0.74f, 0.30f) : rank == 2 ? new Color(0.70f, 0.44f, 0.22f) : main * 0.45f;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var c = new Color[vertTrim.Length];
            for (int i = 0; i < c.Length; i++)
            {
                Color col = (vertTrim[i] ? band : main) * (1.04f - 0.16f * vertDown[i]);
                if (linear) col = col.linear;
                col.a = 1f;
                c[i] = col;
            }
            return c;
        }
    }
}
