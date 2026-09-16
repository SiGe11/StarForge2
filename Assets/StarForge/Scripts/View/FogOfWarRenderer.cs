// FogOfWarRenderer.cs — uploads the player's visibility grid for the full-screen
// fog pass. The simulation grid is 64x64 and flips in discrete steps on its own
// timer, so each texel is eased toward its target rather than copied (a straight
// copy pops open four metres at a time) and upsampled to 128 for softer edges.
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
        Texture2D tex;
        float[] seen, known;
        Color32[] pixels;
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
            Shader.SetGlobalTexture(FogTexId, tex);
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        float Sample(int kind, float gx, float gz)
        {
            int n = GameWorld.VIS;
            gx = Mathf.Clamp(gx, 0f, n - 1.001f);
            gz = Mathf.Clamp(gz, 0f, n - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            int x1 = Mathf.Min(n - 1, x0 + 1), z1 = Mathf.Min(n - 1, z0 + 1);
            float a, b, c, d;
            if (kind == 0)
            {
                a = world.VisibleCell(team, x0, z0); b = world.VisibleCell(team, x1, z0);
                c = world.VisibleCell(team, x0, z1); d = world.VisibleCell(team, x1, z1);
            }
            else
            {
                a = world.ExploredCell(team, x0, z0); b = world.ExploredCell(team, x1, z0);
                c = world.ExploredCell(team, x0, z1); d = world.ExploredCell(team, x1, z1);
            }
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
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
            float scale = (float)GameWorld.VIS / R;
            for (int z = 0; z < R; z++)
                for (int x = 0; x < R; x++)
                {
                    int i = z * R + x;
                    float gx = (x + 0.5f) * scale - 0.5f, gz = (z + 0.5f) * scale - 0.5f;
                    seen[i] = Mathf.Lerp(seen[i], Sample(0, gx, gz), k);
                    known[i] = Mathf.Lerp(known[i], Sample(1, gx, gz), k);
                    pixels[i] = new Color32((byte)(seen[i] * 255f), (byte)(known[i] * 255f), 0, 255);
                }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            Shader.SetGlobalVector(ParamsId, new Vector4(1f / world.MapSize, 1f, Time.time, 0f));
        }

        void OnDisable() => Shader.SetGlobalVector(ParamsId, Vector4.zero);
    }
}
