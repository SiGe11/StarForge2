// UnitView.cs — presentation for one Unit: team visibility, surface state,
// procedural animation of the model's movable parts, hover and death. Reads
// the Unit; never changes it.
//
// Movable parts are the child objects the Blender exporter makes from
// Model.group, found by name and animated about their own pivots:
//   Trooper    legs swing with the stride, the gun kicks when firing
//   Digger     the arm dips and the cutter spins while mining; the drum turns while hauling;
//              ore heaps up in the hopper and its glass glows with the load
//   Ore seam   the crystal cluster shrinks as the seam is mined out
//   Mauler, Sentinel   barrels recoil and ease back
//   Foundry    the control head sweeps slowly; Workshop: the crane trolley travels
// Surface state goes to StarForge/Unit on materials of the unit's own -- damage
// charring, the construction hologram, a hit flash, burning on death, a Digger's
// load glowing -- made the first time something shows, worn only while it does and
// written only when a value changes, so every renderer stays SRP-batched (through
// property blocks, every working Digger, every hurt unit and every site went the
// slow way round, a few hundred draws a frame in a match). A
// destroyed structure is swapped for its pre-cut debris; a unit that dies hands its
// model to a UnitWreck, which plays the death out with rigid bodies.
using UnityEngine;
using StarForge.Sim;
using StarForge.View;

namespace StarForge.World
{
    [DisallowMultipleComponent]
    public sealed class UnitView : MonoBehaviour
    {
        [Tooltip("Rotates independently of the body (Mauler turret, Sentinel head).")]
        public Transform turret;
        [Tooltip("Visual root that bobs, hovers and sinks; child of the unit root.")]
        public Transform body;
        public float hoverHeight;
        [Tooltip("Pre-cut chunks thrown when the structure is destroyed (DebrisBurst).")]
        public GameObject debris;

        Unit unit;
        Renderer[] renderers;
        float[] rendererBaseY;
        MaterialPropertyBlock mpb;
        bool shown = true;
        bool blockActive;
        // The unit's own copies of its materials (per renderer, per slot), and the shared
        // ones they stand in for. ownActive: the renderers wear the copies now.
        Material[][] sharedMats, ownMats;
        bool ownActive;
        int ownWorn;
        Color ownFlash = new Color(-1f, 0f, 0f);
        float ownFlashAmount;
        float ownDamage = -1f, ownBurn = -1f, ownGlow = -1f, ownLevel = float.NaN;
        /// <summary>A/B: surface state through property blocks, as before (cleared at each play entry).</summary>
        public static bool LegacyBlocks;
        /// <summary>Renderers wearing a unit's own materials now, over all units (for checks).</summary>
        public static int OwnSurfaces;
        bool debrisSpawned, handedOff;
        float bodyBaseY;
        // How the model has been moving (the agent is switched off by the time the
        // unit is dead, and its wreck carries on the way it was going).
        Vector3 lastPos, velocity;
        // A Mauler shouldering a tree: how far its nose is up, and the jolt as it meets the trunk.
        float pushLean, bumpT = -1f, prevPush;

        /// <summary>A working cutter's turn in degrees a second. At 900 its eight teeth passed 20
        /// times a second, a third of a tooth a frame, and strobed (RosterTrials.DiggerFlicker, which sets it for its A/B).</summary>
        public static float CutterSpin = CutterSpinUsual;
        public const float CutterSpinUsual = 300f;

        Transform legL, legR, gun, arm, cutter, drum, barrel, head, trolley, load, crystals, rotorL, rotorR;
        // A Biolab creature's animal model (Quaternius, legacy clips): what it plays moving,
        // standing, striking and dying.
        Animation anim;
        string clipMove, clipIdle, clipAttack, clipDeath, clipJump;
        float rotorAngle, bank;
        Quaternion legLRest, legRRest, armRest, cutterRest, drumRest, headRest;
        Vector3 gunRest, barrelRest, trolleyRest, loadRest, crystalsRest;
        float recoil, prevCooldown, armDip, cutterAngle, drumAngle, loadFill, oreScale = -1f;
        // Seconds since the last shot, or -1: the Mauler's hull rocks on its
        // suspension when the gun goes off (RecoilRock).
        float recoilT = -1f;
        float bodyBaseZ;
        float glowFill, glowFlare;
        int prevCarrying;
        // A soldier's cape (InfantryCape), earned at a promotion: whether this unit can wear
        // one, how far back its torso and pack reach, and the last rank looked at.
        InfantryCape cape;
        SoldierRigView rig;
        // The Mech-Hunter's flag: cloth (the Mech's standard's), the rigid one while cloth cannot run.
        Transform banner;
        MechBannerCloth bannerCloth;
        float prevDip, divePitch;
        // A creature's blow: seconds since it struck (-1 none), for the body's lunge, gore or swoop.
        float strikeT = -1f;
        int bannerResets;
        bool capeable;
        float capeBackZ;
        int seenRank;

        /// <summary>How brightly a Digger's ore glass glows, 0..~1.4: eased in as the
        /// load goes aboard with a flare as it is sealed, eased out at the drop-off.
        /// FXDirector lights the ground round the Digger with it.</summary>
        public float OreGlow => glowFill + glowFlare;

