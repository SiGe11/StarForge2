// IroncladLegs.cs — the Ironclad's walk, MechView's way sized for a leg 2.5x shorter: each foot
// stays planted where it landed until the hip has carried the body past it, then steps (lifted
// in an arc to where the body will be when it lands) while the other holds; thigh > shin > foot,
// rigid plates, reach the planted foot by two-bone IK with the knee forward, the foot laid on the
// ground's slope and held at the heading it landed with (a planted foot never spins on a turn).
// The body crouches just enough to keep a planted foot in reach (slopes, 8x); a swing hurries to
// land before the foot holding the body trails too far (a start from standstill, a steep climb),
// and a foot that would still be out of reach lifts early, even while the other is up: a clamped
// solve moves the drawn foot off its plant, which is the slide this replaces. Presentation only:
// reads the unit's transform and the ground, writes nothing but the leg transforms.
using UnityEngine;
using StarForge.World;

namespace StarForge.View
{
    public sealed class IroncladLegs
    {
        public sealed class Leg
        {
            public Transform thigh, shin, foot;
            public Vector3 hip, knee, ankle;               // rest, legs-root local
            public float l1, l2, reach;
            public Vector3 upperDir, lowerDir, hinge;      // rest, legs-root local
            public Quaternion thighRest, shinRest, footRest;
            public Vector3 planted, from, to;              // world, on the ground under the ankle
            public float plantedYaw, fromYaw;
            public bool swinging;
            public float s, sinceLand = 9f;
            /// <summary>How far the last solve fell short of its target (metres, 0 = reached).</summary>
            public float miss;
        }

        // Sized for this leg (hip 2.05, thigh + shin 2.04, hip-to-ankle 0.84 of that: about a
        // metre of reach fore and aft). A step lands half a stride ahead of the hip and lifts
        // half a stride behind it: 1.6 m a stride keeps both inside the reach at the Ironclad's
        // 3.8 m/s. MechView's numbers are for a leg 2.5x longer.
        const float Stride = 1.6f, StepMin = 0.36f, StepMax = 0.6f, Lift = 0.36f;
        const float TriggerBase = 0.35f, TriggerPerSpeed = 0.12f, StandStep = 0.2f;
        // A trailing foot lifts before the body would crouch past CrouchMax for it; a foot ahead
        // of the hip (one coming down a bank or a 40-degree descent) may take the body down to
        // CrouchDeep, and keeps that limit once down, or it was lifted again the next frame.
        const float Dip = 0.08f, SwayAmp = 0.04f, ReachUse = 0.985f, CrouchMax = 0.5f, CrouchDeep = 0.85f;
        // A swing lands before the foot holding the body is BackMax behind the hip, or soon when
        // that foot trails and needs NeedHurry of crouch: started slow, the first step from
        // standstill took 0.6 s while the body reached 3.8 m/s, and the body squatted 0.36 m on the
        // foot left 1.4 m behind. A hurried swing takes at least MinSwing for a whole swing (a
        // turning one may be as short as TurnStepMin): hurried to land at once, a step on a 30-40
        // degree descent (the foot ahead needs crouch by design) or a turn in place landed in the
        // frame it lifted, the foot thrown up to 2 m, and at 0.15 a hurried foot whipped 0.54 m a
        // frame at 1x (the 2D port Art/Source/staged/ironclad/port.py, the review's turn3d.py).
        // Only in a frame over LongFrame does a swing still land at once, when the planted foot
        // would otherwise leave its reach or pass TwistMax by the next frame.
        const float BackMax = 0.9f, NeedHurry = 0.3f, MinSwing = 0.25f;
        // 20 fps of game time: 8x, a hitch. At 1/30 it fired at the player's own 33-37 fps lows and
        // slammed 1x landings again (the fourth review's model); 8x frames are 150-200 ms either way.
        const float LongFrame = 0.05f;
        // A planted foot keeps its heading; past TwistStep from the body's it steps, past
        // TwistMax it lifts whatever the other does (the shin leaves the ankle's drum past ~16).
        // Turning, a swing is short enough to land before the other foot passes TwistMax
        // (0.8 of the time the turn takes from TwistStep to TwistMax, at least TurnStepMin).
        const float TwistStep = 25f, TwistMax = 50f, TurnStepMin = 0.2f;

        public readonly Leg[] legs;
        readonly Transform root, frame;
        readonly MapInfo map;
        int lastFrame = -10;
        float bob, sway, crouch, pitch, lastYaw, yawRate;

