// ContactRelay.cs — hands a rigid body's hard landings on the terrain to FXDirector:
// the dust a wreck or a structure's chunk throws up where it comes down (and the
// splash where it comes down in water). PhysX only sends collision messages to the
// body's own GameObject, so UnitWreck and DebrisBurst put one of these on each body.
// Presentation only.
using UnityEngine;

namespace StarForge.View
{
    public sealed class ContactRelay : MonoBehaviour
    {
        /// <summary>Roughly how big the body is (its half-width, metres): the size of its dust.</summary>
        public float size = 1f;
        /// <summary>Slower than this into the ground (m/s) and it throws nothing up.</summary>
        public float minSpeed = 2.5f;
        float quietUntil;

        void OnCollisionEnter(Collision c)
        {
            if (Time.time < quietUntil || !(c.collider is TerrainCollider) || c.contactCount == 0) return;
            var contact = c.GetContact(0);
            float speed = Mathf.Abs(Vector3.Dot(c.relativeVelocity, contact.normal));
            if (speed < minSpeed) return;
            quietUntil = Time.time + 0.3f;   // a bounce is one landing, not a dozen contacts
            FXDirector.Current?.Landed(contact.point, speed, size);
        }
    }
}