        static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        static readonly int DamageId = Shader.PropertyToID("_Damage");
        static readonly int BuildLevelId = Shader.PropertyToID("_BuildLevel");
        static readonly int BurnId = Shader.PropertyToID("_Burn");
        static readonly int TeamGlowId = Shader.PropertyToID("_TeamGlow");
        static readonly int OreGlowId = Shader.PropertyToID("_OreGlow");
        const float BuildOff = 100000f;

        public Unit Unit => unit;

        /// <summary>A creature's body over its blow, <paramref name="s"/> seconds after it struck: how far
        /// forward and up it is thrown (metres), its head down (+) or tossed up (-), and the tusks' sweep
        /// (degrees). The bite darts in and back; the spit throws the head; the tusks drive down and toss
        /// up through a sweep; the flyer swoops down at its prey and climbs away.</summary>
        static void Strike(WeaponKind w, float s, out float fwd, out float up, out float pitch, out float yaw)
        {
            fwd = up = pitch = yaw = 0f;
            float Peak(float p, float at) => p < at ? p / at : 1f - Mathf.SmoothStep(0f, 1f, (p - at) / (1f - at));
            switch (w)
            {
                case WeaponKind.Claws:
                {
                    float p = Mathf.Clamp01(s / 0.40f), k = Peak(p, 0.2f);
                    fwd = 0.55f * k;
                    up = 0.18f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(p / 0.35f));
                    pitch = 18f * k;
                    break;
                }
                case WeaponKind.Acid:
                {
                    float p = Mathf.Clamp01(s / 0.50f), k = Peak(p, 0.18f);
                    fwd = 0.25f * k;
                    pitch = 14f * k - 10f * (1f - p) * (p < 0.18f ? 1f - p / 0.18f : 0f);
                    break;
                }
                case WeaponKind.Gore:
                {
                    float p = Mathf.Clamp01(s / 0.70f);
                    fwd = 0.9f * Peak(p, 0.15f);
                    float down = Peak(p, 0.12f), toss = p < 0.12f ? 0f : Mathf.Sin(Mathf.PI * Mathf.Clamp01((p - 0.12f) / 0.5f));
                    pitch = 16f * down * (p < 0.2f ? 1f : 0f) - 16f * toss;
                    up = 0.15f * toss;
                    yaw = 18f * Mathf.Sin(Mathf.PI * 2f * Mathf.Clamp01((p - 0.08f) / 0.4f)) * (1f - p);
                    break;
                }
                case WeaponKind.Glaive:
                {
                    float p = Mathf.Clamp01(s / 0.60f), arc = Mathf.Sin(Mathf.PI * p);
                    fwd = 0.9f * arc;
                    up = -1.3f * arc;
                    pitch = 26f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(p * 1.25f));
                    break;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // -sfblocks on the player: the old way, for an fps A/B.
            LegacyBlocks = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sfblocks") >= 0;
            OwnSurfaces = 0;
        }