        /// <summary>The body's height above the unit's transform this frame (the ground's,
        /// the walk's dip, the crouch), its sideways sway and its lean (degrees).</summary>
        public float BodyY { get; private set; }
        public float Sway => sway;
        public float Pitch => pitch;
        public float Roll => -sway * 30f;
        /// <summary>Feet that came down, and feet re-planted outright (first frame, back in sight, a jump).</summary>
        public int Steps, Replants;
        /// <summary>Lifts by what set them off: a twisted foot, a foot out of reach, the stride (for checks).</summary>
        public int LiftsTwist, LiftsReach, LiftsStride;
        /// <summary>Time in Step and Pose over every Ironclad, and Pose calls, counted only while
        /// <see cref="Profile"/> is on (a check sets it; reset at every play entry).</summary>
        public static bool Profile;
        public static double CostMs;
        public static int CostCalls;

        /// <summary>For the fps A/B: <c>-sfnoironcladik</c> on the player's command line leaves the
        /// legs standing at rest and the body on the NavMesh, as other units ride it.</summary>
        static readonly bool Off = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sfnoironcladik") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Profile = false; CostMs = 0.0; CostCalls = 0; }
        /// <summary>The last frame's crouch (metres), for checks.</summary>
        public float Crouch => crouch;

        IroncladLegs(Transform root, Transform frame, MapInfo map, Leg[] legs)
        {
            this.root = root; this.frame = frame; this.map = map; this.legs = legs;
        }

