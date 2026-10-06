// Banners.cs — a team banner on a pole beside each Foundry: a cloth (Unity's Cloth,
// pinned along the pole) blown by the match's wind (World/Wind: the same heading and
// gusts the grass, the trees and the smoke follow), so a base shows whose it is and
// which way the air is moving. Presentation only; FXDirector adds it. A banner is shown
// only while its Foundry is (fog of war, construction) and goes when the Foundry does.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed class Banners : MonoBehaviour
    {
        const int Cols = 16, Rows = 9;           // cloth vertices along and down the banner
        const float Width = 2.6f, Drop = 1.5f;   // metres
        const float PoleHeight = 6.8f;

        sealed class Flag
        {
            public GameObject root;
            public Cloth cloth;
            public Renderer[] shownBy;
            public float phase;
        }

        GameWorld world;
        Material cloth, pole;
        Mesh poleMesh;
        readonly Dictionary<Unit, Flag> flags = new Dictionary<Unit, Flag>();
        readonly List<Unit> gone = new List<Unit>();
        float nextScan;

        /// <summary>How many banners are up (FxLook reports it).</summary>
        public int Count => flags.Count;

        void Awake()
        {
            world = FindAnyObjectByType<GameWorld>();
            poleMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        }

        void OnDisable()
        {
            foreach (var f in flags.Values) if (f.root != null) Destroy(f.root);
            flags.Clear();
        }

        void Update()
        {
            if (world == null || !world.running) return;
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.5f;
                Scan();
            }
            // The wind as a push on the cloth, and a flutter that grows with it.
            var w = Wind.At(world.time);
            var push = new Vector3(w.x, 0f, w.y) * 16f;
            float flutter = 3f + 9f * w.magnitude;
            foreach (var kv in flags)
            {
                var f = kv.Value;
                if (f.root == null) continue;
                bool shown = kv.Key.Complete && Shown(f.shownBy);
                if (f.root.activeSelf != shown) f.root.SetActive(shown);
                if (!shown || f.cloth == null) continue;
                // Each banner catches the gusts a little out of step with the next.
                float gust = 0.8f + 0.35f * Mathf.PerlinNoise(Time.time * 0.7f, f.phase);
                f.cloth.externalAcceleration = push * gust;
                f.cloth.randomAcceleration = new Vector3(flutter, flutter * 0.5f, flutter);
            }
        }

        static bool Shown(Renderer[] rs)
        {
            foreach (var r in rs) if (r != null && r.enabled) return true;
            return false;
        }

        void Scan()
        {
            gone.Clear();
            foreach (var kv in flags)
                if (kv.Key == null || kv.Key.dying || kv.Key.view == null || kv.Value.root == null) gone.Add(kv.Key);
            foreach (var u in gone)
            {
                if (flags.TryGetValue(u, out var f) && f.root != null) Destroy(f.root);
                flags.Remove(u);
            }
            foreach (var u in world.units)
            {
                if (u == null || u.dying || u.Type != UnitType.Foundry || u.view == null || flags.ContainsKey(u)) continue;
                var f = Make(u);
                if (f != null) flags[u] = f;
            }
        }

        Flag Make(Unit u)
        {
            var fx = FXDirector.Current;
            if (fx == null || fx.debrisMaterial == null) return null;
            if (cloth == null)
            {
                // The debris material (URP Particles Simple Lit) drawn from both sides and
                // white under the vertex colours, which paint the banner: no new shader,
                // no new variant.
                cloth = new Material(fx.debrisMaterial) { name = "SF_Banner" };
                cloth.SetColor("_BaseColor", Color.white);
                cloth.SetFloat("_Cull", 0f);
                pole = new Material(fx.debrisMaterial) { name = "SF_BannerPole" };
                pole.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.33f));
            }
            var body = u.view.body != null ? u.view.body : u.view.transform;
            var root = new GameObject("Banner");
            root.transform.SetParent(u.view.transform, false);
            // On the ground just outside the footprint, at the back corner.
            float r = u.def.radius + 0.35f;
            root.transform.localPosition = new Vector3(-0.72f * r, 0f, -0.72f * r);
            root.transform.localRotation = Quaternion.identity;

            var poleGo = new GameObject("Pole");
            poleGo.transform.SetParent(root.transform, false);
            poleGo.transform.localPosition = Vector3.up * PoleHeight * 0.5f;
            poleGo.transform.localScale = new Vector3(0.11f, PoleHeight * 0.5f, 0.11f);
            poleGo.AddComponent<MeshFilter>().sharedMesh = poleMesh;
            var pr = poleGo.AddComponent<MeshRenderer>();
            pr.sharedMaterial = pole;
            pr.shadowCastingMode = ShadowCastingMode.On;

            var clothGo = new GameObject("Cloth");
            clothGo.transform.SetParent(root.transform, false);
            clothGo.transform.localPosition = Vector3.up * (PoleHeight - 0.15f);
            var smr = clothGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = BannerMesh(u.team);
            smr.sharedMaterial = cloth;
            smr.shadowCastingMode = ShadowCastingMode.On;
            var c = clothGo.AddComponent<Cloth>();
            c.useGravity = true;
            c.damping = 0.08f;
            c.stretchingStiffness = 0.95f;
            c.bendingStiffness = 0.35f;
            c.worldVelocityScale = 0f;
            c.worldAccelerationScale = 0f;
            c.sleepThreshold = 0.05f;
            // Pinned along the pole; the rest free to move a banner's length.
            var coeff = c.coefficients;
            var verts = c.vertices;
            for (int i = 0; i < coeff.Length && i < verts.Length; i++)
            {
                coeff[i].maxDistance = verts[i].x < 0.01f ? 0f : Width * 1.2f;
                coeff[i].collisionSphereDistance = 0f;
            }
            c.coefficients = coeff;

            return new Flag { root = root, cloth = c, shownBy = body.GetComponentsInChildren<Renderer>(), phase = u.id * 0.37f };
        }

        /// <summary>A grid hanging from the top of the pole along +x: the team's colour, a
        /// dark hem round the free edges and a pale band across the middle.</summary>
        static Mesh BannerMesh(int team)
        {
            Color main = team == 0 ? FXDirector.PlayerColor : FXDirector.EnemyColor;
            main = Color.Lerp(main, Color.gray, 0.15f);
            Color hem = main * 0.45f, band = Color.Lerp(main, Color.white, 0.6f);
            hem.a = band.a = main.a = 1f;
            var v = new Vector3[Cols * Rows];
            var col = new Color[v.Length];
            var uv = new Vector2[v.Length];
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    float fx = x / (Cols - 1f), fy = y / (Rows - 1f);
                    int i = y * Cols + x;
                    v[i] = new Vector3(fx * Width, -fy * Drop, 0f);
                    uv[i] = new Vector2(fx, 1f - fy);
                    bool edge = x == Cols - 1 || y == 0 || y == Rows - 1;
                    bool mid = y == Rows / 2 && x > 1 && x < Cols - 2;
                    col[i] = edge && x > 0 ? hem : mid ? band : main;
                }
            var tris = new List<int>();
            for (int y = 0; y < Rows - 1; y++)
                for (int x = 0; x < Cols - 1; x++)
                {
                    int a = y * Cols + x, b = a + 1, c = a + Cols, d = c + 1;
                    tris.AddRange(new[] { a, b, c, b, d, c });
                }
            var mesh = new Mesh { name = "SF_Banner" };
            mesh.vertices = v;
            mesh.colors = col;
            mesh.uv = uv;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
