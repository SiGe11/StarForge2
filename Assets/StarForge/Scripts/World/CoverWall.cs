// CoverWall.cs -- one straight run of low wall or ruin (the new combat; MapGenerator.PlaceCover).
//
// The rules read this, never the mesh: a rectangle on the ground from a to b, so thick and so high over
// the ground it stands on. A man crouched behind a low one (1.12-1.2 m) is out of a round's way where its top
// stands DuckedClear over his ground; standing he fires over it.
// Rounds (SightMap.FirstStop) and sight (stamped into SightMap's standing heights) stop on it, the cover
// search offers the ground behind it, and its band is the NavMesh's Vault (a low run: soldiers vault it) or
// Wall (a ruin's tall run), so no vehicle but a Mauler or a Mech goes through, breaking it.
using UnityEngine;

namespace StarForge.World
{
    public sealed class CoverWall
    {
        public Vector2 a, b;
        /// <summary>Half its thickness, its height over its foot, and where its top is (world height: a
        /// piece stands on the lowest ground under it).</summary>
        public float half, height, top;
        public bool smashed;
        /// <summary>The placed piece it belongs to (a ruin's corner is four runs of one piece).</summary>
        public GameObject piece;
        public int cluster;
        /// <summary>FirstStop's: a wall in several leaf cells is tried once a line.</summary>
        internal int mark;

        public Vector2 Mid => (a + b) * 0.5f;

        /// <summary>The nearest point of its middle line.</summary>
        public Vector2 Closest(Vector2 p)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return a + ab * t;
        }

        public float Distance(Vector2 p) => (p - Closest(p)).magnitude;

        /// <summary>Where the ground line o + d*t (t 0..1) is inside the wall's rectangle: from enter to
        /// leave. False if it never is.</summary>
        public bool Ray(float ox, float oz, float dx, float dz, out float enter, out float leave)
        {
            enter = 0f; leave = 1f;
            Vector2 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-4f) return false;
            float ux = ab.x / len, uz = ab.y / len;
            float rx = ox - a.x, rz = oz - a.y;
            float pu = rx * ux + rz * uz, pv = -rx * uz + rz * ux;
            float du = dx * ux + dz * uz, dv = -dx * uz + dz * ux;
            return Slab(pu, du, 0f, len, ref enter, ref leave) && Slab(pv, dv, -half, half, ref enter, ref leave);
        }

        static bool Slab(float p, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-6f) return p >= lo && p <= hi;
            float ta = (lo - p) / d, tb = (hi - p) / d;
            if (ta > tb) { float s = ta; ta = tb; tb = s; }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            return t0 <= t1;
        }
    }
}
