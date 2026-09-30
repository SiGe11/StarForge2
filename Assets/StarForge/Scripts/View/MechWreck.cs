// MechWreck.cs — a Mech's death, played out with rigid bodies on the terrain.
//
// The reactor goes (GameWorld.Kill's blast, FXDirector's chain of explosions): the
// torso is heaved off its waist -- or left slumped on it -- guns tear off their mounts
// and tumble away, and the legs buckle, knees folding, until what stood seven metres
// tall lies in the grass burning. It smoulders a long while (FXDirector), then sinks.
// Presentation only: the simulation removed the Mech when it died.
using System.Collections.Generic;
using UnityEngine;
using StarForge.World;

namespace StarForge.View
{
    public sealed class MechWreck : MonoBehaviour
    {
        public static readonly List<MechWreck> Active = new List<MechWreck>(4);

        public float age;
        public int team;
        /// <summary>How hard it burns, 0..1.</summary>
        public float Heat => Mathf.Clamp01(1f - age / BurnFor) * Mathf.Clamp01(age * 3f);
        /// <summary>Points on the wreck that burn (the torso, the hips), wherever they have come to rest.</summary>
        public readonly List<Transform> firePoints = new List<Transform>(4);

        const float BurnFor = 32f, LieFor = 46f, SinkFor = 7f;

        readonly List<Rigidbody> bodies = new List<Rigidbody>(8);
        readonly List<Collider> colliders = new List<Collider>(8);
        readonly List<(Transform t, Quaternion from, Quaternion to)> folds = new List<(Transform, Quaternion, Quaternion)>();
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        bool frozen;
        float sunk, sinkDepth = 8f;

        static readonly int BurnId = Shader.PropertyToID("_Burn");
        static readonly int DamageId = Shader.PropertyToID("_Damage");
        static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        static PhysicsMaterial heavyMat;

        public static void Create(MechView view, Unit unit, Transform body, Transform legs, Transform torso, Vector3 velocity)
        {
            if (body == null) return;
            var go = new GameObject("Mech Wreck");
            var w = go.AddComponent<MechWreck>();
            w.Build(view, unit, body, legs, torso, velocity);
        }

        void Build(MechView view, Unit unit, Transform body, Transform legs, Transform torso, Vector3 vel)
        {
            team = unit.team;
            heavyMat ??= new PhysicsMaterial("Wreck Mech") { staticFriction = 0.9f, dynamicFriction = 0.7f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            var map = unit.World.Map;
            Vector3 k = new Vector3(unit.killDir.x, 0f, unit.killDir.y);
            if (k.sqrMagnitude < 1e-4f) k = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            k.Normalize();
            Vector3 up = Vector3.up;

            // Guns first: some tear away on their own.
            var loose = new List<Transform>();
            if (torso != null)
                foreach (Transform c in torso)
                    if (c.name.StartsWith("MECH_W_") && Random.value < 0.55f) loose.Add(c);

            // The torso: blown off the waist more often than not.
            bool torsoOff = torso != null && Random.value < 0.65f;
            Rigidbody torsoRb = null;
            if (torsoOff)
            {
                var tg = Holder("Torso", torso);
                torsoRb = Body(tg, 9000f, 0.4f);
                torsoRb.linearVelocity = vel * 0.5f + up * Random.Range(6f, 9f) + (k + Random.insideUnitSphere * 0.4f) * Random.Range(1.5f, 3.5f);
                torsoRb.angularVelocity = Random.insideUnitSphere * Random.Range(1.2f, 2.6f);
                Fire(tg.transform, 0.6f);
            }

            foreach (var g in loose)
            {
                var hg = Holder(g.name, g);
                var rb = Body(hg, 700f, 0.8f);
                Vector3 away = (g.position - unit.transform.position);
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere;
                rb.linearVelocity = vel * 0.4f + up * Random.Range(5f, 11f) + away * Random.Range(3f, 7f);
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(4f, 10f);
            }

            // What is left standing: the legs (and the torso, if it stayed on), falling over.
            var lg = new GameObject("Legs");
            lg.transform.SetParent(transform, false);
            lg.transform.SetPositionAndRotation(unit.transform.position, unit.transform.rotation);
            body.SetParent(lg.transform, true);
            bool walker = view.Locomotion == MechLocomotion.Biped || view.Locomotion == MechLocomotion.Quad;
            // A walker's legs give way under it: the box is round the hips alone (and the
            // torso if it stayed on), so the body comes down as the knees fold, the legs
            // splaying out through the grass, instead of standing on a box the size of
            // its stride.
            var legsRb = Body(lg, 30000f, 0.2f, walker ? (System.Func<Transform, bool>)(t => !IsLeg(t)) : null);
            var lb = legsRb.GetComponent<BoxCollider>();
            if (lb != null) legsRb.centerOfMass = lb.center + Vector3.up * lb.size.y * (walker ? 0.2f : -0.2f);
            Vector3 tip = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * k;
            legsRb.linearVelocity = vel * 0.5f + up * (walker ? 1.2f : 3f) + k * 0.8f;
            legsRb.angularVelocity = Vector3.Cross(up, tip) * (walker ? Random.Range(0.5f, 0.9f) : Random.Range(0.2f, 0.5f));
            Fire(lg.transform, walker ? 4f : 2f);
            if (!torsoOff && torso != null) Fire(torso, 0.6f);

            // Knees give: a Strider's thighs swing forward and its shins fold back under it;
            // an Arachnid's legs lift and splay as its belly comes down.
            if (legs != null)
                foreach (var t in legs.GetComponentsInChildren<Transform>())
                {
                    if (view.Locomotion == MechLocomotion.Quad && t.name.StartsWith("Leg"))
                    {
                        Vector3 d = t.localPosition;
                        d.y = 0f;
                        Vector3 axis = Vector3.Cross(Vector3.up, d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward);
                        folds.Add((t, t.localRotation, Quaternion.AngleAxis(-Random.Range(25f, 45f), axis) * t.localRotation));
                    }
                    else if (view.Locomotion == MechLocomotion.Quad && t.name.StartsWith("Shin"))
                        folds.Add((t, t.localRotation, t.localRotation * Quaternion.Euler(Random.Range(-20f, 20f), 0f, Random.Range(-15f, 15f))));
                    else if (t.name.StartsWith("Thigh"))
                        folds.Add((t, t.localRotation, t.localRotation * Quaternion.Euler(-Random.Range(35f, 60f), Random.Range(-12f, 12f), 0f)));
                    else if (t.name.StartsWith("Shin"))
                        folds.Add((t, t.localRotation, t.localRotation * Quaternion.Euler(Random.Range(50f, 80f), 0f, 0f)));
                }

            // The pieces overlap where they were joined: they must not push each other apart.
            for (int i = 0; i < colliders.Count; i++)
                for (int j = i + 1; j < colliders.Count; j++)
                    Physics.IgnoreCollision(colliders[i], colliders[j]);

            // Clear of the ground (PhysX throws out a body it finds buried).
            float ground = map.HeightAt(unit.pos);
            foreach (var rb in bodies)
            {
                var c = rb.GetComponent<Collider>();
                if (c == null) continue;
                float bottom = c.bounds.min.y;
                if (bottom < ground + 0.05f) rb.transform.position += Vector3.up * (ground + 0.05f - bottom);
            }

            if (lb != null) sinkDepth = lb.size.y + 1f;
            renderers = GetComponentsInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            Surface();
            Active.Add(this);
        }

        GameObject Holder(string name, Transform part)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            g.transform.SetPositionAndRotation(part.position, part.rotation);
            part.SetParent(g.transform, true);
            return g;
        }

