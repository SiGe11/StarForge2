// FXDirector.Mech.cs — what the Mechs and the Mech Bay look like in action.
//
//   guns       each weapon its own muzzle: the autocannon's flash and puff, the
//              rotary cannon's stream of tracers, the missile rack's smoke-wreathed
//              launches and the corkscrewing trails the missiles leave, the mortar's
//              heavy slam, the laser's white-hot lance, the railgun's ionised line
//              that hangs in the air, the flamer's rolling jet of burning gel
//   impacts    missiles and mortar bombs burst as explosions sized to them; the
//              railgun's slug throws up a spray of rock
//   the drop   a beacon lights at the spot, then the Mech comes in hot from orbit on
//              retro-rockets, a streak of fire above it, and lands in a burst of dust,
//              flattening the grass for fifty metres
//   walking    every footfall kicks up dust (or water), leaves a footprint and sends a
//              ripple through the grass; the camera feels the heavy ones
//   the rest   engine haze from the stacks, a grav skirt's downwash, the shield
//              flaring where it is hit, welding arcs while the bay repairs it, smoke and
//              then fire from a badly hurt one, and a wreck that burns for half a minute
using System.Collections.Generic;
using UnityEngine;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed partial class FXDirector
    {
        struct Beam { public Vector3 from, to; public float born, life, width; public Color core, halo; public bool trail; }
        readonly List<Beam> beams = new List<Beam>(64);
        static readonly string[] Stacks = { "StackR", "StackL" }, Jets = { "JetFR", "JetFL", "JetBR", "JetBL" };
        struct Beacon { public Vector3 at; public float born, until; public int team; }
        readonly List<Beacon> beacons = new List<Beacon>(2);
        readonly Dictionary<Projectile, Vector3> missileTrail = new Dictionary<Projectile, Vector3>();
        readonly List<Projectile> missileGone = new List<Projectile>();
        readonly Dictionary<int, float> shieldSeen = new Dictionary<int, float>();
        float mechPuffT, repairT;
        bool mechHooked;

        void HookMechs()
        {
            if (mechHooked) return;
            mechHooked = true;
            MechView.Footfall += OnFootfall;
        }

        void UnhookMechs()
        {
            if (!mechHooked) return;
            mechHooked = false;
            MechView.Footfall -= OnFootfall;
        }

        // ------------------------------------------------------------ guns
        /// <summary>A Mech's projectile gun fired (autocannon, missile rack, mortar).</summary>
        void MechFire(GameEvent e)
        {
            Vector3 fwd = e.dir.sqrMagnitude > 1e-4f ? e.dir.normalized : Vector3.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            switch (e.projectileKind)
            {
                case 4:   // autocannon: a hard orange flash, a puff, a spent case
                    AddFlare(e.pos, fwd, 2.8f, 1.4f, new Color(3.8f, 2.2f, 0.8f), 0.07f);
                    AddFlare(e.pos, fwd, 1.2f, 0.9f, new Color(5f, 4.2f, 3f), 0.05f);
                    Emit(glow, e.pos, fwd, 2.4f, 0.08f, new Color(1f, 0.7f, 0.35f));
                    Emit(trail, e.pos + fwd * 0.4f, fwd * Random.Range(2f, 5f) + Vector3.up * 0.5f, Random.Range(0.9f, 1.4f), Random.Range(0.9f, 1.4f),
                         new Color(0.78f, 0.75f, 0.71f, 0.35f), Random.Range(0f, 360f));
                    for (int i = 0; i < 4; i++)
                        Emit(sparks, e.pos, fwd * Random.Range(10f, 18f) + Random.insideUnitSphere * 3f, 0.12f, Random.Range(0.1f, 0.2f), new Color(1f, 0.75f, 0.4f));
                    Flash(e.pos, new Color(1f, 0.65f, 0.3f), 5f, 10f, 0.1f);
                    PressureWave(e.pos, 0.18f, 14f, fwd, 0.7f);
                    break;
                case 5:   // missile: a burst of flame and smoke out of the rack
                    AddFlare(e.pos, Vector3.up, 2.2f, 1.2f, new Color(3.5f, 2.0f, 0.8f), 0.08f);
                    Emit(glow, e.pos + Vector3.up * 0.4f, Vector3.up * 2f, 2.2f, 0.12f, new Color(1f, 0.6f, 0.3f));
                    for (int i = 0; i < 3; i++)
                        Emit(trail, e.pos + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 2.2f + Vector3.up * Random.Range(1f, 3f),
                             Random.Range(1.2f, 2.0f), Random.Range(1.6f, 2.6f), new Color(0.82f, 0.8f, 0.77f, 0.45f), Random.Range(0f, 360f));
                    break;
                case 6:   // mortar: a heavy slam, a ring of smoke round the mount, the ground shoved
                    AddFlare(e.pos, fwd, 4.2f, 2.8f, new Color(3.8f, 2.1f, 0.7f), 0.12f);
                    AddFlare(e.pos, fwd, 2.0f, 1.8f, new Color(5f, 4.3f, 3f), 0.08f);
                    Emit(glow, e.pos + fwd, fwd * 2f, 5.5f, 0.18f, new Color(1f, 0.6f, 0.28f));
                    for (int i = 0; i < 8; i++)
                        Emit(trail, e.pos + fwd * Random.Range(0.3f, 1.5f), fwd * Random.Range(3f, 9f) + Random.insideUnitSphere * 1.5f,
                             Random.Range(2.2f, 3.4f), Random.Range(2.2f, 3.4f), new Color(0.8f, 0.77f, 0.73f, 0.55f), Random.Range(0f, 360f));
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i / 10f * Mathf.PI * 2f;
                        var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        Emit(trail, e.pos - Vector3.up * 0.4f + d * 0.5f, d * Random.Range(2.5f, 4.5f), Random.Range(1.4f, 2.2f), Random.Range(1.2f, 1.8f),
                             new Color(0.8f, 0.77f, 0.73f, 0.35f), Random.Range(0f, 360f));
                    }
                    Flash(e.pos, new Color(1f, 0.6f, 0.28f), 12f, 20f, 0.2f);
                    PressureWave(e.pos, 0.6f, 30f, Vector3.zero, 0f);
                    ShakeFrom(e.pos, 0.12f);
                    break;
            }
        }

        /// <summary>A hitscan shot from a Mech: the beam, tracer or jet from muzzle to mark.</summary>
        void MechBeam(GameEvent e)
        {
            Vector3 from = e.pos, to = e.end;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.1f) return;
            Vector3 dir = d / len;
            Color team = TeamColor(e.team);
            switch ((MechWeapon)e.projectileKind)
            {
                case MechWeapon.Gatling:
                    // One tracer in three shows; the muzzle flickers every round.
                    if (Random.value < 0.4f)
                        beams.Add(new Beam { from = from + dir * Random.Range(0f, 3f), to = Vector3.Lerp(from, to, Random.Range(0.55f, 1f)), born = Time.time, life = 0.05f, width = 0.10f,
                                             core = new Color(3.2f, 2.3f, 1.1f), halo = Color.clear });
                    AddFlare(from, dir, Random.Range(0.8f, 1.3f), 0.55f, new Color(4f, 2.8f, 1.2f), 0.035f);
                    if (Random.value < 0.25f) Emit(glow, from, dir, 1.3f, 0.05f, new Color(1f, 0.75f, 0.4f));
                    if (Random.value < 0.15f)
                        Emit(trail, from + dir * 0.3f, dir * 2f + Vector3.up * 0.6f, 0.6f, 0.8f, new Color(0.8f, 0.78f, 0.74f, 0.18f), Random.Range(0f, 360f));
                    break;
                case MechWeapon.Laser:
                {
                    Color core = Color.Lerp(new Color(3.2f, 4.6f, 6f), team * 5f, 0.25f);
                    beams.Add(new Beam { from = from, to = to, born = Time.time, life = 0.22f, width = 0.34f, core = core, halo = team * 0.9f + new Color(0.2f, 0.3f, 0.5f) });
                    AddFlare(from, dir, 1.6f, 1.2f, core, 0.12f);
                    Emit(glow, from, Vector3.zero, 2.2f, 0.15f, Color.Lerp(new Color(0.7f, 0.85f, 1f), team, 0.3f));
                    Emit(glow, to, Vector3.zero, 3f, 0.2f, new Color(1f, 0.85f, 0.6f));
                    for (int i = 0; i < 10; i++)
                        Emit(sparks, to, Random.insideUnitSphere * 7f + Vector3.up * 3f - dir * 3f, 0.16f, Random.Range(0.2f, 0.45f), new Color(1f, 0.8f, 0.45f));
                    Emit(smoke, to + Vector3.up * 0.3f, Vector3.up * 1.2f, 1.2f, 1.2f, new Color(0.45f, 0.42f, 0.4f, 0.35f), Random.Range(0f, 360f));
                    Flash(to, new Color(0.7f, 0.85f, 1f), 8f, 12f, 0.18f);
                    GroundScar(to, 0.9f);
                    break;
                }
                case MechWeapon.Railgun:
                {
                    Color core = new Color(4.5f, 5.5f, 7f);
                    beams.Add(new Beam { from = from, to = to, born = Time.time, life = 0.35f, width = 0.5f, core = core, halo = new Color(0.4f, 0.6f, 1.2f) });
                    beams.Add(new Beam { from = from, to = to, born = Time.time, life = 1.6f, width = 0.18f, core = new Color(0.5f, 0.7f, 1.3f) * 0.8f, halo = Color.clear, trail = true });
                    AddFlare(from, dir, 5.5f, 2.8f, new Color(3.4f, 4.4f, 6f), 0.14f);
                    AddFlare(from, -dir, 2.4f, 1.6f, new Color(2f, 2.4f, 3f), 0.1f);
                    Emit(glow, from, dir * 3f, 6f, 0.2f, new Color(0.7f, 0.85f, 1f));
                    // Ionised air along the line, hanging and drifting.
                    int n = Mathf.Min(24, Mathf.RoundToInt(len / 2.2f));
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = Vector3.Lerp(from, to, (i + Random.value) / n);
                        Emit(trail, p, Random.insideUnitSphere * 0.3f + Vector3.up * 0.15f, Random.Range(0.6f, 1.0f), Random.Range(1.2f, 2.2f),
                             new Color(0.86f, 0.88f, 0.92f, 0.28f), Random.Range(0f, 360f));
                    }
                    Flash(from, new Color(0.6f, 0.8f, 1f), 14f, 22f, 0.22f);
                    PressureWave(from, 0.5f, 24f, dir, 0.8f);
                    ShakeFrom(from, 0.14f);
                    break;
                }
                case MechWeapon.Flamer:
                case MechWeapon.FlameTower:
                {
                    // A rolling jet: tongues of flame thrown along the line, spreading and
                    // rising, with a glow at the nozzle and smoke off the far end.
                    Vector3 v = dir * Mathf.Clamp(len * 1.9f, 8f, 22f);
                    for (int i = 0; i < 3; i++)
                    {
                        float w = Random.Range(0.9f, 1.4f);
                        EmitFlame(flames, from + dir * Random.Range(0.2f, 1.2f) + Random.insideUnitSphere * 0.15f,
                                  v * Random.Range(0.8f, 1.05f) + Random.insideUnitSphere * 1.6f + Vector3.up * 0.8f, w, w * 1.3f, Random.Range(0.45f, 0.6f), Color.white);
                    }
                    Emit(fire, from + dir * Random.Range(1f, 3f), v * 0.7f + Random.insideUnitSphere, Random.Range(1.2f, 2f), Random.Range(0.4f, 0.55f),
                         new Color(1f, 0.8f, 0.6f), Random.Range(0f, 360f));
                    if (Random.value < 0.5f)
                        EmitFlame(flames, to + Random.insideUnitSphere * 1.2f + Vector3.up * 0.4f, Vector3.up * Random.Range(1f, 2f), 1.4f, 2.4f, Random.Range(0.5f, 0.8f), Color.white);
                    Emit(glow, from, dir * 2f, 2.2f, 0.1f, new Color(1f, 0.55f, 0.2f));
                    if (Random.value < 0.35f)
                        Emit(smoke, to + Vector3.up * 1.2f, Vector3.up * Random.Range(1.5f, 2.5f), Random.Range(1.6f, 2.4f), Random.Range(2f, 3f),
                             new Color(0.24f, 0.22f, 0.2f, 0.45f), Random.Range(0f, 360f));
                    if (Random.value < 0.3f) Flash(Vector3.Lerp(from, to, 0.5f), new Color(1f, 0.55f, 0.2f), 6f, 12f, 0.12f);
                    break;
                }
            }
        }

        void MechImpact(GameEvent e)
        {
            switch (e.projectileKind)
            {
                case 4:
                    ImpactSparks(e.pos, e.team, 1);
                    for (int i = 0; i < 3; i++)
                        Emit(trail, e.pos + Vector3.up * 0.3f, Random.insideUnitSphere * 1.2f + Vector3.up * 1.2f, Random.Range(0.8f, 1.3f), Random.Range(0.8f, 1.2f),
                             new Color(0.55f, 0.5f, 0.42f, 0.4f), Random.Range(0f, 360f));
                    Emit(glow, e.pos, Vector3.zero, 2.2f, 0.1f, new Color(1f, 0.7f, 0.35f));
                    break;
                case 5:
                    Explosion(e.pos, 0.85f, false, pressure: 0.55f);
                    break;
                case 6:
                    Explosion(e.pos, 2.2f, false, pressure: 1.3f);
                    ShakeFrom(e.pos, 0.2f);
                    break;
                case 7:
                    // The railgun's slug going into the ground: rock and dust flung up the line it came in on.
                    Explosion(e.pos, 0.7f, false, pressure: 0.6f);
                    for (int i = 0; i < 10; i++)
                        Emit(sparks, e.pos, (-e.dir + Random.insideUnitSphere * 0.7f + Vector3.up) * Random.Range(6f, 14f), 0.2f, Random.Range(0.3f, 0.6f), new Color(0.8f, 0.9f, 1f));
                    break;
            }
        }

        void GroundScar(Vector3 at, float size)
        {
            float gy = world.Map.HeightAt(new Vector2(at.x, at.z));
            if (at.y - gy > 0.8f) return;
            if (scorchList.Count >= 96) scorchList.RemoveAt(0);
            scorchList.Add(new Scorch { pos = new Vector3(at.x, gy, at.z), rot = Random.Range(0f, 6.283f), size = size, born = Time.time, frame = Random.Range(0, 4) });
        }

        void ShakeFrom(Vector3 at, float amount)
        {
            if (rig == null || rig.cam == null) return;
            rig.Shake(Mathf.Clamp01(1.2f - Vector3.Distance(rig.cam.transform.position, at) / 140f) * amount);
        }

        // ------------------------------------------------------------ the drop
        void MechInbound(GameEvent e)
        {
            beacons.Add(new Beacon { at = e.pos, born = Time.time, until = Time.time + GameWorld.MechInboundLead + 1.5f, team = e.team });
        }

        void MechLanded(GameEvent e)
        {
            Vector3 at = e.pos;
            // A ground burst without fire: the dust thrown out in a ring, clods, a flash
            // of the retro-rockets' last push, and the air rolling out over the field.
            Flash(at + Vector3.up * 3f, new Color(1f, 0.7f, 0.4f), 16f, 28f, 0.35f);
            Emit(glow, at + Vector3.up * 1.5f, Vector3.zero, 12f, 0.25f, new Color(1f, 0.7f, 0.4f));
            int ring = 30;
            for (int i = 0; i < ring; i++)
            {
                float a = (i + Random.value * 0.5f) / ring * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Emit(trail, at + d * 2.5f + Vector3.up * 0.5f, d * Random.Range(9f, 16f) + Vector3.up * Random.Range(0.4f, 1.6f),
                     Random.Range(3.4f, 5f), Random.Range(2.2f, 3.4f), new Color(0.62f, 0.55f, 0.46f, 0.55f), Random.Range(0f, 360f));
            }
            for (int i = 0; i < 10; i++)
                Emit(trail, at + Random.insideUnitSphere * 2f + Vector3.up, Vector3.up * Random.Range(4f, 9f) + Random.insideUnitSphere * 2f,
                     Random.Range(3f, 4.5f), Random.Range(2.5f, 4f), new Color(0.58f, 0.52f, 0.44f, 0.5f), Random.Range(0f, 360f));
            if (debris != null)
                for (int i = 0; i < 40; i++)
                {
                    Vector3 v = Random.insideUnitSphere * 9f;
                    v.y = Random.Range(5f, 13f);
                    float g = Random.Range(0.55f, 0.9f);
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = at + Random.insideUnitSphere * 2f + Vector3.up * 0.3f, velocity = v,
                        startSize = Random.Range(0.08f, 0.22f), startLifetime = Random.Range(2f, 3.5f),
                        startColor = new Color(g * 1.1f, g * 0.95f, g * 0.75f),
                        rotation3D = Random.insideUnitSphere * 180f, applyShapeToPosition = false
                    }, 1);
                }
            for (int i = 0; i < 16; i++)
                Emit(sparks, at + Vector3.up, Random.insideUnitSphere * 10f + Vector3.up * 5f, 0.25f, Random.Range(0.3f, 0.7f), new Color(1f, 0.7f, 0.35f));
            GroundScar(at, 5.5f);
            PressureWave(at, 2.2f, 60f, Vector3.zero, 0f);
            ShakeFrom(at, 0.55f);
            if (world.Map.WaterDepth(new Vector2(at.x, at.z)) > 0.1f) WaterBurst(at, 2f);
            for (int i = beacons.Count - 1; i >= 0; i--) if (beacons[i].team == e.team) beacons.RemoveAt(i);
        }

        void MechDeath(GameEvent e)
        {
            // The reactor: the biggest thing that happens on the field short of a
            // structure going up, and a chain of blasts as the ammunition cooks off.
            Explosion(e.pos, 3.0f, true, pressure: 1.6f);
            Flash(e.pos + Vector3.up * 3f, new Color(0.7f, 0.85f, 1f), 22f, 36f, 0.5f);
            Emit(glow, e.pos + Vector3.up * 3f, Vector3.zero, 18f, 0.3f, new Color(0.75f, 0.88f, 1f));
            for (int i = 0; i < 5; i++)
            {
                Vector3 off = Random.insideUnitSphere * 3.5f;
                off.y = Mathf.Abs(off.y) + 1f;
                blasts.Add(new Blast { t = Time.time + Random.Range(0.3f, 2.6f), pos = e.pos + off, scale = Random.Range(0.8f, 1.5f) });
            }
            ShakeFrom(e.pos, 0.7f);
        }

        // ------------------------------------------------------------ footfalls
        void OnFootfall(Unit u, Vector3 at, float weight)
        {
            if (u == null || !(u.visibleToPlayer || u.team == (player != null ? player.team : 0) || MatchSettings.spectate)) return;
            if (rig != null && new Vector2(at.x - rig.Focus.x, at.z - rig.Focus.y).sqrMagnitude > 130f * 130f) return;
            bool wet = world.Map.WaterDepth(new Vector2(at.x, at.z)) > 0.05f;
            if (wet)
            {
                AddRipple(new Vector3(at.x, world.Map.waterLevel + 0.03f, at.z), 4.5f * weight, 1.6f, 0);
                Splash(new Vector3(at.x, world.Map.waterLevel, at.z), 1.2f * weight, Vector3.zero);
            }
            else
            {
                for (int i = 0; i < 7; i++)
                {
                    float a = (i + Random.value * 0.5f) / 7f * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Emit(trail, at + d * 0.6f + Vector3.up * 0.2f, d * Random.Range(1.5f, 3.2f) * weight + Vector3.up * Random.Range(0.2f, 0.7f),
                         Random.Range(1.2f, 1.9f) * (0.6f + weight * 0.5f), Random.Range(1.4f, 2.2f), new Color(0.66f, 0.6f, 0.5f, 0.32f * weight + 0.1f), Random.Range(0f, 360f));
                }
                if (debris != null && Random.value < 0.6f)
                    for (int i = 0; i < 4; i++)
                    {
                        float g = Random.Range(0.4f, 0.7f);
                        debris.Emit(new ParticleSystem.EmitParams
                        {
                            position = at + Vector3.up * 0.15f, velocity = Random.insideUnitSphere * 2.5f + Vector3.up * Random.Range(2f, 4f),
                            startSize = Random.Range(0.04f, 0.1f), startLifetime = Random.Range(1f, 1.8f),
                            startColor = new Color(g, g * 0.95f, g * 0.88f), rotation3D = Random.insideUnitSphere * 180f, applyShapeToPosition = false
                        }, 1);
                    }
                // A footprint, lasting as long as a tank's tracks do.
                float yaw = u.yaw * Mathf.Rad2Deg;
                trackMarks[trackHead] = new TrackMark
                {
                    // A solid, soft-edged pad: with the tread's cleats (pitch 2.5) a line of
                    // them read as the rungs of a ladder.
                    pos = new Vector3(at.x, world.Map.HeightAt(new Vector2(at.x, at.z)), at.z), yaw = yaw, length = 1.7f, width = 1.3f,
                    born = Time.time, band = 0f, halfBand = 0.85f, pitch = 0f
                };
                trackHead = (trackHead + 1) % MaxTracks;
                trackCount = Mathf.Min(trackCount + 1, MaxTracks);
            }
            PressureWave(at, 0.28f * weight, 12f, Vector3.zero, 0f);
            ShakeFrom(at, 0.05f * weight);
        }

        // ------------------------------------------------------------ every frame
        void MechEffects(float dt, Vector3 camFocus, Vector3 wind)
        {
            HookMechs();
            float now = Time.time;
            mechPuffT -= dt;
            repairT -= dt;
            bool puff = mechPuffT <= 0f;
            if (puff) mechPuffT = 0.14f;
            bool weld = repairT <= 0f;
            if (weld) repairT = 0.07f;

            foreach (var v in MechView.Active)
            {
                if (v == null || v.Unit == null || v.Core == null || v.Core.design == null) continue;
                var u = v.Unit;
                if (u.dying) continue;
                bool seen = u.team == (player != null ? player.team : 0) || u.visibleToPlayer || MatchSettings.spectate;
                if (!seen) continue;
                Vector3 pos = v.transform.position;
                if (new Vector2(pos.x - camFocus.x, pos.z - camFocus.z).sqrMagnitude > 170f * 170f && v.DropHeight <= 0f) continue;
                var core = v.Core;

                // Coming down: retro-rockets under it, fire streaming off it, smoke behind.
                if (v.DropHeight > 0.5f && v.Torso != null)
                {
                    Vector3 body = v.Torso.position;
                    float h = v.DropHeight;
                    float burn = Mathf.Clamp01(1f - h / 170f) * 0.6f + 0.4f;
                    // Four retro-rockets firing straight down: a white-hot jet each (the
                    // flame sprites stand upward from where they are born, so a downward jet
                    // is a streak), fire boiling off its end, a glow at the nozzle.
                    float flick = 0.8f + 0.4f * Random.value;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 nozzle = pos + Vector3.up * (h + 0.5f) + new Vector3((k & 1) == 0 ? 1.4f : -1.4f, 0f, (k & 2) == 0 ? 1.2f : -1.2f);
                        float jet = (3.5f + 3f * burn) * flick;
                        beams.Add(new Beam { from = nozzle, to = nozzle + Vector3.down * jet, born = now, life = 0.05f, width = 0.9f * burn,
                                             core = new Color(4.5f, 3.2f, 1.6f), halo = new Color(1.4f, 0.55f, 0.15f) });
                        if (Random.value < 0.6f)
                            Emit(fire, nozzle + Vector3.down * jet * Random.Range(0.6f, 1f), Vector3.down * Random.Range(6f, 12f) + Random.insideUnitSphere * 2f,
                                 Random.Range(1.2f, 2f) * burn, Random.Range(0.25f, 0.4f), new Color(1f, 0.8f, 0.6f), Random.Range(0f, 360f));
                        if (Random.value < 0.5f)
                            Emit(glow, nozzle, Vector3.down * 3f, 2.6f * burn, 0.07f, new Color(1f, 0.62f, 0.28f));
                    }
                    if (Random.value < dt * 30f)
                        Emit(trail, body + Random.insideUnitSphere * 1.5f, Vector3.up * Random.Range(4f, 12f) + Random.insideUnitSphere * 2f,
                             Random.Range(2.5f, 4f), Random.Range(2.5f, 4f), new Color(0.55f, 0.52f, 0.5f, 0.55f), Random.Range(0f, 360f));
                    if (Random.value < dt * 20f)
                        beams.Add(new Beam { from = body + Vector3.up * 3f, to = body + Vector3.up * Random.Range(18f, 40f), born = now, life = 0.1f, width = 2.2f,
                                             core = new Color(2.2f, 1.1f, 0.4f) * burn, halo = new Color(0.6f, 0.25f, 0.08f) });
                    // The downwash reaches the ground before it does.
                    if (h < 40f && Random.value < dt * 25f)
                    {
                        float a = Random.Range(0f, Mathf.PI * 2f);
                        var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        Vector3 g = world.Map.Ground(new Vector2(pos.x, pos.z));
                        Emit(trail, g + d * Random.Range(1f, 4f) + Vector3.up * 0.3f, d * Random.Range(5f, 12f) * (1f - h / 40f) + Vector3.up * 0.6f,
                             Random.Range(2.5f, 4f), Random.Range(1.5f, 2.5f), new Color(0.64f, 0.58f, 0.48f, 0.45f), Random.Range(0f, 360f));
                    }
                    continue;
                }

                // Engine haze from the stacks: a light shimmer, darker when it is hurt.
                float hp = u.hp / Mathf.Max(1f, u.MaxHp);
                if (puff && Random.value < 0.55f)
                {
                    float shade = Mathf.Lerp(0.42f, 0.2f, 1f - hp);
                    foreach (var stack in Stacks)
                    {
                        Vector3 s = v.FramePoint(stack, new Vector3(0.5f, 2.2f, -1.4f)) + Vector3.up * 0.5f;
                        Emit(trail, s + Random.insideUnitSphere * 0.1f, Vector3.up * Random.Range(2f, 3.2f) + wind * 0.7f - v.Velocity * 0.2f,
                             Random.Range(0.7f, 1.1f), Random.Range(1.6f, 2.4f), new Color(shade, shade * 0.97f, shade * 0.94f, 0.18f + 0.3f * (1f - hp)), Random.Range(0f, 360f));
                    }
                }
                // Hurt: a column of smoke from the reactor, fire at a quarter.
                if (hp < 0.5f && Random.value < dt * (4f + 10f * (0.5f - hp)))
                {
                    Vector3 r = v.FramePoint("Reactor", new Vector3(0f, 1.5f, -1.4f));
                    Emit(smoke, r + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(2f, 3f) + wind, Random.Range(1.6f, 2.4f), Random.Range(3f, 4.5f),
                         new Color(0.24f, 0.23f, 0.22f, 0.5f), Random.Range(0f, 360f));
                    if (hp < 0.25f)
                        EmitFlame(flames, r + Vector3.up * 0.4f + Random.insideUnitSphere * 0.3f, Vector3.up * 1.2f + wind * 0.3f, 0.8f, 1.4f, Random.Range(0.35f, 0.55f), Color.white);
                }
                // A grav skirt's downwash and its jets.
                if (v.Locomotion == MechLocomotion.Hover)
                {
                    if (Random.value < dt * 10f)
                    {
                        Vector3 j = v.LocoPoint(Jets[Random.Range(0, 4)], new Vector3(1.7f, 0.5f, 1.5f));
                        Vector3 g = world.Map.Ground(new Vector2(j.x, j.z));
                        bool overWater = world.Map.WaterDepth(new Vector2(j.x, j.z)) > 0.05f;
                        Vector3 d = new Vector3(j.x - pos.x, 0f, j.z - pos.z).normalized;
                        Emit(trail, new Vector3(j.x, Mathf.Max(g.y, world.Map.waterLevel) + 0.2f, j.z), d * Random.Range(2f, 5f) + Vector3.up * 0.3f,
                             Random.Range(1.2f, 2f), Random.Range(0.9f, 1.5f), overWater ? new Color(0.86f, 0.92f, 0.94f, 0.3f) : new Color(0.66f, 0.6f, 0.5f, 0.28f), Random.Range(0f, 360f));
                        Emit(glow, j, Vector3.down * 2f, 1.4f, 0.1f, new Color(0.45f, 0.8f, 1f));
                    }
                }
                // Tracks: dust off the runs.
                if (v.Locomotion == MechLocomotion.Tracks && v.Velocity.sqrMagnitude > 1f && Random.value < dt * 14f && world.Map.WaterDepth(u.pos) < 0.1f)
                {
                    Vector3 fwd = v.transform.forward, right = v.transform.right;
                    float side = Random.value < 0.5f ? -1f : 1f;
                    Emit(trail, pos + right * 1.55f * side - fwd * 2.1f + Vector3.up * 0.2f, -fwd * Random.Range(0.5f, 1.8f) + Vector3.up * Random.Range(0.4f, 1.1f),
                         Random.Range(1.8f, 2.8f), Random.Range(1.8f, 2.8f), new Color(0.72f, 0.66f, 0.56f, 0.3f), Random.Range(0f, 360f));
                }

                // The shield flares where it is hit.
                shieldSeen.TryGetValue(u.id, out float lastHit);
                if (core.lastShieldHit > lastHit + 0.01f)
                {
                    shieldSeen[u.id] = core.lastShieldHit;
                    if (core.HasShield && now - lastHit > 0.08f)
                    {
                        Vector3 c = pos + Vector3.up * (u.def.visualHeight * 0.55f);
                        float strength = core.ShieldMax > 0f ? core.shield / core.ShieldMax : 0f;
                        Emit(glow, c, Vector3.zero, 8.5f, 0.16f, new Color(0.3f, 0.65f, 1f) * (0.35f + 0.5f * strength));
                        Emit(glow, c + Random.onUnitSphere * 3.2f, Vector3.zero, 2.4f, 0.12f, new Color(0.55f, 0.85f, 1f));
                    }
                }

                // At the bay: welding arcs from the gantry's arms and sparks off the hull.
                if (core.repairing && weld)
                {
                    var bay = world.BayOf(u.team);
                    if (bay != null)
                    {
                        Vector3 target = pos + Vector3.up * Random.Range(2f, u.def.visualHeight * 0.8f) + Random.insideUnitSphere * 1.2f;
                        Vector3 arm = bay.transform.TransformPoint(MechKit.Point("MECH_BAY", Random.value < 0.5f ? "ArmR" : "ArmL", new Vector3(2f, 3.9f, 3f)));
                        Vector3 mid = Vector3.Lerp(arm, target, 0.5f) + Random.insideUnitSphere * 0.8f;
                        beams.Add(new Beam { from = arm, to = mid, born = now, life = 0.06f, width = 0.12f, core = new Color(2.4f, 3.6f, 5f), halo = Color.clear });
                        beams.Add(new Beam { from = mid, to = target, born = now, life = 0.06f, width = 0.12f, core = new Color(2.4f, 3.6f, 5f), halo = Color.clear });
                        for (int i = 0; i < 3; i++)
                            Emit(sparks, target, Random.insideUnitSphere * 4f + Vector3.up * 2f, 0.12f, Random.Range(0.2f, 0.5f), new Color(0.8f, 0.9f, 1f));
                        Emit(glow, target, Vector3.zero, 1.6f, 0.08f, new Color(0.6f, 0.8f, 1f));
                    }
                }
            }

            // Missiles in flight: a bright motor and a twisting trail of smoke.
            missileGone.Clear();
            foreach (var kv in missileTrail) if (!kv.Key.alive || kv.Key.kind != 5) missileGone.Add(kv.Key);
            foreach (var p in missileGone) missileTrail.Remove(p);
            foreach (var p in world.projectiles)
            {
                if (p.kind != 5 || !Seen(p.pos)) continue;
                if (!missileTrail.TryGetValue(p, out var last)) { missileTrail[p] = p.pos; continue; }
                // A puff every 1.5 m, two a frame at most, gone in a second or so: a salvo is eight
                // missiles, and at a puff every 0.9 m lasting two seconds a Mech firing two racks
                // kept several hundred soft smoke puffs alive and cost the frame its refresh.
                float step = (p.pos - last).magnitude;
                if (step < 1.5f) continue;
                int n = Mathf.Min(2, Mathf.FloorToInt(step / 1.5f));
                for (int i = 1; i <= n; i++)
                {
                    Vector3 q = Vector3.Lerp(last, p.pos, i / (float)n);
                    Emit(trail, q + Random.insideUnitSphere * 0.1f, Random.insideUnitSphere * 0.3f + Vector3.up * 0.25f + wind * 0.4f,
                         Random.Range(0.55f, 0.85f), Random.Range(0.9f, 1.4f), new Color(0.84f, 0.82f, 0.8f, 0.46f), Random.Range(0f, 360f));
                }
                missileTrail[p] = p.pos;
            }

            // Wrecks burn for half a minute.
            foreach (var w in MechWreck.Active)
            {
                if (w == null) continue;
                float heat = w.Heat;
                if (heat <= 0.02f) continue;
                foreach (var fp in w.firePoints)
                {
                    if (fp == null) continue;
                    Vector3 at = fp.position;
                    if (!Seen(at) || new Vector2(at.x - camFocus.x, at.z - camFocus.z).sqrMagnitude > 160f * 160f) continue;
                    for (int n = Mathf.FloorToInt(heat * 14f * dt + Random.value); n > 0; n--)
                    {
                        var off = Random.insideUnitCircle * 1.4f;
                        float fw = Random.Range(1.1f, 1.8f) * Mathf.Lerp(0.5f, 1f, heat), fh = fw * Random.Range(1.5f, 2.2f);
                        EmitFlame(flames, at + new Vector3(off.x, fh * 0.3f, off.y), Vector3.up * Random.Range(0.8f, 1.6f) + wind * 0.3f, fw, fh, Random.Range(0.5f, 0.8f), Color.white);
                    }
                    if (Random.value < dt * (2f + heat * 5f))
                    {
                        float shade = Random.Range(0.24f, 0.32f);
                        Emit(smoke, at + Vector3.up * 1.4f, Vector3.up * Random.Range(2.2f, 3.4f) + wind, Random.Range(3f, 4.4f), Random.Range(4f, 6f),
                             new Color(shade, shade * 0.97f, shade * 0.93f, 0.5f + 0.3f * heat), Random.Range(0f, 360f));
                    }
                    if (Random.value < dt * heat * 4f)
                        Emit(embers, at + Vector3.up, Random.insideUnitSphere + Vector3.up * Random.Range(2f, 4f) + wind, Random.Range(0.08f, 0.16f), Random.Range(1.2f, 2f), new Color(1f, 0.6f, 0.25f));
                }
            }
        }

        /// <summary>Beams, and the drop beacons, drawn with the streaks each frame.</summary>
        void DrawMechOverlays(float now)
        {
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                var b = beams[i];
                float k = (now - b.born) / Mathf.Max(0.01f, b.life);
                if (k >= 1f) { beams.RemoveAt(i); continue; }
                Vector3 d = b.to - b.from;
                float len = d.magnitude;
                if (len < 0.05f || !Seen(b.to) && !Seen(b.from)) continue;
                Vector3 dir = d / len;
                float fade = b.trail ? (1f - k) * (1f - k) * 0.6f : 1f - k * k;
                float width = b.width * (b.trail ? 1f + k * 3f : 1f - 0.4f * k);
                AddStreak(b.to, dir, len, width, b.core * fade, 1f);
                if (b.halo.maxColorComponent > 0f) AddStreak(b.to, dir, len, width * 3.2f, b.halo * fade * 0.8f, 1f);
            }
            for (int i = beacons.Count - 1; i >= 0; i--)
            {
                var bc = beacons[i];
                if (now > bc.until) { beacons.RemoveAt(i); continue; }
                float pulse = 0.5f + 0.5f * Mathf.Sin((now - bc.born) * 9f);
                Color c = (bc.team == 0 ? PlayerColor : EnemyColor) * (1.2f + pulse * 1.5f);
                if (!Seen(bc.at) && bc.team != (player != null ? player.team : 0)) continue;
                rings.Add(Matrix4x4.TRS(bc.at, Quaternion.identity, new Vector3(9f, 4f, 9f)), c, new Vector4(4f, 0f, 0f, 0f));
                rings.Add(Matrix4x4.TRS(bc.at, Quaternion.identity, new Vector3(5f + pulse * 3f, 4f, 5f + pulse * 3f)), c * 0.7f, new Vector4(3f, pulse, 0f, 0f));
                AddStreak(bc.at + Vector3.up * 60f, Vector3.up, 60f, 0.45f + pulse * 0.3f, c * 0.8f, 1f);
            }
        }
    }
}