        void CollectRenderers()
        {
            // The list is taken again (a cloth rebuilt): back onto the shared materials first,
            // the copies were made for the old list.
            DropOwnMaterials();
            renderers = GetComponentsInChildren<Renderer>(true);
            // Each renderer's height above the model origin, so the construction
            // line (in object space) lines up across a turret or a raised head.
            rendererBaseY = new float[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                rendererBaseY[i] = transform.InverseTransformPoint(renderers[i].transform.position).y;
        }

        void OnDestroy()
        {
            if (bannerCloth != null) bannerCloth.Destroy();
            if (rig != null) rig.Destroyed();
            DropOwnMaterials();
        }

        /// <summary>Put the renderers back on their shared materials and destroy the unit's copies
        /// (Unity does not: a material made in play lives until the scene goes).</summary>
        void DropOwnMaterials()
        {
            WearOwn(false);
            if (ownMats != null)
                foreach (var set in ownMats)
                    if (set != null)
                        foreach (var m in set) if (m != null && m.name.EndsWith(OwnSuffix)) Destroy(m);
            ownMats = sharedMats = null;
        }

        /// <summary>What a unit's own copy of a material is called after the shared one's name (and how
        /// a copy is told from a shared material left in its slot).</summary>
        public const string OwnSuffix = " (unit)";

        /// <summary>The renderers onto the unit's own materials, or back onto the shared ones.</summary>
        void WearOwn(bool own)
        {
            if (own == ownActive || renderers == null) return;
            if (own)
            {
                if (ownMats == null || ownMats.Length != renderers.Length)
                {
                    sharedMats = new Material[renderers.Length][];
                    ownMats = new Material[renderers.Length][];
                }
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    if (ownMats[i] == null)
                    {
                        var src = r.sharedMaterials;
                        var mine = new Material[src.Length];
                        // Only what reads the surface state is copied (StarForge/Unit): a creature's
                        // URP/Lit and the Mech-Hunter's cloth keep their shared materials in their slots.
                        for (int k = 0; k < src.Length; k++)
                            mine[k] = src[k] != null && src[k].HasProperty(FlashId) ? new Material(src[k]) { name = src[k].name + OwnSuffix } : src[k];
                        sharedMats[i] = src;
                        ownMats[i] = mine;
                    }
                    r.sharedMaterials = ownMats[i];
                    ownWorn++;
                }
                OwnSurfaces += ownWorn;
                // Everything is written on the first frame they are worn.
                ownFlash = new Color(-1f, 0f, 0f);
                ownDamage = ownBurn = ownGlow = -1f;
                ownLevel = float.NaN;
            }
            else
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || sharedMats == null || i >= sharedMats.Length || sharedMats[i] == null) continue;
                    r.sharedMaterials = sharedMats[i];
                }
                OwnSurfaces -= ownWorn;
                ownWorn = 0;
            }
            ownActive = own;
        }

        /// <summary>The model is about to go to a wreck or a corpse, which takes the surface up
        /// from the renderers' property blocks: give it there what the unit showed, on the shared
        /// materials (the copies are this view's, and go with it).</summary>
        void HandSurfaceOver()
        {
            if (!ownActive) return;
            WearOwn(false);
            if (rig != null || unit == null) return;
            // What it showed on its last frame, as the old block held it (not this frame's flash:
            // the killing hit has just set that to full).
            WriteBlocks(ownDamage < 0f ? 0f : ownFlashAmount, ownDamage < 0f ? 0f : ownDamage, false, 0f);
        }

        public void Bind(Unit u)
        {
            unit = u;
            renderers = GetComponentsInChildren<Renderer>(true);
            mpb = new MaterialPropertyBlock();
            if (body != null) { bodyBaseY = body.localPosition.y; bodyBaseZ = body.localPosition.z; }
            lastPos = transform.position;

            // Each renderer's height above the model origin, so the construction
            // line (in object space) lines up across a turret or a raised head.
            rendererBaseY = new float[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                rendererBaseY[i] = transform.InverseTransformPoint(renderers[i].transform.position).y;

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "LegL": legL = t; legLRest = t.localRotation; break;
                    case "LegR": legR = t; legRRest = t.localRotation; break;
                    case "Gun": gun = t; gunRest = t.localPosition; break;
                    case "Arm": arm = t; armRest = t.localRotation; break;
                    case "Cutter": cutter = t; cutterRest = t.localRotation; break;
                    case "Drum": drum = t; drumRest = t.localRotation; break;
                    case "Barrel":
                    case "Barrels": barrel = t; barrelRest = t.localPosition; break;
                    case "Head": head = t; headRest = t.localRotation; break;
                    case "Trolley": trolley = t; trolleyRest = t.localPosition; break;
                    case "Load": load = t; loadRest = t.localScale; break;
                    case "Crystals": crystals = t; crystalsRest = t.localScale; break;
                    case "RotorL": rotorL = t; break;
                    case "RotorR": rotorR = t; break;
                    case "Banner": banner = t; break;
                }
            }
            anim = GetComponentInChildren<Animation>(true);
            if (anim != null)
            {
                foreach (AnimationState st in anim)
                {
                    string n = st.name.ToLowerInvariant();
                    if (n.Contains("death")) clipDeath = st.name;
                    else if (n.Contains("attack")) clipAttack = st.name;
                    else if (n.Contains("idle")) clipIdle = st.name;
                    // The plain walk before a slow one or a run (the Gorehorn's cow has all three).
                    else if ((n.Contains("walk") && !n.Contains("slow")) || n.Contains("flying")) clipMove = st.name;
                    else if (n.Contains("run") && clipMove == null) clipMove = st.name;
                    else if (n.Contains("jump")) clipJump = st.name;
                }
                // A frog hops; a beast with no strike of its own lunges (its jump) instead.
                if (clipMove == null) clipMove = clipJump;
                else if (clipAttack == null) clipAttack = clipJump;
                if (clipIdle == null) clipIdle = clipMove;
                // A quick, sharp strike: at the clip's own pace the blow looked like a nod.
                if (clipAttack != null) { anim[clipAttack].wrapMode = WrapMode.Once; anim[clipAttack].layer = 1; anim[clipAttack].speed = 1.4f; }
                if (clipMove != null) anim.Play(clipMove);
            }
            // Made before the fog's renderer list is final, so it hides with the unit.
            if (banner != null && bannerCloth == null && (bannerCloth = MechBannerCloth.Make(banner, u.team, u.id)) != null)
                CollectRenderers();
            capeable = body != null && InfantryCape.Eligible(u.def);
            if (capeable)
            {
                capeBackZ = BackOf(body);
                seenRank = u.rank;
                if (InfantryCape.All) cape = InfantryCape.Make(body, capeBackZ, u.team, u.rank, u.id);
            }
            // Under the new combat a soldier wears the rigged body (the rigid parts stay, hidden, for his wreck).
            if (u.World.Tactical && u.def.bodyRadius > 0f)
            {
                rig = SoldierRigView.Attach(u, renderers);
                if (rig != null) { renderers = rig.Renderers; rendererBaseY = new float[1]; }
            }
            // The exporter writes nested groups side by side (Tools/blender/export_fbx.py):
            // the cutter rides the arm.
            if (cutter != null && arm != null && cutter.parent != arm)
            {
                cutter.SetParent(arm, true);
                cutterRest = cutter.localRotation;
            }
        }

        public void OnDeath() { }

        /// <summary>How far back (body space, the model facing +Z) the torso and its pack reach:
        /// where a cape hangs. Legs and gun are left out.</summary>
        static float BackOf(Transform body)
        {
            float back = -0.45f;
            foreach (var mf in body.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name == "LegL" || mf.name == "LegR" || mf.name == "Gun") continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    back = Mathf.Min(back, body.InverseTransformPoint(mf.transform.TransformPoint(c)).z);
                }
            }
            return back;
        }

        void LateUpdate()
        {
            if (unit == null || handedOff) return;

            // A unit (not a structure or an ore seam) that dies leaves a wreck, if the
            // player saw it go; one that dies out of sight just disappears.
            if (unit.dying && unit.def.IsMobile)
            {
                handedOff = true;
                if (cape != null) cape.Freeze();
                // The wreck keeps the rigid flag: the cloth would go on swinging on a dead hull.
                if (bannerCloth != null) { bannerCloth.Destroy(); bannerCloth = null; if (banner != null) banner.gameObject.SetActive(true); }
                if (rig != null && rig.Die(shown)) return;
                HandSurfaceOver();
                if (shown && body != null)
                {
                    if (unit.def.organic) CreatureCorpse.Create(unit, body, anim, clipDeath);
                    else UnitWreck.Create(unit, body, turret, gun, legL, legR, velocity);
                }
                // A tank that died out of sight still leaves its hulk, to be seen when someone gets there.
                else if (unit.hulk != null && body != null) UnitWreck.Create(unit, body, turret, gun, legL, legR, velocity, false);
                return;
            }
            float step = Time.deltaTime;
            if (step > 1e-4f) velocity = Vector3.Lerp(velocity, (transform.position - lastPos) / step, 0.5f);
            lastPos = transform.position;

            // Enemy units vanish outside vision; enemy structures stay once seen
            // (what the player remembers of a base), as in any fogged RTS.
            bool visible = unit.team != 1 || unit.visibleToPlayer ||
                           (unit.def.building && unit.everSeenByPlayer) ||
                           StarForge.Game.MatchSettings.spectate;
            visible &= !debrisSpawned;
            if (visible != shown)
            {
                shown = visible;
                foreach (var r in renderers) if (r != null) r.enabled = visible;
                if (visible && bannerCloth != null) bannerCloth.Resume();
            }
            if (capeable)
            {
                // Each promotion to rank 2 or above, a soldier without a cape has its chance of one;
                // a further stripe re-trims the one he has. Hidden by the fog, so is the cape.
                if (unit.rank > seenRank)
                {
                    for (int r = Mathf.Max(seenRank + 1, 2); r <= unit.rank && cape == null; r++)
                        if (InfantryCape.Earned(unit.id, r)) cape = InfantryCape.Make(body, capeBackZ, unit.team, unit.rank, unit.id);
                    if (cape != null) cape.SetRank(unit.rank);
                    seenRank = unit.rank;
                }
                if (cape != null)
                {
                    cape.WearerDown = rig != null && unit.posture != Posture.Stand;
                    cape.Tick(visible, velocity, transform.forward);
                }
            }
            if (!visible) return;

            float t = Time.time, dt = Time.deltaTime;
            if (rig != null) rig.Tick(dt, velocity);
            if (bannerCloth != null)
            {
                bool onShow = bannerCloth.Tick(Wind.At(t));
                // A cloth rebuilt after going bad has new renderers for the fog to hide.
                if (bannerCloth.Resets != bannerResets) { bannerResets = bannerCloth.Resets; CollectRenderers(); }
                if (banner.gameObject.activeSelf == onShow) banner.gameObject.SetActive(!onShow);
            }
            // The turret is a child of the body, so it takes its yaw locally and
            // rides whatever the hull does under it (the recoil rock below).
            if (turret != null)
                turret.localRotation = Quaternion.Euler(0f, (unit.turretYaw - unit.yaw) * Mathf.Rad2Deg, 0f);

            // Unit resets its cooldown when it fires.
            if (unit.cooldown > prevCooldown + 0.05f) { recoil = 1f; recoilT = 0f; if (unit.def.organic) strikeT = 0f; }
            if (strikeT >= 0f && (strikeT += dt) > 0.8f) strikeT = -1f;
            prevCooldown = unit.cooldown;
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 4.5f);
            if (recoilT >= 0f)
            {
                recoilT += dt;
                if (recoilT > RockTime) recoilT = -1f;
            }

            if (body != null)
            {
                float y = bodyBaseY;
                if (unit.Type == UnitType.Skimmer)
                {
                    y += hoverHeight + Mathf.Sin(t * 2.3f + unit.id) * 0.12f;
                    // The NavMesh runs along the lake bed through the shallows; a
                    // hover craft skims the surface instead of diving to it.
                    float aboveWater = unit.World.Map.waterLevel + hoverHeight * 0.8f - transform.position.y;
                    y = Mathf.Max(y, bodyBaseY + aboveWater);
                }
                else if (unit.def.flying)
                {
                    // Aloft, bobbing on the air, never lower over a crest than its height over
                    // the ground a little ahead (the simulation flies it at a fixed height over
                    // whatever is under it).
                    // A plane dives a little on its runs (Unit.dip, which the simulation aims at too).
                    y += hoverHeight - unit.dip + Mathf.Sin(t * 1.7f + unit.id) * 0.18f;
                    // Over the lake its height is over the water, not the bed (GameWorld.AirPoint).
                    y = Mathf.Max(y, bodyBaseY + hoverHeight + unit.World.Map.waterLevel - transform.position.y);
                    var ahead = unit.pos + new Vector2(Mathf.Sin(unit.yaw), Mathf.Cos(unit.yaw)) * 3f;
                    if (unit.World.Map.InBounds(ahead))
                        y = Mathf.Max(y, bodyBaseY + hoverHeight * 0.8f + unit.World.Map.HeightAt(ahead) - transform.position.y);
                }
                else if (unit.Type == UnitType.Trooper || unit.def.upgradeClass == UnitClass.Infantry)
                    y += Mathf.Abs(Mathf.Sin(unit.bob)) * 0.06f;
                else if (unit.Type == UnitType.Ironclad)
                    y -= Mathf.Abs(Mathf.Sin(unit.bob * 0.45f)) * 0.10f;
                // Units ride the NavMesh, which craters do not change: sink the model
                // into the crater instead.
                if (!unit.def.building && !unit.def.flying && unit.World.GroundShape != null)
                    y -= unit.World.GroundShape.Drop(unit.pos);
                if (unit.dying)
                {
                    float ttl = unit.def.building ? 2.4f : 1.3f;
                    y -= Mathf.SmoothStep(0f, 1f, unit.deathTimer / ttl) * unit.def.visualHeight * (unit.def.building ? 1.1f : 0.7f);
                }
                // A cape hangs on the hidden rigid body: it goes down with the man who wears it.
                if (rig != null)
                {
                    y += rig.CapeDrop;
                    body.localRotation = Quaternion.Euler(rig.CapeLean, 0f, 0f);
                }
                var lp = body.localPosition;
                float z = lp.z;
                // A gun that size shoves the whole tank: the hull is thrown back on
                // its suspension, the nose comes up, and it rocks down and settles
                // (a damped swing, not a slide back to rest). Presentation only --
                // the hull's own position is the simulation's.
                if (unit.Type == UnitType.Mauler)
                {
                    float rock = recoilT >= 0f ? RecoilRock(recoilT) : 0f;
                    // Shouldering a tree over: the nose rides up the trunk while it
                    // pushes, with a jolt as the glacis meets it, and settles after.
                    float push = unit.pushLoad;
                    if (push > 0.05f && prevPush <= 0.05f) bumpT = 0f;
                    prevPush = push;
                    pushLean = Mathf.MoveTowards(pushLean, push, dt * (push > pushLean ? 3f : 1.5f));
                    float bump = 0f;
                    if (bumpT >= 0f)
                    {
                        bumpT += dt;
                        bump = RecoilRock(bumpT) * 0.8f;
                        if (bumpT > RockTime) bumpT = -1f;
                    }
                    body.localRotation = Quaternion.Euler(-2.6f * rock - 3.2f * pushLean + 1.8f * bump, 0f, 0f);
                    z = bodyBaseZ - 0.16f * Mathf.Max(0f, rock) - 0.06f * bump;
                    y += 0.09f * pushLean;
                }
                // A creature throws its body into the blow (Strike): forward, up, its head down or tossed.
                float sPitch = 0f, sYaw = 0f;
                if (unit.def.organic)
                {
                    float sFwd = 0f, sUp = 0f;
                    if (strikeT >= 0f) Strike(unit.def.weapon, strikeT, out sFwd, out sUp, out sPitch, out sYaw);
                    z = bodyBaseZ + sFwd;
                    y += sUp;
                    if (!unit.def.flying) body.localRotation = Quaternion.Euler(sPitch, sYaw, 0f);
                }
                body.localPosition = new Vector3(lp.x, y, z);

                if (unit.Type == UnitType.Skimmer)
                {
                    // Bank into turns.
                    var agent = unit.agent;
                    float bank = 0f;
                    if (agent != null && agent.enabled)
                    {
                        Vector3 lv = transform.InverseTransformDirection(agent.velocity);
                        bank = Mathf.Clamp(-lv.x * 3.5f, -25f, 25f);
                    }
                    body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(0f, 0f, bank), dt * 6f);
                }
                else if (unit.def.flying)
                {
                    // Bank into the turn (by how fast the heading swings) and lean into the speed.
                    Vector3 lv = transform.InverseTransformDirection(unit.Velocity);
                    float turnRate = Mathf.DeltaAngle(prevYaw * Mathf.Rad2Deg, unit.yaw * Mathf.Rad2Deg) / Mathf.Max(dt, 1e-4f);
                    prevYaw = unit.yaw;
                    bank = Mathf.Lerp(bank, Mathf.Clamp(-turnRate * 0.35f - lv.x * 2f, -35f, 35f), 1f - Mathf.Exp(-dt * 5f));
                    float pitch = Mathf.Clamp(lv.z * 0.9f, -6f, 10f);
                    // Nose down into a dive, up as it climbs out.
                    if (unit.def.fixedWing)
                    {
                        float climb = (prevDip - unit.dip) / Mathf.Max(dt, 1e-4f);
                        prevDip = unit.dip;
                        divePitch = Mathf.Lerp(divePitch, Mathf.Clamp(-climb * 14f, -16f, 16f), 1f - Mathf.Exp(-dt * 6f));
                        pitch = divePitch + 4f;
                    }
                    body.localRotation = Quaternion.Euler(pitch + sPitch, 0f, bank);
                }
            }

            AnimateParts(t, dt);
            ApplySurface(t);

            if (unit.dying && debris != null && !debrisSpawned && unit.deathTimer > 0.12f)
            {
                debrisSpawned = true;
                var d = Instantiate(debris, transform.position, transform.rotation);
                var burst = d.GetComponent<DebrisBurst>();
                if (burst != null) burst.Launch(transform.position + Vector3.up * unit.def.visualHeight * 0.25f);
                foreach (var r in renderers) if (r != null) r.enabled = false;
                shown = false;
            }
        }

        float prevYaw;

        /// <summary>How long the hull goes on rocking after a shot.</summary>
        const float RockTime = 0.85f;

        /// <summary>The hull's swing after the gun fires, 1 at the shot: a damped
        /// oscillation, so it is thrown back hard, comes down past level and settles,
        /// rather than easing home in one direction.</summary>
        static float RecoilRock(float age) => Mathf.Exp(-5.5f * age) * Mathf.Cos(13f * age);

        void AnimateParts(float t, float dt)
        {
            float speed = unit.Velocity.magnitude;
            float kick = 1f - (1f - recoil) * (1f - recoil);   // snaps back, eases home
            if (rotorL != null || rotorR != null)
            {
                rotorAngle += dt * Mathf.Lerp(1300f, 1900f, Mathf.Clamp01(speed / Mathf.Max(1f, unit.def.speed)));
                if (rotorL != null) rotorL.localRotation = Quaternion.Euler(0f, rotorAngle, 0f);
                if (rotorR != null) rotorR.localRotation = Quaternion.Euler(0f, -rotorAngle, 0f);
            }
            if (anim != null && !unit.dying)
            {
                // The animal's own cycles: its gait at the pace it is going, standing still
                // its idle, and a strike over either when it fires.
                bool moving = speed > 0.4f || unit.def.flying;
                string want = moving ? clipMove : clipIdle;
                if (want != null)
                {
                    if (!anim.IsPlaying(want)) anim.CrossFade(want, 0.2f);
                    if (moving && !unit.def.flying) anim[want].speed = Mathf.Clamp(speed / Mathf.Max(1f, unit.def.speed) * 1.6f, 0.5f, 2f);
                }
                if (recoil >= 0.99f && clipAttack != null) anim.CrossFade(clipAttack, 0.05f, PlayMode.StopSameLayer);
            }

            if (legL != null && legR != null)
            {
                // The mini-mech strides at half the cadence, longer and heavier.
                float phase = unit.Type == UnitType.Ironclad ? unit.bob * 0.45f : unit.bob;
                float swing = Mathf.Sin(phase) * (unit.Type == UnitType.Ironclad ? 24f : 30f) * Mathf.Clamp01(speed / 2f);
                legL.localRotation = legLRest * Quaternion.Euler(swing, 0f, 0f);
                legR.localRotation = legRRest * Quaternion.Euler(-swing, 0f, 0f);
            }
            if (gun != null) gun.localPosition = gunRest + Vector3.back * 0.12f * kick;
            if (barrel != null) barrel.localPosition = barrelRest + Vector3.back * (unit.Type == UnitType.Mauler || unit.Type == UnitType.Lancer ? 0.38f : 0.22f) * kick;

            bool mining = unit.working && unit.order == Order.Harvest;
            // The arm dips while it mines (the Digger); a drill with no arm (the Prospector's)
            // still spins up.
            armDip = Mathf.MoveTowards(armDip, mining ? 1f : 0f, dt * 2.5f);
            if (arm != null)
            {
                float jitter = mining ? Mathf.Sin(t * 11f + unit.id) * 1.5f : 0f;
                arm.localRotation = armRest * Quaternion.Euler(armDip * 16f + jitter, 0f, 0f);
            }
            if (cutter != null)
            {
                cutterAngle += dt * Mathf.Lerp(40f, CutterSpin, armDip);
                cutter.localRotation = cutterRest * Quaternion.Euler(0f, 0f, cutterAngle);
            }
            if (drum != null)
            {
                if (mining || unit.carrying > 0) drumAngle += dt * (mining ? 140f : 60f);
                drum.localRotation = drumRest * Quaternion.Euler(drumAngle, 0f, 0f);
            }
            if (load != null)
            {
                // The heap grows while the cutter works, stays heaped on the way
                // home and empties at the drop-off.
                float want = unit.carrying > 0 ? 1f
                           : mining ? Mathf.Clamp01(unit.harvestTimer / unit.def.harvestTime) * 0.85f : 0f;
                loadFill = Mathf.MoveTowards(loadFill, want, dt * (want < loadFill ? 2.5f : 1.2f));
                float f = Mathf.Max(0.02f, loadFill);
                load.localScale = Vector3.Scale(loadRest, new Vector3(Mathf.Lerp(0.55f, 1f, f), f, Mathf.Lerp(0.55f, 1f, f)));

                // The glow: a faint stir while the cutter works, swelling to full once
                // the load is aboard, eased both ways (a smoothstep of an exponential
                // approach, so it neither pops on nor snaps off), with a brief flare
                // as the hopper seals and a slower fade as it is emptied.
                float glowWant = unit.carrying > 0 ? 1f : mining ? Mathf.Clamp01(unit.harvestTimer / unit.def.harvestTime) * 0.3f : 0f;
                float rate = glowWant > glowFill ? 1.6f : 0.9f;
                glowFill = Mathf.Lerp(glowFill, glowWant, 1f - Mathf.Exp(-dt * rate * 2.2f));
                if (Mathf.Abs(glowFill - glowWant) < 0.002f) glowFill = glowWant;
                if (unit.carrying > 0 && prevCarrying == 0) glowFlare = 0.45f;
                glowFlare = Mathf.MoveTowards(glowFlare, 0f, dt * 0.6f);
                prevCarrying = unit.carrying;
            }
            if (crystals != null)
            {
                float want = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(unit.oreLeft / (float)Unit.NodeCapacity));
                oreScale = oreScale < 0f ? want : Mathf.MoveTowards(oreScale, want, dt * 0.3f);
                crystals.localScale = crystalsRest * oreScale;
            }

            if (unit.Complete && !unit.dying)
            {
                if (head != null) head.localRotation = headRest * Quaternion.Euler(0f, t * 7f + unit.id * 37f, 0f);
                if (trolley != null)
                {
                    float travel = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(t * 0.11f + unit.id * 0.13f, 1f));
                    trolley.localPosition = trolleyRest + Vector3.right * travel * 3.4f;
                }
            }
        }

        void ApplySurface(float t)
        {
            float flash = unit.damageFlash;
            float damage = unit.Complete && !unit.def.neutral ? Mathf.Clamp01((1f - unit.hp / unit.MaxHp - 0.25f) / 0.75f) : 0f;
            bool constructing = !unit.Complete && unit.def.building;
            float burn = unit.dying ? Mathf.Clamp01(unit.deathTimer / 0.8f) : 0f;
            if (rig != null) { rig.ApplySurface(flash, damage, burn); return; }

            bool showing = flash > 0.01f || damage > 0.01f || constructing || unit.dying || loadFill > 0.001f || OreGlow > 0.001f;
            if (LegacyBlocks)
            {
                WearOwn(false);
                if (showing) WriteBlocks(flash, damage, constructing, burn);
                else ClearBlocks();
                return;
            }
            ClearBlocks();
            WearOwn(showing);
            if (!showing) return;

            // Written only when a value has moved: most of what wears its own materials is a
            // Digger at work or a unit hurt a while ago, and nothing on it changes frame to frame.
            Color flashColor = FlashColor(flash);
            float glow = Glow;
            float level = constructing ? unit.buildProgress * (unit.def.visualHeight + 0.2f) : BuildOff;
            bool first = ownDamage < 0f;
            bool setFlash = first || Mathf.Abs(flashColor.r - ownFlash.r) > 0.002f;
            bool setDamage = first || Mathf.Abs(damage - ownDamage) > 0.004f;
            bool setBurn = first || Mathf.Abs(burn - ownBurn) > 0.004f;
            bool setGlow = first || Mathf.Abs(glow - ownGlow) > 0.002f;
            bool setLevel = first || level != ownLevel;
            if (!(setFlash || setDamage || setBurn || setGlow || setLevel)) return;
            Color teamGlow = TeamGlow;
            for (int i = 0; i < renderers.Length; i++)
            {
                var set = i < ownMats.Length ? ownMats[i] : null;
                if (set == null) continue;
                foreach (var m in set)
                {
                    // A slot left on its shared material is never written to.
                    if (m == null || !m.name.EndsWith(OwnSuffix)) continue;
                    if (setFlash) m.SetColor(FlashId, flashColor);
                    if (setDamage) m.SetFloat(DamageId, damage);
                    if (setBurn) m.SetFloat(BurnId, burn);
                    if (setGlow) m.SetFloat(OreGlowId, glow);
                    if (setLevel) m.SetFloat(BuildLevelId, constructing ? level - rendererBaseY[i] : BuildOff);
                    if (first) m.SetColor(TeamGlowId, teamGlow);
                }
            }
            if (setFlash) { ownFlash = flashColor; ownFlashAmount = flash; }
            if (setDamage) ownDamage = damage;
            if (setBurn) ownBurn = burn;
            if (setGlow) ownGlow = glow;
            if (setLevel) ownLevel = level;
        }

        /// <summary>Renderers of this unit wearing its own materials now, and how many copies it holds (for checks).</summary>
        public int OwnWorn => ownWorn;
        public int OwnCopies
        {
            get
            {
                int n = 0;
                if (ownMats != null)
                    foreach (var set in ownMats)
                        if (set != null)
                            foreach (var m in set) if (m != null && m.name.EndsWith(OwnSuffix)) n++;
                return n;
            }
        }

        /// <summary>For checks, asked at the end of a frame: null when the renderers wear what they
        /// should and the unit's own materials hold what ApplySurface would write now (within the
        /// written-on-change tolerances), else what is off. Allocates; nothing in a match calls it.</summary>
        public string SurfaceFault()
        {
            if (unit == null || handedOff || rig != null || renderers == null || LegacyBlocks) return null;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var want = ownActive ? (ownMats != null && i < ownMats.Length ? ownMats[i] : null)
                                     : (sharedMats != null && i < sharedMats.Length ? sharedMats[i] : null);
                if (want == null) { if (ownActive) return $"{unit.Type}: wearing, but no materials kept for renderer {i}"; continue; }
                var now = r.sharedMaterials;
                if (now.Length != want.Length) return $"{unit.Type}: renderer {i} has {now.Length} slots, {want.Length} kept";
                for (int k = 0; k < now.Length; k++)
                    if (now[k] != want[k]) return $"{unit.Type}: renderer {i} slot {k} wears {(now[k] != null ? now[k].name : "nothing")}, should wear {(want[k] != null ? want[k].name : "nothing")}";
            }
            if (!shown) return null;      // not kept up while the fog hides it
            float flash = unit.damageFlash;
            float damage = unit.Complete && !unit.def.neutral ? Mathf.Clamp01((1f - unit.hp / unit.MaxHp - 0.25f) / 0.75f) : 0f;
            bool constructing = !unit.Complete && unit.def.building;
            float burn = unit.dying ? Mathf.Clamp01(unit.deathTimer / 0.8f) : 0f;
            bool showing = flash > 0.01f || damage > 0.01f || constructing || unit.dying || loadFill > 0.001f || OreGlow > 0.001f;
            if (showing != ownActive) return $"{unit.Type}: showing {showing} but wearing own {ownActive}";
            if (!ownActive) return null;
            float level = constructing ? unit.buildProgress * (unit.def.visualHeight + 0.2f) : BuildOff;
            for (int i = 0; i < renderers.Length && i < ownMats.Length; i++)
            {
                if (ownMats[i] == null) continue;
                foreach (var m in ownMats[i])
                {
                    if (m == null || !m.name.EndsWith(OwnSuffix)) continue;
                    if (Mathf.Abs(m.GetColor(FlashId).r - FlashColor(flash).r) > 0.006f) return $"{unit.Type}: flash {m.GetColor(FlashId).r:0.000}, should be {FlashColor(flash).r:0.000}";
                    if (Mathf.Abs(m.GetFloat(DamageId) - damage) > 0.008f) return $"{unit.Type}: damage {m.GetFloat(DamageId):0.000}, should be {damage:0.000}";
                    if (Mathf.Abs(m.GetFloat(BurnId) - burn) > 0.008f) return $"{unit.Type}: burn {m.GetFloat(BurnId):0.000}, should be {burn:0.000}";
                    if (Mathf.Abs(m.GetFloat(OreGlowId) - Glow) > 0.006f) return $"{unit.Type}: glow {m.GetFloat(OreGlowId):0.000}, should be {Glow:0.000}";
                    float wantLevel = constructing ? level - rendererBaseY[i] : BuildOff;
                    if (Mathf.Abs(m.GetFloat(BuildLevelId) - wantLevel) > 0.001f * Mathf.Max(1f, Mathf.Abs(wantLevel))) return $"{unit.Type}: build level {m.GetFloat(BuildLevelId):0.000}, should be {wantLevel:0.000}";
                    if (Mathf.Abs(m.GetColor(TeamGlowId).r - TeamGlow.r) > 0.01f) return $"{unit.Type}: team glow not its side's";
                }
            }
            return null;
        }

        // A hit flash that suits a trooper turns a whole building white.
        Color FlashColor(float flash) => new Color(1f, 0.55f, 0.3f) * (flash * (unit.def.building ? 0.4f : 1.2f));
        Color TeamGlow => (unit.team == 0 ? new Color(0.15f, 0.55f, 1f) : new Color(1f, 0.25f, 0.12f)) * 1.5f;
        float Glow => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(glowFill)) + glowFlare;

        /// <summary>The same surface state through one property block per renderer: the old way
        /// (LegacyBlocks), and what a wreck or a corpse is handed (HandSurfaceOver).</summary>
        void WriteBlocks(float flash, float damage, bool constructing, float burn)
        {
            Color teamGlow = TeamGlow;
            float level = unit.buildProgress * (unit.def.visualHeight + 0.2f);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(FlashId, FlashColor(flash));
                mpb.SetFloat(DamageId, damage);
                mpb.SetFloat(BuildLevelId, constructing ? level - rendererBaseY[i] : BuildOff);
                mpb.SetFloat(BurnId, burn);
                mpb.SetColor(TeamGlowId, teamGlow);
                mpb.SetFloat(OreGlowId, Glow);
                r.SetPropertyBlock(mpb);
            }
            blockActive = true;
        }

        void ClearBlocks()
        {
            if (!blockActive) return;
            foreach (var r in renderers) if (r != null) r.SetPropertyBlock(null);
            blockActive = false;
        }
    }
}
