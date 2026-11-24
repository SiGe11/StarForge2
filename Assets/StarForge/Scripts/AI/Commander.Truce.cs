// Commander.Truce.cs -- the Night of the Quenching (World/GameWorld.Night): no shot is fired until
// lauds, by custom and with no order given, so the AI does not send anyone to stand in front of
// guns that will not fire. Macro and scouting go on; tactics and micro stop at the hooks in
// RunTactics and RunMicro. Fog-fair (its Snapshot only) and through Issue (Cmd*, its budget).
using StarForge.Sim;
using StarForge.World;
using UnityEngine;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed partial class Commander
    {
        /// <summary>For trials: where the army was last told to stand during the truce.</summary>
        public Vector2 TrucePost { get; private set; }

        /// <summary>For trials: the share of the army within <paramref name="reach"/> of the truce post.</summary>
        public float TruceArmyAt(float reach)
        {
            if (main.Count == 0) return 1f;
            int n = 0;
            foreach (var h in main) if (Unit.Live(h) && (h.pos - TrucePost).magnitude <= reach) n++;
            return n / (float)main.Count;
        }

        /// <summary>For trials: a wave is out.</summary>
        public bool WaveOut => waveOn;

        /// <summary>Not more often than this does the truce re-order the army (a scout walking about by its
        /// base must not drag it to and fro).</summary>
        const float TruceReorder = 3f;
        float truceOrderAt = -99f;
        readonly System.Collections.Generic.List<(Vector2 at, float reach)> truceForts = new System.Collections.Generic.List<(Vector2, float)>(4);

        /// <summary>A pass of tactics during the truce: a wave out is called off (not beaten), raiders
        /// and the prong fold into the army, and the army holds its guard post until lauds, walking out
        /// (no shot) only to what home defence would answer.</summary>
        void KeepTruce(Snapshot s)
        {
            if (waveOn) EndWave(true);
            committed = false;
            fallingBack = false;
            if (harass.Count > 0) { main.AddRange(harass); harass.Clear(); }
            if (prong.Count > 0) { main.AddRange(prong); prong.Clear(); }
            if (main.Count == 0 || !Budget.Can(2)) return;
            Vector2 home = Perception.Home;
            Vector2 face = s.enemyBaseKnown ? s.enemyBase : Perception.BaseGuesses[0];
            Vector2 guard = home + Norm(face - home) * 16f;
            guard = w.NearestReachable(home, Influence.SafestNear(w.NearestReachable(home, guard), 14f));   // as the normal guard post
            // Home defence still walks out (no shot), by its own filters (RunTactics' home defence): no
            // reaction before the difficulty's delay; men seen in the last 10 s, a site only while in sight
            // (5 s); of structures only a Mech Bay or Sentinel. Never to a finished gun, and never to what
            // stands under one's cover (its reach and 3 m, at least 32: `fortReach`) more than 25 m from
            // home, as `holdOff` and the site exclusion there; men only when worth more than `divertAt`.
            // A site going up comes first, then men. Else the truce would be free building time by its base.
            truceForts.Clear();
            float threatHome = 0f;
            foreach (var r in Perception.Enemies)
            {
                bool structure = Defs.Get(r.type).building;
                if (structure && r.type != UnitType.MechBay && r.type != UnitType.Sentinel) continue;
                if (!Perception.Reactable(r, reactionDelay)) continue;
                if (w.time - r.lastSeen > (structure ? (r.unfinished ? 5f : 60f) : 10f)) continue;
                if ((r.pos - home).magnitude >= 55f) continue;
                if (structure && !r.unfinished)
                {
                    var def = Defs.Get(r.type);
                    truceForts.Add((r.pos, Mathf.Max(32f, (def != null ? def.range : 29f) + 3f)));
                }
            }
            bool Covered(Vector2 p)
            {
                if ((p - home).magnitude <= 25f) return false;
                foreach (var (at, reach) in truceForts) if ((p - at).magnitude < reach) return true;
                return false;
            }
            // Men under a fort's cover do not count toward the threat (as holdOff subtracts them).
            foreach (var r in Perception.Enemies)
            {
                if (Defs.Get(r.type).building || !Perception.Reactable(r, reactionDelay) || w.time - r.lastSeen > 10f) continue;
                if ((r.pos - home).magnitude >= 55f || Covered(r.pos)) continue;
                threatHome += Defs.IsWorkerType(r.type) && !Defs.Get(r.type).Armed ? 15f : r.type == UnitType.Worker ? 15f : Defs.ArmyValue(r.type);
            }
            // The post itself never stands in a fort's reach (no 25 m exemption for it): stepped toward home
            // until it is outside every one, and never past home.
            // (reach + 2 m, and checked again after the snap to reachable ground, which can move it back in.)
            for (int step = 0; step < 40; step++)
            {
                bool inReach = false;
                foreach (var (at, reach) in truceForts) if ((guard - at).magnitude < reach + 2f) { inReach = true; break; }
                if (!inReach || (guard - home).sqrMagnitude < 1f) break;
                guard = w.NearestReachable(home, Vector2.MoveTowards(guard, home, 2f));
            }
            float divertAt = Mathf.Max(45f, s.armyValue * 0.14f);
            Remembered near = null;
            int rank = 3;
            float bestD = float.MaxValue;
            foreach (var r in Perception.Enemies)
            {
                bool structure = Defs.Get(r.type).building;
                if (structure && (!r.unfinished || (r.type != UnitType.MechBay && r.type != UnitType.Sentinel))) continue;
                if (!structure && threatHome <= divertAt) continue;
                if (!Perception.Reactable(r, reactionDelay)) continue;
                if (w.time - r.lastSeen > (structure ? 5f : 10f)) continue;
                float d = (r.pos - home).magnitude;
                if (d >= 55f || Covered(r.pos)) continue;
                int k = structure ? 0 : 1;
                if (k < rank || (k == rank && d < bestD)) { near = r; rank = k; bestD = d; }
            }
            if (near != null) guard = w.NearestReachable(home, near.pos);
            TrucePost = guard;
            if (w.time - truceOrderAt < TruceReorder) return;
            bool away = false;
            foreach (var h in main)
                if (h.order != Order.Move && h.Dist(guard) > (h.def.fixedWing ? 30f : 18f)) { away = true; break; }
            if ((away || WorthReissuing(guard, rally, 16f)) && Issue(main, 0, guard, null)) { rally = guard; truceOrderAt = w.time; }
        }
    }
}
