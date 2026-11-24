// Sanctuary.cs -- the kept Vesper twin house (lore section 4: "keeps the hours between the lines, and nobody
// fires on it"). The user's design (2026-11-24): an open sanctuary on about half the maps; foot units may go in
// (soldiers and Diggers); a unit inside can neither be fired on nor fire; nothing crosses it (no round, shell or
// splash over or into the grounds). Not a Unit: a plan in the world's plane, placed when a match begins
// (MapGenerator.PlaceSanctuary), asked by the rules (GameWorld.Sanctuary.cs). Its plan is the model's
// (Tools/blender/build_env.py, VESPER_HOUSE): change the two together.
using UnityEngine;

namespace StarForge.World
{
    public sealed class Sanctuary
    {
        /// <summary>The outer wall's line, half its length (local X) and depth (local Z); its thickness.</summary>
        public const float HalfX = 12f, HalfZ = 8f, Thick = 0.6f;
        /// <summary>The two gates in the front wall (local -Z), centred on these X, 3 m wide.</summary>
        public static readonly float[] GateX = { -6f, 6f };
        public const float GateWidth = 3f;
        /// <summary>The shared hall's front (local Z): solid in the rules from here to the back wall.</summary>
        public const float HallZ = 2f;
        /// <summary>How far outside the wall's line a point still counts as the grounds: the wall's half
        /// thickness and a little (a man pressed to its outside is outside).</summary>
        const float Edge = Thick * 0.5f + 0.15f;

        public Vector2 centre;
        /// <summary>The model's local X and Z in the world's plane (unit vectors).</summary>
        public Vector2 ax, az;
        public float yawDeg;
        public GameObject piece;
        /// <summary>Its cleared plants and rocks put back (MapGenerator.RestoreCleared).</summary>
        public bool restored;
        /// <summary>What was cleared off its ground when it was placed (put back if it is taken away).</summary>
        public readonly System.Collections.Generic.List<int> plantsCleared = new System.Collections.Generic.List<int>();
        public readonly System.Collections.Generic.List<Boulder> bouldersCleared = new System.Collections.Generic.List<Boulder>();

        public Sanctuary(Vector2 centre, float yawDeg)
        {
            this.centre = centre;
            this.yawDeg = yawDeg;
            float r = yawDeg * Mathf.Deg2Rad;
            // Unity's rotation about Y by yaw takes local +Z to (sin, cos) and local +X to (cos, -sin).
            az = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            ax = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
        }

        public Vector2 Local(Vector2 p)
        {
            Vector2 d = p - centre;
            return new Vector2(Vector2.Dot(d, ax), Vector2.Dot(d, az));
        }

        public Vector2 ToWorld(float x, float z) => centre + ax * x + az * z;

        /// <summary>Inside the grounds: within the outer wall (the wall itself included).</summary>
        public bool Contains(Vector2 p)
        {
            Vector2 l = Local(p);
            return Mathf.Abs(l.x) <= HalfX + Edge && Mathf.Abs(l.y) <= HalfZ + Edge;
        }

        /// <summary>Where along a to b (0..1) the line first meets the grounds; 0 if a is inside, 2 if it never
        /// does. Plan only: nothing passes over the grounds, however high (the truce holds in the air too).</summary>
        public float Enter(Vector2 a, Vector2 b)
        {
            Vector2 la = Local(a), lb = Local(b);
            float t0 = 0f, t1 = 1f;
            float dx = lb.x - la.x, dz = lb.y - la.y;
            float hx = HalfX + Edge, hz = HalfZ + Edge;
            if (!Clip(-dx, la.x + hx, ref t0, ref t1) || !Clip(dx, hx - la.x, ref t0, ref t1) ||
                !Clip(-dz, la.y + hz, ref t0, ref t1) || !Clip(dz, hz - la.y, ref t0, ref t1)) return 2f;
            return t0;
        }

        /// <summary>The line from a to b meets the grounds anywhere (an end inside counts).</summary>
        public bool Crosses(Vector2 a, Vector2 b) => Enter(a, b) <= 1f;

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            // Liang-Barsky: p = the step's component against the edge, q = the distance inside it.
            if (Mathf.Abs(p) < 1e-6f) return q >= 0f;
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        /// <summary>How far a point is from the house and the aprons before its gates (0 inside).</summary>
        public float DistanceToPlan(Vector2 p)
        {
            Vector2 l = Local(p);
            float dx = Mathf.Max(0f, Mathf.Abs(l.x) - HalfX - Edge);
            float dz = Mathf.Max(0f, Mathf.Max(l.y - HalfZ - Edge, -HalfZ - Apron - l.y));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>How far before the front wall the gates' aprons (area Sanctuary) reach.</summary>
        public const float Apron = 3f;
        /// <summary>The apron's width, the gate's 3 m and 2 m either side.</summary>
        public const float ApronWidth = 7f;

        /// <summary>The wall's height in the model (build_env.py): rounds to or from aircraft pass over it.</summary>
        public const float WallTop = 2.6f;

        /// <summary>A gate's own point just outside the wall (the way in), in the world.</summary>
        public Vector2 Gate(int i) => ToWorld(GateX[i], -HalfZ - 2f);

        /// <summary>The nearer gate to a point.</summary>
        public Vector2 NearestGate(Vector2 p) => (Gate(0) - p).sqrMagnitude <= (Gate(1) - p).sqrMagnitude ? Gate(0) : Gate(1);
    }
}
