// FXDirector.Roster.cs — the wider roster's effects: muzzles and strikes for the
// infantry mortar (the Mech's, a man's size), the sniper rifle, the bazooka, the tank
// destroyer's cannon, acid, claws and the glaive that leaps; the infantry flamer's
// jet; an avenging animal's bite; how a creature goes down. The flak,
// the interceptor's gun and the gunship's rockets borrow the Mech's autocannon and
// missile effects (projectile kinds 4 and 5), sized by the event.
using System.Collections.Generic;
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
                case GameWorld.ProjPistol:
                    AddFlare(e.pos, fwd, 0.8f, 0.22f, new Color(4f, 3f, 1.7f), 0.05f);
                    break;
                case GameWorld.ProjMG:
                    AddFlare(e.pos, fwd, 1.5f, 0.42f, new Color(4.5f, 3.3f, 1.6f), 0.05f);
                    if (Random.value < 0.25f) Emit(smoke, e.pos + fwd * 0.3f + Vector3.up * 0.1f, fwd * 1.2f + Vector3.up * 0.5f, 0.5f, 0.6f, new Color(0.6f, 0.58f, 0.56f, 0.28f), Random.Range(0f, 360f));
                    break;
                case GameWorld.ProjKnife:
                    for (int i = 0; i < 3; i++)
                        Emit(sparks, e.pos, Random.insideUnitSphere * 1.6f, 0.05f, 0.14f, new Color(1f, 0.95f, 0.85f));
                    break;
                case GameWorld.ProjFrag:
                case GameWorld.ProjSmoke:
                case GameWorld.ProjFlash:
                    break;
                case GameWorld.ProjLob:
                    // The Mech's mortar, a man's size.
                    MortarSlam(e.pos, fwd, ThumperScale);
                    break;
                case GameWorld.ProjSniper:
                    // A sharp white crack, a puff that hangs at the muzzle and a line of the round's
                    // heat across the air (the Mech's railgun, thin and brief).
                    AddFlare(e.pos, fwd, 2.4f, 0.6f, new Color(4.5f, 4.2f, 3.6f), 0.06f);
                    Emit(trail, e.pos + fwd * 0.2f, fwd * 1.2f + Vector3.up * 0.2f, 0.8f, 1.6f, new Color(0.82f, 0.8f, 0.78f, 0.4f), Random.Range(0f, 360f));
                    Flash(e.pos, new Color(1f, 0.9f, 0.7f), 4f, 9f, 0.07f);
                    if (e.end.sqrMagnitude > 0f)
                    {
                        beams.Add(new Beam { from = e.pos, to = e.end, born = Time.time, life = 0.1f, width = 0.14f, core = new Color(4.6f, 4.3f, 3.7f), halo = new Color(0.5f, 0.45f, 0.35f) });
                        beams.Add(new Beam { from = e.pos, to = e.end, born = Time.time, life = 0.7f, width = 0.05f, core = new Color(0.5f, 0.52f, 0.58f), halo = Color.clear, trail = true });
                    }
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
                {
                    // A rocket leaving the tube: a flash forward, the backblast thrown out behind
                    // in flame and smoke, and the dust it kicks off the ground.
                    AddFlare(e.pos, fwd, 2.4f, 1.1f, new Color(4.2f, 2.7f, 1.0f), 0.1f);
                    AddFlare(e.pos, fwd, 1.1f, 0.7f, new Color(5f, 4.3f, 3f), 0.06f);
                    AddFlare(e.pos - fwd * 1.6f, -fwd, 3.6f, 1.8f, new Color(3.6f, 2.0f, 0.7f), 0.13f);
                    Emit(glow, e.pos - fwd * 1.2f, -fwd * 2f, 3.2f, 0.14f, new Color(1f, 0.6f, 0.28f));
                    for (int i = 0; i < 6; i++)
                        Emit(trail, e.pos - fwd * Random.Range(1.4f, 2.8f), -fwd * Random.Range(3f, 9f) + Random.insideUnitSphere * 1.4f,
                             Random.Range(1.4f, 2.2f), Random.Range(0.9f, 1.4f), new Color(0.8f, 0.77f, 0.73f, 0.55f), Random.Range(0f, 360f));
                    Vector3 behind = world.Map.Ground(new Vector2(e.pos.x - fwd.x * 2.4f, e.pos.z - fwd.z * 2.4f));
                    for (int i = 0; i < 3; i++)
                        Emit(trail, behind + Random.insideUnitSphere * 0.6f + Vector3.up * 0.3f, Random.insideUnitSphere * 1.6f + Vector3.up * 0.8f - fwd * 2f,
                             Random.Range(1.3f, 2.1f), Random.Range(1.0f, 1.6f), new Color(0.58f, 0.51f, 0.42f, 0.4f), Random.Range(0f, 360f));
                    Flash(e.pos, new Color(1f, 0.65f, 0.3f), 7f, 13f, 0.12f);
                    PressureWave(e.pos - fwd, 0.22f, 14f, -fwd, 0.7f);
                    ShakeFrom(e.pos, 0.03f);
                    break;
                }
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
                case GameWorld.ProjFrag:
                    Explosion(e.pos, 0.95f, false, pressure: 0.55f);
                    for (int i = 0; i < 12; i++)
                        Emit(sparks, e.pos + Vector3.up * 0.3f, Random.insideUnitSphere * 11f + Vector3.up * 2f, 0.1f, Random.Range(0.15f, 0.35f), new Color(1f, 0.8f, 0.5f));
                    ShakeFrom(e.pos, 0.05f);
                    break;
                case GameWorld.ProjFlash:
                    // No fire and no smoke to speak of: a white crack that fills the eye.
                    Emit(glow, e.pos + Vector3.up * 0.8f, Vector3.zero, 16f, 0.16f, new Color(1f, 1f, 0.97f));
                    Emit(glow, e.pos + Vector3.up * 0.8f, Vector3.zero, 7f, 0.32f, new Color(0.85f, 0.9f, 1f));
                    Flash(e.pos + Vector3.up, new Color(0.95f, 0.97f, 1f), 9f, 16f, 0.14f);
                    Emit(smoke, e.pos + Vector3.up * 0.5f, Vector3.up * 0.6f, 1.1f, 1.1f, new Color(0.6f, 0.6f, 0.58f, 0.3f), Random.Range(0f, 360f));
                    break;
                case GameWorld.ProjSmoke:
                    // The can's pop; the cloud itself is SmokeScreens.
                    Emit(smoke, e.pos + Vector3.up * 0.6f, Vector3.up * 1.2f, 1.6f, 1.2f, new Color(0.72f, 0.72f, 0.7f, 0.5f), Random.Range(0f, 360f));
                    break;
                case GameWorld.ProjLob:
                    Explosion(e.pos, 2.2f * ThumperScale * 1.15f, false, pressure: 1.3f * ThumperScale);
                    ShakeFrom(e.pos, 0.2f * ThumperScale);
                    break;
                case GameWorld.ProjRocket:
                    Explosion(e.pos, 1.3f, false, pressure: 0.85f);
                    for (int i = 0; i < 10; i++)
                        Emit(sparks, e.pos, Random.insideUnitSphere * 10f + Vector3.up * 4f, 0.16f, Random.Range(0.25f, 0.5f), new Color(1f, 0.72f, 0.35f));
                    ShakeFrom(e.pos, 0.07f);
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

        /// <summary>The Thumper's mortar against the Mech's (MortarSlam, MechImpact): what is left of it
        /// once it is a man's size.</summary>
        const float ThumperScale = 0.62f;

        /// <summary>A Scorcher firing: its gun ticks every 0.3 s, and one tick's worth of tongues left a
        /// few sparse flames pulsing three times a second, where the Mech's ticks every 0.1 s. So a tick
        /// holds the jet open and <see cref="FlameJets"/> feeds it every frame.</summary>
        struct FlameJet
        {
            public Unit unit;
            public Vector3 from, to;
            public float until, tongues, billows, core, licks, embers;
        }

        readonly List<FlameJet> flameJets = new List<FlameJet>(16);

        void RosterFlame(GameEvent e)
        {
            float hold = e.unit != null ? Mathf.Max(0.2f, e.unit.def.cooldown * 1.3f) : 0.35f;
            for (int i = 0; i < flameJets.Count; i++)
            {
                if (flameJets[i].unit != e.unit) continue;
                var j = flameJets[i];
                j.from = e.pos;
                j.to = e.end;
                j.until = Time.time + hold;
                flameJets[i] = j;
                return;
            }
            flameJets.Add(new FlameJet { unit = e.unit, from = e.pos, to = e.end, until = Time.time + hold });
        }

        /// <summary>The clouds the world has, as it has them: what blocks sight is what is drawn. Billows
        /// lifted off the ground (a puff born on geometry fades out), a rate per second capped by the 0.1 s step.</summary>
        void SmokeScreens(float dt)
        {
            if (!world.Tactical) return;
            foreach (var c in world.SmokeClouds)
            {
                Vector3 foot = new Vector3(c.at.x, world.Map.HeightAt(c.at), c.at.y);
                // A cloud hides its own middle from whoever threw it: it is drawn if any of it is in view.
                if (!CloudInView(foot)) continue;
                float age = world.time - c.born, left = c.until - world.time;
                float grow = Mathf.Clamp01(age / 2f);
                // It thins for its last few seconds, and the particles already up outlive it by theirs.
                float rate = left < 4f ? 3f : age < 2f ? 34f : 15f;
                float want = rate * Mathf.Min(dt, 0.1f);
                int n = (int)want + (Random.value < want - (int)want ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    Vector2 disk = Random.insideUnitCircle * (StarForge.Sim.TacticalRules.SmokeRadius * 0.85f * grow + 1f);
                    Vector3 p = foot + new Vector3(disk.x, Random.Range(1f, 4.5f), disk.y);
                    Vector3 vel = Vector3.up * Random.Range(0.1f, 0.4f) + Random.insideUnitSphere * 0.25f;
                    float g = Random.Range(0.7f, 0.86f);
                    Emit(smoke, p, vel, Random.Range(5f, 8f), Random.Range(3.5f, 5f), new Color(g, g, g * 0.98f, 0.85f), Random.Range(0f, 360f));
                }
            }
        }

        bool CloudInView(Vector3 foot)
        {
            if (Seen(foot)) return true;
            float r = StarForge.Sim.TacticalRules.SmokeRadius + 3f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                if (Seen(foot + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r))) return true;
            }
            return false;
        }

        void FlameJets(float dt)
        {
            for (int i = flameJets.Count - 1; i >= 0; i--)
            {
                var j = flameJets[i];
                if (Time.time > j.until || (j.unit != null && !Unit.Live(j.unit))) { flameJets.RemoveAt(i); continue; }
                if (Seen(j.from) || Seen(j.to)) FeedJet(ref j, dt);
                flameJets[i] = j;
            }
        }

        /// <summary>The Mech flamer's look, a man's size and running every frame: tongues thrown along the
        /// line, thin at the nozzle and swelling toward the target, rolling fire down its length, a hot core,
        /// and flames, embers and smoke standing up off whatever it is on.</summary>
        void FeedJet(ref FlameJet j, float dt)
        {
            Vector3 d = j.to - j.from;
            float len = d.magnitude;
            if (len < 0.1f) return;
            Vector3 dir = d / len;
            float v = Mathf.Clamp(len * 1.7f, 6f, 12f);
            dt = Mathf.Min(dt, 0.1f);    // a long frame's worth, no more

            for (j.tongues += dt * 56f; j.tongues >= 1f; j.tongues -= 1f)
            {
                float u = Random.value;
                float w = Mathf.Lerp(1.0f, 2.6f, u) * Random.Range(0.85f, 1.15f);
                EmitFlame(flames, j.from + dir * (len * u * 0.72f) + Random.insideUnitSphere * (0.05f + 0.22f * u) + Vector3.up * (0.25f * w),
                          dir * v * Random.Range(0.85f, 1.05f) + Random.insideUnitSphere * (0.5f + 1.3f * u) + Vector3.up * 0.7f,
                          w, w * 1.3f, Mathf.Clamp(len * (1f - u * 0.72f) / v, 0.26f, 0.5f) * Random.Range(0.9f, 1.15f), Color.white);
            }
            for (j.billows += dt * 16f; j.billows >= 1f; j.billows -= 1f)
            {
                float u = Random.Range(0.05f, 0.8f);
                Emit(fire, j.from + dir * (len * u) - Vector3.up * 0.3f, dir * v * 0.45f + Random.insideUnitSphere * 0.7f,
                     Mathf.Lerp(0.6f, 1.35f, u) * Random.Range(0.9f, 1.1f), Random.Range(0.32f, 0.46f), new Color(1f, 0.8f, 0.6f), Random.Range(0f, 360f));
            }
            for (j.core += dt * 26f; j.core >= 1f; j.core -= 1f)
                beams.Add(new Beam { from = j.from, to = j.from + dir * (len * Random.Range(0.45f, 0.85f)), born = Time.time, life = 0.05f,
                                     width = Random.Range(0.16f, 0.3f), core = new Color(3.4f, 1.9f, 0.6f), halo = new Color(0.9f, 0.34f, 0.08f) });
            for (j.licks += dt * 15f; j.licks >= 1f; j.licks -= 1f)
                EmitFlame(flames, j.to - dir * 0.6f + Random.insideUnitSphere * 0.55f + Vector3.up * 0.2f, Vector3.up * Random.Range(0.8f, 2f) + dir * 0.5f,
                          1.1f, 1.9f, Random.Range(0.35f, 0.55f), Color.white);
            for (j.embers += dt * 12f; j.embers >= 1f; j.embers -= 1f)
                Emit(sparks, j.to + Random.insideUnitSphere * 0.5f, Random.insideUnitSphere * 2.5f + Vector3.up * 3f + dir * 2f, 0.1f, Random.Range(0.3f, 0.6f), new Color(1f, 0.6f, 0.25f));
            if (Random.value < dt * 12f) Emit(glow, j.from, dir * 1.5f, 1.6f, 0.1f, new Color(1f, 0.55f, 0.2f));
            if (Random.value < dt * 2.5f)
                Emit(smoke, j.to + Vector3.up * 0.9f, Vector3.up * Random.Range(1.2f, 2.2f), Random.Range(1.1f, 1.7f), Random.Range(1.8f, 2.6f),
                     new Color(0.24f, 0.22f, 0.2f, 0.45f), Random.Range(0f, 360f));
            if (Random.value < dt * 5f) Flash(Vector3.Lerp(j.from, j.to, 0.5f), new Color(1f, 0.55f, 0.2f), 4f, 9f, 0.1f);
            // Few: scars share one list of 96 with the shells', and a line of Scorchers would turn it over.
            if (Random.value < dt * 0.2f) GroundScar(j.to, 0.55f);
        }

        /// <summary>An animal's teeth in one of a cheater's (GameWorld.Cheat): the Skitter's rake and a burst of grit.</summary>
        void AnimalBite(GameEvent e)
        {
            Slash(e.pos, e.dir, false);
            for (int i = 0; i < 8; i++)
                Emit(sparks, e.pos, Random.insideUnitSphere * 4f + Vector3.up * 1.5f, 0.11f, Random.Range(0.12f, 0.25f), new Color(1f, 0.35f, 0.25f));
            Emit(glow, e.pos, Vector3.zero, 1.8f, 0.1f, new Color(1f, 0.25f, 0.15f));
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
