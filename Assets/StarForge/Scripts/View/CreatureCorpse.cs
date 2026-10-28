// CreatureCorpse.cs — a Biolab creature's death: no wreck to throw about, just the
// animal's own death clip played out where it fell (dropping from the air first if it
// flew), a while lying there, then sinking into the ground. Presentation only, like
// UnitWreck: the unit itself is gone from the simulation.
using System.Collections.Generic;
using UnityEngine;
using StarForge.World;

namespace StarForge.View
{
    public sealed class CreatureCorpse : MonoBehaviour
    {
        /// <summary>Corpses lying now; past the cap the oldest sinks early.</summary>
        static readonly List<CreatureCorpse> Active = new List<CreatureCorpse>(32);
        const int Cap = 40;

        Transform body;
        float age, groundY, fallFrom, lieFor, sinkFor = 2f;
        bool falling;

        /// <summary>Takes over <paramref name="body"/> (the creature's model) at its pose and plays
        /// <paramref name="death"/> on <paramref name="anim"/> (if it has one).</summary>
        public static void Create(Unit unit, Transform body, Animation anim, string death)
        {
            if (body == null) return;
            var go = new GameObject("Corpse " + unit.def.displayName);
            go.transform.SetPositionAndRotation(body.position, body.rotation);
            var c = go.AddComponent<CreatureCorpse>();
            c.body = body;
            body.SetParent(go.transform, true);
            c.groundY = unit.World.Map.HeightAt(unit.pos);
            c.fallFrom = body.position.y;
            c.falling = body.position.y - c.groundY > 0.3f;
            c.lieFor = Random.Range(6f, 9f);
            if (anim != null && !string.IsNullOrEmpty(death) && anim.GetClip(death) != null)
            {
                var st = anim[death];
                st.wrapMode = WrapMode.ClampForever;
                st.speed = 1f;
                anim.CrossFade(death, 0.08f);
            }
            Active.Add(c);
            if (Active.Count > Cap) Active[0].lieFor = Mathf.Min(Active[0].lieFor, Active[0].age);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (body == null) { Destroy(gameObject); return; }
            var p = body.position;
            if (falling)
            {
                // Down out of the air, faster as it goes.
                float t = Mathf.Clamp01(age / 0.7f);
                p.y = Mathf.Lerp(fallFrom, groundY, t * t);
                if (t >= 1f) falling = false;
            }
            else if (age > lieFor)
            {
                float t = Mathf.Clamp01((age - lieFor) / sinkFor);
                p.y = groundY - t * 1.2f;
                if (t >= 1f) { Destroy(gameObject); return; }
            }
            body.position = p;
        }

        void OnDestroy() => Active.Remove(this);
    }
}
