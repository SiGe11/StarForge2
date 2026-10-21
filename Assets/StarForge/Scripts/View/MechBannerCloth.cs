// MechBannerCloth.cs — the Mech's standard as real cloth (Unity's Cloth, pinned along its
// crossbar) in place of the rigid banner the model carries. It hangs from the same pivot, is
// thrown about by the Mech's own movement (the Cloth's world velocity/acceleration scales)
// and blown by the match's wind. While it cannot run -- above 3.5x time, or after a bad
// frame -- MechView falls back to the rigid banner's spring. Presentation only.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarForge.View
{
    public sealed class MechBannerCloth
    {
        const int Cols = 17, Rows = 29;          // cloth vertices across and down
        const float Width = 0.60f, Drop = 1.12f; // model units, as the rigid standard was
        const float Reach = 3f;                  // the corner rests 1.16 away and swings to ~1.45 (at most ~2.5): only NaN/Inf gets past this
        const int MaxResets = 3;                 // a cloth that keeps going bad is given up: the rigid banner stays
        const float MaxStep = 0.1f;              // a longer frame (a hitch) is not simulated

        static Material clothMat, barMat;
        static Mesh barMesh;

        readonly Transform pivot;
        readonly float phase;
        readonly int team;
        GameObject root;
        Cloth cloth;
        Mesh mesh;
        float nextCheck;
        bool running, gaveUp;

        /// <summary>Times the cloth has been rebuilt after going bad.</summary>
        public int Resets { get; private set; }

        /// <summary>The rigid banner's pivot is where the crossbar hangs; the cloth is made as
        /// its sibling, so it moves with the torso but not with the rigid banner's swing.</summary>
        /// <summary>For the fps A/B: <c>-sfnocloth</c> on the player's command line keeps the rigid
        /// spring banner, so one build measures both.</summary>
        static readonly bool Off = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sfnocloth") >= 0;

        public static MechBannerCloth Make(Transform rigid, int team, int id)
        {
            if (Off || rigid == null || rigid.parent == null || FXDirector.Current == null || FXDirector.Current.debrisMaterial == null) return null;
            var b = new MechBannerCloth(rigid, team, id);
            b.Build();
            return b.root != null ? b : null;
        }

        MechBannerCloth(Transform rigid, int team, int id) { pivot = rigid; this.team = team; phase = id * 0.37f; }

        public bool Alive => root != null && cloth != null && !gaveUp;

        /// <summary>For FxLook: how far the free bottom corner is from the crossbar (-1: none).</summary>
        public float TipDistance()
        {
            if (!Alive) return -1f;
            var v = cloth.vertices;
            return v.Length > 0 ? v[v.Length - 1].magnitude : -1f;
        }

        /// <summary>One call a frame while the Mech is shown. Returns whether the cloth is the
        /// banner on show (otherwise the rigid one is).</summary>
        public bool Tick(Vector2 wind)
        {
            if (!Alive) return false;
            // Not above 3.5x, and not through a hitch: a long step is what blows a spring up.
            bool simulate = Time.timeScale <= 3.5f && Time.deltaTime <= MaxStep;
            if (simulate != running)
            {
                running = simulate;
                root.SetActive(simulate);
                if (simulate) cloth.ClearTransformMotion();
            }
            if (!simulate) return false;
            float gust = 0.8f + 0.35f * Mathf.PerlinNoise(Time.time * 0.7f, phase);
            float flutter = 2f + 8f * wind.magnitude;
            cloth.externalAcceleration = new Vector3(wind.x, 0f, wind.y) * 12f * gust;
            cloth.randomAcceleration = new Vector3(flutter, flutter * 0.5f, flutter);
            // A hitch can leave the cloth NaN (every comparison below fails for it): once a
            // second, read it back and put it up again. Cloth.vertices allocates.
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 1f;
                var v = cloth.vertices;
                if (v.Length > 0 && !(v[v.Length - 1].magnitude < Reach))
                {
                    Resets++;
                    Destroy();
                    if (Resets >= MaxResets) { gaveUp = true; return false; }
                    Build();   // made inactive: the rigid banner shows until the next Tick switches it on
                    return false;
                }
            }
            return true;
        }

        /// <summary>The Mech is shown again after fog: forget what it moved while unseen.</summary>
        public void Resume() { if (Alive && running) cloth.ClearTransformMotion(); }

        void Build()
        {
            var fx = FXDirector.Current;
            if (clothMat == null)
            {
                // The debris material (URP Particles Simple Lit), drawn from both sides and
                // white under the vertex colours that paint the standard: no new shader.
                clothMat = new Material(fx.debrisMaterial) { name = "SF_MechBanner" };
                clothMat.SetColor("_BaseColor", Color.white);
                clothMat.SetFloat("_Cull", 0f);
                barMat = new Material(fx.debrisMaterial) { name = "SF_MechBannerBar" };
                barMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
                barMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }
            root = new GameObject("BannerCloth");
            root.transform.SetParent(pivot.parent, false);
            root.transform.localPosition = pivot.localPosition;
            root.transform.localRotation = pivot.localRotation;

            var bar = new GameObject("Crossbar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localScale = new Vector3(Width + 0.08f, 0.05f, 0.05f);
            bar.AddComponent<MeshFilter>().sharedMesh = barMesh;
            var br = bar.AddComponent<MeshRenderer>();
            br.sharedMaterial = barMat;
            br.shadowCastingMode = ShadowCastingMode.Off;

            var go = new GameObject("Cloth");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.02f, -0.01f);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            mesh = ClothMesh(team);
            smr.sharedMesh = mesh;
            smr.sharedMaterial = clothMat;
            smr.shadowCastingMode = ShadowCastingMode.Off;   // Particles/Simple Lit has no shadow pass
            cloth = go.AddComponent<Cloth>();
            cloth.useGravity = true;
            cloth.damping = 0.12f;
            cloth.stretchingStiffness = 1f;
            cloth.bendingStiffness = 0.5f;
            cloth.worldVelocityScale = 0.6f;
            cloth.worldAccelerationScale = 1f;
            cloth.sleepThreshold = 0f;
            // The top row is pinned to the crossbar; the rest may move a little over the
            // standard's own length.
            var coeff = cloth.coefficients;
            var verts = cloth.vertices;
            for (int i = 0; i < coeff.Length && i < verts.Length; i++)
            {
                coeff[i].maxDistance = verts[i].y > -0.01f ? 0f : Drop * 1.15f;
                coeff[i].collisionSphereDistance = 0f;
            }
            cloth.coefficients = coeff;
            // Off until the first Tick (the Mech is landed and shown): through the drop the
            // rigid banner stays, and the landing's jolt is not simulated.
            root.SetActive(false);
            running = false;
            nextCheck = Time.time + 1f;
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            if (mesh != null) Object.Destroy(mesh);
            root = null;
            cloth = null;
            mesh = null;
        }

        /// <summary>A grid hanging from the crossbar: the side's colour, dark trim along the
        /// bottom and sides, and the model's diamond sigil (a lit rim round a pale core).</summary>
        static Mesh ClothMesh(int team)
        {
            Color main = team == 0 ? FXDirector.PlayerColor : FXDirector.EnemyColor;
            main = Color.Lerp(main, Color.gray, 0.15f);
            Color trim = main * 0.35f, rim = new Color(1f, 0.8f, 0.45f), core = new Color(0.85f, 0.88f, 0.9f);
            trim.a = main.a = 1f;
            var v = new Vector3[Cols * Rows];
            var col = new Color[v.Length];
            var uv = new Vector2[v.Length];
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    float fx = x / (Cols - 1f), fy = y / (Rows - 1f);
                    int i = y * Cols + x;
                    float px = (fx - 0.5f) * Width, py = -fy * Drop;
                    v[i] = new Vector3(px, py, 0f);
                    uv[i] = new Vector2(fx, 1f - fy);
                    float d = Mathf.Abs(px) + Mathf.Abs(py + 0.50f);   // diamond round the sigil
                    Color c = main;
                    if (x == 0 || x == Cols - 1 || y >= Rows - 2) c = trim;
                    else if (d < 0.075f) c = core;
                    else if (d < 0.155f) c = rim;
                    col[i] = c;
                }
            var tris = new List<int>();
            for (int y = 0; y < Rows - 1; y++)
                for (int x = 0; x < Cols - 1; x++)
                {
                    int a = y * Cols + x, b = a + 1, c = a + Cols, d = c + 1;
                    tris.AddRange(new[] { a, b, c, b, d, c });
                }
            var m = new Mesh { name = "SF_MechBanner" };
            m.vertices = v;
            m.colors = col;
            m.uv = uv;
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
