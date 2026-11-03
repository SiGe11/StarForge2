// GameWorld.Cheat.cs — the testing cheat (Ctrl or Cmd + Shift + M, README) and its price.
//
// Each press gives ore and supply and turns the side's fast training on or off. A match it
// was used in teaches the AI's memory nothing, and its mark stays on the player's screen.
//
// And the cheater cannot win. Where the enemy's last structure and Digger would have made it
// the winner, the reckoning begins instead: most of its army throws down its oath and runs for
// the camp (GameWorld.Morale), and every animal on the map (View/Fauna enlists them) comes for
// what is left until the side has lost. The animals are not units: nothing can target or hurt
// them, and they never touch a deserter. Only a match that used this key ever gets here.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StarForge.Sim;

namespace StarForge.World
{
    /// <summary>An animal come for the cheater. Lives in the simulation (the bites are real);
    /// View/Fauna draws one of the map's animals where it is.</summary>
    public sealed class Avenger
    {
        public int id;
        public bool flies;
        public float speed, cooldown, retarget, pathAt;
        /// <summary>It raises its head and comes on at this match time: they come one after another,
        /// over some seconds, and the player gets to watch it coming.</summary>
        public float wakeAt;
        public Vector2 pos, heading;
        public bool moving;
        public Unit target;
        /// <summary>The match time of its last bite, for the view's lunge.</summary>
        public float bitAt = -10f;
        public Vector3[] corners = new Vector3[16];
        public int cornerCount, corner;
    }

    public sealed partial class GameWorld
    {
        public const int CheatOreGift = 5000, CheatSupplyGift = 100;
        public const float CheatTrainRate = 5f;
        /// <summary>The share of a cheater's units that desert when the reckoning begins.</summary>
        public const float ReckoningDeserters = 0.6f;
        /// <summary>Most things die to one bite and a Foundry to three.</summary>
        public const float BiteDamage = 600f, BiteEvery = 1.1f;
        const float BiteReach = 1.2f;
        /// <summary>With no animals to enlist (-sfnofauna) the side must still lose.</summary>
        const int AvengersAtLeast = 12;

        /// <summary>The side the animals have come for, or -1.</summary>
        public int ReckoningTeam { get; private set; } = -1;
        public bool Reckoning => ReckoningTeam >= 0;
        public readonly List<Avenger> avengers = new List<Avenger>();
        NavMeshPath avengerPath;
        bool reckoningFilled;
        uint matchSeed;

        /// <summary>How fast a side's training runs.</summary>
        public float TrainRate(int team) => team < 2 && factions[team].fastTrain ? CheatTrainRate : 1f;

        /// <summary>The cheat's key: ore and supply every press; fast training turned on, then off.</summary>
        public void Cheat(int team)
        {
            var F = factions[team];
            F.ore += CheatOreGift;
            F.cheatSupply += CheatSupplyGift;
            F.fastTrain = !F.fastTrain;
            F.cheater = true;
            cheated = true;
            Raise(new GameEvent { kind = GameEventKind.Notice, team = team,
                text = $"+{CheatOreGift} ore, +{CheatSupplyGift} supply, " + (F.fastTrain ? $"training x{CheatTrainRate:0}" : "training back to normal") });
        }

        void BeginReckoning(int t)
        {
            ReckoningTeam = t;
            avengers.Clear();
            reckoningFilled = false;
            Desertions(t);
            // View/Fauna enlists the map's animals on this, there and then.
            Raise(new GameEvent { kind = GameEventKind.Reckoning, team = t });
        }

        /// <summary>The share of all it has that can walk away does. Aircraft cannot (they have no
        /// way to land among the camp) but count in the share, so what walks makes it up. The Mech
        /// takes no oath and is not counted.</summary>
        void Desertions(int t)
        {
            moraleScratch.Clear();
            int all = 0;
            foreach (var u in units)
            {
                if (!Unit.Live(u) || u.team != t || u.def.building || u.def.neutral || u.deserted || u.def.Autonomous || !u.Complete) continue;
                all++;
                if (!u.def.flying) moraleScratch.Add(u);
            }
            int n = Mathf.Min(moraleScratch.Count, Mathf.CeilToInt(all * ReckoningDeserters));
            if (n == 0) return;
            var M = morale[t];
            EnsureCamp(t);
            System.Array.Clear(whoCount, 0, whoCount.Length);
            // Its own stream, never the match's. (A cheater's match does not replay exactly from here on:
            // the animals start where View/Fauna has them, which goes by the frame rate. Accepted: such a
            // match is over, and nothing measures it.)
            var pick = new Rng(matchSeed ^ 0xC4EA7u);
            Unit first = null;
            for (int k = 0; k < n; k++)
            {
                int i = (int)(pick.Next() % (uint)moraleScratch.Count);
                var u = moraleScratch[i];
                moraleScratch.RemoveAt(i);
                MakeDeserter(u);
                if (first == null) first = u;
                whoCount[(int)u.Type]++;
            }
            M.episodes++;
            M.mechAtBreak = MechOf(t) is Unit mm && mm.mech.Landed;
            M.deserted += n;
            M.lastEpisode = time;
            Raise(new GameEvent
            {
                kind = GameEventKind.Desertion, team = t, unit = first, type = first.Type, index = n,
                pos = first.Ground, end = map.Ground(M.camp), text = Who(whoCount)
            });
        }

