// ScriptedOpponent.cs — fixed-style opponents for evaluating the adaptive AI.
//
// Each archetype has a recognisable behaviour, so an evaluation can measure
// both whether the AI wins and whether it correctly *identifies* what it is
// playing against. These drive the same public command API as everything else.
using System.Collections.Generic;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed class ScriptedOpponent
    {
        public enum Kind { Rusher = 0, Macro, Turtle, Harasser, Count }

        public readonly Kind kind;
        readonly GameWorld w;
        readonly int team;
        float acc;
        readonly List<Unit> army = new List<Unit>(), raid = new List<Unit>();
        readonly Rng rng = new Rng(99);

        public ScriptedOpponent(GameWorld world, int team, Kind kind)
        {
            w = world;
            this.team = team;
            this.kind = kind;
        }

        public PlayerStrat Truth
        {
            get
            {
                switch (kind)
                {
                    case Kind.Rusher: return PlayerStrat.Rushing;
                    case Kind.Macro: return PlayerStrat.Macro;
                    case Kind.Turtle: return PlayerStrat.Turtling;
                    default: return PlayerStrat.Harassing;
                }
            }
        }

        List<Unit> Own(UnitType t)
        {
            var v = new List<Unit>();
            foreach (var e in w.units)
                if (e != null && !e.dying && e.team == team && e.Type == t && e.Complete) v.Add(e);
            return v;
        }

        public void Update(float dt)
        {
            acc += dt;
            if (acc < 0.5f) return;
            acc = 0f;
            var F = w.factions[team];
            Vector2 home = w.Map.StartPos(team), foe = w.Map.StartPos(1 - team);

            int workers = Own(UnitType.Worker).Count, rax = Own(UnitType.Garrison).Count;
            int fac = Own(UnitType.Workshop).Count, ccs = Own(UnitType.Foundry).Count;
            int pending = 0;
            foreach (var e in w.units) if (e != null && !e.dying && e.team == team && !e.Complete) pending++;

            int wantWorkers = kind == Kind.Macro ? 22 : (kind == Kind.Rusher ? 10 : 16);
            int wantRax = kind == Kind.Rusher ? 3 : (kind == Kind.Macro ? 1 : 2);
            int wantFac = kind == Kind.Turtle ? 2 : (kind == Kind.Macro ? 1 : 0);

            bool PlaceAndBuild(UnitType what)
            {
                var ws = Own(UnitType.Worker);
                if (ws.Count == 0) return false;
                for (int i = 0; i < 24; i++)
                {
                    float a = rng.F01() * TAU, r = rng.Range(10f, 26f);
                    var p = home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (w.CanPlace(what, p)) return w.CmdBuild(new List<Unit> { ws[0] }, what, p);
                }
                return false;
            }

            if (F.supplyCap - F.supplyUsed < 5 && F.ore >= 100 && pending == 0) { PlaceAndBuild(UnitType.Bunkhouse); return; }
            if (rax < wantRax && F.ore >= 150 && pending == 0) { PlaceAndBuild(UnitType.Garrison); return; }
            if (rax >= 1 && fac < wantFac && F.ore >= 200 && pending == 0) { PlaceAndBuild(UnitType.Workshop); return; }
            if (kind == Kind.Macro && ccs < 2 && F.ore >= 400 && pending == 0) { PlaceAndBuild(UnitType.Foundry); return; }

            foreach (var e in w.units.ToArray())
            {
                if (e == null || e.dying || e.team != team || !e.Complete) continue;
                if (e.Type == UnitType.Foundry && workers < wantWorkers && e.queue.Count == 0) w.CmdTrain(e, UnitType.Worker);
                else if (e.Type == UnitType.Garrison && e.queue.Count < 2) w.CmdTrain(e, UnitType.Trooper);
                else if (e.Type == UnitType.Workshop && e.queue.Count < 2) w.CmdTrain(e, UnitType.Mauler);
            }

            var idle = new List<Unit>();
            foreach (var e in w.units)
                if (e != null && !e.dying && e.team == team && e.Type == UnitType.Worker && e.order == Order.Idle) idle.Add(e);
            if (idle.Count > 0)
                foreach (var n in w.units)
                    if (n != null && !n.dying && n.Type == UnitType.Ore && n.oreLeft > 0 && (n.pos - home).magnitude < 45f)
                    { w.CmdHarvest(idle, n); break; }

            army.Clear();
            army.AddRange(Own(UnitType.Trooper));
            army.AddRange(Own(UnitType.Mauler));
            if (army.Count == 0) return;
            Vector2 guard = home + Norm(foe - home) * 12f;

            switch (kind)
            {
                case Kind.Rusher:
                    if (army.Count >= 5) w.CmdMove(army, foe, true);
                    break;
                case Kind.Macro:
                    w.CmdMove(army, army.Count >= 14 ? foe : home + Norm(foe - home) * 14f, true);
                    break;
                case Kind.Turtle:
                    w.CmdMove(army, guard, true);
                    break;
                case Kind.Harasser:
                {
                    raid.RemoveAll(u => !Unit.Live(u));
                    foreach (var h in army)
                    {
                        if (raid.Count >= 3) break;
                        if (!raid.Contains(h)) raid.Add(h);
                    }
                    if (raid.Count > 0) w.CmdMove(raid, foe, true);
                    var rest = new List<Unit>();
                    foreach (var h in army) if (!raid.Contains(h)) rest.Add(h);
                    if (rest.Count > 0) w.CmdMove(rest, guard, true);
                    break;
                }
            }
        }
    }
}
