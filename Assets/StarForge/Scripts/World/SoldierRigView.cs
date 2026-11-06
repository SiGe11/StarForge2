// SoldierRigView.cs -- a soldier's body under the new combat: the rigged soldier (Tools/blender/
// build_soldier_rig.py) in place of the rigid parts. Presentation only: it reads how the Unit stands
// and moves, and never writes to it. The rigid parts stay under the unit, hidden, for the wreck: a
// man who falls from his feet still falls as the ragdoll he always did (UnitWreck). One who dies lying
// flat stays as he lay, then sinks.
using UnityEngine;
using StarForge.Sim;

namespace StarForge.World
{
    public sealed class SoldierRigView
    {
        static readonly int Idle = Animator.StringToHash("Idle"), Walk = Animator.StringToHash("Walk"), Run = Animator.StringToHash("Run"),
            CrouchWalk = Animator.StringToHash("CrouchWalk"), CrouchIdle = Animator.StringToHash("CrouchIdle"), CrouchAim = Animator.StringToHash("CrouchAim"),
            Crawl = Animator.StringToHash("Crawl"), ProneAim = Animator.StringToHash("ProneAim"), Aim = Animator.StringToHash("Aim"),
            Fire = Animator.StringToHash("Fire"), CrouchFire = Animator.StringToHash("CrouchFire"), ProneFire = Animator.StringToHash("ProneFire"),
            Dive = Animator.StringToHash("Dive"), ThrowClip = Animator.StringToHash("Throw"), Stab = Animator.StringToHash("Stab"),
            Reload = Animator.StringToHash("Reload"), CrouchReload = Animator.StringToHash("CrouchReload"), ProneReload = Animator.StringToHash("ProneReload"),
            PistolAim = Animator.StringToHash("PistolAim"), PistolFire = Animator.StringToHash("PistolFire"),
            CrouchPistol = Animator.StringToHash("CrouchPistol"), RunPistol = Animator.StringToHash("RunPistol");
        static readonly int FlashId = Shader.PropertyToID("_FlashColor"), DamageId = Shader.PropertyToID("_Damage"), BurnId = Shader.PropertyToID("_Burn");

        // Metres a second the clips' feet move at, at their own speed (build_soldier_rig.py -- stride):
        // the Animator's speed follows the ground, or the feet slide.
        const float WalkFeet = 2.5f, RunFeet = 5.05f, CrouchFeet = 3.9f, CrawlFeet = 1.16f;
        const float FireLasts = 0.33f, DiveLasts = 0.42f, ThrowLasts = 1.1f, StabLasts = 1.1f;

        readonly Unit unit;
        readonly Transform root;
        readonly Animator anim;
        readonly SkinnedMeshRenderer smr;
        readonly Renderer[] rigidParts;
        Material surface;
        float shownFlash, shownDamage, shownBurn;
        int state;
        float busyUntil;
        float prevCooldown;
        Posture prevPosture;
        float shownThrow = -99f, shownStab = -99f, shownPistol = -99f, shownReload = -99f;
        float pistolUntil, busySpeed;

        public readonly Renderer[] Renderers;
        /// <summary>Where a cape hanging on the rigid body should sit, with him down.</summary>
        public float CapeDrop { get; private set; }
        public float CapeLean { get; private set; }

        /// <summary>The rigged body in place of these parts; null if the prefab is not there (the old look).</summary>
        public static SoldierRigView Attach(Unit u, Renderer[] parts)
        {
            int side = Mathf.Clamp(u.team, 0, 1);
            var prefab = Resources.Load<GameObject>(u.Type == UnitType.Trooper ? $"SoldierRig_T{side}" : $"SoldierRig_{u.Type}_T{side}");
            return prefab == null ? null : new SoldierRigView(u, parts, prefab);
        }

        SoldierRigView(Unit u, Renderer[] parts, GameObject prefab)
        {
            unit = u;
            rigidParts = parts;
            foreach (var r in parts) if (r != null) r.enabled = false;
            var go = Object.Instantiate(prefab, u.transform, false);
            go.name = "SoldierRig";
            root = go.transform;
            anim = go.GetComponentInChildren<Animator>();
            smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Renderers = new Renderer[] { smr };
            state = Idle;
            prevCooldown = u.cooldown;
            prevPosture = u.posture;
        }

