// MechArmoury.cs — picks a Mech's parts when it is called down.
//
// A side's Mech is not drawn at the start of the match any more: only its pilot is (the
// bay names who is coming). When the drop is called, nine seconds before it lands, the
// pilot's order fits the Mech out for the field it is dropping into. It draws a dozen
// designs from the generator -- each a legal 100-point build off the fixed parts list --
// and weighs each against what the enemy fields right now: how much of it is infantry,
// how much armour, how much of it is built, and whether an enemy Mech is on the field or
// coming. A crowd of Troopers calls for flame and splash, a column of Maulers for lasers
// and railguns, a base full of towers for the siege mortar. It then picks by weighted
// chance rather than always the best, so two matches against the same army still differ,
// and it avoids repeating the other side's Mech. The order sees the whole field, as the
// Mech's own AI does (MechBrain): this is not the opponent AI, which never reads it.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public static class MechArmoury
    {
        /// <summary>Designs drawn and weighed for each drop.</summary>
        public const int Candidates = 12;
        /// <summary>How hard the pick leans on the best-scored designs: the weight of a
        /// candidate is (score / best) to this power.</summary>
        public const float Sharpness = 3.5f;

        /// <summary>The enemy's forces, as value (ore) in each class.</summary>
        public struct Threat
        {
            public float light, heavy, structures, mech;
            public bool enemyMech;

            public override string ToString()
            {
                float sum = Mathf.Max(1f, light + heavy + structures + mech);
                return $"infantry {100f * light / sum:0}%, armour {100f * heavy / sum:0}%, Mech {100f * mech / sum:0}%, structures {100f * structures / sum:0}%";
            }
        }

        public static Threat Read(GameWorld w, int team)
        {
            var t = new Threat();
            foreach (var u in w.units)
            {
                if (u == null || u.dying || u.team == team || u.team > 1) continue;
                switch (u.Type)
                {
                    case UnitType.Mech: t.mech += 3200f; t.enemyMech = true; break;
                    case UnitType.Mauler: t.heavy += u.def.cost; break;
                    case UnitType.Trooper: case UnitType.Skimmer: t.light += u.def.cost; break;
                    case UnitType.Worker: t.light += u.def.cost * 0.4f; break;
                    default:
                        if (u.def.building) t.structures += u.def.cost * (u.def.Armed ? 1.5f : 0.5f);
                        break;
                }
            }
            // Their Mech still to come: a bay standing with nothing dropped yet.
            if (team < 2)
            {
                var foe = w.factions[1 - team];
                if (!t.enemyMech && Unit.Live(foe.bay) && !foe.mechDropped && !foe.mechLost) { t.mech += 1800f; t.enemyMech = true; }
            }
            return t;
        }

        /// <summary>Fit out <paramref name="identity"/>'s Mech against <paramref name="threat"/>.
        /// <paramref name="other"/> is the other side's Mech if it has been fitted out, which
        /// this one avoids copying; <paramref name="minReserve"/> is how many Extra Hardpoints
        /// its side has already paid for.</summary>
        public static MechDesign Compose(MechDesign identity, Threat threat, MechDesign other, int minReserve, StringBuilder log = null)
        {
            uint seed = identity.seed * 2654435761u ^ 0xA2B3C4D5u;
            var r = new Rng(seed == 0 ? 1u : seed);
            var cands = new List<MechDesign>(Candidates);
            for (int k = 0; k < Candidates * 3 && cands.Count < Candidates; k++)
            {
                var d = MechParts.Generate(seed + (uint)(k + 1) * 0x9E3779B1u);
                if (d.reserve.Count < minReserve) continue;
                cands.Add(d);
            }
            if (cands.Count == 0) cands.Add(MechParts.Generate(seed ^ 0x5555u));

            // Shares of the threat, none below a floor: a Mech must still answer what the
            // enemy might field next, not only what it fields now.
            float L = threat.light, H = threat.heavy + threat.mech, S = threat.structures * 0.35f;
            float sum = L + H + S;
            if (sum < 1f) { L = 1f; H = 1f; S = 0.3f; sum = 2.3f; }
            float l = Mathf.Max(0.12f, L / sum), h = Mathf.Max(0.12f, H / sum), st = S / sum;
            float norm = l + h + st;
            l /= norm; h /= norm; st /= norm;

            var scores = new float[cands.Count];
            float best = 0f;
            for (int i = 0; i < cands.Count; i++)
            {
                scores[i] = Score(cands[i], l, h, st, other);
                best = Mathf.Max(best, scores[i]);
            }
            var weights = new float[cands.Count];
            for (int i = 0; i < cands.Count; i++) weights[i] = Mathf.Pow(scores[i] / Mathf.Max(1e-3f, best), Sharpness);
            int pick = Pick(r, weights);

            var chosen = cands[pick];
            chosen.pilot = identity.pilot;
            chosen.callsign = identity.callsign;
            chosen.order = identity.order;
            if (log != null)
            {
                int rank = 1;
                for (int i = 0; i < cands.Count; i++) if (scores[i] > scores[pick]) rank++;
                log.Append($"threat {threat}; picked {chosen.Loadout()} (rank {rank} of {cands.Count}, score {scores[pick] / best:0.00} of the best)");
            }
            return chosen;
        }

        /// <summary>What a design is worth against a threat of these shares: its guns
        /// against that mix (splash and cones count for more against a crowd), times how
        /// long it lasts, less if it has no answer at all to part of it, and less again if it
        /// is the other side's Mech over again.</summary>
        public static float Score(MechDesign d, float l, float h, float st, MechDesign other)
        {
            float offense = 0f;
            foreach (var w in d.weapons)
            {
                var p = MechParts.Weapon(w);
                float raw = p.damage * p.burst / Mathf.Max(0.05f, p.cooldown);
                float vsLight = p.Dps(false) * (p.antiGroup ? 1f + 2.5f * l : 1f);
                // Tanks and Mechs outrange a flame: against them it rarely gets to fire.
                float vsHeavy = p.Dps(true) * (p.range < 20f ? 0.5f : 1f);
                offense += vsLight * l + vsHeavy * h + raw * p.vsStructure * st;
            }
            if (!MechParts.Covers(d, true)) offense *= 1f - 0.6f * l;
            if (!MechParts.Covers(d, false)) offense *= 1f - 0.6f * h;

            var frame = d.Frame;
            var loco = d.Loco;
            float armour = Mathf.Clamp(frame.armour + loco.armour, 0f, 0.5f);
            float hp = frame.hp * loco.hpMul * (1f + d.spare * MechParts.SparePointHp) / (1f - armour);
            if (d.utility == MechUtility.Shield) hp += MechParts.ShieldCapacity * 1.5f;
            else if (d.utility == MechUtility.Nanites) hp += 300f;
            else if (d.utility == MechUtility.Reactive) hp += 250f;
            float score = offense * Mathf.Pow(hp / 2000f, 0.5f);

            if (other != null)
            {
                if (other.locomotion == d.locomotion && other.frame == d.frame) score *= 0.55f;
                else if (other.locomotion == d.locomotion) score *= 0.85f;
                int shared = 0;
                foreach (var w in d.weapons) if (other.weapons.Contains(w)) shared++;
                score *= 1f - 0.08f * shared;
            }
            return score;
        }

        static int Pick(Rng r, float[] weights)
        {
            float sum = 0f;
            foreach (var w in weights) sum += w;
            float x = r.F01() * sum;
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x <= 0f) return i;
            }
            return weights.Length - 1;
        }
    }
}
