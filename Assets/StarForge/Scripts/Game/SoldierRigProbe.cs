// SoldierRigProbe.cs -- the proof test for rigged soldiers, and nothing in a match reads it.
//
//     StarForge -sfbench 120 -sfplay -sfrigprobe 200 [-sfrigrigid]
//     StarForge -sfbench 420 -sfplay -sfrigprobe 200 -sfrigfollow
//     StarForge -sfbench 12 -sfplay -sfrigposes -sfshot poses.png -sfshotat 2
//
// -sfrigprobe N stands N soldiers in a block by the player's base with the camera held on it,
// walking, running, crawling, aiming and firing as a fight would have them, while the match goes
// on behind them: N 0 holds the same camera on the same ground for the comparison. -sfrigrigid
// makes them today's soldiers instead (four rigid parts, the legs swung by hand), the cost a
// rigged one replaces. -sfrigfollow lets the benchmark's camera follow the fighting as it always
// does and keeps the block in the middle of its view: 200 soldiers on top of a real fight, Mechs
// and all (-sfrigprobe 0 alone is then the plain benchmark to compare with).
// -sfrigfreeze stands them still in a pose with their Animators off, so what is left is drawing and
// skinning: against the animated run, that splits the cost. -sfrigposes lines up one soldier per
// clip at close zoom for a picture.
//
// What it does not carry: Unit.Tick and NavMeshAgents, the view's property blocks, capes, bones
// exposed for what hangs on them, muzzle effects. Read it as what animating and drawing cost.
using System.Collections.Generic;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;
using UnityEngine;

namespace StarForge.Game
{
    public sealed class SoldierRigProbe : MonoBehaviour
    {
        const float Spacing = 2.2f;
        const float Wander = 3.5f;

        struct Move
        {
            public int state; public float speed, once;
            public Move(string s, float v, float clip = 0f) { state = Animator.StringToHash(s); speed = v; once = clip; }
        }

        // About a third of the block is aiming and firing at any time; the rest on the move, down,
        // or in a one-off (a pose held still costs an Animator almost nothing, so few of those).
        static readonly Move[] Moves =
        {
            new Move("Aim", 0f), new Move("Aim", 0f), new Move("Aim", 0f), new Move("Idle", 0f), new Move("CrouchAim", 0f), new Move("ProneAim", 0f),
            new Move("Walk", 3.5f), new Move("Run", 6f), new Move("Sneak", 2f), new Move("CrouchWalk", 2.5f), new Move("Crawl", 1f),
            new Move("Reload", 0f, 2f), new Move("Throw", 0f, 1.38f), new Move("Hit", 0f, 0.67f), new Move("Dive", 2f, 0.42f),
        };
        static readonly string[] Clips =
        {
            "Idle", "Walk", "Run", "Sneak", "CrouchWalk", "CrouchIdle", "CrouchAim", "Crawl", "ProneAim", "Aim",
            "Fire", "Reload", "Throw", "Stab", "Dive", "Vault", "Hit",
        };
        static readonly int FireState = Animator.StringToHash("Fire");
        static readonly int AimState = Animator.StringToHash("Aim");

        sealed class Soldier
        {
            public Transform t, legL, legR;
            public Animator anim;
            public Vector2 home, pos;
            public float yaw, speed, nextMove, nextShot, phase;
            public bool shoots;
        }

        readonly List<Soldier> soldiers = new List<Soldier>();
        readonly System.Random rng = new System.Random(1000);
        MapInfo map;
        RTSCamera camera;
        Vector2 focus, right, ahead;
        float zoom;
        Vector2 anchor;
        bool rigid, poses, follow, freeze, frozen;
        float freezeAt = 8f;   // through the warm-up, so every soldier has moved once
        int asked;
        string error;

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void Begin(MapInfo info, RTSCamera rig, int count, bool rigidOnes, bool poseSheet, bool followCamera, bool freezeThem)
        {
            freeze = freezeThem;
            map = info;
            rigid = rigidOnes;
            poses = poseSheet;
            follow = followCamera;
            asked = poseSheet ? Clips.Length : count;
            camera = rig;
            Vector2 at = follow ? rig.Focus : FlatGround(map.StartPos(0));
            anchor = at;
            float yaw = rig.yaw * Mathf.Deg2Rad;
            right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
            ahead = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            // The HUD covers the bottom of the picture: look a little past the block's middle.
            focus = at - ahead * (poses ? 1.5f : 6f);
            zoom = poses ? 26f : 72f;
            if (poses) { LineUp(at, yaw); return; }

            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                var p = at + new Vector2((i % side - side * 0.5f) * Spacing, (i / side - side * 0.5f) * Spacing);
                var s = Spawn(i % 2);
                if (s == null) return;
                s.home = s.pos = p;
                s.yaw = Range(0f, Mathf.PI * 2f);
                s.phase = Range(0f, 6f);
                s.nextMove = Range(0f, 4f);
                Place(s);
                soldiers.Add(s);
            }
        }

