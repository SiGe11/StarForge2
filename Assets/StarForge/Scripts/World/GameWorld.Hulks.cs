// GameWorld.Hulks.cs -- a dead tank stays where it died, as something to get behind (TacticalRules). The
// record is the simulation's, the same for both sides and whatever the camera saw: it stops rounds and
// sight, and agents walk round it. It stands at least a minute, and for as long as a man is using it or
// a fight is going on round it; only then does the wreck the player sees go into the ground.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StarForge.Sim;

namespace StarForge.World
{
    public sealed class Hulk
    {
        public Vector2 at;
        public float radius, height, until;
        public bool gone;
        public GameObject blocker;
    }

    public sealed partial class GameWorld
    {
        const float HulkLasts = 60f, HulkHolds = 20f, HulkFightReach = 30f, HulkFightWithin = 8f;
        const int MostHulks = 24;
        public readonly List<Hulk> Hulks = new List<Hulk>(MostHulks);
        float hulksNext;

        void BeginHulks()
        {
            Hulks.Clear();
            hulksNext = 0f;
            tanksCountedAt = -1f;
        }

        float tanksCountedAt = -1f;
        readonly int[] tanksAlive = new int[2];

        /// <summary>Live tanks of a side, counted once a tick: most of the time there are none, and every
        /// cover line asks.</summary>
        int TanksAlive(int team)
        {
            if (tanksCountedAt != time)
            {
                tanksCountedAt = time;
                tanksAlive[0] = tanksAlive[1] = 0;
                foreach (var o in units)
                    if (o != null && !o.dying && o.team <= 1 && IsTankLike(o)) tanksAlive[o.team]++;
            }
            return tanksAlive[team];
        }

        /// <summary>A tank that can be got behind: a walking Mech has a wreck of its own.</summary>
        public static bool IsTankLike(Unit o) => o.def.Heavy && o.def.IsMobile && !o.def.flying && !o.def.organic && o.mech == null;

        void AddHulk(Unit u)
        {
            if (!IsTankLike(u)) return;
            if (Hulks.Count >= MostHulks) RetireHulk(0);
            var h = new Hulk
            {
                at = u.pos,
                radius = Mathf.Max(1f, u.def.radius * 0.85f),
                height = Mathf.Max(1.4f, u.def.visualHeight * 0.7f),
                until = time + HulkLasts,
            };
            h.blocker = new GameObject("Hulk");
            h.blocker.transform.position = map.Ground(u.pos);
            var ob = h.blocker.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Capsule;
            ob.radius = h.radius;
            ob.height = h.height;
            ob.center = Vector3.up * (h.height * 0.5f);
            ob.carving = true;
            ob.carveOnlyStationary = false;   // at once: NavIslands may rebuild two frames after the invalidate
            Hulks.Add(h);
            u.hulk = h;
            Sight.StampHulk(h);
            if (Islands != null) Islands.Invalidate(u.pos);
        }

        void RetireHulk(int index)
        {
            var h = Hulks[index];
            h.gone = true;
            if (h.blocker != null) Destroy(h.blocker);
            Hulks.RemoveAt(index);
            if (Islands != null) Islands.Invalidate(h.at);
        }

        void TickHulks()
        {
            if (time < hulksNext) return;
            hulksNext = time + 1f;
            for (int i = Hulks.Count - 1; i >= 0; i--)
            {
                var h = Hulks[i];
                if (HulkBusy(h)) h.until = Mathf.Max(h.until, time + HulkHolds);
                else if (time >= h.until) RetireHulk(i);
            }
        }

        /// <summary>A man is behind it or making for it, or there is fighting within reach of it.</summary>
        bool HulkBusy(Hulk h)
        {
            CellRange(h.at, HulkFightReach + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team > 1) continue;
                        if ((o.pos - h.at).sqrMagnitude > HulkFightReach * HulkFightReach) continue;
                        if (time - o.firedAt < HulkFightWithin || time - o.lastDamagedT < HulkFightWithin) return true;
                        if (o.def.bodyRadius > 0f && (o.mode == SoldierMode.ToCover || o.mode == SoldierMode.InCover) &&
                            (o.coverAt - h.at).sqrMagnitude < (h.radius + 2f) * (h.radius + 2f)) return true;
                    }
            return false;
        }

        /// <summary>A live tank of this side on the way from a gun to a man near it: a round would strike the tank.</summary>
        bool TankInWay(Vector3 a, Vector3 b, int team)
        {
            if (team > 1 || TanksAlive(team) == 0) return false;
            float dx = b.x - a.x, dz = b.z - a.z, dy = b.y - a.y, flat2 = dx * dx + dz * dz;
            if (flat2 < 1e-3f) return false;
            CellRange(new Vector2(b.x, b.z), 6f + LargestRadius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * GRID + x]; i >= 0; i = gridNext[i])
                    {
                        var o = i < units.Count ? units[i] : null;
                        if (o == null || o.dying || o.team != team || !IsTankLike(o)) continue;
                        float hit = SightMap.RayCircle(a.x, a.z, dx, dz, flat2, o.pos.x, o.pos.y, o.BodyRadius);
                        if (hit <= 1f && a.y + dy * hit < Sight.GroundAt(o.pos.x, o.pos.y) + o.BodyHeight) return true;
                    }
            return false;
        }
    }
}
