// Wind.cs — the wind over the battlefield: one heading and one strength for the
// whole map, gusting.
//
// Every match draws its own weather from the map seed: a heading, a base
// strength (a still day or a blustery one) and its own gust rhythm. Strength and
// heading are pure functions of the match clock, so nothing has to be stored or
// synchronised and a replayed seed blows exactly the same way.
//
// Everything that should agree about the wind reads it here: the trees and the
// grass sway with it (through the _SF_Wind global Atmosphere uploads), smoke and
// embers lean with it, fire runs downwind and spreads faster in a gust, a tree
// leans as it falls, and leaves tear off the crowns when it blows hard.
using UnityEngine;

namespace StarForge.World
{
    public static class Wind
    {
        /// <summary>The heading the wind blows toward, before the slow wander.</summary>
        public static Vector2 Heading { get; private set; } = new Vector2(0.82f, 0.57f).normalized;

        /// <summary>The day's strength, 0 (still) .. 1 (blustery), before gusts.</summary>
        public static float Weather { get; private set; } = 0.55f;

        static float phase;

        /// <summary>Draw this match's weather from the map seed.</summary>
        public static void Set(uint seed)
        {
            var rng = new Rng(seed ^ 0x5F1D0Cu);
            float a = rng.Range(0f, Mathf.PI * 2f);
            Heading = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            // Mostly a light breeze; now and then a windy day, rarely a still one.
            Weather = Mathf.Clamp(0.35f + rng.Range(0f, 0.75f) * rng.Range(0.3f, 1f), 0.15f, 1f);
            phase = rng.Range(0f, 100f);
        }

        /// <summary>How hard it is blowing at time <paramref name="t"/>: the day's strength
        /// under three gust rhythms, so it comes in waves and now and then drops away.</summary>
        public static float Speed(float t)
        {
            float g = 0.62f
                    + 0.26f * Mathf.Sin(t * 0.21f + phase)
                    + 0.14f * Mathf.Sin(t * 0.083f + phase * 1.7f)
                    + 0.10f * Mathf.Sin(t * 0.55f + phase * 2.3f);
            return Mathf.Clamp(Weather * g * 1.35f, 0.05f, 1.4f);
        }

        /// <summary>The heading now: it wanders a little either side of the day's.</summary>
        public static Vector2 Direction(float t)
        {
            float turn = (Mathf.Sin(t * 0.037f + phase) * 0.5f + Mathf.Sin(t * 0.011f) * 0.5f) * 14f * Mathf.Deg2Rad;
            float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
            return new Vector2(Heading.x * c - Heading.y * s, Heading.x * s + Heading.y * c);
        }

        /// <summary>Heading times strength: what smoke and embers drift with.</summary>
        public static Vector2 At(float t) => Direction(t) * Speed(t);
    }
}