        public void Tick(float dt, Vector3 velocity)
        {
            var posture = unit.posture;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            float now = Time.time;

            // Unit resets its cooldown when it fires.
            bool fired = unit.cooldown > prevCooldown + 0.05f;
            prevCooldown = unit.cooldown;
            if (fired && speed < 0.5f)
            {
                int shot = posture == Posture.Prone ? ProneFire : posture == Posture.Crouch ? CrouchFire : Fire;
                anim.Play(shot, 0, 0f);
                state = shot;
                busyUntil = now + FireLasts;
            }
            // Thrown down from a run he dives; otherwise he drops.
            if (posture == Posture.Prone && prevPosture != Posture.Prone && speed > 2f)
            {
                anim.CrossFadeInFixedTime(Dive, 0.08f, 0);
                state = Dive;
                busyUntil = now + DiveLasts;
            }
            prevPosture = posture;
            // A round from the pistol, standing still: as a shot.
            if (unit.pistolAtT != shownPistol)
            {
                shownPistol = unit.pistolAtT;
                pistolUntil = now + 1.2f;
                if (speed < 0.5f && now >= busyUntil)
                {
                    int shot = posture == Posture.Prone ? ProneFire : posture == Posture.Crouch ? CrouchPistol : PistolFire;
                    anim.Play(shot, 0, 0f);
                    state = shot;
                    busyUntil = now + FireLasts;
                }
            }
            if (unit.reloadAtT != shownReload)
            {
                shownReload = unit.reloadAtT;
                // On the move he does it as he goes: there is no clip for that, and it does not show at this range.
                if (speed < 0.5f && unit.def.reloadTime > 0.2f)
                {
                    int clip = posture == Posture.Prone ? ProneReload : posture == Posture.Crouch ? CrouchReload : Reload;
                    anim.Play(clip, 0, 0f);
                    state = clip;
                    busyUntil = now + unit.def.reloadTime;
                    busySpeed = 2f / unit.def.reloadTime;   // the clips are two seconds long
                }
            }
            if (unit.thrownAtT != shownThrow)
            {
                shownThrow = unit.thrownAtT;
                // Flat on the ground he has no room to swing: the smoke comes from where he lies.
                if (posture != Posture.Prone)
                {
                    anim.Play(ThrowClip, 0, 0f);
                    state = ThrowClip;
                    busyUntil = now + ThrowLasts;
                }
            }
            if (unit.stabbedAtT != shownStab)
            {
                shownStab = unit.stabbedAtT;
                anim.Play(Stab, 0, 0f);
                state = Stab;
                busyUntil = now + StabLasts;
            }

            int want;
            float feet = 0f;
            bool aiming = Unit.Live(unit.target) && !unit.hiding && unit.Dist(unit.target) - unit.target.def.radius <= unit.def.range;
            if (posture == Posture.Prone)
            {
                want = speed > 0.2f ? Crawl : ProneAim;
                if (want == Crawl) feet = CrawlFeet;
            }
            else if (posture == Posture.Crouch)
            {
                want = speed > 0.3f ? CrouchWalk : now < pistolUntil ? CrouchPistol : aiming ? CrouchAim : CrouchIdle;
                if (want == CrouchWalk) feet = CrouchFeet;
            }
            else
            {
                want = speed > 4.4f ? (now < pistolUntil ? RunPistol : Run) : speed > 0.3f ? Walk : now < pistolUntil ? PistolAim : aiming ? Aim : Idle;
                if (want == Walk) feet = WalkFeet;
                else if (want == Run || want == RunPistol) feet = RunFeet;
            }
            if (now >= busyUntil) busySpeed = 0f;
            anim.speed = busySpeed > 0f ? busySpeed : feet > 0f ? Mathf.Clamp(speed / feet, 0.4f, 1.8f) : 1f;
            if (now >= busyUntil && want != state)
            {
                anim.CrossFadeInFixedTime(want, want == Run || state == Run || want == RunPistol ? 0.12f : 0.2f, 0);
                state = want;
            }

            var shape = unit.World.GroundShape;
            root.localPosition = shape != null ? Vector3.down * shape.Drop(unit.pos) : Vector3.zero;
            float wantDrop = posture == Posture.Prone ? -1.25f : posture == Posture.Crouch ? -0.55f : 0f;
            float wantLean = posture == Posture.Prone ? 82f : posture == Posture.Crouch ? 28f : 0f;
            CapeDrop = Mathf.MoveTowards(CapeDrop, wantDrop, dt * 3f);
            CapeLean = Mathf.MoveTowards(CapeLean, wantLean, dt * 180f);
        }

        /// <summary>The hit flash, damage and burning, on a material of his own: a property block would take
        /// him out of the SRP Batcher, and most soldiers in a fight are hurt. Written only when it changes.</summary>
        public void ApplySurface(float flash, float damage, float burn)
        {
            if (surface == null && flash <= 0.01f && damage <= 0.01f && burn <= 0.01f) return;
            if (surface == null) surface = smr.material;
            if (!Mathf.Approximately(flash, shownFlash))
            {
                shownFlash = flash;
                surface.SetColor(FlashId, new Color(1f, 0.55f, 0.3f) * (flash * 1.2f));
            }
            if (Mathf.Abs(damage - shownDamage) > 0.004f) { shownDamage = damage; surface.SetFloat(DamageId, damage); }
            if (Mathf.Abs(burn - shownBurn) > 0.004f) { shownBurn = burn; surface.SetFloat(BurnId, burn); }
        }

        /// <summary>He is dead. Lying flat he stays as he lay and this keeps him (true); from his feet the
        /// rigid parts come back for the wreck to take (false).</summary>
        public bool Die(bool seen)
        {
            if (unit.posture == Posture.Prone && seen)
            {
                anim.enabled = false;
                root.SetParent(null, true);
                root.gameObject.AddComponent<SoldierBody>().Begin(surface);
                surface = null;
                return true;
            }
            root.gameObject.SetActive(false);
            // Only for a wreck: out of sight there is none, and nothing is shown for his last moment.
            if (seen) foreach (var r in rigidParts) if (r != null) r.enabled = true;
            return false;
        }

        public void Destroyed()
        {
            if (surface != null) Object.Destroy(surface);
            surface = null;
        }
    }

    /// <summary>A man who died lying down: he stays a while, then goes into the ground like any wreck.</summary>
    public sealed class SoldierBody : MonoBehaviour
    {
        const float Lies = 7f, Sinks = 2f;
        float born;
        Material own;

        public void Begin(Material surface)
        {
            own = surface;
            born = Time.time;
        }

        void Update()
        {
            float age = Time.time - born;
            if (age < Lies) return;
            transform.position += Vector3.down * (0.6f * Time.deltaTime / Sinks);
            if (age > Lies + Sinks) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (own != null) Destroy(own);
        }
    }
}
