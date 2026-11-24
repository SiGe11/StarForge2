// GameWorld.Night.cs -- night maps and the Night of the Quenching (docs/lore/starforge-lore.md
// sections 4 and 9). Whether a map is night is drawn from the map seed's own stream, never the
// match's, and only a real match gets one (NightRules.RealMatch): the benchmark, the evaluation,
// trials and every test flag play by day. The one rule the simulation keeps is the Quenching's
// truce: from the darkening to lauds no gun fires (Truce: read by Unit.UpdateCombat, TryThrow, the
// deserters' alarm, Hostile and the squads' reports). Everything else is the view's (FXDirector.Night).
using UnityEngine;

namespace StarForge.World
{
    /// <summary>What a match's night holds. Times are match seconds.</summary>
    public struct NightPlan
    {
        public bool night, quenching;
        /// <summary>When the lamps begin to go out: the truce runs from here to lauds.</summary>
        public float darkeningAt;
        /// <summary>For trials: the darkening to lauds, if not the usual <see cref="Lauds"/>.</summary>
        public float span;
        /// <summary>The Great Liturgy's Taking of the Light (lore section 4, every ninth night) at
        /// <see cref="liturgyAt"/>, on about one night map in nine with no Quenching. Presentation only:
        /// FXDirector.Night puts the lamps out and lights them again from each Foundry's; nothing else changes.</summary>
        public bool liturgy;
        public float liturgyAt;

        /// <summary>The first star: the great bell, the lamps lit one by one.</summary>
        public const float VespersAt = 24f;
        /// <summary>The match starts in late twilight; full night by then.</summary>
        public const float DuskEnd = 90f, DuskLevel = 0.55f;
        /// <summary>The Quenching, from its darkening: every lamp out one by one over the first
        /// span, dark, dawn from DawnFrom, lauds (and day) at Lauds.</summary>
        public const float Darkening = 60f, DawnFrom = 180f, Lauds = 240f;

        public float Span => span > 0f ? span : Lauds;
        public float LaudsAt => darkeningAt + Span;
        float DawnAt => darkeningAt + Span - (Lauds - DawnFrom);

        /// <summary>0 day .. 1 full night at match time <paramref name="t"/>.</summary>
        public float Level(float t)
        {
            if (!night) return 0f;
            float k = Mathf.Lerp(DuskLevel, 1f, Mathf.SmoothStep(0f, 1f, t / DuskEnd));
            if (quenching && t >= DawnAt)
                k *= 1f - Mathf.SmoothStep(0f, 1f, (t - DawnAt) / (Lauds - DawnFrom));
            return k;
        }

        /// <summary>How far the Quenching's lamps are out, 0..1 (0 before the darkening, and back to
        /// 0 over 20 s after lauds, when it is day and the works' lights are on again).</summary>
        public float LampsOut(float t)
        {
            if (!quenching || t < darkeningAt) return 0f;
            if (t < LaudsAt) return Mathf.Clamp01((t - darkeningAt) / Mathf.Min(Darkening, Span * 0.5f));
            return 1f - Mathf.Clamp01((t - LaudsAt) / 20f);
        }

        public bool TruceAt(float t) => quenching && t >= darkeningAt && t < LaudsAt;
    }

    /// <summary>Which matches are night (the gate) and the checks' way to force one.</summary>
    public static class NightRules
    {
        /// <summary>Salts chosen so that every measurement and trial seed hashes to day as well
        /// (1000, 1037, 1074, 1111, 1148, 1185, 1222, 1259; maps 2090223571, 3469857940,
        /// 1370439406, 2694109903): the gate already keeps them day, this is the second lock.</summary>
        public const uint NightSalt = 0x4E161Cu, QuenchSalt = 0x9E7C40u, LiturgySalt = 0x7A11E5u;
        /// <summary>1 map in 3 is night (333 in 1000); 12% of nights hold a Quenching (4% of maps).</summary>
        public const uint NightIn1000 = 333u, QuenchIn1000 = 120u;

        /// <summary>For checks: -1 the map and the gate decide, 0 day, 1 night, 2 a Quenching whose
        /// darkening starts at <see cref="ForceDarkeningAt"/> (120 s if unset), 3 a night whose Taking of the
        /// Light comes at that time. Cleared at every play entry and by AgentInbox when play stops; on the
        /// player <c>-sfnight</c>, <c>-sfnight quenching [seconds]</c> and <c>-sfnight liturgy [seconds]</c> set it.</summary>
        public static int Force = -1;
        public static float ForceDarkeningAt = -1f;

        static readonly string[] TestFlags = { "-sfbench", "-sfgallery", "-sfgalleryquick", "-sfburst", "-sfshot", "-sfscreentest",
                                               "-sflook", "-sfvegprobe", "-sfrigprobe", "-sfironclads" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Force = -1;
            ForceDarkeningAt = -1f;
            if (Application.isEditor) return;   // the Editor's command line is Unity's own
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-sfnight");
            if (i < 0) return;
            Force = 1;
            if (i + 1 < args.Length && (args[i + 1] == "quenching" || args[i + 1] == "liturgy"))
            {
                Force = args[i + 1] == "liturgy" ? 3 : 2;
                if (i + 2 < args.Length && float.TryParse(args[i + 2], System.Globalization.NumberStyles.Float,
                                                          System.Globalization.CultureInfo.InvariantCulture, out float at))
                    ForceDarkeningAt = Mathf.Max(30f, at);
            }
        }

