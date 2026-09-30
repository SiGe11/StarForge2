// FogOfWarRenderer.cs — uploads the player's visibility grid for the full-screen
// fog pass. The simulation grid is 64x64 and flips in discrete steps on its own
// timer, so each texel is eased toward its target rather than copied (a straight
// copy pops open four metres at a time) and upsampled to 128 for softer edges.
//
// The upsampled targets change only when a cell of the grid does (a few times a
// second at most), so they are kept, and worked out again only for the texels
// round a cell that changed: resampling all 128x128 of them twice a frame cost
// thirty times what the easing does. The easing still runs every frame, and the
// texture goes up only when a byte of it changed.
using UnityEngine;
using StarForge.World;

namespace StarForge.View
{
    public sealed class FogOfWarRenderer : MonoBehaviour
    {
        public GameWorld world;
        public int team;
        public bool fogEnabled = true;

        const int R = 128;
        const int N = GameWorld.VIS;
        Texture2D tex;
        float[] seen, known;
        Color32[] pixels;
        bool uploaded;

        // The grid the targets were worked out from, and the targets: the grid
        // bilinearly upsampled to R x R.
        byte[] visAt, exploredAt;
        int[] changedCells;
        float[] seenTarget, knownTarget;
        bool targetsValid;
        int targetsTeam, targetsVersion;
        GameWorld targetsWorld;

        // Where a texel samples the grid along either axis: the two cells and the
        // weight between them. And the other way round: the texels, lo..hi, that
        // read a cell, so a changed cell resamples only those.
        int[] tap0, tap1, texLo, texHi;
        float[] tapW;

        static readonly int FogTexId = Shader.PropertyToID("_SF_FogTex");
        static readonly int ParamsId = Shader.PropertyToID("_SF_FogParams");

        void Awake()
        {
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            tex = new Texture2D(R, R, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SF_FogOfWar"
            };
            seen = new float[R * R];
            known = new float[R * R];
            pixels = new Color32[R * R];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 255);
            visAt = new byte[N * N];
            exploredAt = new byte[N * N];
            changedCells = new int[N * N];
            seenTarget = new float[R * R];
            knownTarget = new float[R * R];
            BuildTaps();
            Shader.SetGlobalTexture(FogTexId, tex);
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        void BuildTaps()
        {
            tap0 = new int[R]; tap1 = new int[R]; tapW = new float[R];
            texLo = new int[N]; texHi = new int[N];
            int n = N;
            float scale = (float)GameWorld.VIS / R;
            for (int c = 0; c < n; c++) { texLo[c] = R; texHi[c] = -1; }
            for (int t = 0; t < R; t++)
            {
                float g = (t + 0.5f) * scale - 0.5f;
                g = Mathf.Clamp(g, 0f, n - 1.001f);
                int g0 = (int)g;
                tap0[t] = g0;
                tapW[t] = g - g0;
                tap1[t] = Mathf.Min(n - 1, g0 + 1);
                texLo[g0] = Mathf.Min(texLo[g0], t); texHi[g0] = Mathf.Max(texHi[g0], t);
                texLo[tap1[t]] = Mathf.Min(texLo[tap1[t]], t); texHi[tap1[t]] = Mathf.Max(texHi[tap1[t]], t);
            }
        }

        float Sample(byte[] grid, int tx, int tz)
        {
            int x0 = tap0[tx], x1 = tap1[tx], z0 = tap0[tz], z1 = tap1[tz];
            float fx = tapW[tx], fz = tapW[tz];
            float a = grid[z0 * N + x0], b = grid[z0 * N + x1];
            float c = grid[z1 * N + x0], d = grid[z1 * N + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        void Resample(int tx0, int tx1, int tz0, int tz1)
        {
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    int i = tz * R + tx;
                    seenTarget[i] = Sample(visAt, tx, tz);
                    knownTarget[i] = Sample(exploredAt, tx, tz);
                }
        }

        /// <summary>Bring the targets up to date with the grid: all of them for a new
        /// side or world, else only the texels that read a cell that changed.</summary>
        void UpdateTargets()
        {
            bool all = !targetsValid || team != targetsTeam || !ReferenceEquals(world, targetsWorld);
            // The grid is written only when the world works out what each side sees.
            if (!all && world.VisVersion == targetsVersion) return;
            targetsVersion = world.VisVersion;
            int count = 0;
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    int i = z * N + x;
                    byte v = world.VisibleCell(team, x, z), e = world.ExploredCell(team, x, z);
                    if (v == visAt[i] && e == exploredAt[i]) continue;
                    visAt[i] = v;
                    exploredAt[i] = e;
                    changedCells[count++] = i;
                }
            // A cell is read by about 4 x 4 texels; past a sixteenth of the grid it is
            // cheaper to do them all.
            if (all || count * 16 >= R * R)
            {
                Resample(0, R - 1, 0, R - 1);
                targetsValid = true;
                targetsTeam = team;
                targetsWorld = world;
                return;
            }
            for (int n = 0; n < count; n++)
            {
                int x = changedCells[n] % N, z = changedCells[n] / N;
                Resample(texLo[x], texHi[x], texLo[z], texHi[z]);
            }
        }

        void LateUpdate()
        {
            // A spectator is not a side in the match, so there is nothing to fog.
            if (world == null || !world.running || !fogEnabled || StarForge.Game.MatchSettings.spectate)
            {
                Shader.SetGlobalVector(ParamsId, Vector4.zero);
                return;
            }
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 7f);
            UpdateTargets();
            bool changed = !uploaded;
            for (int i = 0; i < R * R; i++)
            {
                seen[i] = Mathf.Lerp(seen[i], seenTarget[i], k);
                known[i] = Mathf.Lerp(known[i], knownTarget[i], k);
                byte r = (byte)(seen[i] * 255f), g = (byte)(known[i] * 255f);
                if (r == pixels[i].r && g == pixels[i].g) continue;
                pixels[i] = new Color32(r, g, 0, 255);
                changed = true;
            }
            if (changed)
            {
                tex.SetPixels32(pixels);
                tex.Apply(false);
                uploaded = true;
            }
            Shader.SetGlobalVector(ParamsId, new Vector4(1f / world.MapSize, 1f, Time.time, 0f));
        }

        void OnDisable() => Shader.SetGlobalVector(ParamsId, Vector4.zero);
    }
}
