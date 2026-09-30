// DebrisBurst.cs — a destroyed structure's pre-cut chunks (SF_*_CHUNKS.fbx):
// thrown apart by the blast glowing hot, left to tumble and settle on the
// terrain while they cool to char, then sunk out of sight. Presentation only.
//
// The chunks are rigid bodies on the terrain collider. The structure's own blast
// throws them (AddExplosionForce from low in the building, so they fly up and out),
// later blasts nearby shove the ones still loose (Blast), each hard landing throws
// up dust (ContactRelay), and FXDirector trails smoke and embers off the hot ones
// while they fly.
using System.Collections.Generic;
using UnityEngine;

namespace StarForge.View
{
    public sealed class DebrisBurst : MonoBehaviour
    {
        /// <summary>Bursts on the field (FXDirector trails smoke off their chunks).</summary>
        public static readonly List<DebrisBurst> Active = new List<DebrisBurst>(8);

        public float outward = 6f;
        public float upward = 7f;
        public float spin = 5f;
        public float settleTime = 5f;
        public float sinkTime = 3f;

        Rigidbody[] bodies;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        float age, settleAt = -1f;
        bool frozen;

        public float Age => age;
        public bool Frozen => frozen;
        public Rigidbody[] Bodies => bodies;
        /// <summary>How hot the chunks still are, 1 at the blast.</summary>
        public float Heat => Mathf.Clamp01(1f - age / (settleTime * 0.6f));

        static readonly int BurnId = Shader.PropertyToID("_Burn");
        static PhysicsMaterial rubble;

        public void Launch(Vector3 origin)
        {
            bodies = GetComponentsInChildren<Rigidbody>();
            renderers = GetComponentsInChildren<Renderer>();
            // Broken masonry and plate: grips the ground, barely bounces.
            rubble ??= new PhysicsMaterial("Rubble")
            {
                staticFriction = 0.8f, dynamicFriction = 0.6f, bounciness = 0.12f,
                frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum
            };
            foreach (var rb in bodies)
            {
                var col = rb.GetComponent<Collider>();
                if (col != null) col.sharedMaterial = rubble;
                var relay = rb.gameObject.AddComponent<ContactRelay>();
                var b = col != null ? col.bounds : new Bounds(rb.worldCenterOfMass, Vector3.one);
                relay.size = Mathf.Max(b.extents.x, b.extents.z);
                relay.minSpeed = 3f;

                // Up and out from low in the building, the upper chunks furthest: the
                // velocity it would give a unit mass, so a slab and a splinter fly alike.
                Vector3 away = rb.worldCenterOfMass - origin;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-4f) away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                rb.linearVelocity = away.normalized * outward * Random.Range(0.5f, 1.2f) + Vector3.up * upward * Random.Range(0.4f, 1.1f);
                rb.angularVelocity = Random.insideUnitSphere * spin;
            }
            Active.Add(this);
        }

        /// <summary>A blast nearby shoves the chunks still loose, the lighter ones further.</summary>
        public static void Blast(Vector3 at, float radius, float strength)
        {
            foreach (var d in Active)
            {
                if (d == null || d.frozen || d.bodies == null) continue;
                foreach (var rb in d.bodies)
                {
                    if (rb == null || rb.isKinematic) continue;
                    if ((rb.worldCenterOfMass - at).sqrMagnitude > radius * radius) continue;
                    float light = Mathf.Clamp(200f / rb.mass, 0.25f, 1f);
                    rb.AddExplosionForce(strength * light, at, radius, 0.7f, ForceMode.VelocityChange);
                    rb.AddTorque(Random.insideUnitSphere * strength * light * 0.6f, ForceMode.VelocityChange);
                }
                d.settleAt = Mathf.Max(d.settleAt, d.age + 2.5f);   // thrown again: settles again
            }
        }

        void OnDestroy() => Active.Remove(this);

        void Update()
        {
            age += Time.deltaTime;
            if (settleAt < 0f) settleAt = settleTime;
            mpb ??= new MaterialPropertyBlock();
            float burn = Mathf.Lerp(0.95f, 0.4f, Mathf.Clamp01(age / settleTime));
            if (renderers != null)
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(BurnId, burn);
                    r.SetPropertyBlock(mpb);
                }

            if (!frozen && age > settleAt)
            {
                // Settled: stop simulating, then sink into the ground.
                frozen = true;
                if (bodies != null)
                    foreach (var rb in bodies)
                    {
                        rb.interpolation = RigidbodyInterpolation.None;
                        rb.isKinematic = true;
                        var c = rb.GetComponent<Collider>();
                        if (c != null) c.enabled = false;
                    }
            }
            if (frozen) transform.position += Vector3.down * (Time.deltaTime * 1.4f);
            if (age > settleAt + sinkTime) Destroy(gameObject);
        }
    }
}
