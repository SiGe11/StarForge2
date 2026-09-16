// SFMath.cs — scalar helpers, deterministic PRNG and value noise.
//
// Ported from the original C++ core (Math.h / Random.h). The PRNG and noise are
// bit-for-bit deterministic so a seed produces the same map on every machine.
using System;
using UnityEngine;

namespace StarForge
{
    public static class SFMath
    {
        public const float PI = Mathf.PI;
        public const float TAU = Mathf.PI * 2f;

        public static float Saturate(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
        public static float Clamp(float x, float a, float b) => x < a ? a : (x > b ? b : x);
        public static int ClampI(int x, int a, int b) => x < a ? a : (x > b ? b : x);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Smoothstep(float a, float b, float x)
        {
            float t = Saturate((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static float WrapAngle(float a)
        {
            a = (a + PI) % TAU;
            if (a < 0f) a += TAU;
            return a - PI;
        }

        public static float ApproachAngle(float cur, float target, float maxStep)
        {
            float d = WrapAngle(target - cur);
            if (Mathf.Abs(d) <= maxStep) return target;
            return WrapAngle(cur + Mathf.Sign(d) * maxStep);
        }

        public static Vector2 Perp(Vector2 v) => new Vector2(-v.y, v.x);

        public static Vector2 Norm(Vector2 v)
        {
            float l = v.magnitude;
            return l > 1e-6f ? v / l : Vector2.zero;
        }

        public static float SoftIndicator(float x, float lo, float hi) =>
            Saturate((x - lo) / Mathf.Max(1e-3f, hi - lo));
    }

    /// <summary>xorshift PRNG, identical sequence to the original C++ Rng.</summary>
    public sealed class Rng
    {
        ulong s;

        public Rng(ulong seed = 12345)
        {
            unchecked { s = seed * 6364136223846793005UL + 1442695040888963407UL; }
            Next();
        }

        public uint Next()
        {
            unchecked
            {
                s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
                return (uint)((s * 2685821657736338717UL) >> 32);
            }
        }

        public float F01() => (Next() & 0xFFFFFF) / (float)0x1000000;
        public float Range(float a, float b) => a + (b - a) * F01();
        public int IRange(int a, int b) => b <= a ? a : a + (int)(Next() % (uint)(b - a));
    }

    public static class Noise
    {
        public static float Hash2(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0x1000000;
            }
        }

        public static float Value(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);
            float a = Hash2(xi, yi), b = Hash2(xi + 1, yi);
            float c = Hash2(xi, yi + 1), d = Hash2(xi + 1, yi + 1);
            return SFMath.Lerp(SFMath.Lerp(a, b, ux), SFMath.Lerp(c, d, ux), uy);
        }

        public static float Fbm(float x, float y, int octaves = 5, float lac = 2.03f, float gain = 0.5f)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Value(x, y);
                norm += amp;
                x *= lac; y *= lac; amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Ridged variant: mountain spines that read well under a low sun.</summary>
        public static float Ridge(float x, float y, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Math.Abs(Value(x, y) * 2f - 1f);
                sum += amp * n * n;
                norm += amp;
                x *= 2.07f; y *= 2.07f; amp *= 0.5f;
            }
            return sum / norm;
        }
    }

    /// <summary>Binary min-heap of (float key, int value); no allocation once warm.</summary>
    public sealed class MinHeap
    {
        float[] keys = new float[1024];
        int[] vals = new int[1024];
        int n;

        public int Count => n;
        public void Clear() => n = 0;

        public void Push(float key, int val)
        {
            if (n == keys.Length)
            {
                Array.Resize(ref keys, n * 2);
                Array.Resize(ref vals, n * 2);
            }
            int i = n++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (keys[p] <= key) break;
                keys[i] = keys[p]; vals[i] = vals[p];
                i = p;
            }
            keys[i] = key; vals[i] = val;
        }

        public void Pop(out float key, out int val)
        {
            key = keys[0]; val = vals[0];
            n--;
            if (n <= 0) return;
            float lk = keys[n]; int lv = vals[n];
            int i = 0;
            while (true)
            {
                int c = 2 * i + 1;
                if (c >= n) break;
                if (c + 1 < n && keys[c + 1] < keys[c]) c++;
                if (keys[c] >= lk) break;
                keys[i] = keys[c]; vals[i] = vals[c];
                i = c;
            }
            keys[i] = lk; vals[i] = lv;
        }
    }
}
