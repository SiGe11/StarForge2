// MechView.cs — the Mech on screen: assembled from its parts when it drops, walked,
// turned, fired, dressed with its upgrades, and wrecked. Reads the Unit and its
// MechCore; never changes either.
//
// Assembly: the locomotion module (MECH_LEGS_BIPED, _QUAD, MECH_TRACKS, MECH_HOVER)
// under Body, a Torso transform at its waist carrying the frame, and each gun on its
// mount -- left-hand mounts mirrored. The prefabs and the points come from the
// MechKit asset (Editor/MechKitBuilder from Tools/blender/build_mechs.py).
//
// Walking is procedural. Each foot stays planted where it landed until the hip has
// carried the body far enough past it, then steps -- lifted in an arc to where the
// body will be by the time it lands -- while its partner holds (a biped's two feet
// alternate, a quad's diagonal pairs). The legs reach the feet by two-bone IK (the
// Strider's knees bend forward, a bird's; the Arachnid's up), and the body bobs down
// as each foot takes the weight. A foot coming down raises Footfall, which shakes the
// ground (FXDirector's dust and ripples, AudioDirector's stomp, the camera).
// Tracks tilt the hull to the ground under them; the grav skirt floats, banks and
// spins its fans.
using System;
using System.Collections.Generic;
using UnityEngine;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    [DisallowMultipleComponent]
    public sealed class MechView : MonoBehaviour
    {
        public Transform body;

        /// <summary>A foot has come down: where, how heavily (0..1), and whose.</summary>
        public static event Action<Unit, Vector3, float> Footfall;
        /// <summary>The Mechs on the field with a view (FXDirector and AudioDirector read them).</summary>
        public static readonly List<MechView> Active = new List<MechView>(4);

        Unit unit;
        MechCore core;
        bool built, shown = true, blockActive, handedOff;
        Transform legsRoot, torso, frame;
        float waist;
        MechLocomotion loco;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        Vector3 lastPos, velocity;
        float bob, sway, crouch, recoilPitch, tiltX, tiltZ, hoverT;
        bool wasLanded;

        // ---------------------------------------------------------------- legs
        sealed class Leg
        {
            public string tag;
            public Transform upper, lower, foot;
            public Vector3 hip, knee, ankle;          // rest, legs-local
            public float l1, l2;
            public Vector3 upperDir, lowerDir;         // rest bone directions
            public Vector3 planted, from, to;          // world
            public bool swinging;
            public float s, sinceLand = 9f;
            public int group;
            public float footYaw;
            public Quaternion footRest;
        }
        readonly List<Leg> legs = new List<Leg>(4);
        Transform[] fans;

        // ---------------------------------------------------------------- guns
        sealed class Gun
        {
            public MechGun gun;
            public Transform root, moving;
            public Vector3 movingRest;
            public Quaternion rootRest, movingRestRot;
            public int lastShots;
            public float kick, spin, pitch;
            public Vector3 recoilAxis;
            /// <summary>A turret weapon's ring (the Flame Tower).</summary>
            public Transform turret;
            public Quaternion turretRest;
            public bool mirrored;
        }
        readonly List<Gun> guns = new List<Gun>(7);
        readonly Dictionary<string, Transform> extras = new Dictionary<string, Transform>();
        Transform banner;
        Light glowLight;
        Color teamLight;
        Quaternion bannerRest;
        float bannerPitch, bannerVel, bannerRoll;

        static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        static readonly int DamageId = Shader.PropertyToID("_Damage");
        static readonly int BurnId = Shader.PropertyToID("_Burn");
        static readonly int TeamGlowId = Shader.PropertyToID("_TeamGlow");

        public Unit Unit => unit;
        /// <summary>The hull (everything above the ground contact), for the editor's ride trial.</summary>
        public Transform Body => body;
        public MechCore Core => core;
        public bool Shown => shown && built;
        public Transform Torso => torso;
        public MechLocomotion Locomotion => loco;
        /// <summary>How far above its landing spot it still is while it comes down (metres).</summary>
        public float DropHeight { get; private set; }
        public Vector3 Velocity => velocity;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <summary>World position of a named point on the frame (a stack, the reactor, the cockpit).</summary>
        public Vector3 FramePoint(string name, Vector3 fallback)
        {
            if (torso == null || core == null || core.design == null) return transform.position + fallback;
            return torso.TransformPoint(MechKit.Point(core.design.Frame.model, name, fallback));
        }

        /// <summary>World position of a named point on the locomotion (the hover jets, the track stacks).</summary>
        public Vector3 LocoPoint(string name, Vector3 fallback)
        {
            if (legsRoot == null || core == null || core.design == null) return transform.position + fallback;
            return legsRoot.TransformPoint(MechKit.Point(core.design.Loco.model, name, fallback));
        }

        public Vector3 MuzzleOf(MechGun g)
        {
            foreach (var v in guns)
                if (v.gun == g && v.root != null)
                    return v.root.TransformPoint(MechKit.Point(g.part.model, "Muzzle", MechCore.DefaultMuzzle(g.part.id)));
            return core != null ? core.Muzzle(g) : transform.position;
        }

        // ---------------------------------------------------------------- assembly
        void Build()
        {
            unit = GetComponent<Unit>();
            core = unit != null ? unit.mech : null;
            if (core == null || core.design == null || body == null) return;
            var kit = MechKit.Instance;
            var d = core.design;
            loco = d.locomotion;
            int team = unit.team;

            body.localScale = Vector3.one * MechCore.ModelScale;
            legsRoot = Spawn(kit, d.Loco.model, team, body);
            waist = MechKit.Point(d.Loco.model, "Waist", new Vector3(0f, d.Loco.waist, 0f)).y;
            torso = new GameObject("Torso").transform;
            torso.SetParent(body, false);
            torso.localPosition = new Vector3(0f, waist, 0f);
            torsoRest = torso.localPosition;
            frame = Spawn(kit, d.Frame.model, team, torso);

            if (frame != null)
                foreach (var t in frame.GetComponentsInChildren<Transform>(true))
                    switch (t.name)
                    {
                        case "Banner": banner = t; bannerRest = t.localRotation; break;
                        case "Plates1": case "Plates2": case "Plates3": case "Uplink":
                        case "Mast": case "Emitters": case "Nanites": case "Reactive":
                            extras[t.name] = t;
                            t.gameObject.SetActive(false);
                            break;
                    }
            SyncGuns();
            RigLegs();
            // Its light: a soft glow off the cockpit in the side's colour, which the drop
            // turns into the glare of the retro-rockets on the ground below.
            var lg = new GameObject("MechLight");
            lg.transform.SetParent(torso, false);
            lg.transform.localPosition = MechKit.Point(d.Frame.model, "Cockpit", new Vector3(0f, 1.6f, 1.25f)) + Vector3.forward * 0.6f;
            glowLight = lg.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.shadows = LightShadows.None;
            glowLight.range = 11f;
            glowLight.intensity = 0f;
            teamLight = unit.team == 0 ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.55f, 0.35f);
            glowLight.color = teamLight;
            renderers = GetComponentsInChildren<Renderer>(true);
            mpb = new MaterialPropertyBlock();
            lastPos = transform.position;
            built = true;
        }

        static Transform Spawn(MechKit kit, string model, int team, Transform parent)
        {
            var prefab = kit != null ? kit.Prefab(model, team) : null;
            if (prefab == null)
            {
                // No kit built: a stand-in block so the Mech is still there to see.
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(parent, false);
                g.transform.localScale = new Vector3(2f, 2f, 2f);
                g.transform.localPosition = Vector3.up;
                return g.transform;
            }
            var go = Instantiate(prefab, parent, false);
            go.name = model;
            return go.transform;
        }

        /// <summary>Put a weapon model on every gun the Mech has (the Extra Hardpoint
        /// upgrade adds guns while it lives).</summary>
        void SyncGuns()
        {
            var kit = MechKit.Instance;
            var frameModel = core.design.Frame.model;
            for (int i = guns.Count; i < core.guns.Count; i++)
            {
                var g = core.guns[i];
                Vector3 mount = MechKit.Point(frameModel, g.mount, MechCore.DefaultMount(core.design.frame, g.mount));
                var t = Spawn(kit, g.part.model, unit.team, torso);
                t.localPosition = mount;
                if (mount.x < -0.01f) t.localScale = new Vector3(-1f, 1f, 1f);
                var v = new Gun { gun = g, root = t, rootRest = t.localRotation, lastShots = g.shots };
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    if (c.name == "Barrel" || c.name == "Spin" || c.name == "Tube") { v.moving = c; break; }
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    if (c.name == "Turret") { v.turret = c; v.turretRest = c.localRotation; v.mirrored = mount.x < -0.01f; break; }
                if (v.moving != null)
                {
                    v.movingRest = v.moving.localPosition;
                    v.movingRestRot = v.moving.localRotation;
                    v.recoilAxis = v.moving.name == "Tube"
                        ? -new Vector3(0f, Mathf.Sin(55f * Mathf.Deg2Rad), Mathf.Cos(55f * Mathf.Deg2Rad))
                        : Vector3.back;
                }
                guns.Add(v);
                if (i >= core.design.weapons.Count && built)
                {
                    // Bolted on in the field: flash it in.
                    renderers = GetComponentsInChildren<Renderer>(true);
                }
            }
        }

        void RigLegs()
        {
            legs.Clear();
            fans = null;
            if (legsRoot == null) return;
            var d = core.design;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in legsRoot.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            string model = d.Loco.model;
            // The segments come out of the kit side by side (Tools/blender/build_mechs.py):
            // chain them, each keeping where it stands.
            void Chain(string child, string parent)
            {
                if (byName.TryGetValue(child, out var c) && byName.TryGetValue(parent, out var p)) c.SetParent(p, true);
            }
            foreach (var tag in new[] { "R", "L" }) { Chain("Shin" + tag, "Thigh" + tag); Chain("Foot" + tag, "Shin" + tag); }
            foreach (var tag in new[] { "FR", "FL", "BR", "BL" }) Chain("Shin" + tag, "Leg" + tag);
            if (loco == MechLocomotion.Biped)
            {
                int k = 0;
                foreach (var tag in new[] { "R", "L" })
                {
                    if (!byName.TryGetValue("Thigh" + tag, out var up) || !byName.TryGetValue("Shin" + tag, out var lo)) continue;
                    byName.TryGetValue("Foot" + tag, out var ft);
                    var leg = new Leg
                    {
                        tag = tag, upper = up, lower = lo, foot = ft, group = k++,
                        hip = MechKit.Point(model, "Hip" + tag, up.localPosition),
                        knee = MechKit.Point(model, "Knee" + tag, up.localPosition + lo.localPosition),
                        ankle = MechKit.Point(model, "Ankle" + tag, Vector3.zero),
                    };
                    Finish(leg);
                    legs.Add(leg);
                }
            }
            else if (loco == MechLocomotion.Quad)
            {
                foreach (var tag in new[] { "FR", "BL", "FL", "BR" })
                {
                    if (!byName.TryGetValue("Leg" + tag, out var up) || !byName.TryGetValue("Shin" + tag, out var lo)) continue;
                    var leg = new Leg
                    {
                        tag = tag, upper = up, lower = lo, group = tag == "FR" || tag == "BL" ? 0 : 1,
                        hip = MechKit.Point(model, "Hip" + tag, up.localPosition),
                        knee = MechKit.Point(model, "Knee" + tag, up.localPosition + lo.localPosition),
                        ankle = MechKit.Point(model, "Foot" + tag, Vector3.zero),
                    };
                    Finish(leg);
                    legs.Add(leg);
                }
            }
            else if (loco == MechLocomotion.Hover)
            {
                var f = new List<Transform>();
                foreach (var tag in new[] { "FR", "FL", "BR", "BL" })
                    if (byName.TryGetValue("Fan" + tag, out var t)) f.Add(t);
                fans = f.ToArray();
            }
        }

        void Finish(Leg leg)
        {
            leg.l1 = (leg.knee - leg.hip).magnitude;
            leg.l2 = (leg.ankle - leg.knee).magnitude;
            leg.upperDir = (leg.knee - leg.hip).normalized;
            leg.lowerDir = (leg.ankle - leg.knee).normalized;
            leg.planted = RestFoot(leg, Vector3.zero);
            if (leg.foot != null) leg.footRest = leg.foot.localRotation;
        }

        // ---------------------------------------------------------------- per frame
        void LateUpdate()
        {
            if (handedOff) return;
            if (!built)
            {
                if (unit == null) unit = GetComponent<Unit>();
                if (unit == null || unit.mech == null || unit.mech.design == null) return;
                Build();
                if (!built) return;
            }

            if (unit.dying)
            {
                handedOff = true;
                if (shown) MechWreck.Create(this, unit, body, legsRoot, torso, velocity);
                return;
            }

            float dt = Time.deltaTime;
            if (dt > 1e-4f) velocity = Vector3.Lerp(velocity, (transform.position - lastPos) / dt, 0.4f);
            lastPos = transform.position;

            bool visible = unit.team == 0 || unit.visibleToPlayer || MatchSettings.spectate;
            if (visible != shown)
            {
                shown = visible;
                foreach (var r in renderers) if (r != null) r.enabled = visible;
            }

            if (guns.Count < core.guns.Count) { SyncGuns(); renderers = GetComponentsInChildren<Renderer>(true); if (!shown) foreach (var r in renderers) r.enabled = false; }
            SyncExtras();

            // Coming down from orbit: high above its spot, dropping, the legs drawn up.
            bool landed = core.Landed;
            if (!landed)
            {
                float t = Mathf.Clamp01(core.dropLeft / MechCore.DropTime);
                DropHeight = 170f * (0.82f * t * t + 0.18f * t);
                body.localPosition = new Vector3(0f, DropHeight, 0f);
                body.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 7f) * 2f * t, 0f, Mathf.Cos(Time.time * 5f) * 2f * t);
                TuckLegs(0.6f + 0.4f * t);
                torso.localRotation = Quaternion.identity;
                wasLanded = false;
                if (glowLight != null)
                {
                    // Under the retro-rockets, lighting the ground as it comes in.
                    glowLight.transform.position = transform.position + Vector3.up * Mathf.Max(1.5f, DropHeight - 1f);
                    glowLight.color = new Color(1f, 0.62f, 0.3f);
                    glowLight.range = 28f;
                    glowLight.intensity = shown ? (2.5f + 5f * (1f - t)) * (0.85f + 0.3f * UnityEngine.Random.value) : 0f;
                }
                ApplySurface();
                return;
            }
            DropHeight = 0f;
            if (!wasLanded)
            {
                wasLanded = true;
                crouch = 1f;
                foreach (var leg in legs) { leg.planted = RestFoot(leg, Vector3.zero); leg.swinging = false; leg.sinceLand = 0f; }
            }
            crouch = Mathf.MoveTowards(crouch, 0f, dt * 1.1f);
            if (glowLight != null)
            {
                glowLight.transform.localPosition = MechKit.Point(core.design.Frame.model, "Cockpit", new Vector3(0f, 1.6f, 1.25f)) + Vector3.forward * 0.6f;
                glowLight.color = teamLight;
                glowLight.range = 11f;
                float hurt = 1f - unit.hp / Mathf.Max(1f, unit.MaxHp);
                // It flickers as it takes damage.
                glowLight.intensity = shown ? 1.1f * (hurt > 0.6f ? (UnityEngine.Random.value < 0.15f ? 0.2f : 1f) : 1f) : 0f;
            }

            if (!shown) return;

            switch (loco)
            {
                case MechLocomotion.Biped:
                case MechLocomotion.Quad: Walk(dt); break;
                case MechLocomotion.Tracks: if (LegacyTrackRide) RollLegacy(dt); else Roll(dt); break;
                case MechLocomotion.Hover: Hover(dt); break;
            }
            AimTorso(dt);
            AnimateGuns(dt);
            Banner(dt);
            ApplySurface();
        }

        /// <summary>The standard swings: thrown back as the Mech strides out, jolted by each
        /// footfall and every gun's kick, stirred by the wind -- a damped pendulum.</summary>
        void Banner(float dt)
        {
            if (banner == null) return;
            Vector3 lv = torso.InverseTransformDirection(velocity);
            var wind = StarForge.World.Wind.At(Time.time);
            Vector3 wl = torso.InverseTransformDirection(new Vector3(wind.x, 0f, wind.y));
            float want = -Mathf.Clamp(lv.z * 5f, -10f, 28f) - wl.z * 10f + Mathf.Sin(Time.time * 1.9f + unit.id) * 3f;
            float jolt = bob * 40f + recoilPitch * 2f;
            // A stiff spring, stepped at most 20 ms at a time: in one step a long frame (the
            // evaluation runs the game at 8x and more) threw it past stability to NaN, and the
            // banner then logged an error every frame for the rest of the match.
            float left = Mathf.Min(dt, 0.4f);
            while (left > 1e-5f)
            {
                float h = Mathf.Min(left, 0.02f);
                bannerVel += ((want + jolt - bannerPitch) * 22f - bannerVel * 3.2f) * h;
                bannerPitch += bannerVel * h;
                left -= h;
            }
            bannerRoll = Mathf.Lerp(bannerRoll, -lv.x * 4f + wl.x * 8f + Mathf.Sin(Time.time * 1.3f + unit.id) * 2.5f, 1f - Mathf.Exp(-dt * 3f));
            if (float.IsNaN(bannerPitch + bannerVel + bannerRoll)) bannerPitch = bannerVel = bannerRoll = 0f;
            banner.localRotation = bannerRest * Quaternion.Euler(Mathf.Clamp(bannerPitch, -35f, 45f), 0f, bannerRoll);
        }

        void SyncExtras()
        {
            var d = core.design;
            int armour = core.Level(MechUpgrade.Armour);
            Show("Plates1", armour >= 1);
            Show("Plates2", armour >= 2);
            Show("Plates3", armour >= 3);
            Show("Uplink", core.Level(MechUpgrade.Targeting) >= 1);
            Show("Mast", d.utility == MechUtility.Sensors);
            Show("Emitters", d.utility == MechUtility.Shield);
            Show("Nanites", d.utility == MechUtility.Nanites);
            Show("Reactive", d.utility == MechUtility.Reactive);
        }

        void Show(string name, bool on)
        {
            if (extras.TryGetValue(name, out var t) && t.gameObject.activeSelf != on)
            {
                t.gameObject.SetActive(on);
                if (on && shown) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            }
        }

        // ---------------------------------------------------------------- walking
        float GroundAt(Vector3 p) => unit.World.Map.HeightAt(new Vector2(p.x, p.z));

        /// <summary>Where a foot rests under its hip now, on the ground, led by the way
        /// the body is going.</summary>
        Vector3 RestFoot(Leg leg, Vector3 lead)
        {
            Vector3 w = legsRoot != null ? legsRoot.TransformPoint(new Vector3(leg.ankle.x, 0f, leg.ankle.z)) : transform.position;
            w += lead;
            w.y = GroundAt(w);
            return w;
        }

        void Walk(float dt)
        {
            Vector3 v = velocity;
            v.y = 0f;
            float speed = v.magnitude;
            bool quad = loco == MechLocomotion.Quad;
            float stepTime = quad ? Mathf.Clamp(1.9f / Mathf.Max(0.5f, speed), 0.42f, 0.7f) : Mathf.Clamp(2.3f / Mathf.Max(0.5f, speed), 0.38f, 0.65f);
            float trigger = quad ? 0.75f + speed * 0.14f : 0.8f + speed * 0.16f;
            Vector3 lead = v * stepTime * 0.55f;
            float yawRate = 0f;

            // Which group may lift a foot: none while another group is in the air.
            int airborne = -1;
            foreach (var leg in legs) if (leg.swinging) { airborne = leg.group; break; }

            foreach (var leg in legs)
            {
                leg.sinceLand += dt;
                Vector3 want = RestFoot(leg, lead);
                if (!leg.swinging)
                {
                    float off = (Flat(leg.planted) - Flat(want)).magnitude;
                    bool tooFar = off > trigger || (speed < 0.3f && off > 0.35f && leg.sinceLand > 0.4f);
                    if (tooFar && (airborne < 0 || airborne == leg.group) && leg.sinceLand > stepTime * 0.3f)
                    {
                        leg.swinging = true;
                        leg.s = 0f;
                        leg.from = leg.planted;
                        airborne = leg.group;
                    }
                }
                if (leg.swinging)
                {
                    leg.s += dt / stepTime;
                    leg.to = want;
                    if (leg.s >= 1f)
                    {
                        leg.swinging = false;
                        leg.planted = want;
                        leg.sinceLand = 0f;
                        float heavy = core.design.frame == MechFrame.Heavy ? 1f : core.design.frame == MechFrame.Medium ? 0.8f : 0.6f;
                        Footfall?.Invoke(unit, leg.planted, heavy * Mathf.Clamp01(0.5f + speed / 6f));
                    }
                }
            }

            // The body: down as a foot takes the weight, a little sideways onto it.
            float dip = 0f, lean = 0f;
            foreach (var leg in legs)
            {
                float k = Mathf.Exp(-leg.sinceLand * 7f) * Mathf.Clamp01(speed / 2f + 0.2f);
                dip += k;
                if (!leg.swinging) lean += (leg.tag.EndsWith("R") ? 1f : -1f) * 0.5f;
            }
            float groundHere = GroundAt(transform.position);
            float feet = 0f;
            foreach (var leg in legs) feet += leg.swinging ? Mathf.Lerp(leg.from.y, leg.to.y, leg.s) : leg.planted.y;
            feet /= Mathf.Max(1, legs.Count);
            float height = Mathf.Lerp(groundHere, feet, 0.6f) - transform.position.y;
            bob = Mathf.Lerp(bob, -dip * (quad ? 0.10f : 0.2f) - crouch * (quad ? 0.6f : 1.1f), 1f - Mathf.Exp(-dt * 14f));
            sway = Mathf.Lerp(sway, quad ? 0f : lean * 0.1f * Mathf.Clamp01(speed / 2f), 1f - Mathf.Exp(-dt * 5f));
            body.localPosition = new Vector3(sway, height + bob, 0f);
            // Lean into the walk a little.
            Vector3 lv = transform.InverseTransformDirection(v);
            float pitch = Mathf.Clamp(lv.z * 0.9f, -4f, 5f) + crouch * 4f;
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(pitch, 0f, -sway * 12f), 1f - Mathf.Exp(-dt * 6f));

            foreach (var leg in legs)
            {
                Vector3 foot = leg.swinging
                    ? Vector3.Lerp(leg.from, leg.to, Smooth(leg.s)) + Vector3.up * Mathf.Sin(leg.s * Mathf.PI) * (quad ? 0.9f : 1.1f)
                    : leg.planted;
                Solve(leg, foot, quad);
            }
            _ = yawRate;
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
        static float Smooth(float s) => s * s * (3f - 2f * s);

        /// <summary>Two-bone IK: bring the leg's ankle (foot) to <paramref name="footWorld"/>,
        /// the knee bending forward (a bird's leg) or upward (a spider's).</summary>
        void Solve(Leg leg, Vector3 footWorld, bool quad)
        {
            // Legs-local, where the rest points live; the ankle sits above the sole.
            Vector3 target = legsRoot.InverseTransformPoint(footWorld) + Vector3.up * leg.ankle.y;
            Vector3 hip = leg.hip;
            Vector3 v = target - hip;
            float d = Mathf.Clamp(v.magnitude, Mathf.Abs(leg.l1 - leg.l2) + 0.05f, leg.l1 + leg.l2 - 0.02f);
            Vector3 u = v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.down;
            Quaternion yaw = Quaternion.identity;
            Vector3 hint;
            if (quad)
            {
                // Turn the whole leg to face its foot, then bend in that vertical plane.
                Vector3 restFlat = new Vector3(leg.ankle.x - hip.x, 0f, leg.ankle.z - hip.z);
                Vector3 nowFlat = new Vector3(v.x, 0f, v.z);
                if (restFlat.sqrMagnitude > 1e-4f && nowFlat.sqrMagnitude > 1e-4f)
                    yaw = Quaternion.FromToRotation(restFlat.normalized, nowFlat.normalized);
                hint = Vector3.up;
            }
            else hint = Vector3.forward;
            float x = (leg.l1 * leg.l1 - leg.l2 * leg.l2 + d * d) / (2f * d);
            float y = Mathf.Sqrt(Mathf.Max(0f, leg.l1 * leg.l1 - x * x));
            Vector3 w = (hint - u * Vector3.Dot(hint, u));
            w = w.sqrMagnitude > 1e-6f ? w.normalized : Vector3.forward;
            Vector3 knee = hip + u * x + w * y;
            Vector3 up0 = yaw * leg.upperDir;
            Quaternion upperRot = Quaternion.FromToRotation(up0, (knee - hip).normalized) * yaw;
            leg.upper.localRotation = upperRot;
            Vector3 lowerWant = Quaternion.Inverse(upperRot) * (hip + u * d - knee).normalized;
            leg.lower.localRotation = Quaternion.FromToRotation(leg.lowerDir, lowerWant);
            if (leg.foot != null)
            {
                // Feet flat on the ground (the slope under them), toes lifting on a step.
                Vector3 n = unit.World.Map.NormalAt(new Vector2(footWorld.x, footWorld.z));
                float toe = leg.swinging ? Mathf.Sin(leg.s * Mathf.PI) * -18f : 0f;
                Quaternion flat = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, legsRoot.eulerAngles.y, 0f) * Quaternion.Euler(toe, 0f, 0f);
                leg.foot.rotation = flat * leg.footRest;
            }
        }

        /// <summary>Legs drawn up for the drop (0 straight .. 1 tucked).</summary>
        void TuckLegs(float k)
        {
            foreach (var leg in legs)
            {
                Vector3 restFoot = legsRoot.TransformPoint(new Vector3(leg.ankle.x, 0f, leg.ankle.z));
                Vector3 up = legsRoot.TransformDirection(Vector3.up);
                Solve(leg, restFoot + up * (k * (loco == MechLocomotion.Quad ? 1.4f : 1.6f)), loco == MechLocomotion.Quad);
            }
        }

        // ---------------------------------------------------------------- tracks, hover
        void Roll(float dt)
        {
            // The hull rides on its tracks. Each run spans the ground under it and bridges
            // the small bumps, so the hull settles onto a plane fitted through the ground
            // along both runs, through a damped spring with its rate and tilt capped -- and
            // then is lifted so no point of either track is under the ground at the tilt it
            // actually has, since a hull rests on its highest contacts and a spring lags a
            // rise. It used to snap to four raw ground samples, its height taken straight off
            // the NavMesh under it, and rocked like a tumbler toy (MechTrackRide: 22 cm of
            // height jitter, tilts of 20 degrees); and it has always rolled the wrong way on a
            // side slope -- a positive Euler z lifts the right side, and it was given minus
            // the slope -- so the uphill track sank into the hillside.
            Vector3 p = transform.position, f = transform.forward, r = transform.right;
            float k = MechCore.ModelScale;
            float L = 1.9f * k, End = 2.3f * k, W = 1.55f * k;
            float sumL = 0f, sumR = 0f, sxh = 0f, sxx = 0f;
            for (int i = 0; i < TrackSamples; i++)
            {
                float x = Mathf.Lerp(-L, L, i / (float)(TrackSamples - 1));
                float hl = GroundAt(p + f * x - r * W), hr = GroundAt(p + f * x + r * W);
                trackL[i] = hl; trackR[i] = hr;
                sumL += hl; sumR += hr;
                sxh += x * (hl + hr) * 0.5f;
                sxx += x * x;
            }
            float meanL = sumL / TrackSamples, meanR = sumR / TrackSamples;
            float pitch = Mathf.Clamp(-Mathf.Atan(sxh / sxx) * Mathf.Rad2Deg, -24f, 24f);
            float roll = Mathf.Clamp(Mathf.Atan2(meanR - meanL, 2f * W) * Mathf.Rad2Deg, -16f, 16f);
            // The height is smoothed as a height in the world, not as an offset from the
            // agent's: the NavMesh under the agent steps as it crosses polygons, and an
            // offset smoothed against it carried every step into the hull.
            float ground = (meanL + meanR) * 0.5f;
            if (!hullSettled) { tiltX = pitch; tiltZ = roll; hullH = ground; hullSettled = true; }
            tiltX = Spring(tiltX, ref tiltXVel, pitch, 3.5f, dt, 16f);
            tiltZ = Spring(tiltZ, ref tiltZVel, roll, 3.5f, dt, 12f);
            // Up quickly (the tracks climb what is in front of them), down gently.
            hullH = ground > hullH ? Spring(hullH, ref hullHVel, ground, 9f, dt, 5f) : Spring(hullH, ref hullHVel, ground, 3.5f, dt, 3f);

            // Nothing under the ground: at the tilt the hull has now, the lowest point of the
            // tracks above each sample must clear it (the curved ends by a little less).
            float tx = Mathf.Tan(tiltX * Mathf.Deg2Rad), tz = Mathf.Tan(tiltZ * Mathf.Deg2Rad);
            float lift = 0f;
            for (int i = 0; i < TrackSamples; i++)
            {
                float x = Mathf.Lerp(-L, L, i / (float)(TrackSamples - 1));
                lift = Mathf.Max(lift, trackL[i] - (hullH - x * tx - W * tz));
                lift = Mathf.Max(lift, trackR[i] - (hullH - x * tx + W * tz));
            }
            for (int e = -1; e <= 1; e += 2)
            {
                float x = e * End;
                float gl = GroundAt(p + f * x - r * W), gr = GroundAt(p + f * x + r * W);
                lift = Mathf.Max(lift, gl - 0.25f - (hullH - x * tx - W * tz));
                lift = Mathf.Max(lift, gr - 0.25f - (hullH - x * tx + W * tz));
            }
            if (lift > 0f) { hullH += lift; if (hullHVel < 0f) hullHVel = 0f; }

            body.localPosition = new Vector3(0f, hullH - p.y, 0f);
            body.localRotation = Quaternion.Euler(tiltX - recoilPitch * 0.4f, 0f, tiltZ);
            // Coming down hard, the upper hull settles onto its suspension; the tracks stay on
            // the ground (squatting the whole Mech put them 40 cm into it for a second).
            torso.localPosition = torsoRest + Vector3.down * (crouch * 0.35f);
        }

        const int TrackSamples = 7;
        readonly float[] trackL = new float[TrackSamples], trackR = new float[TrackSamples];

        float tiltXVel, tiltZVel, hullH, hullHVel;
        bool hullSettled;
        Vector3 torsoRest;

        /// <summary>Editor A/B only (MechTrackRide): the hull motion from before, snapping
        /// to four ground samples with its height taken off the NavMesh agent.</summary>
        public static bool LegacyTrackRide;

        void RollLegacy(float dt)
        {
            Vector3 p = transform.position, f = transform.forward, r = transform.right;
            float k = MechCore.ModelScale;
            float front = GroundAt(p + f * 2.1f * k), back = GroundAt(p - f * 2.1f * k);
            float right = GroundAt(p + r * 1.5f * k), left = GroundAt(p - r * 1.5f * k);
            tiltX = Mathf.Lerp(tiltX, Mathf.Atan2(back - front, 4.2f * k) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 5f));
            tiltZ = Mathf.Lerp(tiltZ, -Mathf.Atan2(right - left, 3.0f * k) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 5f));
            float h = (front + back + left + right) * 0.25f - p.y;
            float surge = Mathf.Clamp(transform.InverseTransformDirection(velocity).z, -2f, 5f);
            body.localPosition = new Vector3(0f, h - crouch * 0.4f + Mathf.Sin(Time.time * 17f) * 0.015f * Mathf.Clamp01(surge), 0f);
            body.localRotation = Quaternion.Euler(tiltX - recoilPitch * 0.4f, 0f, tiltZ);
            hullSettled = false;
        }

        /// <summary>A critically damped spring toward <paramref name="target"/> (implicit, so
        /// stable at any frame time), its speed capped at <paramref name="maxRate"/>.</summary>
        static float Spring(float x, ref float v, float target, float omega, float dt, float maxRate)
        {
            float f = 1f + 2f * dt * omega, oo = omega * omega, hoo = dt * oo, hhoo = dt * hoo;
            float det = 1f / (f + hhoo);
            float nx = (f * x + dt * v + hhoo * target) * det;
            v = Mathf.Clamp((v + hoo * (target - x)) * det, -maxRate, maxRate);
            return Mathf.Clamp(nx, x - maxRate * dt, x + maxRate * dt);
        }

        void Hover(float dt)
        {
            var map = unit.World.Map;
            hoverT += dt;
            Vector3 p = transform.position;
            float ground = GroundAt(p);
            float lift = Mathf.Max(ground, map.waterLevel + 0.1f) - p.y;
            Vector3 lv = transform.InverseTransformDirection(velocity);
            tiltX = Mathf.Lerp(tiltX, Mathf.Clamp(lv.z * 1.1f, -6f, 8f), 1f - Mathf.Exp(-dt * 4f));
            tiltZ = Mathf.Lerp(tiltZ, Mathf.Clamp(-lv.x * 3f, -14f, 14f), 1f - Mathf.Exp(-dt * 4f));
            float bobH = Mathf.Sin(hoverT * 1.7f + unit.id) * 0.12f + Mathf.Sin(hoverT * 3.1f) * 0.04f;
            body.localPosition = new Vector3(0f, lift + bobH - crouch * 0.5f, 0f);
            body.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
            if (fans != null)
                for (int i = 0; i < fans.Length; i++)
                    fans[i].localRotation = Quaternion.Euler(0f, (hoverT * (620f + velocity.magnitude * 90f)) * (i % 2 == 0 ? 1f : -1f), 0f);
        }

        // ---------------------------------------------------------------- torso and guns
        void AimTorso(float dt)
        {
            // Recoil throws the torso back; it rocks forward and settles.
            recoilPitch = Mathf.Lerp(recoilPitch, core.kick * 7f, 1f - Mathf.Exp(-dt * 18f));
            float yaw = Mathf.DeltaAngle(0f, (unit.turretYaw - unit.yaw) * Mathf.Rad2Deg);
            torso.localRotation = Quaternion.Euler(-recoilPitch, yaw, 0f);
        }

        void AnimateGuns(float dt)
        {
            foreach (var v in guns)
            {
                var g = v.gun;
                if (g.shots != v.lastShots) { v.lastShots = g.shots; v.kick = 1f; }
                v.kick = Mathf.MoveTowards(v.kick, 0f, dt * (g.part.id == MechWeapon.Gatling ? 20f : g.part.id == MechWeapon.Railgun ? 1.6f : 5f));
                // Arm guns tip toward what they shoot at.
                if (g.kind == MountKind.Arm)
                {
                    float want = 0f;
                    if (Unit.Live(g.target))
                    {
                        Vector3 aim = GameWorld.AimPoint(g.target) - v.root.position;
                        float flat = new Vector2(aim.x, aim.z).magnitude;
                        want = Mathf.Clamp(-Mathf.Atan2(aim.y, Mathf.Max(1f, flat)) * Mathf.Rad2Deg, -18f, 14f);
                    }
                    v.pitch = Mathf.MoveTowards(v.pitch, want, dt * 40f);
                    v.root.localRotation = v.rootRest * Quaternion.Euler(v.pitch, 0f, 0f);
                }
                // A turret turns on its ring (mirrored on a left-hand mount, so it turns the
                // other way in its own frame to face the same way in the world).
                if (v.turret != null)
                    v.turret.localRotation = v.turretRest * Quaternion.Euler(0f, (v.mirrored ? -g.yaw : g.yaw) * Mathf.Rad2Deg, 0f);
                if (v.moving == null) continue;
                if (g.part.id == MechWeapon.Gatling)
                {
                    v.spin += dt * 1600f * Mathf.Clamp01(g.firing);
                    v.moving.localRotation = v.movingRestRot * Quaternion.Euler(0f, 0f, v.spin);
                }
                else
                {
                    float travel = g.part.id == MechWeapon.Railgun ? 0.55f : g.part.id == MechWeapon.Mortar ? 0.4f : 0.3f;
                    float k = 1f - (1f - v.kick) * (1f - v.kick);
                    v.moving.localPosition = v.movingRest + v.recoilAxis * travel * k;
                }
            }
        }

        void ApplySurface()
        {
            float flash = unit.damageFlash;
            // Scorched and cracked only once it is badly hurt: over a hull this size the
            // glowing cracks read as a Mech on fire at half health.
            float damage = 0.85f * Mathf.Clamp01((1f - unit.hp / Mathf.Max(1f, unit.MaxHp) - 0.4f) / 0.6f);
            bool drop = !core.Landed;
            if (flash > 0.01f || damage > 0.01f || drop)
            {
                Color teamGlow = (unit.team == 0 ? new Color(0.15f, 0.55f, 1f) : new Color(1f, 0.25f, 0.12f)) * 1.5f;
                // Re-entry: the whole hull glows hot as it comes in, cooling as it slows.
                float heat = drop ? Mathf.Clamp01(core.dropLeft / MechCore.DropTime * 1.4f - 0.2f) : 0f;
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    // A faint flash: a Mech under rifle fire is hit many times a second, and at
                    // a Trooper's strength the flash never decayed and it glowed solid orange.
                    mpb.SetColor(FlashId, new Color(1f, 0.55f, 0.3f) * (flash * 0.12f) + new Color(1f, 0.42f, 0.15f) * heat * 0.9f);
                    mpb.SetFloat(DamageId, damage);
                    mpb.SetFloat(BurnId, 0f);
                    mpb.SetColor(TeamGlowId, teamGlow);
                    r.SetPropertyBlock(mpb);
                }
                blockActive = true;
            }
            else if (blockActive)
            {
                foreach (var r in renderers) if (r != null) r.SetPropertyBlock(null);
                blockActive = false;
            }
        }

        /// <summary>For the editor's icon renderer: build a Mech's model under
        /// <paramref name="parent"/> in its rest pose, with no Unit behind it.</summary>
        public static void AssembleStatic(Transform parent, MechDesign d, int team)
        {
            var kit = MechKit.Instance;
            if (kit == null) return;
            parent.localScale = Vector3.one * MechCore.ModelScale;
            var legs = kit.Prefab(d.Loco.model, team);
            if (legs != null) Instantiate(legs, parent, false);
            float waist = MechKit.Point(d.Loco.model, "Waist", new Vector3(0f, d.Loco.waist, 0f)).y;
            var torso = new GameObject("Torso").transform;
            torso.SetParent(parent, false);
            torso.localPosition = new Vector3(0f, waist, 0f);
            var frame = kit.Prefab(d.Frame.model, team);
            if (frame != null)
            {
                var f = Instantiate(frame, torso, false);
                foreach (var t in f.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Plates") || t.name == "Uplink" || t.name == "Mast" || t.name == "Emitters" || t.name == "Nanites" || t.name == "Reactive")
                        t.gameObject.SetActive(t.name == "Plates1" || (t.name == "Emitters" && d.utility == MechUtility.Shield));
            }
            for (int i = 0; i < d.weapons.Count; i++)
            {
                var part = MechParts.Weapon(d.weapons[i]);
                var w = kit.Prefab(part.model, team);
                if (w == null) continue;
                string mount = d.Frame.mounts[d.MountOf(i)];
                var t = Instantiate(w, torso, false).transform;
                t.localPosition = MechKit.Point(d.Frame.model, mount, MechCore.DefaultMount(d.frame, mount));
                if (t.localPosition.x < -0.01f) t.localScale = new Vector3(-1f, 1f, 1f);
            }
        }
    }
}
