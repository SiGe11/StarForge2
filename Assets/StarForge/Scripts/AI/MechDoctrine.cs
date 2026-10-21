// MechDoctrine.cs — the numbers a side's Mech weighs its choices by (MechBrain).
//
// Both Mechs fight the same way: cautious, more defending than attacking, going in when the
// moment calls for it -- the judgement the player's Mech has always had. They are not each
// other's copy, though: the opponent's Mech (team 1) draws a temper each match from the
// match seed -- steady (the same numbers), wary (turns for repairs sooner, wants wider odds,
// hunts less and nearer) or bold (stays a little longer, goes in on slightly closer odds) --
// with a few percent of jitter on top, all inside the cautious band. The player's Mech
// (team 0) keeps its numbers exactly.
using UnityEngine;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed class MechDoctrine
    {
        public string name = "Guardian";

        /// <summary>Walks back to its bay under this share of its hull, and leaves it at this.</summary>
        public float repairAt = 0.38f, repairedAt = 0.92f;
        /// <summary>The odds it wants in a fight (its side's power over theirs), whole and badly hurt.</summary>
        public float cautionWhole = 1.5f, cautionHurt = 3.4f;
        /// <summary>Times the odds it wants before it takes on their Mech on its own.</summary>
        public float duel = 0.85f;
        /// <summary>Goes hunting above this share of its hull, as far as this.</summary>
        public float huntFrom = 0.72f, huntReach = 150f;
        /// <summary>The least a hunt must promise before it goes.</summary>
        public float huntWorth = 150f;
        /// <summary>How far out from its base, toward theirs, it waits.</summary>
        public float guardOut = 22f;

        /// <summary>The player's Mech: the numbers it has always had.</summary>
        public static MechDoctrine Guardian() => new MechDoctrine();

        /// <summary>The opponent's Mech: the same judgement, with this match's temper. Every
        /// number is clamped to the band after the jitter (repair 0.34-0.42, odds x0.92-1.1,
        /// hunt from 0.68-0.78, reach 135-165 m, worth 135-170, guard 20-24 m), and their Mech
        /// is never taken on at odds under 0.92 of what the player's Mech wants (the caution
        /// and the duel factor multiply; unclamped, a bold one went in at 0.83).</summary>
        public static MechDoctrine Tempered(uint seed)
        {
            var rng = new Rng(seed ^ 0x4B7E5u);
            float J() => rng.Range(0.96f, 1.04f);
            var d = new MechDoctrine();
            switch (rng.IRange(0, 3))
            {
                case 1:
                    d.name = "Guardian (wary)";
                    d.repairAt = 0.41f; d.repairedAt = 0.95f;
                    d.cautionWhole = 1.6f; d.cautionHurt = 3.55f; d.duel = 0.88f;
                    d.huntFrom = 0.76f; d.huntReach = 140f; d.huntWorth = 165f;
                    break;
                case 2:
                    d.name = "Guardian (bold)";
                    d.repairAt = 0.35f; d.repairedAt = 0.9f;
                    d.cautionWhole = 1.42f; d.cautionHurt = 3.25f; d.duel = 0.83f;
                    d.huntFrom = 0.69f; d.huntReach = 160f; d.huntWorth = 140f;
                    break;
                default:
                    d.name = "Guardian (steady)";
                    break;
            }
            var g = Guardian();
            d.repairAt = Mathf.Clamp(d.repairAt * J(), 0.34f, 0.42f);
            d.cautionWhole = Mathf.Clamp(d.cautionWhole * J(), g.cautionWhole * 0.92f, g.cautionWhole * 1.1f);
            d.cautionHurt = Mathf.Clamp(d.cautionHurt * J(), g.cautionHurt * 0.92f, g.cautionHurt * 1.1f);
            d.duel = Mathf.Clamp(d.duel * J(), 0.78f, 0.94f);
            d.duel = Mathf.Max(d.duel, 0.92f * g.cautionWhole * g.duel / d.cautionWhole);
            d.huntFrom = Mathf.Clamp(d.huntFrom * J(), 0.68f, 0.78f);
            d.huntReach = Mathf.Clamp(d.huntReach * J(), 135f, 165f);
            d.huntWorth = Mathf.Clamp(d.huntWorth * J(), 135f, 170f);
            d.guardOut = Mathf.Clamp(d.guardOut * J(), 20f, 24f);
            return d;
        }

        /// <summary>The odds it wants at this share of its hull.</summary>
        public float Caution(float hpFrac) => Lerp(cautionWhole, cautionHurt, Saturate((1f - hpFrac) / 0.62f));

        public override string ToString() =>
            $"{name}: repair {repairAt:0.00}-{repairedAt:0.00}, odds {cautionWhole:0.00}-{cautionHurt:0.00}, duel x{duel:0.00}, " +
            $"hunt from {huntFrom:0.00} to {huntReach:0} m (worth {huntWorth:0}), guard {guardOut:0} m out";
    }
}