        /// <summary>The legs of a model with ThighX, ShinX and FootX (X = R, L) side by side
        /// under its root (nested groups do not survive the FBX export): chained here, each
        /// keeping where it stands. Null if the model has no such legs.</summary>
        public static IroncladLegs Rig(Transform frame, Transform body, MapInfo map)
        {
            if (Off || body == null || map == null) return null;
            Transform thR = null, shR = null, ftR = null, thL = null, shL = null, ftL = null;
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "ThighR": thR = t; break;
                    case "ShinR": shR = t; break;
                    case "FootR": ftR = t; break;
                    case "ThighL": thL = t; break;
                    case "ShinL": shL = t; break;
                    case "FootL": ftL = t; break;
                }
            }
            var legs = new Leg[2];
            Transform root = null;
            for (int i = 0; i < 2; i++)
            {
                Transform th = i == 0 ? thR : thL, sh = i == 0 ? shR : shL, ft = i == 0 ? ftR : ftL;
                if (th == null || sh == null || ft == null) return null;
                root = th.parent;
                // The joints at rest are the pivots, read in the root's frame before chaining.
                var leg = new Leg
                {
                    thigh = th, shin = sh, foot = ft,
                    hip = root.InverseTransformPoint(th.position),
                    knee = root.InverseTransformPoint(sh.position),
                    ankle = root.InverseTransformPoint(ft.position),
                };
                sh.SetParent(th, true);
                ft.SetParent(sh, true);
                leg.thighRest = th.localRotation;
                leg.shinRest = Quaternion.Inverse(root.rotation) * sh.rotation;
                leg.footRest = Quaternion.Inverse(root.rotation) * ft.rotation;
                leg.l1 = (leg.knee - leg.hip).magnitude;
                leg.l2 = (leg.ankle - leg.knee).magnitude;
                leg.reach = leg.l1 + leg.l2 - 0.02f;
                leg.upperDir = (leg.knee - leg.hip).normalized;
                leg.lowerDir = (leg.ankle - leg.knee).normalized;
                leg.hinge = Vector3.Cross(leg.ankle - leg.hip, leg.knee - leg.hip).normalized;
                legs[i] = leg;
            }
            return new IroncladLegs(root, frame, map, legs);
        }

        float GroundAt(Vector3 p) => map.HeightAt(new Vector2(p.x, p.z));

        /// <summary>Where a foot rests under its hip now, on the ground, led by the way the body goes.</summary>
        Vector3 RestFoot(Leg leg, Vector3 lead)
        {
            Vector3 w = frame.TransformPoint(new Vector3(leg.ankle.x, 0f, leg.ankle.z)) + lead;
            w.y = GroundAt(w);
            return w;
        }

        void Replant()
        {
            float yaw = frame.eulerAngles.y;
            foreach (var leg in legs)
            {
                leg.planted = RestFoot(leg, Vector3.zero);
                leg.plantedYaw = yaw;
                leg.swinging = false;
                leg.sinceLand = 9f;
            }
            // Stood up afresh: last seen crouched in the fog, it rose for a second once seen again.
            crouch = bob = sway = pitch = yawRate = 0f;
            lastYaw = yaw;
            Replants++;
        }

        /// <summary>Decide the steps and where the body rides, before UnitView places the body.</summary>
        public void Step(float dt, Vector3 velocity)
        {
            long t0 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            if (Time.frameCount - lastFrame > 1) Replant();   // first frame, or back in sight after the fog
            lastFrame = Time.frameCount;
            Vector3 v = velocity;
            v.y = 0f;
            float speed = v.magnitude;
            float yaw = frame.eulerAngles.y;
            if (dt > 0f) yawRate = Mathf.Lerp(yawRate, Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / dt, 0.5f);
            lastYaw = yaw;
            float stepTime = Mathf.Clamp(Stride / Mathf.Max(0.5f, speed), StepMin, StepMax);
            // Turning swings faster, but the stride (where a foot lands) stays the walk's: shortening
            // both pulled a swinging foot's target back 0.4 m in a frame as a turn began.
            float swingTime = yawRate > 1f ? Mathf.Min(stepTime, Mathf.Max(TurnStepMin, 0.8f * (TwistMax - TwistStep) / yawRate)) : stepTime;
            float trigger = TriggerBase + TriggerPerSpeed * speed;
            Vector3 lead = v * stepTime * 0.5f;

            // Where the hips will be this frame, before any crouch (UnitView places the body after this).
            float groundHere = GroundAt(frame.position);
            float feet = 0f;
            foreach (var leg in legs) feet += leg.swinging ? Mathf.Lerp(leg.from.y, leg.to.y, leg.s) : leg.planted.y;
            feet /= legs.Length;
            float dip = 0f, lean = 0f;
            foreach (var leg in legs)
            {
                dip += Mathf.Exp(-leg.sinceLand * 7f) * Mathf.Clamp01(speed / 2f + 0.2f);
                if (!leg.swinging) lean += leg == legs[0] ? 0.5f : -0.5f;
            }
            bob = Mathf.Lerp(bob, -dip * Dip, 1f - Mathf.Exp(-dt * 14f));
            sway = Mathf.Lerp(sway, lean * SwayAmp * Mathf.Clamp01(speed / 2f), 1f - Mathf.Exp(-dt * 5f));
            Vector3 lv = frame.InverseTransformDirection(v);
            pitch = Mathf.Lerp(pitch, Mathf.Clamp(lv.z * 0.7f, -3f, 3.5f), 1f - Mathf.Exp(-dt * 6f));
            float baseY = Mathf.Lerp(groundHere, feet, 0.5f) + bob;
            Quaternion bodyRot = frame.rotation * Quaternion.Euler(pitch, 0f, Roll);
            Vector3 bodyPos = frame.TransformPoint(new Vector3(sway, 0f, 0f));
            bodyPos.y = baseY;
            // Each hip this frame (where the legs are posed from); legs[0] and legs[1] only.
            Vector3 hip0 = bodyPos + bodyRot * legs[0].hip, hip1 = bodyPos + bodyRot * legs[1].hip;
            Vector2 heading = speed > 1e-3f ? Flat(v) / speed : Vector2.zero;

            // A step: a planted foot the body has left behind lifts when the other is down; one
            // that would be out of reach lifts whatever the other is doing. The crouch keeps a
            // planted foot that is still in reach horizontally reachable on a slope.
            int airborne = -1;
            for (int i = 0; i < legs.Length; i++) if (legs[i].swinging) { airborne = i; break; }
            float needCrouch = 0f;
            for (int i = 0; i < legs.Length; i++)
            {
                var leg = legs[i];
                leg.sinceLand += dt;
                // The hip this frame, and a frame on (a trailing foot lifts before it would be out
                // of reach then: 0.5 m at 8x).
                Vector3 hip = i == 0 ? hip0 : hip1, hipNext = hip + v * dt;
                float r = leg.reach * ReachUse;
                Vector3 want = RestFoot(leg, lead);
                // Never put a foot down beyond the reach (a fast turn, a long frame at 8x).
                Vector2 toWant = Flat(want) - Flat(hip);
                if (toWant.magnitude > r * 0.75f)
                {
                    toWant = toWant.normalized * (r * 0.75f);
                    want = new Vector3(hip.x + toWant.x, 0f, hip.z + toWant.y);
                    want.y = GroundAt(want);
                }
                if (!leg.swinging)
                {
                    leg.planted.y = GroundAt(leg.planted);          // a crater under a planted foot
                    // Only a teleport takes a foot this far from where it should be.
                    if ((leg.planted - want).sqrMagnitude > 25f) { leg.planted = RestFoot(leg, Vector3.zero); leg.plantedYaw = yaw; Replants++; }
                    // The crouch that brings it in reach; past the most the body will crouch, it lifts.
                    float need = Need(leg, hip, r, leg.planted, leg.plantedYaw);
                    bool trailing = Vector2.Dot(Flat(leg.planted) - Flat(hip), heading) < 0f;
                    float limit = trailing ? CrouchMax : CrouchDeep;
                    float twist = Mathf.Abs(Mathf.DeltaAngle(leg.plantedYaw, yaw));
                    bool outOfReach = need > limit || Need(leg, hipNext, r, leg.planted, leg.plantedYaw) > limit || twist > TwistMax;
                    if (!outOfReach) needCrouch = Mathf.Max(needCrouch, need);
                    float off = (Flat(leg.planted) - Flat(want)).magnitude + speed * dt;
                    bool tooFar = off > trigger || twist > TwistStep || (speed < 0.3f && off > StandStep && leg.sinceLand > 0.4f);
                    bool free = airborne < 0 || airborne == i;
                    if (outOfReach || (tooFar && free && leg.sinceLand > stepTime * 0.3f))
                    {
                        if (twist > TwistStep) LiftsTwist++; else if (outOfReach) LiftsReach++; else LiftsStride++;
                        leg.swinging = true;
                        leg.s = 0f;
                        leg.from = leg.planted;
                        leg.fromYaw = leg.plantedYaw;
                        if (airborne < 0) airborne = i;
                    }
                }
                if (leg.swinging)
                {
                    float rate = 1f / swingTime;
                    var other = legs[1 - i];
                    if (!other.swinging && dt > 0f)
                    {
                        // Land before the other foot trails BackMax behind its hip, or soon if it trails
                        // stretched already (a foot ahead of the hip needs crouch on a descent by design).
                        Vector3 oHip = i == 0 ? hip1 : hip0;
                        float ahead = Vector2.Dot(Flat(other.planted) - Flat(oHip), heading);
                        float due = speed > 0.3f ? (BackMax + ahead) / speed : float.MaxValue;
                        if (ahead < 0f && Need(other, oHip, r, other.planted, other.plantedYaw) > NeedHurry) due = 0f;
                        float cap = 1f / MinSwing;
                        // In a long frame the planted foot would leave its reach before the capped swing
                        // lands, and lift with this one still up: both feet up 6 samples at 8x in round 3,
                        // 0 after (the third review's model: 108 to 0). The reach half acts only while
                        // legs[0] swings: a planted legs[0] lifts first by its own reach rule.
                        if (dt > LongFrame)
                        {
                            float oLimit = ahead < 0f ? CrouchMax : CrouchDeep;
                            if (Need(other, oHip + v * dt, r, other.planted, other.plantedYaw) > oLimit
                                || Mathf.Abs(Mathf.DeltaAngle(other.plantedYaw, yaw)) + yawRate * dt > TwistMax) { due = 0f; cap = float.MaxValue; }
                        }
                        if (due < (1f - leg.s) * swingTime)
                            rate = Mathf.Max(rate, Mathf.Min((1f - leg.s) / Mathf.Max(due, dt) + 1e-3f, cap));
                    }
                    leg.s += dt * rate;
                    // Never aim a foot where the body cannot crouch to reach it (a drop under the step):
                    // landed anyway, it was drawn up to 1.7 m short for a frame (8x, a 31-degree slope).
                    float need = Need(leg, hip, r, want, yaw);
                    if (need > CrouchDeep)
                    {
                        Vector3 h0 = new Vector3(hip.x, 0f, hip.z), w0 = want;
                        float lo = 0f, hi = 1f;
                        for (int it = 0; it < 6; it++)
                        {
                            float m = (lo + hi) * 0.5f;
                            Vector3 q = Vector3.Lerp(h0, w0, m);
                            q.y = GroundAt(q);
                            if (Need(leg, hip, r, q, yaw) > CrouchDeep) hi = m; else lo = m;
                        }
                        want = Vector3.Lerp(h0, w0, lo);
                        want.y = GroundAt(want);
                        need = Need(leg, hip, r, want, yaw);
                    }
                    leg.to = want;
                    // Crouch ahead of the landing, so the foot comes down where it is drawn: crouched
                    // only once it was down, a foot landing downhill was drawn short and then dropped.
                    float k = Mathf.Clamp01(leg.s);
                    if (need <= CrouchDeep) needCrouch = Mathf.Max(needCrouch, need * k * k * (3f - 2f * k));
                    if (leg.s >= 1f)
                    {
                        leg.swinging = false;
                        leg.planted = want;
                        leg.plantedYaw = yaw;
                        leg.sinceLand = 0f;
                        Steps++;
                    }
                }
            }
            needCrouch = Mathf.Clamp(needCrouch, 0f, CrouchDeep);
            crouch = needCrouch > crouch ? needCrouch : Mathf.MoveTowards(crouch, needCrouch, dt * 0.6f);
            BodyY = baseY - crouch - frame.position.y;
            if (Profile) CostMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>Bring each leg to its foot, after the body has been placed.</summary>
        public void Pose()
        {
            long t0 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            float yaw = frame.eulerAngles.y;
            foreach (var leg in legs)
            {
                Vector3 ground;
                float footYaw, toe = 0f;
                if (leg.swinging)
                {
                    float k = leg.s * leg.s * (3f - 2f * leg.s);
                    ground = Vector3.Lerp(leg.from, leg.to, k);
                    ground.y = Mathf.Max(ground.y, GroundAt(ground)) + Mathf.Sin(leg.s * Mathf.PI) * Lift;
                    footYaw = Mathf.LerpAngle(leg.fromYaw, yaw, k);
                    toe = -Mathf.Sin(leg.s * Mathf.PI) * 15f;
                }
                else { ground = leg.planted; footYaw = leg.plantedYaw; }
                // Flat on the slope under it, held at its own heading; the ankle above the sole.
                Quaternion footRot = FootRot(ground, footYaw, toe);
                Vector3 ankleWorld = ground + footRot * (Vector3.up * leg.ankle.y);
                Solve(leg, root.InverseTransformPoint(ankleWorld));
                leg.foot.rotation = footRot * leg.footRest;
            }
            if (!Profile) return;
            CostMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            CostCalls++;
        }

        /// <summary>Two-bone IK in the legs root's frame, the knee forward, each bone turned from
        /// its rest by a frame (bone, hinge) so the hinge stays across the leg and no bone twists.</summary>
        static void Solve(Leg leg, Vector3 target)
        {
            Vector3 v = target - leg.hip;
            float dist = v.magnitude;
            float d = Mathf.Clamp(dist, Mathf.Abs(leg.l1 - leg.l2) + 0.05f, leg.reach);
            leg.miss = Mathf.Max(0f, dist - leg.reach);
            Vector3 u = dist > 1e-5f ? v / dist : Vector3.down;
            Vector3 w = Vector3.forward - u * Vector3.Dot(Vector3.forward, u);
            w = w.sqrMagnitude > 1e-6f ? w.normalized : Vector3.up;
            float x = (leg.l1 * leg.l1 - leg.l2 * leg.l2 + d * d) / (2f * d);
            float y = Mathf.Sqrt(Mathf.Max(0f, leg.l1 * leg.l1 - x * x));
            Vector3 knee = leg.hip + u * x + w * y;
            Vector3 upper = (knee - leg.hip).normalized;
            Vector3 lower = (leg.hip + u * d - knee).normalized;
            Vector3 hinge = Vector3.Cross(u, w).normalized;
            Quaternion up = Quaternion.LookRotation(upper, hinge) * Quaternion.Inverse(Quaternion.LookRotation(leg.upperDir, leg.hinge));
            Quaternion lo = Quaternion.LookRotation(lower, hinge) * Quaternion.Inverse(Quaternion.LookRotation(leg.lowerDir, leg.hinge));
            leg.thigh.localRotation = up * leg.thighRest;
            leg.shin.localRotation = Quaternion.Inverse(up * leg.thighRest) * lo * leg.shinRest;
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        /// <summary>How far the body must come down for a foot on <paramref name="ground"/> to be
        /// in reach of the hip (negative: in reach as it is; MaxValue: too far out to reach).</summary>
        float Need(Leg leg, Vector3 hip, float r, Vector3 ground, float yaw)
        {
            Vector3 ankle = ground + FootRot(ground, yaw, 0f) * (Vector3.up * leg.ankle.y);
            float dh = new Vector2(hip.x - ankle.x, hip.z - ankle.z).magnitude;
            return dh < r ? hip.y - ankle.y - Mathf.Sqrt(r * r - dh * dh) : float.MaxValue;
        }

        Quaternion FootRot(Vector3 ground, float yaw, float toe) =>
            Quaternion.FromToRotation(Vector3.up, map.NormalAt(new Vector2(ground.x, ground.z)))
            * Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(toe, 0f, 0f);
    }
}
