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
// destroyed structure is swapped for its pre-cut debris.
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
        bool debrisSpawned;
        float bodyBaseY;

        Transform legL, legR, gun, arm, cutter, drum, barrel, head, trolley, load, crystals;
        Quaternion legLRest, legRRest, armRest, cutterRest, drumRest, headRest;
        Vector3 gunRest, barrelRest, trolleyRest, loadRest, crystalsRest;
        float recoil, prevCooldown, armDip, cutterAngle, drumAngle, loadFill, oreScale = -1f;
        float glowFill, glowFlare;
        int prevCarrying;

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
            if (body != null) bodyBaseY = body.localPosition.y;

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
                }
            }
        }

        public void OnDeath() { }

        void LateUpdate()
        {
            if (unit == null) return;

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
            if (!visible) return;

            float t = Time.time, dt = Time.deltaTime;
            if (turret != null)
                turret.rotation = Quaternion.Euler(0f, unit.turretYaw * Mathf.Rad2Deg, 0f);

            // Unit resets its cooldown when it fires.
            if (unit.cooldown > prevCooldown + 0.05f) recoil = 1f;
            prevCooldown = unit.cooldown;
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 4.5f);

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
                else if (unit.Type == UnitType.Trooper)
                    y += Mathf.Abs(Mathf.Sin(unit.bob)) * 0.06f;
                // Units ride the NavMesh, which craters do not change: sink the model
                // into the crater instead.
                if (!unit.def.building && unit.World.GroundShape != null)
                    y -= unit.World.GroundShape.Drop(unit.pos);
                if (unit.dying)
                {
                    float ttl = unit.def.building ? 2.4f : 1.3f;
                    y -= Mathf.SmoothStep(0f, 1f, unit.deathTimer / ttl) * unit.def.visualHeight * (unit.def.building ? 1.1f : 0.7f);
                }
                var lp = body.localPosition;
                body.localPosition = new Vector3(lp.x, y, lp.z);

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

        void AnimateParts(float t, float dt)
        {
            var agent = unit.agent;
            float speed = agent != null && agent.enabled ? agent.velocity.magnitude : 0f;
            float kick = 1f - (1f - recoil) * (1f - recoil);   // snaps back, eases home

            if (legL != null && legR != null)
            {
                float swing = Mathf.Sin(unit.bob) * 30f * Mathf.Clamp01(speed / 2f);
                legL.localRotation = legLRest * Quaternion.Euler(swing, 0f, 0f);
                legR.localRotation = legRRest * Quaternion.Euler(-swing, 0f, 0f);
            }
            if (gun != null) gun.localPosition = gunRest + Vector3.back * 0.12f * kick;
            if (barrel != null) barrel.localPosition = barrelRest + Vector3.back * (unit.Type == UnitType.Mauler ? 0.38f : 0.22f) * kick;

            bool mining = unit.working && unit.order == Order.Harvest;
            if (arm != null)
            {
                armDip = Mathf.MoveTowards(armDip, mining ? 1f : 0f, dt * 2.5f);
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
                           : mining ? Mathf.Clamp01(unit.harvestTimer / Unit.HarvestTime) * 0.85f : 0f;
                loadFill = Mathf.MoveTowards(loadFill, want, dt * (want < loadFill ? 2.5f : 1.2f));
                float f = Mathf.Max(0.02f, loadFill);
                load.localScale = Vector3.Scale(loadRest, new Vector3(Mathf.Lerp(0.55f, 1f, f), f, Mathf.Lerp(0.55f, 1f, f)));

                // The glow: a faint stir while the cutter works, swelling to full once
                // the load is aboard, eased both ways (a smoothstep of an exponential
                // approach, so it neither pops on nor snaps off), with a brief flare
                // as the hopper seals and a slower fade as it is emptied.
                float glowWant = unit.carrying > 0 ? 1f : mining ? Mathf.Clamp01(unit.harvestTimer / Unit.HarvestTime) * 0.3f : 0f;
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
