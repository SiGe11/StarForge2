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
// Surface state goes to StarForge/Unit through one property block per renderer
// -- damage charring, the construction hologram, a hit flash, burning on death --
// set only while something shows, so idle renderers stay SRP-batched. A
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
        bool debrisSpawned, handedOff;
        float bodyBaseY;
        // How the model has been moving (the agent is switched off by the time the
        // unit is dead, and its wreck carries on the way it was going).
        Vector3 lastPos, velocity;
        // A Mauler shouldering a tree: how far its nose is up, and the jolt as it meets the trunk.
        float pushLean, bumpT = -1f, prevPush;

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
                if (clipAttack != null) { anim[clipAttack].wrapMode = WrapMode.Once; anim[clipAttack].layer = 1; }
                if (clipMove != null) anim.Play(clipMove);
            }
            capeable = body != null && InfantryCape.Eligible(u.def);
            if (capeable)
            {
                capeBackZ = BackOf(body);
                seenRank = u.rank;
                if (InfantryCape.All) cape = InfantryCape.Make(body, capeBackZ, u.team, u.rank, u.id);
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
                if (shown && body != null)
                {
                    if (unit.def.organic) CreatureCorpse.Create(unit, body, anim, clipDeath);
                    else UnitWreck.Create(unit, body, turret, gun, legL, legR, velocity);
                }
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
            }
            if (capeable)
            {
                // Each promotion a soldier without a cape has its chance of one; a further
                // stripe re-trims the one he has. Hidden by the fog, so is the cape.
                if (unit.rank > seenRank)
                {
                    for (int r = seenRank + 1; r <= unit.rank && cape == null; r++)
                        if (InfantryCape.Earned(unit.id, r)) cape = InfantryCape.Make(body, capeBackZ, unit.team, unit.rank, unit.id);
                    if (cape != null) cape.SetRank(unit.rank);
                    seenRank = unit.rank;
                }
                if (cape != null) cape.Tick(visible, velocity, transform.forward);
            }
            if (!visible) return;

            float t = Time.time, dt = Time.deltaTime;
            // The turret is a child of the body, so it takes its yaw locally and
            // rides whatever the hull does under it (the recoil rock below).
            if (turret != null)
                turret.localRotation = Quaternion.Euler(0f, (unit.turretYaw - unit.yaw) * Mathf.Rad2Deg, 0f);

            // Unit resets its cooldown when it fires.
            if (unit.cooldown > prevCooldown + 0.05f) { recoil = 1f; recoilT = 0f; }
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
                    y += hoverHeight + Mathf.Sin(t * 1.7f + unit.id) * 0.18f;
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
                    body.localRotation = Quaternion.Euler(pitch, 0f, bank);
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
                cutterAngle += dt * Mathf.Lerp(40f, 900f, armDip);
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

            if (flash > 0.01f || damage > 0.01f || constructing || unit.dying || loadFill > 0.001f || OreGlow > 0.001f)
            {
                Color teamGlow = (unit.team == 0 ? new Color(0.15f, 0.55f, 1f) : new Color(1f, 0.25f, 0.12f)) * 1.5f;
                float level = unit.buildProgress * (unit.def.visualHeight + 0.2f);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    // A hit flash that suits a trooper turns a whole building white.
                    mpb.SetColor(FlashId, new Color(1f, 0.55f, 0.3f) * (flash * (unit.def.building ? 0.4f : 1.2f)));
                    mpb.SetFloat(DamageId, damage);
                    mpb.SetFloat(BuildLevelId, constructing ? level - rendererBaseY[i] : BuildOff);
                    mpb.SetFloat(BurnId, burn);
                    mpb.SetColor(TeamGlowId, teamGlow);
                    mpb.SetFloat(OreGlowId, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(glowFill)) + glowFlare);
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
    }
}