        /// <summary>The levelest open ground 34-50 m from the base's middle (a block on a rock
        /// face reads wrong and hides half the soldiers). The same place every run on a seed.</summary>
        Vector2 FlatGround(Vector2 start)
        {
            Vector2 best = start;
            float bestScore = float.MaxValue;
            for (float r = 34f; r <= 50f; r += 8f)
                for (int a = 0; a < 16; a++)
                {
                    float t = a * Mathf.PI / 8f;
                    var c = start + new Vector2(Mathf.Sin(t), Mathf.Cos(t)) * r;
                    if (c.x < 24f || c.y < 24f || c.x > map.mapSize - 24f || c.y > map.mapSize - 24f) continue;
                    float low = float.MaxValue, high = float.MinValue;
                    for (float x = -16f; x <= 16f; x += 4f)
                        for (float z = -16f; z <= 16f; z += 4f)
                        {
                            float h = map.HeightAt(c + new Vector2(x, z));
                            low = Mathf.Min(low, h);
                            high = Mathf.Max(high, h);
                        }
                    if (low > map.waterLevel + 0.3f && high - low < bestScore) { bestScore = high - low; best = c; }
                }
            return best;
        }

        void LineUp(Vector2 at, float cameraYaw)
        {
            const int Columns = 6;
            for (int i = 0; i < Clips.Length; i++)
            {
                var s = Spawn(0);
                if (s == null) return;
                s.pos = at + right * ((i % Columns - Columns * 0.5f + 0.5f) * 2.7f) + ahead * ((1f - i / Columns) * 3.2f);
                s.yaw = cameraYaw + Mathf.PI * 0.86f;
                Place(s);
                if (s.anim != null)
                {
                    s.anim.Play(Clips[i], 0, 0.3f);
                    s.anim.speed = 0f;
                }
                soldiers.Add(s);
            }
        }

        Soldier Spawn(int team)
        {
            var s = new Soldier();
            if (rigid)
            {
                var body = Defs.Get(UnitType.Trooper).Prefab(team).transform.Find("Body");
                if (body == null) { error = "no Body on the Trooper's prefab"; return null; }
                var go = Instantiate(body.gameObject);
                s.t = go.transform;
                foreach (var c in go.GetComponentsInChildren<Transform>(true))
                {
                    if (c.name == "LegL") s.legL = c;
                    else if (c.name == "LegR") s.legR = c;
                }
                return s;
            }
            var prefab = Resources.Load<GameObject>("SoldierRig_T" + team);
            if (prefab == null) { error = "no SoldierRig prefab"; return null; }
            var inst = Instantiate(prefab);
            s.t = inst.transform;
            s.anim = inst.GetComponentInChildren<Animator>();
            return s;
        }

        void Place(Soldier s)
        {
            s.t.SetPositionAndRotation(map.Ground(s.pos), Quaternion.Euler(0f, s.yaw * Mathf.Rad2Deg, 0f));
        }

        void Update()
        {
            Vector2 moved = Vector2.zero;
            if (follow)
            {
                moved = camera.Focus - anchor;
                anchor = camera.Focus;
            }
            else
            {
                // The match's own start moves the camera after this one's has run: keep saying.
                camera.JumpTo(focus);
                camera.ZoomTo(zoom);
            }
            if (poses) return;
            float dt = Time.deltaTime, now = Time.time;
            if (freeze && !frozen && now >= freezeAt) Freeze();
            foreach (var s in soldiers)
            {
                if (frozen)
                {
                    if (moved == Vector2.zero) continue;
                    s.home += moved;
                    s.pos += moved;
                    Place(s);
                    continue;
                }
                if (now >= s.nextMove)
                {
                    var m = Moves[rng.Next(Moves.Length)];
                    s.speed = m.speed;
                    s.shoots = m.state == AimState;
                    s.nextMove = now + (m.once > 0f ? m.once : Range(3f, 7f));
                    s.nextShot = now + 0.2f + Range(0f, 0.7f);
                    if (s.anim != null) s.anim.CrossFadeInFixedTime(m.state, 0.15f, 0);
                }
                if (s.shoots && now >= s.nextShot)
                {
                    s.nextShot = now + 0.7f;
                    if (s.anim != null) s.anim.Play(FireState, 0, 0f);
                }
                if (s.anim != null && s.shoots && s.anim.GetCurrentAnimatorStateInfo(0).shortNameHash == FireState &&
                    s.anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f) s.anim.Play(AimState, 0, 0f);
                if (s.speed > 0f || moved != Vector2.zero)
                {
                    s.home += moved;
                    s.pos += moved;
                    if ((s.pos - s.home).sqrMagnitude > Wander * Wander)
                        s.yaw = Mathf.Atan2(s.home.x - s.pos.x, s.home.y - s.pos.y);
                    s.pos += new Vector2(Mathf.Sin(s.yaw), Mathf.Cos(s.yaw)) * (s.speed * dt);
                    Place(s);
                }
                if (s.legL != null && s.legR != null)
                {
                    s.phase += dt * 9f * Mathf.Clamp01(s.speed);
                    float swing = Mathf.Sin(s.phase) * 30f * Mathf.Clamp01(s.speed / 2f);
                    s.legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
                    s.legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                }
            }
        }

        /// <summary>Everyone in a moving pose (a still one could cost less to skin), then the
        /// Animators off: an optimised rig keeps the pose it was last given.</summary>
        void Freeze()
        {
            frozen = true;
            foreach (var s in soldiers)
                if (s.anim != null) s.anim.enabled = false;
        }

        public string Report() =>
            error != null ? "rig probe: " + error
            : $"rig probe: {soldiers.Count} of {asked} {(rigid ? "rigid" : "rigged")} soldiers{(poses ? " (pose sheet)" : follow ? " following the camera" : "")}{(frozen ? ", frozen" : "")}";
    }
}
