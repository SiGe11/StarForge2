// IroncladTrials.cs -- checks of the Ironclad's planted-foot walk (View/IroncladLegs) and its wreck.
// Tools/editor.sh call StarForge.EditorTools.IroncladTrials.Walk, then
// Tools/editor.sh wait StarForge.EditorTools.IroncladTrials.WalkReport "wrecks" 900
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class IroncladTrials
    {
        /// <summary>A walking Ironclad's drawn feet, at 1x and 8x: straight, turning, stop and
        /// start, up and down a slope, standing. Targets: a planted foot's sole, toe and heel
        /// move under 3 cm from where it landed; 0 frames with a planted foot out of reach; 0
        /// knees behind the hip-ankle line; a swinging foot never moves more than 4x the body's
        /// step + 10 cm in a frame (a pop); 0 steps over the last 8 s of standing; the wreck,
        /// seen and unseen, made, thrown and settled. Also the walk's cost a frame with twelve.</summary>
        public static string Walk()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<IroncladWalkTrial>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(nameof(IroncladWalkTrial)).AddComponent<IroncladWalkTrial>();
            return "staging; call IroncladTrials.WalkReport";
        }

        public static string WalkReport()
        {
            var p = Object.FindAnyObjectByType<IroncladWalkTrial>();
            return p == null ? "not running" : p.report;
        }
    }

    public sealed class IroncladWalkTrial : BugTrial
    {
        // Foot-local points (the foot's pivot is the ankle, 0.34 m over the sole's middle).
        static readonly Vector3[] FootPts = { new Vector3(0f, -0.34f, 0f), new Vector3(0f, -0.24f, 0.84f), new Vector3(0f, -0.34f, -0.52f) };

        sealed class Probe
        {
            public readonly IroncladLegs walk;
            readonly Unit unit;
            readonly bool[] wasPlanted = new bool[2];
            readonly Vector3[,] anchor = new Vector3[2, 3];
            readonly Vector3[] lastSole = new Vector3[2];
            readonly Vector3[] lastPlant = new Vector3[2];
            int lastFrameSeen = -1;
            public int skipped, hiddenSteps, relifts, bothUp, shortSwings, landPops, speedJumps, hitchJumps, slowJumps;
            readonly float[] lastSpeed = new float[2];
            float dtMean;
            public float landJump;
            readonly int[] swingSamples = new int[2];
            readonly int lt0, lr0, ls0;
            public float crouchMax, twistMax;
            readonly int[] landedAt = { -99, -99 };
            Vector3 lastBody;
            int replants;
            public int frames, plantedFrames, missFrames, kneeBack, pops, steps0;
            public float dtSum;
            public float drift, missMax, popRatio, footStepMax, groundGap, rotStep, landMiss;
            public readonly float[] driftPt = new float[3];
            int replants0;
            readonly Quaternion[] lastThigh = new Quaternion[2];
            public readonly List<float> drifts = new List<float>();
            readonly float[] cur = new float[2];

            public Probe(Unit u)
            {
                unit = u;
                walk = u.GetComponent<UnitView>().Walk;
                replants = walk.Replants;
                replants0 = walk.Replants;
                steps0 = walk.Steps;
                lt0 = walk.LiftsTwist; lr0 = walk.LiftsReach; ls0 = walk.LiftsStride;
                lastBody = u.transform.position;
                for (int i = 0; i < 2; i++)
                {
                    lastSole[i] = Sole(i);
                    lastThigh[i] = walk.legs[i].thigh.rotation;
                    // As things stand, so a lift in the first samples is no re-lift.
                    wasPlanted[i] = !walk.legs[i].swinging;
                    lastPlant[i] = walk.legs[i].planted;
                    for (int k = 0; k < 3; k++) anchor[i, k] = walk.legs[i].foot.TransformPoint(FootPts[k]);
                }
            }

            Vector3 Sole(int i) => walk.legs[i].foot.TransformPoint(FootPts[0]);

            public void Sample(MapInfo map)
            {
                frames++;
                dtSum += Time.deltaTime;
                int gap = lastFrameSeen >= 0 ? Time.frameCount - lastFrameSeen : 1;
                if (gap > 1) skipped += gap - 1;
                lastFrameSeen = Time.frameCount;
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                bool hitch = frames > 10 && dt > 2f * dtMean;
                bool slow = !hitch && dt > 0.05f;   // the walker's LongFrame: a swing may land at once
                if (frames == 1) dtMean = dt; else if (!hitch) dtMean = Mathf.Lerp(dtMean, dt, 0.05f);
                Vector3 body = unit.transform.position;
                float bodyStep = new Vector2(body.x - lastBody.x, body.z - lastBody.z).magnitude;
                lastBody = body;
                bool replanted = walk.Replants != replants;
                replants = walk.Replants;
                crouchMax = Mathf.Max(crouchMax, walk.Crouch);
                if (walk.legs[0].swinging && walk.legs[1].swinging) bothUp++;
                float bodyYaw = unit.transform.eulerAngles.y;
                for (int i = 0; i < 2; i++)
                {
                    var leg = walk.legs[i];
                    Vector3 sole = Sole(i);
                    float footStep = (sole - lastSole[i]).magnitude;
                    lastSole[i] = sole;
                    float rot = Quaternion.Angle(lastThigh[i], leg.thigh.rotation);
                    lastThigh[i] = leg.thigh.rotation;
                    bool planted = !leg.swinging;
                    // A step taken whole between two samples (the end of frame is not reached every
                    // frame at 8x in the Editor): a new plant, not a slide.
                    bool newPlant = planted && wasPlanted[i] && (Flat(leg.planted) - Flat(lastPlant[i])).sqrMagnitude > 1e-6f;
                    if (newPlant) { hiddenSteps++; if (gap == 1) shortSwings++; }
                    // A speed jump: the sole suddenly much faster than the sample before, over a real
                    // distance (a pop by time, not by the body's step, which reads a steady fast swing
                    // on a frame the body hardly moves as one).
                    float footSpeed = footStep / dt;
                    if ((!planted || !wasPlanted[i]) && footStep > 0.25f && footSpeed > 3f * lastSpeed[i] + 2f)
                    {
                        if (hitch) hitchJumps++; else if (slow) slowJumps++; else speedJumps++;
                    }
                    lastSpeed[i] = planted && wasPlanted[i] ? 0f : footSpeed;
                    if (!planted) swingSamples[i]++;
                    if (planted && !wasPlanted[i])
                    {
                        // The landing: how far the drawn foot moved from its last swinging sample.
                        if (swingSamples[i] <= 1) shortSwings++;
                        landJump = Mathf.Max(landJump, footStep);
                        if (footStep > 4f * bodyStep + 0.1f) landPops++;
                        swingSamples[i] = 0;
                    }
                    lastPlant[i] = leg.planted;
                    if (planted) twistMax = Mathf.Max(twistMax, Mathf.Abs(Mathf.DeltaAngle(leg.plantedYaw, bodyYaw)));
                    if (planted && (!wasPlanted[i] || newPlant)) landedAt[i] = frames;
                    if (!planted && wasPlanted[i] && frames - landedAt[i] <= 2) relifts++;
                    if (planted && (!wasPlanted[i] || replanted || newPlant))
                    {
                        if (wasPlanted[i]) drifts.Add(cur[i]);
                        for (int k = 0; k < 3; k++) anchor[i, k] = leg.foot.TransformPoint(FootPts[k]);
                        cur[i] = 0f;
                        landMiss = Mathf.Max(landMiss, leg.miss);
                    }
                    else if (planted)
                    {
                        plantedFrames++;
                        rotStep = Mathf.Max(rotStep, rot);
                        for (int k = 0; k < 3; k++)
                        {
                            float dk = (leg.foot.TransformPoint(FootPts[k]) - anchor[i, k]).magnitude;
                            driftPt[k] = Mathf.Max(driftPt[k], dk);
                            cur[i] = Mathf.Max(cur[i], dk);
                        }
                        drift = Mathf.Max(drift, cur[i]);
                        if (leg.miss > 0.005f) { missFrames++; missMax = Mathf.Max(missMax, leg.miss); }
                        groundGap = Mathf.Max(groundGap, Mathf.Abs(sole.y - map.HeightAt(new Vector2(sole.x, sole.z))));
                    }
                    else
                    {
                        if (wasPlanted[i]) drifts.Add(cur[i]);
                        footStepMax = Mathf.Max(footStepMax, footStep);
                        if (bodyStep > 0.01f) popRatio = Mathf.Max(popRatio, footStep / bodyStep);
                        if (footStep > 4f * bodyStep + 0.1f) pops++;
                    }
                    wasPlanted[i] = planted;
                    // The knee forward of the line from hip to ankle (the legs root's forward).
                    Vector3 hip = leg.thigh.position, knee = leg.shin.position, ankle = leg.foot.position;
                    Vector3 line = (ankle - hip).normalized;
                    Vector3 off = knee - hip - line * Vector3.Dot(knee - hip, line);
                    if (Vector3.Dot(off, leg.thigh.parent.forward) < -0.02f) kneeBack++;
                }
            }

            static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

            public int Steps => walk.Steps - steps0;
            public int Replants => walk.Replants;

            public string Line()
            {
                drifts.Sort();
                float p95 = drifts.Count > 0 ? drifts[Mathf.Min(drifts.Count - 1, (int)(drifts.Count * 0.95f))] : 0f;
                return $"{frames} frames ({dtSum / Mathf.Max(1, frames) * 1000f:0} ms of game a frame), {Steps} steps (lifted by twist {walk.LiftsTwist - lt0}, reach {walk.LiftsReach - lr0}, stride {walk.LiftsStride - ls0}), {walk.Replants - replants0} replants, {skipped} frames between samples ({hiddenSteps} steps taken whole between two), " +
                       $"swings of one sample or none {shortSwings}, landing jump up to {landJump:0.00} m ({landPops} over 4x the body's step + 10 cm), " +
                       $"speed jumps {speedJumps} (+{hitchJumps} on hitch frames, +{slowJumps} on frames over 50 ms), " +
                       $"drift sole/toe/heel {driftPt[0] * 100f:0.0}/{driftPt[1] * 100f:0.0}/{driftPt[2] * 100f:0.0} cm, landing miss up to {landMiss * 100f:0.0} cm, planted drift max {drift * 100f:0.0} cm (95% of plants {p95 * 100f:0.0} cm, {drifts.Count} plants), " +
                       $"reach misses {missFrames} of {plantedFrames} planted frames (worst {missMax * 100f:0.0} cm), sole off the ground {groundGap * 100f:0.0} cm, " +
                       $"knee back {kneeBack}, swing pops {pops} (foot step up to {footStepMax:0.00} m, {popRatio:0.0}x the body's), " +
                       $"thigh turn a frame while planted up to {rotStep:0.0} deg, crouch up to {crouchMax:0.00} m, " +
                       $"foot twist while planted up to {twistMax:0} deg, lifted again within 2 frames of landing {relifts}, both feet up {bothUp} frames";
            }
        }

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(5150);
            var rngRest = new System.Random(6150);
            HoldStatics();
            try
            {
                foreach (float scale in new[] { 1f, 8f })
                {
                    Time.timeScale = scale;
                    rng = new System.Random(5150);   // both speeds walk the same fields
                    if (!FindField(rng, out var b, out var d)) { sb.AppendLine("no open field found"); break; }
                    var u = Put(UnitType.Ironclad, 0, b - d * 30f);
                    yield return Wait(1f);
                    var view = u.GetComponent<UnitView>();
                    if (view == null || view.Walk == null) { report = "the Ironclad has no jointed legs (model not re-exported?)"; yield break; }
                    sb.AppendLine($"{scale:0}x, speed {u.def.speed:0.0} m/s, map {map.seed}:");
                    var sel = new List<Unit> { u };

                    // Straight, 50 m.
                    var p = new Probe(u);
                    world.CmdMove(sel, b + d * 20f, false);
                    yield return Watch(p, u, 30f);
                    sb.AppendLine($"  straight: {p.Line()}");

                    // About turn: to a point behind it and off to the side.
                    p = new Probe(u);
                    Vector2 side = new Vector2(-d.y, d.x);
                    world.CmdMove(sel, u.pos - d * 6f + side * 3f, false);
                    yield return Watch(p, u, 15f);
                    sb.AppendLine($"  about turn: {p.Line()}");

                    // Stop and start: stopped after 2 s, sent on 1.5 s later.
                    p = new Probe(u);
                    world.CmdMove(sel, u.pos + d * 20f, false);
                    float until = world.time + 2f;
                    while (world.time < until) { yield return endOfFrame; p.Sample(world.Map); }
                    world.CmdStop(sel);
                    until = world.time + 1.5f;
                    while (world.time < until) { yield return endOfFrame; p.Sample(world.Map); }
                    world.CmdMove(sel, u.pos + d * 12f, false);
                    yield return Watch(p, u, 15f);
                    sb.AppendLine($"  stop-start: {p.Line()}");

                    // Standing: 2 s to settle, then 8 s counted.
                    until = world.time + 2f;
                    while (world.time < until) yield return null;
                    p = new Probe(u);
                    until = world.time + 8f;
                    while (world.time < until) { yield return endOfFrame; p.Sample(world.Map); }
                    sb.AppendLine($"  standing 8 s: {p.Steps} steps, drift max {p.drift * 100f:0.0} cm");

                    // A slope: up it and back down.
                    if (FindSlope(rng, out var lo, out var hi, out float deg))
                    {
                        Remove(u);
                        yield return Wait(1.5f);
                        u = Put(UnitType.Ironclad, 0, lo);
                        sel = new List<Unit> { u };
                        yield return Wait(1f);
                        p = new Probe(u);
                        world.CmdMove(sel, hi, false);
                        yield return Watch(p, u, 25f);
                        world.CmdMove(sel, lo, false);
                        yield return Watch(p, u, 25f);
                        sb.AppendLine($"  slope {deg:0} deg up and down ({(hi - lo).magnitude:0} m): {p.Line()}");
                    }
                    else sb.AppendLine("  no slope found");
                    // Steep: down a 25-40 degree slope and back up (the walkable NavMesh goes to 40).
                    if (FindSlope(rng, out var slo, out var shi, out float sdeg, 25f, 40f, 6f))
                    {
                        Remove(u);
                        yield return Wait(1.5f);
                        u = Put(UnitType.Ironclad, 0, shi);
                        sel = new List<Unit> { u };
                        yield return Wait(1f);
                        p = new Probe(u);
                        world.CmdMove(sel, slo, false);
                        yield return Watch(p, u, 20f);
                        world.CmdMove(sel, shi, false);
                        yield return Watch(p, u, 20f);
                        sb.AppendLine($"  steep {sdeg:0} deg down and up ({(shi - slo).magnitude:0} m): {p.Line()}");
                    }
                    else sb.AppendLine("  no steep slope found");
                    // Steeper: the next 28-40 degree field on the same stream (appended last, so every
                    // field above is the one earlier rounds walked; on map 1000 the first steep is 25).
                    if (FindSlope(rng, out var vlo, out var vhi, out float vdeg, 28f, 40f, 6f))
                    {
                        Remove(u);
                        yield return Wait(1.5f);
                        u = Put(UnitType.Ironclad, 0, vhi);
                        sel = new List<Unit> { u };
                        yield return Wait(1f);
                        p = new Probe(u);
                        world.CmdMove(sel, vlo, false);
                        yield return Watch(p, u, 20f);
                        world.CmdMove(sel, vhi, false);
                        yield return Watch(p, u, 20f);
                        sb.AppendLine($"  steeper {vdeg:0} deg down and up ({(vhi - vlo).magnitude:0} m): {p.Line()}");
                    }
                    else sb.AppendLine("  no steeper slope found");
                    Remove(u);
                    yield return Wait(2f);
                }
                Time.timeScale = 1f;

                // Cost: twelve walking together.
                if (FindField(rngRest, out var cb, out var cd))
                {
                    var crowd = new List<Unit>();
                    Vector2 cs = new Vector2(-cd.y, cd.x);
                    for (int i = 0; i < 12; i++) crowd.Add(Put(UnitType.Ironclad, 0, cb - cd * 25f + cs * ((i % 4) - 1.5f) * 3.5f - cd * (i / 4) * 4f));
                    yield return Wait(1f);
                    world.CmdMove(crowd, cb + cd * 20f, false);
                    yield return Wait(1f);
                    IroncladLegs.CostMs = 0.0; IroncladLegs.CostCalls = 0;
                    IroncladLegs.Profile = true;
                    int f0 = Time.frameCount;
                    yield return Wait(8f);
                    IroncladLegs.Profile = false;
                    int frames = Time.frameCount - f0;
                    sb.AppendLine($"cost: 12 walking, {IroncladLegs.CostCalls} poses in {frames} frames, {IroncladLegs.CostMs / Mathf.Max(1, frames):0.000} ms a frame, " +
                                  $"{IroncladLegs.CostMs * 1000.0 / Mathf.Max(1, IroncladLegs.CostCalls):0.0} us an Ironclad");
                    foreach (var c in crowd) Remove(c);
                    yield return Wait(2f);
                }

                // Wrecks: one in sight (ours), one of theirs where we see nothing.
                for (int k = 0; k < 2; k++)
                {
                    if (!FindField(rngRest, out var wb, out var wd)) { sb.AppendLine("no field for the wreck"); break; }
                    bool seen = k == 0;
                    var u = Put(UnitType.Ironclad, seen ? 0 : 1, wb);
                    yield return Wait(1f);
                    world.CmdMove(new List<Unit> { u }, wb + wd * 15f, false);
                    yield return Wait(2f);
                    bool shownBefore = u.team == 0 || u.visibleToPlayer;
                    var before = new HashSet<UnitWreck>(Object.FindObjectsByType<UnitWreck>(FindObjectsSortMode.None));
                    Vector3 at = u.transform.position;
                    world.Damage(u, 1e6f, u.pos - wd * 6f, null);   // through Kill, as a real death: the hulk, the throw
                    yield return null; yield return null;
                    bool hulk = u.hulk != null;
                    UnitWreck w = null;
                    foreach (var x in Object.FindObjectsByType<UnitWreck>(FindObjectsSortMode.None)) if (!before.Contains(x)) w = x;
                    if (w == null) { sb.AppendLine($"wreck ({(seen ? "seen" : "unseen")}): none made"); continue; }
                    var hull = w.GetComponentInChildren<Rigidbody>();
                    float y0 = hull.position.y, rise = 0f, tilt = 0f, moved = 0f;
                    bool anyShown = false;
                    float t0 = world.time;
                    while (world.time - t0 < 6f && hull != null)
                    {
                        rise = Mathf.Max(rise, hull.position.y - y0);
                        tilt = Mathf.Max(tilt, Vector3.Angle(hull.transform.up, Vector3.up));
                        moved = Mathf.Max(moved, (hull.position - at).magnitude);
                        foreach (var r in w.GetComponentsInChildren<Renderer>()) if (r.enabled) { anyShown = true; break; }
                        yield return null;
                    }
                    string rest = hull == null ? "gone" : $"rest tilt {Vector3.Angle(hull.transform.up, Vector3.up):0} deg, sleeping {hull.isKinematic || hull.IsSleeping()}";
                    sb.AppendLine($"wreck ({(seen ? "seen" : "unseen")}, shown before death {shownBefore}): made, hulk {hulk}, drawn {anyShown}, rose {rise:0.00} m, tilted up to {tilt:0} deg, moved {moved:0.0} m in 6 s, {rest}");
                }
            }
            finally { Time.timeScale = 1f; IroncladLegs.Profile = false; ReleaseStatics(); }
            report = sb.ToString() + "wrecks done";
        }

        static readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

        IEnumerator Watch(Probe p, Unit u, float limit)
        {
            float until = world.time + limit;
            yield return endOfFrame;
            while (world.time < until && Unit.Live(u))
            {
                p.Sample(world.Map);
                if (!u.Moving && u.order == Order.Idle) break;
                yield return endOfFrame;
            }
        }

        /// <summary>A walkable line 24 m up a slope of 8-22 degrees, dry, away from both bases.</summary>
        bool FindSlope(System.Random rng, out Vector2 lo, out Vector2 hi, out float deg, float minDeg = 8f, float maxDeg = 22f, float half = 12f)
        {
            lo = hi = default; deg = 0f;
            for (int tries = 0; tries < 4000; tries++)
            {
                var c = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                if (!map.InBounds(c, 40f) || map.WaterDepth(c) > -0.3f) continue;
                if ((c - map.StartPos(0)).magnitude < 60f || (c - map.StartPos(1)).magnitude < 60f) continue;
                Vector3 n = map.NormalAt(c);
                float a = Mathf.Acos(Mathf.Clamp01(n.y)) * Mathf.Rad2Deg;
                if (a < minDeg || a > maxDeg) continue;
                var upDir = -new Vector2(n.x, n.z).normalized;
                Vector2 l = c - upDir * half, h = c + upDir * half;
                if (!map.InBounds(l, 10f) || !map.InBounds(h, 10f) || map.WaterDepth(l) > -0.3f) continue;
                bool ok = true;
                for (float s = -half; s <= half; s += 1.5f)
                    if (!world.Walkable(c + upDir * s, 0.5f)) { ok = false; break; }
                float rise = map.HeightAt(h) - map.HeightAt(l);
                if (!ok || rise < 2f * half * Mathf.Tan((minDeg - 4f) * Mathf.Deg2Rad)) continue;
                lo = l; hi = h;
                deg = Mathf.Atan(rise / (2f * half)) * Mathf.Rad2Deg;
                return true;
            }
            return false;
        }
    }
}
