// GameWorld.Sanctuary.cs -- the rules of the kept Vesper twin house (Sanctuary.cs; lore section 4). The user's
// design (2026-11-24): foot units may go in; a unit inside can neither be fired on nor fire; nothing crosses it;
// the sky above it stays open (lines to and from aircraft pass over); a side with nothing left outside is turned
// out after 30 s (TurnOutAfter) and may not go in again that match.
// One pair test every gun asks (SanctBlocks), one flag on the unit (Unit.inGrounds, folded into Untargetable),
// a stop in the sight map's line test (SightMap.sanct) and a backstop for rounds and shells in flight
// (UpdateProjectiles). Real matches only, like the night maps: measurement never meets it.
using System.Collections.Generic;
using StarForge.Sim;
using UnityEngine;

namespace StarForge.World
{
    public partial class GameWorld
    {
        /// <summary>Set by GameBootstrap before BeginMatch: a real match (NightRules.RealMatch) may have the
        /// house and the cloisters. False for every fixed seed, test run and agent's play.</summary>
        public static bool SanctuaryAllowed;
        /// <summary>Trials: -1 the map's own draw (real matches only), 0 never, 1 always (the house where it
        /// fits); likewise <see cref="CloisterForce"/> for the cloisters' count. Cleared at every play entry
        /// (NightRules' reset) and by AgentInbox when play stops.</summary>
        public static int SanctuaryForce = -1, CloisterForce = -1;

        /// <summary>Houses placed and taken away again for a way they cut, since a trial last cleared them.</summary>
        public static int SanctuaryDropped;

        /// <summary>The house this match, or null.</summary>
        public Sanctuary Sanct { get; private set; }
        readonly List<CoverWall> sanctRuns = new List<CoverWall>(8);
        readonly List<CoverWall> sightRuns = new List<CoverWall>(160);

        /// <summary>The house, placed before the ditches and the cover (they keep off its ground).</summary>
        void PlaceSanctuary(MapKit kit, uint mapSeed)
        {
            Sanct = null;
            sanctRuns.Clear();
            bool want = SanctuaryForce >= 0 ? SanctuaryForce == 1 : SanctuaryAllowed && MapGenerator.SanctuaryDrawn(mapSeed);
            if (want) Sanct = MapGenerator.PlaceSanctuary(map, kit, mapSeed, sanctRuns);
            else MapGenerator.ClearSanctuaryReserve();
            // The rocks cleared off its ground leave the world's list (the NavMesh is rebuilt after the cover).
            if (Sanct != null && Sanct.bouldersCleared.Count > 0) boulders.RemoveAll(b => b == null || b.smashed);
            if (Sight != null) Sight.sanct = Sanct;
            if (want) Debug.Log(Sanct != null
                ? $"[StarForge] the Vesper twin house stands at {Sanct.centre} (yaw {Sanct.yawDeg:0}), {Sanct.plantsCleared.Count} plants and {Sanct.bouldersCleared.Count} rocks cleared"
                : $"[StarForge] the Vesper twin house was drawn but found no ground (fails: base {MapGenerator.SanctFails[0]}, ore {MapGenerator.SanctFails[1]}, " +
                  $"ground {MapGenerator.SanctFails[2]}, slope {MapGenerator.SanctFails[3]}, stands {MapGenerator.SanctFails[4]})");
        }

        /// <summary>No house (every match starts so: a match with none leaves nothing of the last one's).</summary>
        void ResetSanctuary()
        {
            // A match begun again in place (a trial's path): what the last house cleared stands again.
            if (Sanct != null)
            {
                MapGenerator.RestoreCleared(Sanct);
                foreach (var b in Sanct.bouldersCleared) if (b != null && !b.smashed && !boulders.Contains(b)) boulders.Add(b);
            }
            hidingSince[0] = hidingSince[1] = -1f;
            turnedOutAt[0] = turnedOutAt[1] = -1f;
            Sanct = null;
            sanctRuns.Clear();
            MapGenerator.ClearSanctuaryReserve();
            if (Sight != null) Sight.sanct = null;
        }

        /// <summary>The house cut a way the bases need: it goes, and the plants and rocks cleared for it stand again.</summary>
        void DropSanctuary()
        {
            Debug.LogWarning($"[StarForge] the Vesper twin house at {Sanct.centre} cut a way: taken away");
            MapGenerator.RemoveSanctuary(Sanct);
            ResetSanctuary();   // puts the boulders back in the world's list
        }