        static bool IsLeg(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.StartsWith("Thigh") || t.name.StartsWith("Shin") || t.name.StartsWith("Foot") || t.name.StartsWith("Leg")) return true;
            return false;
        }

        Rigidbody Body(GameObject g, float mass, float shrink, System.Func<Transform, bool> include = null)
        {
            var b = LocalBounds(g.transform, include);
            var box = g.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = new Vector3(Mathf.Max(0.2f, b.size.x * (1f - shrink * 0.3f)), Mathf.Max(0.2f, b.size.y), Mathf.Max(0.2f, b.size.z * (1f - shrink * 0.3f)));
            box.sharedMaterial = heavyMat;
            colliders.Add(box);
            var rb = g.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxDepenetrationVelocity = 2f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.3f;
            bodies.Add(rb);
            return rb;
        }

        void Fire(Transform on, float up)
        {
            var f = new GameObject("Fire").transform;
            f.SetParent(on, false);
            f.localPosition = Vector3.up * up;
            firePoints.Add(f);
        }

        void OnDestroy() => Active.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            float fk = 1f - (1f - Mathf.Clamp01(age / 0.9f)) * (1f - Mathf.Clamp01(age / 0.9f));
            foreach (var (t, from, to) in folds) if (t != null) t.localRotation = Quaternion.Slerp(from, to, fk);

            if (!frozen)
            {
                bool still = true;
                foreach (var rb in bodies)
                    if (rb != null && !rb.IsSleeping() && (rb.linearVelocity.sqrMagnitude > 0.02f || rb.angularVelocity.sqrMagnitude > 0.02f)) { still = false; break; }
                if ((still && age > 3f) || age > 9f) Freeze();
            }
            Surface();
            if (age > LieFor)
            {
                if (!frozen) Freeze();
                float step = sinkDepth / SinkFor * dt;
                sunk += step;
                transform.position += Vector3.down * step;
                if (sunk >= sinkDepth) Destroy(gameObject);
            }
        }

        void Freeze()
        {
            frozen = true;
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.isKinematic = true;
            }
            foreach (var c in colliders) if (c != null) c.enabled = false;
        }

        void Surface()
        {
            // A dull glow in the cracks for the first seconds, not a hull of lava.
            float burn = 0.16f * (1f - Mathf.Clamp01(age / 8f));
            float damage = 0.95f * Mathf.Clamp01(age / 0.6f) * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(age / (BurnFor * 0.6f)));
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(BurnId, burn);
                mpb.SetFloat(DamageId, damage);
                mpb.SetColor(FlashId, Color.black);
                r.SetPropertyBlock(mpb);
            }
        }

        static Bounds LocalBounds(Transform space, System.Func<Transform, bool> include = null)
        {
            bool any = false;
            var b = new Bounds(Vector3.up * 0.5f, Vector3.one * 0.5f);
            foreach (var mf in space.GetComponentsInChildren<MeshFilter>())
            {
                if (include != null && !include(mf.transform)) continue;
                var m = mf.sharedMesh;
                if (m == null) continue;
                var mb = m.bounds;
                var toSpace = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = toSpace.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