        /// <summary>A test run of the built player (any of its measuring flags).</summary>
        public static bool TestRun()
        {
            if (Application.isEditor) return false;
            var args = System.Environment.GetCommandLineArgs();
            foreach (var f in TestFlags) if (System.Array.IndexOf(args, f) >= 0) return true;
            return false;
        }

        /// <summary>A play the agent started in the Editor (EditorTools.AgentAudio mirrors it).</summary>
        public static bool AgentPlay()
        {
#if UNITY_EDITOR
            return UnityEditor.SessionState.GetBool("sf_agent_play", false);
#else
            return false;
#endif
        }

        /// <summary>The gate: a match the player deployed from the menu (or its rematch), with no
        /// fixed seed, no test flag and not an agent's play. Night is for these only.</summary>
        public static bool RealMatch(bool fromMenu, bool fixedSeed) => fromMenu && !fixedSeed && !TestRun() && !AgentPlay();

        public static NightPlan ForMatch(uint mapSeed, bool real)
        {
            var p = new NightPlan();
            if (Force >= 0)
            {
                p.night = Force >= 1;
                p.quenching = Force == 2;
                p.darkeningAt = ForceDarkeningAt >= 0f ? ForceDarkeningAt : 120f;
                p.liturgy = Force == 3;
                p.liturgyAt = p.darkeningAt;
                return p;
            }
            if (!real) return p;
            p.night = Hash(mapSeed ^ NightSalt) % 1000u < NightIn1000;
            uint q = Hash(mapSeed ^ QuenchSalt);
            p.quenching = p.night && q % 1000u < QuenchIn1000;
            p.darkeningAt = 330f + (q >> 10) % 151u;
            // Every ninth night (lore section 4): of the night maps with no Quenching, 1 in 9 (~10% of nights).
            uint l = Hash(mapSeed ^ LiturgySalt);
            p.liturgy = p.night && !p.quenching && l % 9u == 0u;
            p.liturgyAt = 300f + (l >> 10) % 151u;
            return p;
        }

        /// <summary>The weather's hash (FXDirector.Weather): its own stream from the map seed.</summary>
        public static uint Hash(uint seed)
        {
            uint x = seed * 2654435761u;
            x ^= x >> 15; x *= 0x2c1b3c6du;
            x ^= x >> 12; x *= 0x297a2d39u;
            x ^= x >> 15;
            return x;
        }
    }

    public sealed partial class GameWorld
    {
        /// <summary>This match's night (day by default; GameBootstrap.StartMatch sets it).</summary>
        public NightPlan Night { get; private set; }
        int nightPhase;
        bool liturgyRaised;

        /// <summary>The Quenching's truce: no gun of either side fires, from the darkening to lauds.</summary>
        public bool Truce => Night.quenching && time >= Night.darkeningAt && time < Night.LaudsAt;

        public void SetNight(NightPlan plan)
        {
            Night = plan;
            nightPhase = 0;
            liturgyRaised = false;
            if (plan.night)
                Debug.Log($"[Night] night map{(plan.quenching ? $", the Quenching from {plan.darkeningAt:0} s, lauds at {plan.LaudsAt:0} s" : "")}" +
                          $"{(plan.liturgy ? $", the Taking of the Light at {plan.liturgyAt:0} s" : "")}");
        }

        /// <summary>From GameBootstrap's tick: vespers, the darkening and lauds, each raised once as a
        /// Quenching event (<c>index</c> 0, 1, 2; <c>team</c> 2: both camps).</summary>
        public void TickNight()
        {
            if (!Night.night || winner >= 0) return;
            if (nightPhase < 1 && time >= NightPlan.VespersAt) { nightPhase = 1; RaiseNight(0); }
            if (Night.liturgy && !liturgyRaised && time >= Night.liturgyAt) { liturgyRaised = true; RaiseNight(3); }
            if (!Night.quenching) return;
            if (nightPhase < 2 && time >= Night.darkeningAt) { nightPhase = 2; RaiseNight(1); }
            if (nightPhase < 3 && time >= Night.LaudsAt) { nightPhase = 3; RaiseNight(2); }
        }

        void RaiseNight(int phase)
        {
            Vector2 home = Map.StartPos(0);
            // A picket of the player's side reports it (who, where, what: no lore in a soldier's mouth).
            string who = CallSigns[(int)(NightRules.Hash(Map.seed ^ 0x51C4E7u) % (uint)CallSigns.Length)];
            // Soldiers say lampwardens (lore section 4); the game's own voice keeps "celebrant".
            string text = phase == 1 ? "Lampwardens putting the lamps out in both camps. Nobody is firing. Holding till lauds."
                        : phase == 2 ? "First light. Lauds. Weapons free."
                        : phase == 3 ? "Lampwardens putting our lamps out for the liturgy. They'll light them again."
                        : null;
            Raise(new GameEvent
            {
                kind = GameEventKind.Quenching, team = 2, index = phase, who = who, text = text,
                pos = new Vector3(home.x, 0f, home.y), scale = phase == 1 ? Night.Span : 0f
            });
        }
    }
}
