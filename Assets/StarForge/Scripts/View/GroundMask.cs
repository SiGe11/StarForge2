// GroundMask.cs — what the battle has done to the ground, as a map-wide texture.
//
//   R  flattened: under structures and ore seams (rebuilt twice a second)
//   G  burned: where explosions and deaths landed (heals over a few minutes)
//   B  trampled: behind moving ground units (fades in ~15 s)
//
// The grass shader bends, parts and chars on it; the terrain shader darkens
// scorched and churned soil. Presentation only: nothing reads it back.
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed class GroundMask : MonoBehaviour
    {
        public GameWorld world;

        const int R = 256;
        Texture2D tex;
        readonly byte[] flat = new byte[R * R], burn = new byte[R * R], trample = new byte[R * R];
        Color32[] pixels;
        float texel = 1f;
        float nextFlat, nextTrample, nextHeal, nextUpload;
        bool dirty = true;

        static readonly int TexId = Shader.PropertyToID("_SF_GroundMask");
        static readonly int ParamsId = Shader.PropertyToID("_SF_GroundMaskParams");

        void Awake()
        {
            if (world == null) world = FindAnyObjectByType<GameWorld>();
            tex = new Texture2D(R, R, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SF_GroundMask"
            };
            pixels = new Color32[R * R];
            Shader.SetGlobalTexture(TexId, tex);
        }

        void OnEnable()
        {
            if (world != null) world.Event += OnEvent;
        }

        void OnDisable()
        {
            if (world != null) world.Event -= OnEvent;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        void OnDestroy()
        {
            if (tex != null) Destroy(tex);
        }

        void OnEvent(GameEvent e)
        {
            switch (e.kind)
            {
                case GameEventKind.Impact:
                    if (e.scale > 1f) Stamp(burn, e.pos, 1.3f * e.scale, 200);
                    break;
                case GameEventKind.Death:
                    if (e.type == UnitType.Ore || e.type == UnitType.Boulder) break;
                    float r = e.unit != null && e.unit.def.building ? e.unit.def.radius * 1.5f : 1.6f * Mathf.Max(1f, e.scale);
                    Stamp(burn, e.pos, r, 255);
                    break;
            }
        }

        void Stamp(byte[] ch, Vector3 at, float radius, byte value, bool hard = false)
        {
            float px = at.x / texel, pz = at.z / texel, pr = radius / texel;
            int x0 = Mathf.Max(0, (int)(px - pr - 1)), x1 = Mathf.Min(R - 1, (int)(px + pr + 1));
            int z0 = Mathf.Max(0, (int)(pz - pr - 1)), z1 = Mathf.Min(R - 1, (int)(pz + pr + 1));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - px, dz = z + 0.5f - pz;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    float w = hard ? (d <= pr ? 1f : 0f) : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pr * 0.55f, pr, d));
                    byte v = (byte)(value * w);
                    int i = z * R + x;
                    if (v > ch[i]) { ch[i] = v; dirty = true; }
                }
        }

        void LateUpdate()
        {
            if (world == null || !world.running) return;
            texel = world.MapSize / R;
            float now = Time.time;

            if (now >= nextFlat)
            {
                nextFlat = now + 0.5f;
                System.Array.Clear(flat, 0, flat.Length);
                foreach (var u in world.units)
                {
                    if (!Unit.Live(u)) continue;
                    if (u.def.building) Stamp(flat, u.Ground, u.def.radius + 0.8f, 255);
                    else if (u.Type == UnitType.Ore) Stamp(flat, u.Ground, u.def.radius + 0.6f, 255);
                }
                dirty = true;
            }

            if (now >= nextTrample)
            {
                nextTrample = now + 0.1f;
                for (int i = 0; i < trample.Length; i++)
                    if (trample[i] > 0) { trample[i] = (byte)Mathf.Max(0, trample[i] - 2); dirty = true; }
                foreach (var u in world.units)
                {
                    // Skimmers hover over the grass; everything else parts it.
                    if (!Unit.Live(u) || !u.def.IsMobile || u.Type == UnitType.Skimmer) continue;
                    if (u.agent == null || !u.agent.enabled || u.agent.velocity.sqrMagnitude < 0.25f) continue;
                    Stamp(trample, u.Ground, u.def.radius * (u.Type == UnitType.Mauler ? 0.95f : 0.7f) + 0.25f, 255);
                }
            }

            if (now >= nextHeal)
            {
                nextHeal = now + 1f;
                for (int i = 0; i < burn.Length; i++)
                    if (burn[i] > 0) { burn[i]--; dirty = true; }
            }

            if (dirty && now >= nextUpload)
            {
                nextUpload = now + 0.1f;
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(flat[i], burn[i], trample[i], 255);
                tex.SetPixels32(pixels);
                tex.Apply(false);
                dirty = false;
            }
            Shader.SetGlobalVector(ParamsId, new Vector4(1f / world.MapSize, 1f, 0f, 0f));
        }
    }
}
