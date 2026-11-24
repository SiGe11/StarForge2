// TacticalRules.cs -- the combat's numbers (README "Tactical combat"); the old combat and its switch were
// deleted at phase 8. Defs hands out copies of the unit assets carrying these numbers (Apply), so nothing
// that reads def.range or def.speed needs to know, and the assets keep the old ones.
using UnityEngine;

namespace StarForge.Sim
{
    public enum Posture : byte { Stand, Crouch, Prone }

    public static class TacticalRules
    {
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
        /// <summary>Phase 6: a tank's mounted machine gun (a round decided when fired, from its roof) and its two
        /// smoke launchers, popped as an anti-tank round comes at it and topped up by a depot or a Workshop.</summary>
        public const float TankGunRange = 30f, TankGunSpread = 0.045f, TankGunDamage = 8f, TankGunCooldown = 0.2f, TankGunHeight = 1.9f;
        public const int TankSmokes = 2;
        /// <summary>A tank keeps this far beyond an anti-tank man's reach; a worker runs from an enemy this near; a
        /// creature breaks off under this share of its hull. `NoTankTactics`: the trials' A/B (they clear it).</summary>
        public const float TankBackMargin = 5f, WorkerFleeReach = 20f, CreatureBreakAt = 0.3f, WorkerStaysNear = 25f;
        public static bool NoTankTactics;
        /// <summary>Phase 7, the Swift's flight: speed between `PlaneMin` and `PlaneMax`, easing to `PlaneCruise`; a
        /// hard turn bleeds speed, a dive gives it; height 6-14 m over the ground (dip -2..+1.6 under its 9 m, 7.4-11 m: the view holds it at 0.8 of its height or more). Missiles
        /// (Swift x2) and flares (Swift and Kestrel x2), one of each back every 10 s within `AirRearmReach` of an Airfield.</summary>
        public const float PlaneCruise = 13.5f, PlaneMin = 10f, PlaneMax = 17f, PlaneDipLow = 1.6f;
        public const float MissileSpeed = 35f, MissileDamage = 30f, MissileLockMin = 15f, MissileLockMax = 45f, MissileCone = 0.52f, MissileLockTime = 1f;
        public const float FlareDecoy = 0.6f, AirRearmReach = 25f, CannonHitBase = 0.35f, CannonVsAir = 0.7f;
        public const int AirMissiles = 2, AirFlares = 2;
        /// <summary>The tanks: the Workshop's armour with a main gun (not the Hauler's or the Prospector's hulls,
        /// which IsTankLike counts as cover).</summary>
        public static bool IsTank(UnitType t) => t == UnitType.Mauler || t == UnitType.Lancer || t == UnitType.Aegis || t == UnitType.Ironclad;
        /// <summary>Guns a tank pops its smoke for.</summary>
        public static bool AntiArmour(WeaponKind w) => w == WeaponKind.Bazooka || w == WeaponKind.Lance || w == WeaponKind.Cannon || w == WeaponKind.Battery;
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
            // Phase 8: the creatures meet tanks' machine guns and break off when hurt, so they are hardier.
            if (d.organic) d.hp *= CreatureHardier;
            // Phase 8's range pass: a tower out-reaches a rifleman at its edge (a Trooper's 35 m to its 1.7 m edge is
            // 36.7 from its middle); the Mauler out-reaches towers and riflemen as artillery (38: a Trooper reaches its edge from 36.5 m); the other tanks
            // (30) are still out-reached by rifles at their edge, and an idle one does not answer.
            switch (d.type)
            {
                case UnitType.Sentinel: d.range = Mathf.Max(d.range, 37f); d.sight = Mathf.Max(d.sight, 45f); break;
                case UnitType.Mauler: d.range = Mathf.Max(d.range, 38f); break;
                case UnitType.Lancer: case UnitType.Aegis: case UnitType.Ironclad: d.range = Mathf.Max(d.range, 30f); break;
            }
        }