        // ------------------------------------------------------------ turned out
        /// <summary>How long a side with nothing outside the grounds may stay in them (the user's call, 2026-11-24).</summary>
        public const float TurnOutAfter = 30f;
        readonly float[] hidingSince = { -1f, -1f }, turnedOutAt = { -1f, -1f };

        /// <summary>This side was turned out of the grounds this match: no protection there, no way back in.</summary>
        public bool TurnedOut(int team) => team >= 0 && team < 2 && turnedOutAt[team] >= 0f;

        /// <summary>The win check's count of what keeps a side alive (structures and Diggers): those outside the
        /// grounds and those inside. Nothing outside for TurnOutAfter: the side is turned out (walked out, its
        /// Diggers then fought like any); still inside TurnOutAfter after that, they no longer count.</summary>
        int KeepsAlive(int team, int outside, int inside)
        {
            if (Sanct == null) return outside + inside;
            if (outside == 0 && inside > 0)
            {
                if (hidingSince[team] < 0f) hidingSince[team] = time;
                else if (turnedOutAt[team] < 0f && time >= hidingSince[team] + TurnOutAfter) TurnOut(team);
            }
            else hidingSince[team] = -1f;
            if (outside == 0 && turnedOutAt[team] >= 0f && time >= turnedOutAt[team] + TurnOutAfter) return 0;
            return outside + inside;
        }

        readonly List<Unit> turnOne = new List<Unit>(1);

        /// <summary>Every foot unit of the side in the grounds is walked out by the nearer gate, hurried.</summary>
        void TurnOut(int team)
        {
            turnedOutAt[team] = time;
            Debug.Log($"[StarForge] team {team} has nothing left outside the Vesper twin house: turned out at {time:0} s");
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u.dying || u.team != team || !OnFoot(u) || !Sanct.Contains(u.pos)) continue;
                int g = (Sanct.Gate(0) - u.pos).sqrMagnitude <= (Sanct.Gate(1) - u.pos).sqrMagnitude ? 0 : 1;
                Vector2 outside = Sanct.ToWorld(Sanctuary.GateX[g], -Sanctuary.HalfZ - Sanctuary.Apron - 5f);
                turnOne.Clear();
                turnOne.Add(u);
                CmdMove(turnOne, outside, false, true);
            }
        }

        /// <summary>A unit of a turned-out side, once out of the grounds, loses the way back in (its mask).</summary>
        void KeepTurnedOut(Unit u)
        {
            if (!TurnedOut(u.team) || u.agent == null || !u.agent.enabled) return;
            int bit = 1 << MapGenerator.SanctuaryArea;
            // Off the aprons too: stripped on an apron, the agent stood on an area it may no longer use and froze.
            if ((u.agent.areaMask & bit) != 0 && Sanct.DistanceToPlan(u.pos) > 1f) u.agent.areaMask &= ~bit;
        }

        /// <summary>How many ruined cloisters this match's cover takes (0 off real matches).</summary>
        int CloistersWanted(uint mapSeed) =>
            CloisterForce >= 0 ? CloisterForce : SanctuaryAllowed ? MapGenerator.CloistersDrawn(mapSeed) : 0;

        /// <summary>What the sight map stops at: the cover, and the house's runs when it stands.</summary>
        List<CoverWall> SightRuns()
        {
            if (sanctRuns.Count == 0) return Walls;
            sightRuns.Clear();
            sightRuns.AddRange(Walls);
            sightRuns.AddRange(sanctRuns);
            return sightRuns;
        }

        /// <summary>A point in the grounds.</summary>
        public bool InGrounds(Vector2 p) => Sanct != null && Sanct.Contains(p);

        /// <summary>No shot between these two: one of them in the grounds, or the line between two on the ground
        /// over it. The sky stays open (the user's call): to or from an aircraft only one inside is refused.</summary>
        public bool SanctBlocks(Unit shooter, Unit target) =>
            Sanct != null && (shooter.inGrounds || target.inGrounds ||
                              !shooter.def.flying && !target.def.flying && Sanct.Crosses(shooter.pos, target.pos));

        /// <summary>No shot along this line (a point aimed at: fire at ground, a lob, blind fire).</summary>
        public bool SanctBlocks(Vector2 from, Vector2 to) => Sanct != null && Sanct.Crosses(from, to);

        /// <summary>Foot units may go in (the user's call): soldiers and the Digger.</summary>
        public static bool OnFoot(Unit u) =>
            u.def.bodyRadius > 0f && u.def.upgradeClass == UnitClass.Infantry || u.def.type == UnitType.Worker;
    }
}
