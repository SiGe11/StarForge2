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
        /// <summary>A trial's: nobody errs. Off at every play entry (BugTrial.ClearOnPlay).</summary>
        public static bool NoMistakes;

        /// <summary>How often a man, or a squad, gets it wrong, by rank (his own or the oldest hand with him).</summary>
        public static float MistakeChance(int rank) => rank <= 0 ? 0.25f : rank == 1 ? 0.16f : rank == 2 ? 0.09f : 0.04f;

        /// <summary>How far a small-arms round strays from the aim, standard deviation in radians, for a
        /// shooter standing still at a target standing still. Hit rate falls with range by itself: a
        /// body covers a smaller angle further off.</summary>
        public const float RifleSpread = 0.018f;
        /// <summary>A soldier's own width to a round, whichever way he faces.</summary>
        public const float InfantryBody = 0.35f;

        // What a soldier carries besides his gun (Unit.SideArms, GameWorld.Grenades).
        public const float PistolRange = 15f, PistolSpread = 0.035f, PistolDamage = 9f, PistolCooldown = 0.55f;
        public const float KnifeReach = 1.7f, KnifeDamage = 45f, KnifeCooldown = 1f;
        public const float FragReach = 6f, FragDamage = 110f, FragFuse = 1f, FragMin = 9f, FragMax = 24f;
        public const float SmokeRadius = 6f, SmokeLasts = 20f;
        public const float FlashReach = 6.5f, FlashFuse = 0.5f, FlashStun = 2.5f, FlashMin = 9.5f, FlashMax = 18f;
        public const float RestockReach = 20f;

        public static void Loadout(UnitType t, out int frags, out int smokes, out int flashes)
        {
            frags = smokes = flashes = 0;
            if (t == UnitType.Trooper) { frags = 2; smokes = 1; flashes = 1; }
            else if (t == UnitType.Marksman || t == UnitType.Thumper || t == UnitType.Scorcher || t == UnitType.Rocketeer || t == UnitType.Gunner) { frags = 1; smokes = 1; }
        }

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
                // Twenty rounds and two seconds to reload in the 14 s twenty took before: the same weight of fire.
                d.cooldown = 0.6f;
                d.magazine = 20;
                d.reloadTime = 2f;
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
            else if (d.type == UnitType.Marksman)
            {
                d.speed = 3.5f;
                d.range = 55f;
                d.sight = 55f;
                d.shotSpread = RifleSpread * 0.35f;
                d.cooldown = 2.6f;       // five rounds and three seconds in the 16 s five took before
                d.magazine = 5;
                d.reloadTime = 3f;
                d.bodyRadius = InfantryBody;
                d.vsSoldier = 1f;
                d.pistolInside = 8f;
            }
            else if (d.type == UnitType.Thumper)
            {
                d.speed = 3.5f;
                d.range = 45f;
                d.bodyRadius = InfantryBody;
                d.pistolInside = Mathf.Max(d.minRange, 7f);
                d.cooldown = 0.2f;       // a bomb at a time: the old wait, as a reload
                d.magazine = 1;
                d.reloadTime = 2.6f;
            }
            else if (d.type == UnitType.Scorcher)
            {
                d.speed = 3.5f;
                d.range = 12f;
                d.magazine = 40;         // a tank's worth of jet, then the valves
                d.reloadTime = 2.5f;
                d.bodyRadius = InfantryBody;
                d.needsLine = true;
            }
            else if (d.type == UnitType.Rocketeer)
            {
                d.speed = 3.5f;
                d.range = 35f;
                d.bodyRadius = InfantryBody;
                d.needsLine = true;
                d.pistolInside = 6f;
                d.cooldown = 0.2f;       // a rocket at a time: the old wait, as a reload
                d.magazine = 1;
                d.reloadTime = 1.7f;
            }
            else if (d.type == UnitType.Gunner)
            {
                float old = d.damage;
                d.speed = 3f;
                d.range = 40f;
                d.sight = 45f;
                d.damage = 9f;
                d.cooldown = 0.18f;
                d.shotSpread = RifleSpread * 1.6f;
                d.bodyRadius = InfantryBody;
                d.magazine = 40;
                d.reloadTime = 4f;
                d.suppressMul = 1.8f;
                float keep = old / d.damage;
                d.vsLight *= keep;
                d.vsHeavy *= keep;
                d.vsStructure *= keep;
                d.vsAir *= keep;
                d.vsSoldier = 1f / keep;
                d.pistolInside = 5f;
            }
            // Everything that fights has the eyes the new ranges need (its own numbers come in their phases).
            if (d.Armed && !d.harvester && d.sight < 45f) d.sight = 45f;
        }
    }
}