        // ---- The user's play, 2026-11-23 (README "Tactical combat"). A/B switches, off at each play entry (ResetSwitches).
        /// <summary>Old rounds: a homing round tested for a hit only where a step ends (it looped round a target at low
        /// frame rates and sped-up clocks, up to its 2 s life), and small arms at aircraft as real homing rounds (all but
        /// certain to hit).</summary>
        public static bool LegacyRounds;
        /// <summary>Old smoke: two charges a tank, back every 10 s; a squad's smoke every 15 s and in the open at any
        /// suppression over 0.3; no blind fire into it.</summary>
        public static bool LegacySmoke;
        /// <summary>Old Swift: no rockets.</summary>
        public static bool LegacyAir;
        /// <summary>Old Mech: guns at 0.28 and no more against soldiers than against anything light.</summary>
        public static bool LegacyMechPower;
        /// <summary>Old Mech senses: concealment, smoke, trees and the lie of the ground hid things from it as from anyone.</summary>
        public static bool LegacyMechSensors;
        /// <summary>Old Mech reach: its guns' pre-tactical ranges (most out-reached by a Mauler's 38 m and a rifle's 35).</summary>
        public static bool LegacyMechReach;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSwitches() { LegacyRounds = LegacySmoke = LegacyAir = LegacyMechPower = LegacyMechSensors = LegacyMechReach = false; }

        /// <summary>The Mech's guns under the new combat: 1.3x their reach, never past its own sight (flamers as they were).</summary>
        public const float MechReachMul = 1.3f;

        /// <summary>A homing round lives its straight flight there and half again, and a quarter second.</summary>
        public const float HomingLifeMul = 1.5f, HomingLifeAdd = 0.25f;
        /// <summary>Small arms at an aircraft, decided when fired: the round's error against the share of the
        /// aircraft's radius a round must pass within (ShotSpread already widens the error with the target's speed).</summary>
        public const float AirBodyShare = 0.8f;
        /// <summary>Smoke used less: a tank's two charges back one per 30 s (one charge left it 70% more hurt by rocket men); a squad's smoke every 30 s, and in the open
        /// only when he is nearly pinned.</summary>
        public const int TankSmokesNew = 2;
        public const float TankSmokeRefill = 30f, SmokeSquadWait = 30f, SmokeFreeSuppression = 0.45f;
        public static int TankSmokeCharges => LegacySmoke ? TankSmokes : TankSmokesNew;
        public static float TankSmokeRefillNow => LegacySmoke ? 10f : TankSmokeRefill;
        /// <summary>The counter to smoke (the field manuals: shoot into or through it, and put indirect fire on it): for
        /// a while after an enemy is lost in smoke a rifle or machine gun fires at where its side last saw him, slower and
        /// wider; a mortar lobs a few bombs scattered round that point.</summary>
        public const float BlindFireFor = 6f, BlindSpreadMul = 3f, BlindCadenceMul = 1.5f, BlindLobScatter = 2.5f;
        public const int BlindLobs = 3;
        /// <summary>The player's "Fire at ground" (GameWorld.CmdFireAt): rifles and machine guns at the point this long,
        /// mortars this many bombs.</summary>
        public const float FireAtLasts = 12f;
        public const int FireAtLobs = 5;
        /// <summary>The Swift's rockets: a pair on a run at a ground target, once its nose is on it at 10-32 m; four in
        /// all, a pair back every 10 s by an Airfield. Unguided: each lands where it was aimed.</summary>
        public const int SwiftRockets = 4;
        public const float SwiftRocketDamage = 24f, SwiftRocketSplash = 1.8f, SwiftRocketSpeed = 40f, SwiftRocketMin = 10f,
            SwiftRocketMax = 32f, SwiftRocketCone = 0.3f, SwiftRocketEvery = 1.2f;
        /// <summary>The Mech, a little stronger and most against infantry (the new combat's cover and spotting took the
        /// most from it there: a bare Mech fell to ~6 riflemen against ~10 before).</summary>
        public const float MechGunScale = 0.30f, MechVsSoldier = 1.3f;

        /// <summary>Phase 8's balance: creatures' hull under the new combat; a tank's machine gun by type (the Aegis
        /// and Ironclad won 12 of 16 duels with it at full weight).</summary>
        public const float CreatureHardier = 1.3f;
        public static float TankGunMul(UnitType t) => t == UnitType.Aegis || t == UnitType.Ironclad ? 0.6f : 1f;
    }
}
