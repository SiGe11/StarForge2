// TacticalRules.cs -- the one switch for the new combat, and its numbers (README "Tactical combat").
//
// Off, the game plays exactly as it did: nothing draws from the new random stream, sight is the old
// circle and Defs hands out the assets themselves. On, Defs hands out copies carrying these numbers,
// so nothing that reads def.range or def.speed needs to know. The old combat goes, and these numbers
// move into the assets, at the switch-over.
using UnityEngine;

namespace StarForge.Sim
{
    public enum Posture : byte { Stand, Crouch, Prone }

    public static class TacticalRules
    {
        /// <summary>Read when a match begins (GameWorld.BeginMatch). A static survives play sessions:
        /// whoever turns it on for a run turns it off after.</summary>
        public static bool On;

        /// <summary>How far a small-arms round strays from the aim, standard deviation in radians, for a
        /// shooter standing still at a target standing still. Hit rate falls with range by itself: a
        /// body covers a smaller angle further off.</summary>
        public const float RifleSpread = 0.018f;
        /// <summary>A soldier's own width to a round, whichever way he faces.</summary>
        public const float InfantryBody = 0.35f;

        /// <summary>This definition, under the new rules.</summary>
        public static void Apply(UnitDef d)
        {
            if (d.type == UnitType.Trooper)
            {
                float old = d.damage;
                d.speed = 3.5f;
                d.range = 35f;
                d.sight = 45f;
                d.damage = 16f;          // 55 hp: dead in four hits, three from a veteran
                d.shotSpread = RifleSpread;
                d.bodyRadius = InfantryBody;
                // The same weight as before on what is not a soldier: the round grew for the
                // rifleman's sake, and tanks, walls, Diggers and creatures are hit as they were.
                float keep = old / d.damage;
                d.vsLight *= keep;
                d.vsHeavy *= keep;
                d.vsStructure *= keep;
                d.vsAir *= keep;
                d.vsSoldier = 1f / keep;
            }
            // Everything that fights has the eyes the new ranges need (its own numbers come in their phases).
            if (d.Armed && !d.harvester && d.sight < 45f) d.sight = 45f;
        }
    }
}
