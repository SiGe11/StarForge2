// DebrisBurst.cs — a destroyed structure's pre-cut chunks (SF_*_CHUNKS.fbx):
// thrown apart by the blast glowing hot, left to tumble and settle on the
// terrain while they cool to char, then sunk out of sight. Presentation only.
using UnityEngine;

namespace StarForge.View
{
    public sealed class DebrisBurst : MonoBehaviour
    {
        public float outward = 6f;
        public float upward = 7f;
        public float spin = 5f;
        public float settleTime = 5f;
        public float sinkTime = 3f;

        Rigidbody[] bodies;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        float age;
        bool frozen;

        static readonly int BurnId = Shader.PropertyToID("_Burn");

        public void Launch(Vector3 origin)
        {
            bodies = GetComponentsInChildren<Rigidbody>();
            renderers = GetComponentsInChildren<Renderer>();
            foreach (var rb in bodies)
            {
                Vector3 away = rb.worldCenterOfMass - origin;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-4f) away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                rb.linearVelocity = away.normalized * outward * Random.Range(0.5f, 1.2f) + Vector3.up * upward * Random.Range(0.4f, 1.1f);
                rb.angularVelocity = Random.insideUnitSphere * spin;
            }
        }

        void Update()
        {
            age += Time.deltaTime;
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

            if (!frozen && age > settleTime)
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
            if (age > settleTime + sinkTime) Destroy(gameObject);
        }
    }
}
