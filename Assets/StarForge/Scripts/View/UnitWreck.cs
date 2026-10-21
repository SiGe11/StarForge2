// UnitWreck.cs — what is left when a unit dies. UnitView hands its model over and
// the wreck plays the death out with rigid bodies on the terrain (whose collider
// follows the craters), on its own clock. The simulation removed the unit when it
// died, so nothing here can change a match.
//
//   Trooper   knocked off his feet away from the shot: dropped where he stands by a
//             rifle round, thrown by a shell bursting beside him. A ragdoll: his legs
//             are limbs on hip joints that fold, splay and trail as he goes down, and
//             the rifle flies out of his hands.
//   Mauler    the ammunition goes up: the hull is heaved off its tracks and the
//             turret blown clean off -- or left wrenched round on its ring.
//   Digger    heaved up and rolled over onto its side, away from the blast.
//   Skimmer   the lift dies and it drops out of its hover still going: it noses in,
//             slews round and skids to a stop, or goes under in deep water.
//
// Machines burn -- glowing at first, charring as they cool (SF_Unit _Burn/_Damage) --
// and FXDirector gives them flames and smoke while they do. A wreck lies a while,
// then sinks into the ground. Presentation only.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed class UnitWreck : MonoBehaviour
    {
        /// <summary>Wrecks on the field, oldest first (FXDirector burns and smokes them).</summary>
        public static readonly List<UnitWreck> Active = new List<UnitWreck>(32);
        /// <summary>More than this and the oldest start sinking early: a long battle
        /// should not carpet the map with rigid bodies.</summary>
        const int MaxWrecks = 36;

        public UnitType type;
        public float age;

        /// <summary>How hard it is burning, 0..1 (FXDirector's flames and smoke).</summary>
        public float Heat => burnFor <= 0f ? 0f : Mathf.Clamp01(1f - age / burnFor) * Mathf.Clamp01(age * 4f);
        /// <summary>How fast it is sliding along the ground, m/s (a Skimmer's skid throws dust).</summary>
        public float Skid { get; private set; }
        /// <summary>Where the fire sits: the top of the hull, wherever it has ended up.</summary>
        public Vector3 FirePoint => hull != null ? hull.transform.TransformPoint(fireLocal) : transform.position;
        /// <summary>Roughly how big it is (its footprint's half-width), for the size of the fire.</summary>
        public float Size { get; private set; }
        /// <summary>Where it meets the ground.</summary>
        public Vector3 SkidPoint
        {
            get
            {
                if (hullCol == null) return transform.position;
                var b = hullCol.bounds;
                return new Vector3(b.center.x, b.min.y, b.center.z);
            }
        }

        /// <summary>The hull and the piece that came off it (a turret, a rifle), for the checks.</summary>
        public Transform HullTransform => hull != null ? hull.transform : null;
        public Transform PartTransform => part != null ? part.transform : null;
        /// <summary>A rifleman's legs on their hip joints (null for anything else), for the checks.</summary>
        public Rigidbody[] Limbs => limbs;
        /// <summary>The hull's centre of mass, what a blast's reach is measured to (for the checks).</summary>
        public Vector3 Centre => hull != null ? hull.worldCenterOfMass : transform.position;
        public bool Frozen => frozen;

        Rigidbody hull, part;
        Collider hullCol, partCol;
        Rigidbody[] limbs;          // a rifleman's legs, on hip joints
        Collider[] limbCols;
        Rigidbody[] bodies;         // everything simulated: hull, part, limbs
        Collider[] cols;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        Transform legL, legR;
        Quaternion legLFrom, legRFrom, legLTo, legRTo;
        Vector3 fireLocal;
        float lieFor, sinkFor, burnFor, charTo, sinkDepth, water;
        float woke;   // when it last came loose: at death, or thrown again by a blast
        float lieUntil;   // the latest blasts may keep it lying: its own time plus a few seconds
        bool frozen;
        float sunk;
        MapInfo map;

        static readonly int BurnId = Shader.PropertyToID("_Burn");
        static readonly int DamageId = Shader.PropertyToID("_Damage");
        static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        static PhysicsMaterial troopMat, hullMat, slideMat;

        /// <summary>Take over a dead unit's model. <paramref name="velocity"/> is how it was
        /// moving (UnitView tracks it: the agent is gone by now).</summary>
        public static void Create(Unit unit, Transform body, Transform turret, Transform gun,
                                  Transform legL, Transform legR, Vector3 velocity)
        {
            if (body == null) return;
            var go = new GameObject("Wreck " + unit.def.displayName);
            var w = go.AddComponent<UnitWreck>();
            w.Build(unit, body, turret, gun, legL, legR, velocity);
        }

        void Build(Unit unit, Transform body, Transform turret, Transform gun, Transform lL, Transform lR, Vector3 vel)
        {
            type = unit.Type;
            map = unit.World.Map;
            water = map.waterLevel;
            // Unity's null check, not ??=: the statics outlive a play session (no domain
            // reload) while the native materials they point at may not.
            if (troopMat == null) troopMat = Friction("Wreck Trooper", 0.9f, 0.75f, 0.05f);
            if (hullMat == null) hullMat = Friction("Wreck Hull", 0.8f, 0.65f, 0.12f);
            if (slideMat == null) slideMat = Friction("Wreck Slide", 0.32f, 0.2f, 0.1f);

            Vector3 up = Vector3.up;
            Vector3 fwd = unit.transform.forward;
            Vector3 k = new Vector3(unit.killDir.x, 0f, unit.killDir.y);
            if (k.sqrMagnitude < 1e-4f) k = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            k.Normalize();
            float force = Mathf.Clamp01(unit.killForce);

            // The hull: a rigid body at the unit's pose carrying the model; the turret or
            // the rifle, if it comes away, a second one.
            var hullGo = new GameObject("Hull");
            hullGo.transform.SetParent(transform, false);
            hullGo.transform.SetPositionAndRotation(unit.transform.position, unit.transform.rotation);
            body.SetParent(hullGo.transform, true);

            bool turretOff = type == UnitType.Mauler && turret != null && Random.value < 0.6f;
            Transform loose = type == UnitType.Trooper ? gun : turretOff ? turret : null;
            if (loose != null)
            {
                var partGo = new GameObject(loose.name);
                partGo.transform.SetParent(transform, false);
                partGo.transform.SetPositionAndRotation(loose.position, loose.rotation);
                loose.SetParent(partGo.transform, true);
                part = partGo.AddComponent<Rigidbody>();
                partCol = Box(partGo.transform, partGo.transform, 0.9f);
            }
            if (type == UnitType.Mauler && turret != null && !turretOff)
            {
                // Left on its ring, wrenched round and knocked askew, the gun dropped.
                turret.localRotation *= Quaternion.Euler(Random.Range(-7f, 7f), Random.Range(25f, 75f) * (Random.value < 0.5f ? -1f : 1f), Random.Range(-6f, 6f));
                foreach (Transform t in turret.GetComponentsInChildren<Transform>())
                    if (t.name == "Barrel" || t.name == "Barrels") t.localRotation *= Quaternion.Euler(Random.Range(6f, 14f), 0f, 0f);
            }

            hull = hullGo.AddComponent<Rigidbody>();
            var bounds = LocalBounds(hullGo.transform, hullGo.transform);
            Size = Mathf.Max(bounds.extents.x, bounds.extents.z);
            fireLocal = new Vector3(bounds.center.x, bounds.max.y * 0.85f, bounds.center.z);

            // Standing on the ground, not in it: the NavMesh the unit rode is only
            // within a few centimetres of the terrain, and PhysX throws a body it finds
            // buried out of the ground.
            float ground = map.HeightAt(unit.pos);
            float bottom = hullGo.transform.TransformPoint(new Vector3(0f, bounds.min.y, 0f)).y;
            if (bottom < ground + 0.02f) hullGo.transform.position += Vector3.up * (ground + 0.02f - bottom);

            // A rifleman's legs come away from the hull as limbs on hip joints, so they
            // fold, splay and trail as he goes down rather than being swung to a pose;
            // the hull is then his torso, arms and head.
            if (type == UnitType.Trooper && lL != null && lR != null) MakeLegs(lL, lR);
            var torso = limbs != null ? LocalBounds(hullGo.transform, hullGo.transform) : bounds;
            hullCol = Box(hullGo.transform, hullGo.transform, type == UnitType.Trooper ? 0.8f : 0.92f, torso);
            if (partCol != null) Physics.IgnoreCollision(hullCol, partCol);
            // Its weight a little below the middle of the box: machines are heaviest
            // low down, and a man about the hips.
            hull.centerOfMass = torso.center - Vector3.up * torso.extents.y * (type == UnitType.Trooper ? 0.1f : 0.25f);

            var all = new List<Rigidbody> { hull };
            var allCols = new List<Collider> { hullCol };
            if (part != null) { all.Add(part); allCols.Add(partCol); }
            if (limbs != null) { all.AddRange(limbs); allCols.AddRange(limbCols); }
            bodies = all.ToArray();
            cols = allCols.ToArray();
            foreach (var rb in bodies)
            {
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.maxDepenetrationVelocity = 1.5f;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.35f;
                // Jointed limbs need more solver passes than PhysX's default six, or the
                // hips stretch and the legs jitter against the ground.
                if (limbs != null) { rb.solverIterations = 12; rb.solverVelocityIterations = 4; }
            }

            Vector3 side = Vector3.Cross(up, fwd);
            switch (type)
            {
                case UnitType.Trooper:
                {
                    hull.mass = limbs != null ? 64f : 90f;   // the legs carry the rest
                    hull.angularDamping = 0.4f;
                    hullCol.sharedMaterial = troopMat;
                    // Mostly he goes down the way the hit pushed him; now and then he
                    // crumples forward instead, folding at the hips.
                    bool crumple = force < 0.5f && Random.value < 0.3f;
                    Vector3 fall = crumple ? -k : k;
                    fall = Quaternion.Euler(0f, Random.Range(-28f, 28f), 0f) * fall;
                    // Toppling about his feet, not spinning about his middle: the hips
                    // move off with the turn (a spin about the middle drove the feet into
                    // the ground, which stopped the fall at 25 degrees and let him down
                    // slowly from there).
                    float spin = (crumple ? 1.6f : 2.2f) + 2.4f * force;
                    float hips = hull.centerOfMass.y;
                    hull.linearVelocity = vel * 0.6f + k * (crumple ? 0.3f : 0.3f + 2.6f * force) + fall * spin * hips
                                        + up * (0.1f + 1.7f * force);
                    hull.angularVelocity = Vector3.Cross(up, fall) * spin + up * Random.Range(-1.5f, 1.5f) * (0.3f + force);
                    if (limbs != null) Hips();
                    // Without limbs (a model with no legs to take), the legs are swung to
                    // a pose: a little at the hip going over backwards (at 20-55 degrees
                    // they stood up in the air once he was down), trailing straight going
                    // over on his front, and splayed.
                    bool backwards = Vector3.Dot(fall, fwd) < 0f;
                    if (limbs == null) { legL = lL; legR = lR; }
                    if (legL != null && legR != null)
                    {
                        legLFrom = legL.localRotation; legRFrom = legR.localRotation;
                        float flexL = backwards ? -Random.Range(4f, 22f) : Random.Range(5f, 20f);
                        float flexR = backwards ? -Random.Range(0f, 16f) : Random.Range(0f, 16f);
                        legLTo = legLFrom * Quaternion.Euler(flexL, 0f, -Random.Range(4f, 14f));
                        legRTo = legRFrom * Quaternion.Euler(flexR, 0f, Random.Range(4f, 14f));
                    }
                    if (part != null)
                    {
                        part.mass = 6f;
                        part.angularDamping = 0.3f;
                        partCol.sharedMaterial = hullMat;
                        part.linearVelocity = vel * 0.5f + k * Random.Range(1.0f, 1.8f + 1.8f * force)
                                            + up * Random.Range(1.8f, 3.2f) + Random.insideUnitSphere * 0.8f;
                        part.angularVelocity = Random.insideUnitSphere * 9f;
                    }
                    charTo = 0.1f + 0.45f * force;   // a shell scorches him; a rifle round does not
                    lieFor = Random.Range(7f, 9f);
                    sinkFor = 2.5f;
                    break;
                }
                case UnitType.Mauler:
                {
                    hull.mass = 20000f;
                    hullCol.sharedMaterial = hullMat;
                    float heave = turretOff ? Random.Range(1.3f, 2.1f) : Random.Range(2.0f, 2.9f);
                    var tip = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                    hull.linearVelocity = vel * 0.4f + up * heave + k * 0.6f;
                    hull.angularVelocity = Vector3.Cross(up, tip) * Random.Range(0.4f, 1.0f) + Random.insideUnitSphere * 0.25f;
                    if (part != null)
                    {
                        part.mass = 4000f;
                        part.angularDamping = 0.2f;
                        partCol.sharedMaterial = hullMat;
                        var away = Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * k;
                        part.linearVelocity = vel * 0.4f + up * Random.Range(7f, 10f) + away * Random.Range(1.4f, 3.2f);
                        part.angularVelocity = Random.insideUnitSphere * Random.Range(3f, 6f);
                    }
                    charTo = 0.9f;
                    burnFor = Random.Range(11f, 15f);
                    lieFor = burnFor + 3f;
                    sinkFor = 3.5f;
                    break;
                }
                case UnitType.Worker:
                {
                    hull.mass = 4000f;
                    hullCol.sharedMaterial = hullMat;
                    // Over onto the side away from the blast.
                    Vector3 roll = Vector3.Dot(k, side) >= 0f ? side : -side;
                    hull.linearVelocity = vel * 0.5f + up * Random.Range(2.4f, 3.4f) + k * 1.0f;
                    hull.angularVelocity = Vector3.Cross(up, roll) * Random.Range(2.2f, 3.0f) + Random.insideUnitSphere * 0.4f;
                    charTo = 0.85f;
                    burnFor = Random.Range(8f, 11f);
                    lieFor = burnFor + 3f;
                    sinkFor = 3f;
                    break;
                }
                default:   // Skimmer
                {
                    hull.mass = 1500f;
                    hull.angularDamping = 1.2f;
                    hullCol.sharedMaterial = slideMat;
                    // Drops flat on its belly still going, dipping its nose a little,
                    // and slews round as it slides. (Pitched harder, it dug its nose in
                    // at 11 m/s and cartwheeled onto its back.)
                    Vector3 heading = vel.sqrMagnitude > 0.5f ? new Vector3(vel.x, 0f, vel.z).normalized : fwd;
                    hull.linearVelocity = new Vector3(vel.x, 0f, vel.z) + k * 1.2f - up * 0.5f;
                    hull.angularVelocity = Vector3.Cross(up, heading) * Random.Range(0.15f, 0.4f) + up * Random.Range(-1.8f, 1.8f);
                    charTo = 0.85f;
                    burnFor = Random.Range(7f, 10f);
                    lieFor = burnFor + 3f;
                    sinkFor = 3f;
                    break;
                }
            }
            sinkDepth = bounds.size.y + 0.4f;
            lieUntil = lieFor + 6f;
            // Hard landings throw up dust (FXDirector.Landed).
            var relay = hullGo.AddComponent<ContactRelay>();
            relay.size = Size;
            if (part != null) part.gameObject.AddComponent<ContactRelay>().size = 0.4f;
            if (limbs != null) foreach (var rb in limbs) rb.gameObject.AddComponent<ContactRelay>().size = 0.25f;
            renderers = GetComponentsInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            Surface();

            Active.Add(this);
            // Too many: the oldest go early.
            for (int i = 0; Active.Count - i > MaxWrecks; i++) Active[i].SinkNow();
        }

        /// <summary>A rifleman's legs as rigid bodies of their own, at the pose he died in
        /// (each leg's pivot is its hip: `LegL`/`LegR` in the model).</summary>
        void MakeLegs(Transform lL, Transform lR)
        {
            var legs = new[] { lL, lR };
            limbs = new Rigidbody[2];
            limbCols = new Collider[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(legs[i].name);
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(legs[i].position, legs[i].rotation);
                legs[i].SetParent(go.transform, true);
                limbs[i] = go.AddComponent<Rigidbody>();
                limbCols[i] = Box(go.transform, go.transform, 0.85f);
            }
        }

        /// <summary>Joins the legs to the torso at the hips and starts them moving with it.
        /// Twist is about the hip's side-to-side axis (the torso's right, given in the
        /// leg's own frame: an imported child may carry a rotation). In Unity's joint
        /// convention positive twist about right folds the leg forward, toward the chest
        /// (the Ragdoll Wizard's knees are -80..0, hips -20..70), so -25..+95 is a hip's
        /// kick back and fold; swing1 splays it out to the side, swing2 turns it on its
        /// own axis.</summary>
        void Hips()
        {
            Physics.IgnoreCollision(limbCols[0], limbCols[1]);
            for (int i = 0; i < 2; i++)
            {
                var rb = limbs[i];
                var col = (BoxCollider)limbCols[i];
                Physics.IgnoreCollision(hullCol, col);
                if (partCol != null) Physics.IgnoreCollision(partCol, col);
                col.sharedMaterial = troopMat;
                rb.mass = 13f;
                rb.angularDamping = 0.6f;
                var j = rb.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = hull;
                j.anchor = Vector3.zero;
                j.axis = rb.transform.InverseTransformDirection(hull.transform.right);
                j.swingAxis = rb.transform.InverseTransformDirection(hull.transform.forward);
                j.lowTwistLimit = new SoftJointLimit { limit = -25f };
                j.highTwistLimit = new SoftJointLimit { limit = 95f };
                j.swing1Limit = new SoftJointLimit { limit = 35f };
                j.swing2Limit = new SoftJointLimit { limit = 12f };
                j.enableProjection = true;
                j.enablePreprocessing = false;
                // Moving as part of him at the moment he was hit: the hull's motion at the
                // leg's middle.
                rb.linearVelocity = hull.GetPointVelocity(rb.transform.TransformPoint(col.center));
                rb.angularVelocity = hull.angularVelocity;
            }
        }

        void SinkNow() => lieFor = Mathf.Min(lieFor, age);

        /// <summary>A blast nearby (FXDirector.Explosion): every wreck still lying within
        /// <paramref name="radius"/> is shoved away from it, a settled one woken first. The
        /// shove is a change of speed scaled by what the thing weighs in the fight's terms,
        /// so a rifleman's body is thrown and a tank's hull only rocks on its tracks.</summary>
        public static void Blast(Vector3 at, float radius, float strength)
        {
            foreach (var w in Active)
                if (w != null) w.Shove(at, radius, strength);
        }

        void Shove(Vector3 at, float radius, float strength)
        {
            if (hull == null || age >= lieFor) return;   // already going under
            // Too near its end to be thrown and come to rest before it must sink: left
            // alone, or a fight going on beside it would keep it awake for ever.
            if (age + 4f > lieUntil) return;
            if ((hull.worldCenterOfMass - at).sqrMagnitude > radius * radius) return;
            if (frozen) Wake();
            float heft = type == UnitType.Trooper ? 1f : type == UnitType.Skimmer ? 0.45f : type == UnitType.Worker ? 0.3f : 0.12f;
            hull.AddExplosionForce(strength * heft, at, radius, 0.6f, ForceMode.VelocityChange);
            hull.AddTorque(Random.insideUnitSphere * strength * heft * 0.5f, ForceMode.VelocityChange);
            if (part != null && !part.isKinematic)
            {
                part.AddExplosionForce(strength * Mathf.Min(1f, heft * 2f), at, radius, 0.8f, ForceMode.VelocityChange);
                part.AddTorque(Random.insideUnitSphere * strength, ForceMode.VelocityChange);
            }
            // Each limb takes the blast where it is: the body goes as one, legs flailing.
            if (limbs != null)
                foreach (var rb in limbs)
                    rb.AddExplosionForce(strength * heft, at, radius, 0.6f, ForceMode.VelocityChange);
            // Thrown, it gets its settling time again: neither the freeze timer nor the
            // sinking may catch it in the air.
            woke = age;
            lieFor = Mathf.Max(lieFor, age + 3f);
        }

        void Wake()
        {
            frozen = false;
            woke = age;
            lieFor = Mathf.Max(lieFor, age + 4f);
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            foreach (var c in cols) if (c != null) c.enabled = true;
        }

        void OnDestroy() => Active.Remove(this);

        void FixedUpdate()
        {
            if (frozen || hull == null) return;
            // In deep water it goes under, slowly and stern or nose first, not like a stone.
            float depth = water - hull.worldCenterOfMass.y;
            float drag = depth > 0f ? 2.5f : (type == UnitType.Trooper ? 0.1f : 0.05f);
            foreach (var rb in bodies)
            {
                if (rb == part) continue;
                rb.linearDamping = drag;
                if (depth > 0f) rb.angularDamping = 2f;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;

            if (legL != null && legR != null)
            {
                float f = 1f - (1f - Mathf.Clamp01(age / 0.45f)) * (1f - Mathf.Clamp01(age / 0.45f));
                legL.localRotation = Quaternion.Slerp(legLFrom, legLTo, f);
                legR.localRotation = Quaternion.Slerp(legRFrom, legRTo, f);
            }

            if (!frozen)
            {
                // Sliding: moving along with its lowest point on the ground.
                var sp = SkidPoint;
                bool touching = sp.y < map.HeightAt(new Vector2(sp.x, sp.z)) + 0.15f;
                Vector3 v = hull != null ? hull.linearVelocity : Vector3.zero;
                Skid = touching ? new Vector2(v.x, v.z).magnitude : 0f;
                bool still = true;
                foreach (var rb in bodies)
                    if (rb != null && !rb.IsSleeping() && (rb.linearVelocity.sqrMagnitude >= 0.01f || rb.angularVelocity.sqrMagnitude >= 0.01f))
                        still = false;
                if ((still && age - woke > 2.5f) || age - woke > 7f) Freeze();
            }
            Surface();

            if (age > lieFor)
            {
                if (!frozen) Freeze();
                float step = sinkDepth / sinkFor * dt;
                sunk += step;
                transform.position += Vector3.down * step;
                if (sunk >= sinkDepth) Destroy(gameObject);
            }
        }

        void Freeze()
        {
            frozen = true;
            Skid = 0f;
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.isKinematic = true;
            }
            foreach (var c in cols) if (c != null) c.enabled = false;
        }

        /// <summary>Charred, glowing in the cracks and dully all over at first and cooling;
        /// a Trooper only scorched. (At the debris' full _Burn a whole hull glowed like
        /// a block of lava, and bloom made it a yellow lump.)</summary>
        void Surface()
        {
            float burn = burnFor > 0f ? 0.32f * (1f - Mathf.Clamp01(age / (burnFor * 0.5f))) : 0f;
            // The glowing cracks go out as it cools (they open at char above ~0.5).
            float damage = charTo * Mathf.Clamp01(age / 0.5f) * (burnFor > 0f ? Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(age / (burnFor * 0.6f))) : 1f);
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

        static PhysicsMaterial Friction(string name, float stat, float dyn, float bounce) => new PhysicsMaterial(name)
        {
            staticFriction = stat, dynamicFriction = dyn, bounciness = bounce,
            frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum
        };

        static BoxCollider Box(Transform on, Transform under, float shrink, Bounds? given = null)
        {
            var b = given ?? LocalBounds(on, under);
            var box = on.gameObject.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = new Vector3(Mathf.Max(0.1f, b.size.x * shrink), Mathf.Max(0.1f, b.size.y), Mathf.Max(0.1f, b.size.z * shrink));
            return box;
        }

        /// <summary>The meshes under <paramref name="under"/>, boxed in <paramref name="space"/>'s
        /// frame. Mesh bounds are there even when the mesh data is not readable.</summary>
        static Bounds LocalBounds(Transform space, Transform under)
        {
            bool any = false;
            var b = new Bounds(Vector3.up * 0.5f, Vector3.one * 0.5f);
            foreach (var mf in under.GetComponentsInChildren<MeshFilter>())
            {
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
