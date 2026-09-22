// VegetationRenderer.cs — draws the map's trees and bushes, GPU-instanced.
//
// One draw per kind for its bark and one for its foliage (StarForge/Tree),
// casting shadows, from matrices and per-plant state gathered here each frame:
// a few hundred plants cost a handful of draws instead of a draw per tree.
// Plants outside the camera (with room for the shadows they throw into view)
// are culled first.
//
// The player sees each plant as it was when they last had vision of it: a tree
// felled or burning under the fog of war still stands in their view until they
// look again, just as a destroyed enemy structure does.
using UnityEngine;
using UnityEngine.Rendering;
using StarForge.Game;
using StarForge.World;

namespace StarForge.View
{
    public sealed class VegetationRenderer : MonoBehaviour
    {
        public Vegetation vegetation;
        public GameWorld world;
        public PlayerController player;
        public Material barkMaterial;
        public Material foliageMaterial;
        [Tooltip("How far past the view a plant may stand and still throw its shadow into it.")]
        public float shadowMargin = 14f;
        [Tooltip("A crown this close to the camera shrinks out of its way, so a tree on high ground never fills the view.")]
        public float cameraClearance = 9f;

        Matrix4x4[] seenPose;
        Vector4[] seenParams;
        bool[] seenGone;

        [Tooltip("Beyond this distance from the camera a plant draws its decimated copy.")]
        public float lodDistance = 70f;

        sealed class Batch
        {
            public readonly Matrix4x4[] m = new Matrix4x4[1023];
            public readonly Vector4[] p = new Vector4[1023];
            public int n;
            public Bounds bounds;
            public MaterialPropertyBlock bark, foliage;
        }

        // Two per kind: [kind * 2] near, [kind * 2 + 1] far (the LOD mesh).
        Batch[] batches;
        readonly Plane[] planes = new Plane[6];

        static readonly int ParamsId = Shader.PropertyToID("_Params");
        static readonly int BarkColorId = Shader.PropertyToID("_BarkColor");
        static readonly int LeafColorId = Shader.PropertyToID("_LeafColor");
        static readonly int LeafColor2Id = Shader.PropertyToID("_LeafColor2");
        static readonly int CenterId = Shader.PropertyToID("_CanopyCenter");
        static readonly int RadiiId = Shader.PropertyToID("_CanopyRadii");
        static readonly int LeafStyleId = Shader.PropertyToID("_LeafStyle");
        static readonly int BloomId = Shader.PropertyToID("_Bloom");
        static readonly int BarkStyleId = Shader.PropertyToID("_BarkStyle");
        static readonly int LeafTilingId = Shader.PropertyToID("_LeafTiling");
        static readonly int CardTexId = Shader.PropertyToID("_CardTex");
        static readonly int CardNormalId = Shader.PropertyToID("_CardNormal");
        static readonly int CardTintId = Shader.PropertyToID("_CardTint");

        public int DrawnLastFrame { get; private set; }

        void Start()
        {
            if (vegetation == null) vegetation = FindAnyObjectByType<Vegetation>();
            if (world == null) world = GameWorld.Instance;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vegetation == null || vegetation.plants.Length == 0) { enabled = false; return; }
            int n = vegetation.plants.Length;
            seenPose = new Matrix4x4[n];
            seenParams = new Vector4[n];
            seenGone = new bool[n];
            batches = new Batch[vegetation.kinds.Length * 2];
            for (int k = 0; k < batches.Length; k++)
            {
                var kind = vegetation.kinds[k / 2];
                var b = batches[k] = new Batch { bark = new MaterialPropertyBlock(), foliage = new MaterialPropertyBlock() };
                foreach (var mpb in new[] { b.bark, b.foliage })
                {
                    mpb.SetColor(BarkColorId, kind.bark);
                    mpb.SetColor(LeafColorId, kind.leaf);
                    mpb.SetColor(LeafColor2Id, kind.leaf2);
                    // Each leaf cluster is already shaded round (its exported normals); the
                    // bend toward the crown's shape makes the crown light as one mass, and
                    // the leaf texture (SF_Tree) gives it its surface.
                    mpb.SetVector(CenterId, new Vector4(kind.crownCenter.x, kind.crownCenter.y, kind.crownCenter.z, kind.bush ? 0.3f : 0.5f));
                    mpb.SetVector(RadiiId, kind.crownRadii);
                    mpb.SetFloat(LeafStyleId, kind.needles ? 1f : 0f);
                    mpb.SetColor(BloomId, kind.bloom);
                    mpb.SetFloat(BarkStyleId, kind.birchBark ? 1f : 0f);
                    mpb.SetFloat(LeafTilingId, kind.leafTiling);
                    if (kind.cardTex != null) mpb.SetTexture(CardTexId, kind.cardTex);
                    if (kind.cardNormal != null) mpb.SetTexture(CardNormalId, kind.cardNormal);
                    mpb.SetColor(CardTintId, kind.cardTint);
                }
            }
            for (int i = 0; i < n; i++) Remember(i);
        }

