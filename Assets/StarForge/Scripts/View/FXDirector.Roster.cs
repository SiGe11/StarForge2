// FXDirector.Roster.cs — the wider roster's effects: muzzles and strikes for the
// infantry mortar, the sniper rifle, the tank destroyer's cannon, acid, claws and the
// glaive that leaps; the infantry flamer's jet; how a creature goes down. The flak,
// the interceptor's gun and the gunship's rockets borrow the Mech's autocannon and
// missile effects (projectile kinds 4 and 5), sized by the event.
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed partial class FXDirector
    {
        static readonly Color AcidColor = new Color(0.55f, 1.6f, 0.35f);
        static readonly Color GlaiveColor = new Color(1.6f, 0.6f, 2.2f);

        void RosterFire(GameEvent e)
        {
            Vector3 fwd = e.dir.sqrMagnitude > 1e-4f ? e.dir.normalized : Vector3.forward;
            switch (e.projectileKind)
            {
                case GameWorld.ProjLob:
                    // A thump and a ring of smoke round the tube's mouth.
                    AddFlare(e.pos, fwd, 1.6f, 1.1f, new Color(3.6f, 2.0f, 0.7f), 0.08f);
                    Emit(glow, e.pos, fwd, 1.8f, 0.1f, new Color(1f, 0.62f, 0.3f));
                    for (int i = 0; i < 4; i++)
                        Emit(trail, e.pos + fwd * 0.3f, fwd * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 0.8f,
                             Random.Range(0.9f, 1.4f), Random.Range(1.2f, 1.8f), new Color(0.8f, 0.77f, 0.73f, 0.4f), Random.Range(0f, 360f));
                    Flash(e.pos, new Color(1f, 0.6f, 0.3f), 4f, 9f, 0.1f);
                    PressureWave(e.pos, 0.15f, 10f, Vector3.zero, 0f);
                    break;
                case GameWorld.ProjSniper:
                    // A sharp white crack and a puff that hangs at the muzzle.
                    AddFlare(e.pos, fwd, 1.8f, 0.5f, new Color(4.5f, 4.2f, 3.6f), 0.05f);
                    Emit(trail, e.pos + fwd * 0.2f, fwd * 1.2f + Vector3.up * 0.2f, 0.7f, 1.4f, new Color(0.82f, 0.8f, 0.78f, 0.3f), Random.Range(0f, 360f));
                    Flash(e.pos, new Color(1f, 0.9f, 0.7f), 3f, 7f, 0.06f);
                    break;
                case GameWorld.ProjAP:
                    // Lighter than the Mauler's gun, sharper: a long flash and a cone of smoke.
                    AddFlare(e.pos, fwd, 4.2f, 1.9f, new Color(3.8f, 2.4f, 0.9f), 0.10f);
                    AddFlare(e.pos, fwd, 2.0f, 1.0f, new Color(5f, 4.4f, 3.2f), 0.06f);
                    for (int i = 0; i < 5; i++)
                        Emit(trail, e.pos + fwd * Random.Range(0.3f, 1.4f), fwd * Random.Range(3f, 9f) + Random.insideUnitSphere + Vector3.up * 0.4f,
                             Random.Range(1.6f, 2.4f), Random.Range(1.4f, 2.2f), new Color(0.8f, 0.77f, 0.73f, 0.5f), Random.Range(0f, 360f));
                    for (int i = 0; i < 6; i++)
                        Emit(sparks, e.pos + fwd * 0.4f, fwd * Random.Range(12f, 22f) + Random.insideUnitSphere * 2.5f, 0.14f, Random.Range(0.12f, 0.25f), new Color(1f, 0.72f, 0.35f));
                    Flash(e.pos + fwd, new Color(1f, 0.62f, 0.3f), 8f, 14f, 0.15f);
                    PressureWave(e.pos, 0.45f, 24f, fwd, 0.7f);
                    if (rig != null && rig.cam != null)
                        rig.Shake(Mathf.Clamp01(1f - Vector3.Distance(rig.cam.transform.position, e.pos) / 120f) * 0.08f);
                    break;
                case GameWorld.ProjAcid:
                    for (int i = 0; i < 4; i++)
                        Emit(glow, e.pos, fwd * Random.Range(2f, 5f) + Random.insideUnitSphere, 0.35f, Random.Range(0.2f, 0.35f), AcidColor * 0.5f);
                    break;
                case GameWorld.ProjClaw:
                {
                    // Bites: a scatter of grit off what it struck; the Gorehorn's tusks throw up the ground.
                    Vector3 at = e.pos + fwd * 0.5f;
                    bool beast = e.type == UnitType.Gorehorn;
                    if (e.end.sqrMagnitude > 0f) Slash(e.end, fwd, beast);
                    for (int i = 0; i < (beast ? 10 : 6); i++)
                        Emit(sparks, at, Random.insideUnitSphere * (beast ? 6f : 3f) + Vector3.up, 0.1f, Random.Range(0.1f, 0.2f), new Color(0.9f, 0.85f, 0.7f));
                    if (beast)
                    {
                        Vector3 g = world.Map.Ground(new Vector2(at.x, at.z));
                        for (int i = 0; i < 5; i++)
                            Emit(trail, g + Random.insideUnitSphere * 0.8f + Vector3.up * 0.3f, Random.insideUnitSphere * 2f + Vector3.up * 1.2f,
                                 Random.Range(1.4f, 2.2f), Random.Range(1.0f, 1.6f), new Color(0.58f, 0.51f, 0.42f, 0.4f), Random.Range(0f, 360f));
                        PressureWave(g, 0.25f, 12f, fwd, 0.6f);
                        ShakeFrom(g, 0.05f);
                    }
                    break;
                }
                case GameWorld.ProjRocket:
                    // A rocket leaving the tube: a flash forward, the backblast thrown out behind.
                    AddFlare(e.pos, fwd, 1.6f, 0.8f, new Color(4f, 2.6f, 1.0f), 0.08f);
                    AddFlare(e.pos - fwd * 1.6f, -fwd, 2.4f, 1.3f, new Color(3.2f, 1.8f, 0.7f), 0.10f);
                    for (int i = 0; i < 5; i++)
                        Emit(trail, e.pos - fwd * Random.Range(1.4f, 2.6f), -fwd * Random.Range(3f, 7f) + Random.insideUnitSphere * 1.2f,
                             Random.Range(1.0f, 1.6f), Random.Range(1.0f, 1.6f), new Color(0.8f, 0.77f, 0.73f, 0.45f), Random.Range(0f, 360f));
                    Flash(e.pos, new Color(1f, 0.65f, 0.3f), 4f, 9f, 0.08f);
                    break;
                case GameWorld.ProjGlaive:
                    Emit(glow, e.pos, fwd * 2f, 1.0f, 0.12f, GlaiveColor * 0.5f);
                    break;
            }
        }

        /// <summary>A blow dealt at once, drawn where it lands: a bite rakes three quick streaks across
        /// what it struck; the tusks sweep a crescent through the beast's reach, low over the ground.</summary>
        void Slash(Vector3 at, Vector3 fwd, bool beast)
        {
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
            Color core = new Color(2.6f, 2.2f, 1.7f), halo = new Color(0.55f, 0.3f, 0.22f);
            if (!beast)
            {
                // Raked from one shoulder down to the other side, a little different each bite.
                float lean = Random.Range(0.35f, 0.75f) * (Random.value < 0.5f ? -1f : 1f);
                Vector3 d = (side * lean + Vector3.up).normalized * 0.42f;
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 c = at - fwd * 0.25f + side * (0.14f * k) + Vector3.up * (0.04f * k);
                    beams.Add(new Beam { from = c - d, to = c + d, born = Time.time, life = 0.14f, width = 0.07f, core = core, halo = halo });
                }
                return;
            }
            Vector3 centre = at - fwd * 1.4f;
            Vector3 prev = default;
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(-1.15f, 1.15f, i / 8f);
                Vector3 q = centre + (fwd * Mathf.Cos(a) + side * Mathf.Sin(a)) * 2.1f + Vector3.up * (0.25f + 0.35f * (1f - Mathf.Abs(a)));
                if (i > 0) beams.Add(new Beam { from = prev, to = q, born = Time.time, life = 0.18f, width = 0.16f, core = core, halo = halo });
                prev = q;
            }
        }

        void RosterImpact(GameEvent e)
        {
            switch (e.projectileKind)
            {
                case GameWorld.ProjLob:
                    Explosion(e.pos, 1.15f, false, pressure: 0.7f);
                    ShakeFrom(e.pos, 0.06f);
                    break;
                case GameWorld.ProjRocket:
                    Explosion(e.pos, 0.9f, false, pressure: 0.6f);
                    break;
                case GameWorld.ProjSniper:
                    ImpactSparks(e.pos, e.team, 3);
                    Emit(glow, e.pos, Vector3.zero, 1.4f, 0.08f, new Color(1f, 0.9f, 0.7f));
                    break;
                case GameWorld.ProjAP:
                    ImpactSparks(e.pos, e.team, 1);
                    for (int i = 0; i < 8; i++)
                        Emit(sparks, e.pos, Random.insideUnitSphere * 9f + Vector3.up * 3f, 0.16f, Random.Range(0.2f, 0.4f), new Color(1f, 0.75f, 0.4f));
                    Emit(glow, e.pos, Vector3.zero, 2.6f, 0.12f, new Color(1f, 0.7f, 0.35f));
                    Emit(smoke, e.pos + Vector3.up * 0.3f, Vector3.up, 1.0f, 1.0f, new Color(0.42f, 0.4f, 0.38f, 0.35f), Random.Range(0f, 360f));
                    break;
                case GameWorld.ProjAcid:
                    for (int i = 0; i < 7; i++)
                        Emit(glow, e.pos, Random.insideUnitSphere * 2.2f + Vector3.up * 1.2f, Random.Range(0.25f, 0.45f), Random.Range(0.3f, 0.6f), AcidColor * 0.55f);
                    Emit(smoke, e.pos + Vector3.up * 0.2f, Vector3.up * 0.8f, 0.8f, 0.9f, new Color(0.45f, 0.55f, 0.35f, 0.3f), Random.Range(0f, 360f));
                    break;
                case GameWorld.ProjGlaive:
                {
                    // A leap from the last thing it struck: an arc of light between the two.
                    Emit(glow, e.pos, Vector3.zero, 1.6f, 0.14f, GlaiveColor * 0.5f);
                    for (int i = 0; i < 4; i++)
                        Emit(sparks, e.pos, Random.insideUnitSphere * 5f + Vector3.up, 0.12f, Random.Range(0.15f, 0.3f), new Color(1f, 0.6f, 1f));
                    if (e.end.sqrMagnitude > 0f)
                        beams.Add(new Beam { from = e.end, to = e.pos, born = Time.time, life = 0.12f, width = 0.16f, core = GlaiveColor, halo = GlaiveColor * 0.25f });
                    break;
                }
            }
        }

        /// <summary>The Scorcher's jet: the Mech flamer's tongues, a man's size.</summary>
        void RosterFlame(GameEvent e)
        {
            Vector3 from = e.pos, to = e.end;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.1f) return;
            Vector3 dir = d / len;
            Vector3 v = dir * Mathf.Clamp(len * 2.2f, 6f, 14f);
            for (int i = 0; i < 2; i++)
            {
                float w = Random.Range(0.5f, 0.8f);
                EmitFlame(flames, from + dir * Random.Range(0.1f, 0.6f) + Random.insideUnitSphere * 0.08f,
                          v * Random.Range(0.8f, 1.05f) + Random.insideUnitSphere * 1.0f + Vector3.up * 0.5f, w, w * 1.3f, Random.Range(0.4f, 0.55f), Color.white);
            }
            if (Random.value < 0.4f)
                EmitFlame(flames, to + Random.insideUnitSphere * 0.6f + Vector3.up * 0.2f, Vector3.up * Random.Range(0.8f, 1.5f), 0.8f, 1.4f, Random.Range(0.4f, 0.6f), Color.white);
            Emit(glow, from, dir * 1.5f, 1.2f, 0.08f, new Color(1f, 0.55f, 0.2f));
            if (Random.value < 0.2f)
                Emit(smoke, to + Vector3.up * 0.8f, Vector3.up * Random.Range(1f, 2f), Random.Range(1.0f, 1.6f), Random.Range(1.6f, 2.4f),
                     new Color(0.24f, 0.22f, 0.2f, 0.4f), Random.Range(0f, 360f));
            if (Random.value < 0.2f) Flash(Vector3.Lerp(from, to, 0.5f), new Color(1f, 0.55f, 0.2f), 3.5f, 8f, 0.1f);
        }

        /// <summary>A Biolab creature going down: no fire, a burst of ichor and a little dust.</summary>
        void CreatureDown(GameEvent e)
        {
            Vector3 k = e.dir.sqrMagnitude > 1e-4f ? e.dir.normalized : Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = Random.insideUnitSphere * 2.2f + k * 1.6f;
                v.y = Mathf.Abs(v.y) + 1.0f;
                Emit(glow, e.pos, v, Random.Range(0.18f, 0.32f), Random.Range(0.3f, 0.6f), AcidColor * 0.35f);
            }
            Vector3 g = world.Map.Ground(new Vector2(e.pos.x, e.pos.z));
            Emit(smoke, g + Vector3.up * 0.3f, Vector3.up * 0.5f, 1.3f, 1.2f, new Color(0.5f, 0.47f, 0.4f, 0.32f), Random.Range(0f, 360f));
        }
    }
}