        /// <summary>One of the map's animals joins the hunt, where it stands (View/Fauna).</summary>
        public Avenger Enlist(Vector2 at, bool flies, float speed)
        {
            var a = new Avenger { id = avengers.Count, pos = at, flies = flies, speed = speed, heading = Vector2.up,
                                  cooldown = BiteEvery * (avengers.Count % 4) * 0.25f, wakeAt = time + 1.5f + (avengers.Count % 9) * 1.1f };
            avengers.Add(a);
            return a;
        }

        void TickReckoning(float dt)
        {
            if (!reckoningFilled)
            {
                reckoningFilled = true;
                for (int i = avengers.Count; i < AvengersAtLeast; i++)
                {
                    float angle = i * 2.399f;
                    Vector2 at = map.StartPos(ReckoningTeam) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 90f;
                    Enlist(new Vector2(Mathf.Clamp(at.x, 4f, MapSize - 4f), Mathf.Clamp(at.y, 4f, MapSize - 4f)), i % 4 == 0, 11f);
                }
            }
            if (avengerPath == null) avengerPath = new NavMeshPath();
            foreach (var a in avengers)
            {
                a.cooldown -= dt;
                a.moving = false;
                if (time < a.wakeAt) continue;
                if (!Prey(a, a.target) || (a.retarget -= dt) <= 0f)
                {
                    var was = a.target;
                    a.target = NearestPrey(a);
                    a.retarget = 0.75f;
                    if (a.target != was) a.pathAt = 0f;
                }
                if (a.target == null) continue;
                Vector2 to = a.target.pos - a.pos;
                float d = to.magnitude;
                if (d > 0.01f) a.heading = to / d;
                if (d <= a.target.def.radius + BiteReach)
                {
                    if (a.cooldown <= 0f) Bite(a);
                    continue;
                }
                Step(a, dt);
            }
        }

        bool Prey(Avenger a, Unit u) =>
            Unit.Live(u) && u.team == ReckoningTeam && !u.deserted && !u.Untargetable && (a.flies || !u.def.flying);

        Unit NearestPrey(Avenger a)
        {
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var u in units)
            {
                if (!Prey(a, u)) continue;
                // A little different for each animal, so a pack spreads instead of all taking one rifleman.
                float spread = 1f + 0.35f * Mathf.Repeat((a.id * 7 + u.id * 13) * 0.618f, 1f);
                float score = ((u.pos - a.pos).magnitude - u.def.radius) * spread;
                if (score < bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        void Bite(Avenger a)
        {
            a.cooldown = BiteEvery;
            a.bitAt = time;
            var t = a.target;
            Vector3 at = t.def.building ? map.Ground(a.pos) + Vector3.up * 1.2f + new Vector3(a.heading.x, 0f, a.heading.y) * 1.2f : AimPoint(t);
            // No shooter to credit: thrown from where the animal stands.
            Damage(t, BiteDamage, a.pos, null, true);
            Raise(new GameEvent { kind = GameEventKind.AnimalBite, team = ReckoningTeam, type = t.Type, pos = at,
                                  dir = new Vector3(a.heading.x, 0f, a.heading.y) });
        }

        /// <summary>A flyer goes straight. A ground animal keeps to the ground units use, and when
        /// there is no way there (a target on a terrace, the far side of a carved base) it comes on
        /// over whatever is in the way: it must always arrive, or the side would never lose.</summary>
        void Step(Avenger a, float dt)
        {
            Vector2 goal = a.target.pos;
            if (!a.flies)
            {
                if ((a.pathAt -= dt) <= 0f)
                {
                    a.pathAt = 1.2f + 0.1f * (a.id % 5);
                    a.cornerCount = a.corner = 0;
                    if (NavMesh.SamplePosition(map.Ground(a.pos), out var from, 4f, GroundAreas) &&
                        NavMesh.SamplePosition(map.Ground(goal), out var end, a.target.def.radius + 4f, GroundAreas) &&
                        NavMesh.CalculatePath(from.position, end.position, GroundAreas, avengerPath) &&
                        avengerPath.status == NavMeshPathStatus.PathComplete)
                    {
                        a.cornerCount = avengerPath.GetCornersNonAlloc(a.corners);
                        a.corner = 1;
                    }
                }
                while (a.corner < a.cornerCount)
                {
                    var c = new Vector2(a.corners[a.corner].x, a.corners[a.corner].z);
                    if ((c - a.pos).sqrMagnitude > 0.25f) { goal = c; break; }
                    a.corner++;
                }
            }
            Vector2 to = goal - a.pos;
            float len = to.magnitude;
            if (len < 1e-3f) return;
            a.heading = to / len;
            a.pos += a.heading * Mathf.Min(len, a.speed * dt);
            a.moving = true;
        }
    }
}