        void Remember(int i)
        {
            ref var s = ref vegetation.live[i];
            seenPose[i] = vegetation.Pose(i);
            // Felled and lying trees stop swaying; embers glow with the fire.
            float sway = s.state == PlantState.Standing ? 1f : 0f;
            seenParams[i] = new Vector4(s.charred, s.fire, s.foliageLost, sway);
            seenGone[i] = s.state == PlantState.Gone;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || vegetation.live.Length != seenPose.Length) return;

            int team = player != null ? player.team : 0;
            bool all = world == null || !world.running || MatchSettings.spectate;
            var plants = vegetation.plants;
            for (int i = 0; i < plants.Length; i++)
                if (all || world.Visible(team, new Vector2(plants[i].pos.x, plants[i].pos.z))) Remember(i);

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 camPos = cam.transform.position;
            foreach (var b in batches) { b.n = 0; b.bounds = default; }
            int drawn = 0;
            for (int i = 0; i < plants.Length; i++)
            {
                if (seenGone[i]) continue;
                var kind = vegetation.kinds[plants[i].kind];
                ref var pose = ref seenPose[i];
                float reach = Mathf.Max(kind.height, kind.crownRadii.x * 2f) * plants[i].scale * Mathf.Max(1f, plants[i].stretch);
                Vector3 c = new Vector3(pose.m03, pose.m13 + reach * 0.35f, pose.m23);
                if (!Visible(c, reach * 0.75f + shadowMargin)) continue;
                // Too close to the camera: shrink toward the foot of the trunk.
                float nearCam = Vector3.Distance(camPos, new Vector3(pose.m03, pose.m13 + kind.crownCenter.y * plants[i].scale, pose.m23));
                if (kind.drawDistance > 0f && nearCam > kind.drawDistance) continue;
                Matrix4x4 m = pose;
                if (nearCam < cameraClearance + kind.crownRadii.x * plants[i].scale)
                {
                    float k = Mathf.Clamp01((nearCam - cameraClearance * 0.4f) / (cameraClearance * 0.6f + kind.crownRadii.x * plants[i].scale));
                    k = Mathf.SmoothStep(0f, 1f, k);
                    if (k < 0.02f) continue;
                    m = pose * Matrix4x4.Scale(new Vector3(k, k, k));
                }
                bool far = kind.lodMesh != null && nearCam > lodDistance;
                int slot = plants[i].kind * 2 + (far ? 1 : 0);
                var b = batches[slot];
                if (b.n == b.m.Length) Flush(slot);
                if (b.n == 0) b.bounds = new Bounds(c, Vector3.one * reach * 1.6f);
                else b.bounds.Encapsulate(new Bounds(c, Vector3.one * reach * 1.6f));
                b.m[b.n] = m;
                b.p[b.n] = seenParams[i];
                b.n++;
                drawn++;
            }
            for (int k = 0; k < batches.Length; k++) Flush(k);
            DrawnLastFrame = drawn;
        }

        bool Visible(Vector3 c, float r)
        {
            for (int p = 0; p < 6; p++)
                if (planes[p].GetDistanceToPoint(c) < -r) return false;
            return true;
        }

        void Flush(int slot)
        {
            var b = batches[slot];
            if (b.n == 0) return;
            var kind = vegetation.kinds[slot / 2];
            var mesh = slot % 2 == 1 && kind.lodMesh != null ? kind.lodMesh : kind.mesh;
            if (mesh != null)
            {
                if (kind.barkSubmesh >= 0 && barkMaterial != null) Draw(b, mesh, barkMaterial, b.bark, kind.barkSubmesh, kind.castShadows);
                if (kind.foliageSubmesh >= 0 && foliageMaterial != null) Draw(b, mesh, foliageMaterial, b.foliage, kind.foliageSubmesh, kind.castShadows);
            }
            b.n = 0;
        }

        void Draw(Batch b, Mesh mesh, Material mat, MaterialPropertyBlock mpb, int submesh, bool shadows)
        {
            mpb.SetVectorArray(ParamsId, b.p);
            var rp = new RenderParams(mat)
            {
                matProps = mpb,
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = b.bounds,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.BlendProbes
            };
            if (submesh < mesh.subMeshCount) Graphics.RenderMeshInstanced(rp, mesh, submesh, b.m, b.n);
        }
    }
}
