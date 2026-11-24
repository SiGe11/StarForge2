// MechBrain.Night.cs -- the knight's verdict as its order leans it, and the Night of the Quenching.
using StarForge.World;
using UnityEngine;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed partial class MechBrain
    {
        // ------------------------------------------------------------ the verdict, by order
        /// <summary>How far an order's repute leans the verdict, and how sure it can get
        /// (docs/lore/starforge-lore.md section 7: "by repute the Sable Oath never spares and the
        /// Emberguard seldom punishes"). A lean, not a rule: the knight's temper, pride and mood
        /// still decide, and a warm Sable knight can spare, a cold Emberguard punish.</summary>
        public static void OrderLean(string order, out float tilt, out float cap)
        {
            cap = 0.9f;
            switch (order)
            {
                case "the Sable Oath": tilt = 0.70f; cap = 0.98f; break;                  // hardest on the oathbroken
                case "the Order of the Broken Crown": tilt = 0.12f; break;                  // proud
                case "the Ashen Covenant": tilt = 0.10f; break;                             // oldest, cold
                case "the Wardens of the Long Night": tilt = 0.06f; break;                  // stoic
                case "the Ninth Vigil": tilt = 0.04f; break;                                // kept the watch
                case "the Brazen Host": tilt = 0f; break;                                   // loud, not harsh
                case "the Order of the Iron Lantern": tilt = -0.06f; break;                 // wayfinders
                case "the Emberguard": tilt = -0.32f; break;                                // warm
                default: tilt = 0f; break;                                                  // no design: as before
            }
        }

        /// <summary>The chance it goes after its deserters (one punishRng draw decides): the old
        /// will plus its order's lean, clamped to 0.03 .. the order's cap. Pure: no random number.</summary>
        public static float PunishWill(string order, float temper, float pride, float mood)
        {
            OrderLean(order, out float tilt, out float cap);
            float will = 0.12f + 0.4f * Mathf.Max(0f, -temper) - 0.1f * Mathf.Max(0f, temper)
                       + 0.15f * pride + 0.35f * Saturate(-mood * 2f) + tilt;
            return Mathf.Clamp(will, 0.03f, cap);
        }

        // ------------------------------------------------------------ the Quenching
        bool truceSaid, truceDocking;
        float lastTruceT = -1f;

        /// <summary>For trials: it is out after its deserters now (set off, not merely decided).</summary>
        public bool Pursuing => pursuing;

        /// <summary>A think during the truce (GameWorld.Truce): no calls and no hunt; it holds its guard
        /// post. The purge is off, and its deserter clocks wait for the truce, so the verdict and the
        /// give-up come after lauds as they would have. The player's knight says so once.</summary>
        void KeepTruce(Unit m)
        {
            float gap = lastTruceT >= 0f ? w.time - lastTruceT : 0f;
            lastTruceT = w.time;
            if (gap > 2f) gap = 0f;   // the first think of a truce (or a long hitch): nothing to hold over
            if (punishing)
            {
                w.SetPurge(team, false);
                // Every deserter clock waits for lauds: at the 150 s pursuit limit a Mech pursuing at the
                // darkening would otherwise have timed out the moment the truce ended.
                waitFrom += gap;
                if (lastPursueT >= 0f) lastPursueT += gap;
                if (freeSince >= 0f) freeSince += gap;
            }
            if (verdictAt >= 0f) verdictAt += gap;
            if (m == null || !m.mech.Landed) return;
            var core = m.mech;
            var bay = w.BayOf(team);
            Focus(m, null);
            float hp = m.hp / Mathf.Max(1f, m.MaxHp);
            if (bay == null || hp >= RepairedAt) truceDocking = false;
            else if (hp < RepairAt) truceDocking = true;
            if (truceDocking)
            {
                // Hurt: it goes to its gantry and mends to its full repair line, as any night; after
                // lauds the normal repair logic carries on from there.
                repairing = true;
                SetIntent(core, MechIntent.Repairing, "Mending through the Quenching");
                MoveTo(m, Dock(bay, m), 1.5f);
            }
            else
            {
                SetIntent(core, MechIntent.Guarding, "Keeping the Quenching " + Where(Guard(bay)));
                MoveTo(m, Guard(bay), 6f);
            }
            if (!truceSaid)
            {
                truceSaid = true;
                Narrate(m.pos, "Commander, the lamps are going out on both sides. My guns are cold until lauds.", m);
            }
        }
    }
}
