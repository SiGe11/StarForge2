// FXDirector.Ground.cs — what the ground is made of where something strikes it, so the
// dust and clods that come off it are its own: a pale plume off sand, a puff of ash off
// burnt grass, torn turf and little dust off a meadow, brown dust and stones off gravel,
// chips and hardly any dust off rock, mud and no dust at the water's edge. Read from the
// same splat rule the terrain is painted by (MapGenerator.SplatAt), the grass fire's grid
// and the water level, so it agrees with what the player sees under the foot.
using UnityEngine;
using StarForge.World;

namespace StarForge.View
{
    public sealed partial class FXDirector
    {
        struct Ground
        {
            public Color dust, clod;
            public float dustAmount;   // ~1 on loose dry ground
            public float clods;        // how many clods to throw, relative to gravel
        }

        // Per surface: dust colour, clod colour, dust amount, clods.
        static readonly Color MeadowDust = new Color(0.55f, 0.52f, 0.42f), MeadowClod = new Color(0.24f, 0.29f, 0.14f);
        static readonly Color GravelDust = new Color(0.66f, 0.60f, 0.50f), GravelClod = new Color(0.50f, 0.46f, 0.40f);
        static readonly Color RockDust = new Color(0.62f, 0.61f, 0.58f), RockClod = new Color(0.56f, 0.55f, 0.53f);
        static readonly Color SandDust = new Color(0.80f, 0.74f, 0.60f), SandClod = new Color(0.72f, 0.66f, 0.52f);
        static readonly Color AshDust = new Color(0.30f, 0.29f, 0.28f), AshClod = new Color(0.10f, 0.10f, 0.10f);
        static readonly Color MudClod = new Color(0.22f, 0.18f, 0.13f);

        Ground GroundAt(Vector3 at)
        {
            var map = world.Map;
            var p = new Vector2(at.x, at.z);
            // Splat weights: lichen (the meadows), gravel, cliff, ash (sand and dry ground).
            var w = MapGenerator.SplatAt(at.x, at.z, map.HeightAt(p), map.NormalAt(p));
            var g = new Ground
            {
                dust = MeadowDust * w.x + GravelDust * w.y + RockDust * w.z + SandDust * w.w,
                clod = MeadowClod * w.x + GravelClod * w.y + RockClod * w.z + SandClod * w.w,
                dustAmount = 0.45f * w.x + 1f * w.y + 0.35f * w.z + 1.4f * w.w,
                clods = 0.8f * w.x + 1f * w.y + 0.9f * w.z + 0.35f * w.w,
            };
            // Burnt grass: a puff of ash, charcoal for clods.
            var veg = Plants;
            if (veg != null && veg.GrassState != null)
            {
                int c = veg.GrassIndex(p);
                if (c >= 0 && c < veg.GrassState.Length && veg.GrassState[c] == 2)
                {
                    g.dust = Color.Lerp(g.dust, AshDust, 0.8f);
                    g.clod = Color.Lerp(g.clod, AshClod, 0.7f);
                    g.dustAmount = Mathf.Max(g.dustAmount, 1.2f);
                }
            }
            // At the water's edge the ground is wet: mud, and no dust to speak of.
            float above = -map.WaterDepth(p);
            float damp = 1f - Mathf.Clamp01(above / 0.8f);
            if (damp > 0f)
            {
                g.clod = Color.Lerp(g.clod, MudClod, damp);
                g.dustAmount *= 1f - 0.85f * damp;
                g.clods *= 1f + 0.6f * damp;
            }
            g.dust.a = g.clod.a = 1f;
            return g;
        }

        /// <summary>For checks: what the ground at a point is taken to be.</summary>
        public string GroundInfo(Vector3 at)
        {
            var g = GroundAt(at);
            return $"dust {g.dust.r:0.00}/{g.dust.g:0.00}/{g.dust.b:0.00} x{g.dustAmount:0.00}, clods {g.clod.r:0.00}/{g.clod.g:0.00}/{g.clod.b:0.00} x{g.clods:0.00}";
        }

        /// <summary>A Mech's foot coming down on dry ground (and a check's stand-in for
        /// one): a ring of dust and a spray of clods in the ground's own colours, and a
        /// footprint lasting as long as a tank's tracks do.</summary>
        public void GroundStrike(Vector3 at, float weight, float yawDeg)
        {
            var gr = GroundAt(at);
            int puffs = Mathf.RoundToInt(7f * Mathf.Clamp(gr.dustAmount, 0.3f, 1.4f));
            float thick = Mathf.Min(1.2f, gr.dustAmount + 0.2f);
            for (int i = 0; i < puffs; i++)
            {
                float a = (i + Random.value * 0.5f) / puffs * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Emit(trail, at + d * 0.6f + Vector3.up * 0.2f, d * Random.Range(1.5f, 3.2f) * weight + Vector3.up * Random.Range(0.2f, 0.7f),
                     Random.Range(1.2f, 1.9f) * (0.6f + weight * 0.5f), Random.Range(1.4f, 2.2f),
                     new Color(gr.dust.r, gr.dust.g, gr.dust.b, (0.32f * weight + 0.1f) * thick), Random.Range(0f, 360f));
            }
            if (debris != null && Random.value < 0.6f * Mathf.Min(1f, gr.clods + 0.2f))
                for (int i = Mathf.Max(1, Mathf.RoundToInt(4f * gr.clods)); i > 0; i--)
                {
                    var c = gr.clod * Random.Range(0.8f, 1.15f);
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = at + Vector3.up * 0.15f, velocity = Random.insideUnitSphere * 2.5f + Vector3.up * Random.Range(2f, 4f),
                        startSize = Random.Range(0.04f, 0.1f), startLifetime = Random.Range(1f, 1.8f),
                        startColor = new Color(c.r, c.g, c.b, 1f), rotation3D = Random.insideUnitSphere * 180f, applyShapeToPosition = false
                    }, 1);
                }
            trackMarks[trackHead] = new TrackMark
            {
                // A solid, soft-edged pad: with the tread's cleats (pitch 2.5) a line of
                // them read as the rungs of a ladder.
                pos = new Vector3(at.x, world.Map.HeightAt(new Vector2(at.x, at.z)), at.z), yaw = yawDeg, length = 1.7f, width = 1.3f,
                born = Time.time, band = 0f, halfBand = 0.85f, pitch = 0f
            };
            trackHead = (trackHead + 1) % MaxTracks;
            trackCount = Mathf.Min(trackCount + 1, MaxTracks);
        }
    }
}
