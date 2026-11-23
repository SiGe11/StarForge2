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
        // The order's standard, sewn on as a panel: grid lines, so its edge is a hard one. 0.45 x 0.60 m,
        // 1.33 tall against the pictures' 1.30 (OrderStandards.SourceAspect).
        const int PanelX0 = 2, PanelX1 = 14, PanelY0 = 4, PanelY1 = 19;
        /// <summary>For <see cref="Make"/>'s order: the unit rides under an order of its own (OrderStandards.ForUnit).</summary>
        public const int OwnOrder = -2;
        // The rigid banner's panel: the modelled cloth's faces stand 0.028 and 0.008 from the pivot's plane and its
        // sigil's 0.042 and 0.02 (Tools/blender/build_mechs.py, build_roster.py), so the panel stands proud of both.
        const float RigidFront = -0.052f, RigidBack = 0.03f;

        static Material clothMat, barMat, panelMat;
        static Mesh barMesh;
        static readonly Mesh[] rigidPanels = new Mesh[OrderStandards.Count];

        readonly Transform pivot;
        readonly float phase;
        readonly int team, order;
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

        /// <param name="order">The order whose standard is the banner's panel: a knight's
        /// (OrderStandards.IndexOf, from MechView), or <see cref="OwnOrder"/>, the default, for a unit with no
        /// knight (the Mech-Hunter's flag: UnitView passes none); -1 keeps the plain side's banner with its
        /// diamond sigil.</param>
        public static MechBannerCloth Make(Transform rigid, int team, int id, int order = OwnOrder)
        {
            if (Off || rigid == null || rigid.parent == null || FXDirector.Current == null || FXDirector.Current.debrisMaterial == null) return null;
            if (order == OwnOrder) order = OrderStandards.ForUnit(id);
            if (order < 0 || order >= OrderStandards.Count || OrderStandards.Atlas == null) order = -1;
            var b = new MechBannerCloth(rigid, team, id, order);
            b.Build();
            return b.root != null ? b : null;
        }

        MechBannerCloth(Transform rigid, int team, int id, int order) { pivot = rigid; this.team = team; this.order = order; phase = id * 0.37f; }

        public bool Alive => root != null && cloth != null && !gaveUp;

        /// <summary>The order whose standard the cloth carries, or -1.</summary>
        public int Order => order;

        /// <summary>For FxLook and BannerClothCheck: how far the free bottom corner is from the crossbar (-1: none).</summary>
        public float TipDistance() => Alive ? Farthest(cloth.vertices) : -1f;

        // The corner is the farthest particle. Found by what it is, not by index: Unity's Cloth welds
        // coincident vertices (the panel's edge) and documents no particle order. A NaN or Inf anywhere
        // answers infinity, so the caller's "under Reach" test fails for it.
        static float Farthest(Vector3[] v)
        {
            float far = -1f;
            for (int i = 0; i < v.Length; i++)
            {
                float d = v[i].magnitude;
                if (float.IsNaN(d) || float.IsInfinity(d)) return float.PositiveInfinity;
                if (d > far) far = d;
            }
            return far;
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
                if (v.Length > 0 && !(Farthest(v) < Reach))
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
            if (order >= 0 && panelMat == null)
            {
                // The same material with the orders' atlas as its base map, for the whole banner: the panel's
                // vertices are white under their cell, and the cloth round it samples the atlas's white strip
                // under its own colours. One mesh, one submesh, as the capes are (a Cloth's welding of
                // coincident vertices is proven there; across submeshes it is not).
                panelMat = new Material(fx.debrisMaterial) { name = "SF_MechStandard" };
                panelMat.SetColor("_BaseColor", Color.white);
                panelMat.SetFloat("_Cull", 0f);
                panelMat.SetTexture("_BaseMap", OrderStandards.Atlas);
            }
            if (order >= 0) RigidPanel();
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
            mesh = ClothMesh(team, order);
            smr.sharedMesh = mesh;
            smr.sharedMaterial = order >= 0 ? panelMat : clothMat;
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

        /// <summary>The rigid banner shows whenever the cloth cannot run (the drop, above 3.5x, a hitch) and on the
        /// wreck, so it carries the same standard: two quads just proud of the model's cloth and sigil, one facing each
        /// way so neither reads mirrored. Made once a banner (a cloth rebuilt after going bad finds it there); it is
        /// the rigid banner's child, so it swings, hides and goes to the wreck with it.</summary>
        void RigidPanel()
        {
            if (pivot.Find("Standard") != null) return;
            if (rigidPanels[order] == null) rigidPanels[order] = RigidMesh(order);   // Unity's null check: made in an earlier play, it is gone
            var go = new GameObject("Standard");
            go.transform.SetParent(pivot, false);
            go.AddComponent<MeshFilter>().sharedMesh = rigidPanels[order];
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = panelMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Mesh RigidMesh(int order)
        {
            var cell = OrderStandards.Uv(order);
            float x0 = (PanelX0 / (Cols - 1f) - 0.5f) * Width, x1 = (PanelX1 / (Cols - 1f) - 0.5f) * Width;
            float y0 = -PanelY0 / (Rows - 1f) * Drop, y1 = -PanelY1 / (Rows - 1f) * Drop;
            var v = new Vector3[8];
            var uv = new Vector2[8];
            var col = new Color[8];
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? RigidFront : RigidBack;
                // Seen from -z (side 0) +x is to the right; from +z it is to the left: flip u there.
                float uL = side == 0 ? cell.xMin : cell.xMax, uR = side == 0 ? cell.xMax : cell.xMin;
                int i = side * 4;
                v[i] = new Vector3(x0, y0, z); uv[i] = new Vector2(uL, cell.yMax);
                v[i + 1] = new Vector3(x1, y0, z); uv[i + 1] = new Vector2(uR, cell.yMax);
                v[i + 2] = new Vector3(x0, y1, z); uv[i + 2] = new Vector2(uL, cell.yMin);
                v[i + 3] = new Vector3(x1, y1, z); uv[i + 3] = new Vector2(uR, cell.yMin);
                for (int k = 0; k < 4; k++) col[i + k] = Color.white;
            }
            var m = new Mesh { name = "SF_MechStandardRigid" };
            m.vertices = v;
            m.uv = uv;
            m.colors = col;
            m.triangles = new[] { 0, 1, 2, 1, 3, 2, 4, 6, 5, 5, 6, 7 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
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
        /// bottom and sides, and either the model's diamond sigil (a lit rim round a pale core) or,
        /// with an order, that order's standard as a panel: vertices of its own (white, with the
        /// atlas cell's UVs) lying on the grid's, which the Cloth welds to them, so its edge is a hard
        /// one; the grid's own vertices then sample the atlas's white strip.</summary>
        static Mesh ClothMesh(int team, int order)
        {
            bool panel = order >= 0;
            Color main = team == 0 ? FXDirector.PlayerColor : FXDirector.EnemyColor;
            main = Color.Lerp(main, Color.gray, 0.15f);
            Color trim = main * 0.35f, rim = new Color(1f, 0.8f, 0.45f), core = new Color(0.85f, 0.88f, 0.9f);
            trim.a = main.a = 1f;
            int grid = Cols * Rows, pw = PanelX1 - PanelX0 + 1, ph = PanelY1 - PanelY0 + 1;
            var v = new Vector3[grid + (panel ? pw * ph : 0)];
            var col = new Color[v.Length];
            var uv = new Vector2[v.Length];
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    float fx = x / (Cols - 1f), fy = y / (Rows - 1f);
                    int i = y * Cols + x;
                    float px = (fx - 0.5f) * Width, py = -fy * Drop;
                    v[i] = new Vector3(px, py, 0f);
                    uv[i] = panel ? OrderStandards.White : new Vector2(fx, 1f - fy);
                    float d = Mathf.Abs(px) + Mathf.Abs(py + 0.50f);   // diamond round the sigil
                    Color c = main;
                    if (x == 0 || x == Cols - 1 || y >= Rows - 2) c = trim;
                    else if (panel) c = main;                          // the standard takes the sigil's place
                    else if (d < 0.075f) c = core;
                    else if (d < 0.155f) c = rim;
                    col[i] = c;
                }
            var tris = new List<int>();
            for (int y = 0; y < Rows - 1; y++)
                for (int x = 0; x < Cols - 1; x++)
                {
                    if (panel && x >= PanelX0 && x < PanelX1 && y >= PanelY0 && y < PanelY1) continue;   // the panel's own quads cover these
                    int a = y * Cols + x, b = a + 1, c = a + Cols, d = c + 1;
                    tris.AddRange(new[] { a, b, c, b, d, c });
                }
            var m = new Mesh { name = "SF_MechBanner" };
            if (panel)
            {
                var cell = OrderStandards.Uv(order);
                for (int y = 0; y < ph; y++)
                    for (int x = 0; x < pw; x++)
                    {
                        int i = grid + y * pw + x;
                        v[i] = v[(PanelY0 + y) * Cols + PanelX0 + x];   // on the grid's vertex: one cloth particle
                        col[i] = Color.white;
                        uv[i] = new Vector2(cell.xMin + cell.width * x / (pw - 1f), cell.yMax - cell.height * y / (ph - 1f));
                    }
                for (int y = 0; y < ph - 1; y++)
                    for (int x = 0; x < pw - 1; x++)
                    {
                        int a = grid + y * pw + x, b = a + 1, c = a + pw, d = c + 1;
                        tris.AddRange(new[] { a, b, c, b, d, c });
                    }
            }
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
