// MechBrain.cs — the Mech's own mind. One per side; it drives only its side's Mech.
//
// The Mech is an ally, not a soldier: its pilot is sworn to the side that raised the
// Mech Bay and fights for the same end, but takes no orders from it. The player (or
// the opponent AI) can only buy it upgrades at the bay, and listen -- or not -- to
// what it says. Unlike the opponent AI (Commander), which sees only what its units
// see and is held to a human's click rate, the Mech's AI sees the whole map and acts
// as fast as it likes (a budget of about 1,200 actions a minute): that is what makes
// it a Mech. What it knows, it shares -- its advice to its side names places and
// forces the side may not be able to see.
//
// It fights like a veteran who means to live. Every few tenths of a second it weighs
// what it and its friends can do against what the enemy has at a place (Lanchester's
// square law: hit points times damage a second, summed each side), and it goes in
// only with a margin -- a wide one when it is hurt. In order of need it will:
//   repair     under 38% (or losing badly under 60%), walk back to its bay and stand
//              in the gantry until it is whole again;
//   duel       meet the enemy Mech when the odds favour it, else pull back onto its army;
//   defend     go to the base when it is attacked;
//   support    walk with its side's army when that army goes on the attack;
//   hunt       pick off an enemy force or structure it can beat cleanly, away from towers;
//   guard      otherwise hold the approach between its base and the enemy's.
// And when soldiers of its side desert (GameWorld.Morale), it decides -- by its pilot's
// temper and its mood -- whether to go to their camp and put them down (Punish).
// In a fight it keeps its distance from anything it outranges, and closes on what it does not.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed class MechBrain
    {
        /// <summary>How often it thinks: four times a second at rest, eight in a fight.</summary>
        const float ThinkPeriod = 0.25f, CombatThink = 0.12f;
        const float RepairAt = 0.38f, RepairedAt = 0.92f;

        GameWorld w;
        int team;
        Rng rng;
        float thinkT, lastOrderT, stuckT, thinkEvery = ThinkPeriod;
        Vector2 lastPos, goal;
        bool repairing;
        Unit lastMech;
        MechIntent mode = MechIntent.Dropping;
        float preyT;
        // Keeping out of the enemy Mech's reach after backing off from it.
        Vector2 shyOf;
        float shyReach, shyUntil = -1f;
        bool preyOk;
        Vector2 preyAt;
        string preyWhat = "";
        Unit preyKey;
        readonly List<Vector2> weighed = new List<Vector2>(32);
        readonly List<float> weighedRatio = new List<float>(32);
        readonly List<Unit> scratch = new List<Unit>(128);

        /// <summary>A generous budget: this AI is not held to a human's hands.</summary>
        public readonly ActionBudget Budget = new ActionBudget { regen = 20f, cap = 40f };
        public int Team => team;
        public MechIntent Mode => mode;
        /// <summary>For the debug view and the benchmark: what it last weighed.</summary>
        public string Debug => $"{mode}: {lastText} (ratio here {LastRatio:0.0})";
        string lastText = "";
        public float LastRatio { get; private set; }
        public int AdviceGiven { get; private set; }
        public readonly int[] AdviceByKind = new int[8];

        // Advice pacing.
        float nextAdvice, lastDefendAdvice = -99f, lastAttackAdvice = -99f, lastRegroupAdvice = -99f;
        bool greeted, warnedLow, sawEnemyMech, warnedBayUnsafe;
        // What it says to keep its side's spirits up (its own stream and pacing).
        MechSpeech speech;
        // A forward bay that could not mend it: when it docked, the hull it had then, and
        // how long it keeps away after losing hull in the gantry (longer each time).
        float dockedSince = -1f, dockedHp, bayShunUntil = -1f;
        int bayShuns;
        int killsSeen, enemyMechsBefore = -1;
        // Its side's deserters: when it gives its verdict (a few seconds after they run, once
        // it is free to), and if it goes, until when it keeps at it. Its own stream, so the
        // verdict does not shift the brain's other draws.
        Rng punishRng;
        float verdictAt = -1f, verdictUntil = -1f, punishUntil = -1f;
        bool punishing, punishArrived, forcePunish;
        /// <summary>Is it out to punish its side's deserters?</summary>
        public bool Punishing => punishing;
        /// <summary>How many times it went (for the trials and the evaluation line).</summary>
        public int Punishments { get; private set; }
        /// <summary>Its last verdict on deserters and why (trials, debug view).</summary>
        public string LastVerdict { get; private set; } = "";

        public void Init(GameWorld world, int t, uint seed)
        {
            w = world;
            team = t;
            rng = new Rng(seed ^ 0x3EC4u ^ (uint)(t * 7919));
            punishRng = new Rng(seed ^ 0x9D1E5u ^ (uint)(t * 31337));
            nextAdvice = 60f;
            speech = new MechSpeech(world, t, seed);
            w.Event += OnEvent;
        }

        public void Dispose()
        {
            if (w != null) w.Event -= OnEvent;
            speech?.Dispose();
        }

        Unit Mech => w.MechOf(team);

        bool InFight(Unit m) => w.time - m.lastDamagedT < 4f || NearestThreat(m, m.mech.MaxRange + 4f) != null;
        Faction F => w.factions[team];
        Vector2 Home => w.Map.StartPos(team);
        Vector2 Foe => w.Map.StartPos(1 - team);

        // ------------------------------------------------------------ tick
        public void Update(float dt)
        {
            if (w == null) return;
            if (w.winner >= 0)
            {
                // One last word as the match ends, if it is still standing to say it.
                if (Mech is Unit last && last.mech.Landed) Speak(last, speech.Final(last, F.design, w.winner == team));
                return;
            }
            Budget.Tick(dt);
            thinkT -= dt;
            if (thinkT > 0f) return;
            var m = Mech;
            thinkT = thinkEvery = m != null && m.mech.Landed && InFight(m) ? CombatThink : ThinkPeriod;
            if (m != lastMech) { lastMech = m; repairing = false; greeted = false; }
            // Gone to be mended, or gone: the deserters will keep.
            if (punishing && (m == null || repairing)) StopPunishing(m, null);
            Advise(m);
            if (m == null) return;
            var core = m.mech;
            if (!core.Landed) { SetIntent(core, MechIntent.Dropping, "Coming down"); return; }
            Think(m, core);
        }

        void Think(Unit m, MechCore core)
        {
            float hpFrac = m.hp / Mathf.Max(1f, m.MaxHp);
            var bay = w.BayOf(team);
            // Losing hull in the gantry of a bay out on their half: whatever is shooting it
            // there outpaces the repairs (under fire they are a trickle). It stood in one
            // 45 m from their Mech at 5-13% for half a minute, and died there.
            if (core.repairing && bay != null && !HomeSide(bay))
            {
                if (dockedSince < 0f) { dockedSince = w.time; dockedHp = m.hp; }
                else if (w.time - dockedSince > 6f && m.hp <= dockedHp && w.time - m.lastDamagedT < 1.5f)
                {
                    bayShunUntil = w.time + Mathf.Min(120f, 20f * (1 << Mathf.Min(bayShuns, 3)));
                    bayShuns++;
                    dockedSince = -1f;
                    Say(MechAdviceKind.Status, m.pos, "They have the gantry under their guns -- it cannot keep up. Falling back to our lines.", m);
                }
            }
            else dockedSince = -1f;
            // A bay raised in or beside the enemy's base is no place to be mended: the gantry
            // is under their guns. Hurt, it falls back to its own lines instead.
            if (bay != null && !BaySafe(bay, m))
            {
                if (!warnedBayUnsafe && hpFrac < RepairAt)
                {
                    warnedBayUnsafe = true;
                    Say(MechAdviceKind.Status, m.pos, "The bay is under their guns -- I cannot be mended there. Falling back to our lines.", m);
                }
                bay = null;
            }

            // How it stands where it is.
            float here = Ratio(m.pos, 34f, m, out float enemyHere, out _);
            LastRatio = here;
            float caution = Lerp(1.5f, 3.4f, Saturate((1f - hpFrac) / 0.62f)) + (core.HasShield && core.shield < 1f ? 0.25f : 0f);

            // ---- repair
            if (repairing && (hpFrac >= RepairedAt && (!core.HasShield || core.shield > core.ShieldMax * 0.9f) || bay == null)) repairing = false;
            if (!repairing && bay != null && (hpFrac < RepairAt || (hpFrac < 0.6f && enemyHere > 0f && here < 1f)))
            {
                repairing = true;
                if (!warnedLow)
                {
                    warnedLow = true;
                    Say(MechAdviceKind.Status, m.pos, Pick(
                        $"Hull at {hpFrac * 100f:0}%. Falling back to the bay for repairs -- hold without me.",
                        $"I am hurt, {hpFrac * 100f:0}% armour left. Back to the gantry; I will return.",
                        "Taking too much fire. Withdrawing to the bay to patch up."), m);
                }
            }
            if (repairing)
            {
                warnedLow = hpFrac < 0.6f;
                Vector2 dock = Dock(bay, m);
                // Still fight at the bay if they follow it there: the tower and the gantry are with it.
                Unit near = NearestThreat(m, core.FightRange);
                if (near != null && m.Dist(bay) < bay.def.radius + 14f)
                {
                    SetIntent(core, MechIntent.Defending, "Fighting at the bay");
                    Focus(m, near);
                    Engage(m, core, near.pos, allowAdvance: false);
                    return;
                }
                SetIntent(core, core.repairing ? MechIntent.Repairing : MechIntent.Withdrawing,
                          core.repairing ? $"Repairing at the bay ({hpFrac * 100f:0}%)" : $"Withdrawing for repairs ({hpFrac * 100f:0}%)");
                Focus(m, null);
                MoveTo(m, dock, 2f);
                return;
            }

            // ---- hurt with no bay to go to, far from home: back to its own lines. (The
            //      paths below would send it to its army's middle, which may be forward.)
            if (bay == null && w.BayOf(team) != null && hpFrac < RepairAt && (m.pos - Home).magnitude > 60f)
            {
                SetIntent(core, MechIntent.Withdrawing, $"Falling back to our lines ({hpFrac * 100f:0}%)");
                Focus(m, null);
                MoveTo(m, Guard(null), 6f);
                return;
            }

            // ---- its side's deserters (after the repairs: a hurt Mech mends first)
            if (Punish(m, core, hpFrac)) return;

            // ---- the enemy Mech
            var foeMech = w.MechOf(1 - team);
            if (foeMech != null && foeMech.mech.Landed && m.Dist(foeMech) < 60f)
            {
                float r = Ratio(foeMech.pos, 30f, m, out _, out _);
                // It will not go looking for an even duel -- but a Mech walking into its
                // base or onto its army is met, and so is one its army is going at: it goes
                // in with them, everything there helping.
                bool inBase = DistToBase(foeMech.pos, out _) < 40f;
                bool withArmy = ArmyAttacking(out Vector2 af, out _) && (foeMech.pos - af).magnitude < 40f;
                bool atUs = inBase || withArmy || (ArmyCentre(team, out float mine) is Vector2 amy && mine > 300f && (foeMech.pos - amy).magnitude < 30f);
                // In its own base, with the towers and the gantry on its side, it stands.
                if (inBase) r = Ratio(foeMech.pos, 30f, m, out _, out _, home: true);
                // Once in, it stays in while the fight is close.
                float hold = mode == MechIntent.Duelling ? 0.15f : 0f;
                if (r > caution * 0.85f - hold || (atUs && r > 0.8f - hold && hpFrac > 0.3f) || (inBase && r > 0.55f && hpFrac > 0.35f))
                {
                    SetIntent(core, MechIntent.Duelling, $"Engaging their {foeMech.mech.design.className}");
                    Focus(m, foeMech);
                    Engage(m, core, foeMech.pos, allowAdvance: true);
                    return;
                }
                // Not on its own: back onto its army or its base, make them come -- and keep
                // out of its reach for a while. Walking back in with the next support or
                // hunt order had it spending a third of its time going neither way.
                Vector2 fallback = ArmyCentre(team, out float armyVal) is Vector2 ac && armyVal > 200f ? ac : Guard(bay);
                if (m.Dist(foeMech) < core.FightRange + 6f)
                {
                    // Once back with its army it stands and fights beside them -- holding,
                    // not withdrawing -- and if they are going at their Mech, it tells them
                    // this is a fight they lose together (their Mech rode out wave after
                    // wave while it stood by and said nothing).
                    bool there = (m.pos - fallback).sqrMagnitude < 10f * 10f;
                    SetIntent(core, there ? MechIntent.Holding : MechIntent.Withdrawing,
                              there ? "Holding with the army; their Mech has the edge" : "Their Mech has the edge; drawing it onto our guns");
                    if (withArmy) WarnOff(af, foeMech);
                    Focus(m, foeMech);
                    MoveTo(m, fallback, 4f);
                    shyOf = foeMech.pos;
                    shyReach = foeMech.mech.FightRange + 8f;
                    shyUntil = w.time + 12f;
                    return;
                }
            }
            else shyUntil = -1f;

            // ---- the base under attack
            if (HomeThreat(out Vector2 threatAt, out float threatValue) && threatValue >= 120f)
            {
                float r = Ratio(threatAt, 32f, m, out _, out _, home: true);
                // Without the bay, hurt, it picks its fights at home too.
                float need = bay == null && hpFrac < 0.45f ? 1.5f : 1.1f;
                if (r > need || (bay != null && (threatAt - bay.pos).magnitude < 30f))
                {
                    SetIntent(core, MechIntent.Defending, $"Defending the base {Where(threatAt)}");
                    Engage(m, core, threatAt, allowAdvance: true);
                    return;
                }
            }

            // ---- the army on the attack
            if (ArmyAttacking(out Vector2 front, out float armyValue))
            {
                float r = Ratio(front, 32f, m, out float enemyFront, out _);
                // Once in, it stays in while the fight is close: walking out of a fight its
                // army is still in loses the army, and then the Mech.
                float need = mode == MechIntent.Supporting && hpFrac > 0.5f ? 0.7f : 0.9f;
                if (r > need || enemyFront <= 0f)
                {
                    Vector2 lead = front + Norm(Foe - front) * 7f;
                    if (Shy(ref lead))
                    {
                        // Their Mech has the edge: cover the army from outside its reach.
                        SetIntent(core, MechIntent.Supporting, "Covering the attack from outside their Mech's reach");
                        MoveTo(m, lead, 3f);
                        Engage(m, core, lead, allowAdvance: false);
                        return;
                    }
                    SetIntent(core, MechIntent.Supporting, "Supporting the attack" + (enemyFront > 0f ? "" : ", moving up with the army"));
                    Engage(m, core, lead, allowAdvance: true);
                    return;
                }
            }

            // ---- whatever is in reach now, if it is a fair fight
            Unit threat = NearestThreat(m, core.FightRange + 8f);
            if (threat != null)
            {
                if (here > caution * 0.8f)
                {
                    SetIntent(core, MechIntent.Hunting, "Engaging");
                    Engage(m, core, threat.pos, allowAdvance: true);
                }
                else
                {
                    SetIntent(core, MechIntent.Withdrawing, "Outgunned here; pulling back");
                    Vector2 away = m.pos + Norm(m.pos - threat.pos) * 14f;
                    MoveTo(m, Vector2.Lerp(away, Guard(bay), 0.4f), 3f);
                }
                return;
            }

            // ---- a hunt, if something out there can be beaten cleanly (looked for about
            //      once a second: it weighs a fight at every enemy cluster on the map)
            if (hpFrac > 0.72f)
            {
                if (w.time >= preyT)
                {
                    preyT = w.time + 1.2f;
                    preyOk = FindPrey(m, core, caution, out preyAt, out preyWhat, out preyKey);
                }
                Vector2 near = preyAt;   // a copy: Shy moves what it is given
                if (preyOk && (preyKey == null || Unit.Live(preyKey)) && !Shy(ref near))
                {
                    SetIntent(core, MechIntent.Hunting, "Hunting " + preyWhat);
                    Focus(m, preyKey);
                    Engage(m, core, preyAt, allowAdvance: true);
                    return;
                }
            }
            else preyOk = false;

            // ---- guard
            SetIntent(core, MechIntent.Guarding, "Holding the approach " + Where(Guard(bay)));
            Focus(m, null);
            MoveTo(m, Guard(bay), 6f);
        }

        /// <summary>While it keeps clear of their Mech (after backing off from it): true if
        /// <paramref name="p"/> is in that Mech's reach, and moves it out to the edge.</summary>
        bool Shy(ref Vector2 p)
        {
            if (w.time >= shyUntil) return false;
            Vector2 d = p - shyOf;
            if (d.sqrMagnitude >= shyReach * shyReach) return false;
            p = w.NearestReachable(Home, shyOf + (d.sqrMagnitude > 1f ? d.normalized : Norm(Home - shyOf)) * shyReach, true);
            return true;
        }

        // ------------------------------------------------------------ the fight
        /// <summary>Fight round <paramref name="at"/>: keep out of reach of what it
        /// outranges, close on what it does not, and walk on to <paramref name="at"/>
        /// if nothing is in reach yet.</summary>
        void Engage(Unit m, MechCore core, Vector2 at, bool allowAdvance)
        {
            float fight = core.FightRange;
            Unit nearest = NearestThreat(m, fight + 12f);
            if (nearest == null)
            {
                // Nothing in reach: walk on, stopping short enough to open fire from range.
                Unit any = NearestEnemy(m, at, 20f);
                Vector2 dest = any != null ? any.pos + Norm(m.pos - any.pos) * (fight * 0.8f) : at;
                if (allowAdvance) MoveTo(m, dest, 3f);
                return;
            }
            float d = m.Dist(nearest) - nearest.def.radius;
            float theirs = ThreatRange(nearest);
            // Pick what the arm guns should kill first (the guns choose for themselves
            // otherwise): their Mech, else whatever in reach does the most harm for the
            // least armour -- a wounded Mauler before a fresh one, a Sentinel before a
            // rifleman.
            Focus(m, BestFocus(m, core));
            // How close to stand. Just outside their reach when the fight is even, near
            // enough that as many of its own guns as can reach do; right in among them when
            // they cannot hurt it much -- so the rotary cannon and the flamer get to work
            // instead of idling while a railgun plinks from forty metres.
            float shortR = core.ShortRange;
            float standoff = LastRatio > 3f ? shortR - 2f : Mathf.Clamp(Mathf.Max(shortR - 2f, theirs + 3f), 6f, fight - 3f);
            // A structure cannot chase; everything else keeps coming.
            bool outranges = fight > theirs + 3f && LastRatio < 3f;
            if (outranges && d < theirs + 2.5f)
            {
                // Back off to just outside their reach: away from the crowd, and toward
                // its own side rather than deeper into theirs.
                Vector2 away = Norm(m.pos - EnemyCentroid(m.pos, fight + 12f, nearest.pos));
                Vector2 home = Norm(Home - m.pos);
                Vector2 dir = Norm(away * 1f + home * 0.45f);
                Vector2 spot = m.pos + dir * Mathf.Max(5f, theirs + 4f - d);
                if (w.Reaches(m, spot)) { MoveTo(m, spot, 2f); return; }
            }
            if (d > standoff + 3f && allowAdvance)
            {
                MoveTo(m, nearest.pos + Norm(m.pos - nearest.pos) * (standoff + nearest.def.radius), 2f);
                return;
            }
            // In its reach: stand and let the guns work.
            if (m.order == Order.Move && m.Moving && (m.orderPos - m.pos).magnitude > 3f && d < fight - 1f)
            {
                if (Spend(1)) w.CmdMechHalt(m);
            }
        }

        /// <summary>The target the whole Mech should be on: the enemy Mech if any gun
        /// reaches it, else the most damage-a-second per hit point in reach.</summary>
        Unit BestFocus(Unit m, MechCore core)
        {
            w.EnemiesNear(m, core.FightRange + 2f, scratch);
            Unit best = null, current = core.focus;
            float bestScore = 0f, currentScore = -1f;
            foreach (var e in scratch)
            {
                if (e.Untargetable || e.Type == UnitType.Ore) continue;
                float d = m.Dist(e) - e.def.radius;
                if (d > core.FightRange) continue;
                if (e.mech != null) return e;
                float dps = EnemyDps(e);
                if (dps <= 0f) continue;
                float score = dps / Mathf.Max(20f, e.hp) * (e.def.building ? 0.6f : 1f);
                if (e == current) currentScore = score;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            // Stay on the one it is killing unless something is clearly worse.
            return currentScore >= bestScore * 0.75f ? current : best;
        }

        static float ThreatRange(Unit e) =>
            e.mech != null ? e.mech.FightRange : e.def.Armed ? e.def.range : 0f;

        Unit NearestThreat(Unit m, float radius)
        {
            w.EnemiesNear(m, radius, scratch);
            Unit best = null;
            float bestD = float.MaxValue;
            foreach (var e in scratch)
            {
                if (e.Untargetable || e.Type == UnitType.Ore) continue;
                if (!e.def.Armed && e.Type != UnitType.Worker && !e.def.building) continue;
                float d = m.Dist(e) - e.def.radius;
                if (d > radius) continue;
                // Armed things first: a Digger is not a threat, only a target.
                if (!e.def.Armed) d += 12f;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        Unit NearestEnemy(Unit m, Vector2 at, float radius)
        {
            Unit best = null;
            float bestD = radius;
            foreach (var e in w.UnitsNear(at, radius))
            {
                if (e.team == team || e.team > 1 || e.Untargetable) continue;
                float d = (e.pos - at).magnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        Vector2 EnemyCentroid(Vector2 around, float radius, Vector2 fallback)
        {
            Vector2 c = Vector2.zero;
            float wsum = 0f;
            foreach (var e in w.UnitsNear(around, radius))
            {
                if (e.team == team || e.team > 1 || !e.def.Armed) continue;
                float k = EnemyDps(e) + 1f;
                c += e.pos * k;
                wsum += k;
            }
            return wsum > 0f ? c / wsum : fallback;
        }

        // ------------------------------------------------------------ weighing a fight
        /// <summary>Damage a second a unit does to a Mech.</summary>
        static float EnemyDps(Unit e)
        {
            if (e.mech != null) return e.mech.Dps(true);
            if (!e.def.Armed || e.Type == UnitType.Worker) return 0f;
            return e.def.damage * (1f + 0.15f * e.rank) / Mathf.Max(0.1f, e.def.cooldown);
        }

        static float Toughness(Unit e) => e.mech != null ? e.mech.EffectiveHp : Mathf.Max(1f, e.hp);

        /// <summary>Its side's power against the enemy's round <paramref name="at"/>
        /// (Lanchester: summed hit points times summed damage a second, each side),
        /// counting the Mech itself wherever it is. Above 1 it should win there.</summary>
        float Ratio(Vector2 at, float radius, Unit m, out float enemyPower, out float friendPower, bool home = false)
        {
            float eh = 0f, ed = 0f, fh = 0f, fd = 0f;
            int eHeavy = 0, eAll = 0;
            foreach (var e in w.UnitsNear(at, radius))
            {
                if (e.team > 1 || e.dying || e.Untargetable) continue;
                if (e.team != team)
                {
                    float dps = EnemyDps(e);
                    if (dps <= 0f && !(e.def.building)) continue;
                    // Towers hurt, but a structure without guns is only a target.
                    if (e.def.building && !e.def.Armed) continue;
                    eh += Toughness(e);
                    ed += dps;
                    eAll++;
                    if (e.def.Heavy) eHeavy++;
                }
                else if (e != m && (e.def.Armed || e.def.building) && !e.deserted)   // deserters will not help
                {
                    if (e.def.building && !e.def.Armed) continue;
                    fh += Toughness(e) * (e.def.building ? 0.6f : 1f);
                    fd += EnemyDps(e) * (home && e.def.building ? 1.2f : 1f);
                }
            }
            // The Mech itself: its guns against the enemy's mix, its hull and shield.
            float heavyShare = eAll > 0 ? eHeavy / (float)eAll : 0.5f;
            var core = m.mech;
            fh += core.EffectiveHp;
            fd += Lerp(core.Dps(false), core.Dps(true), heavyShare) * (eAll > 3 ? 1.25f : 1f);   // its splash likes a crowd
            enemyPower = eh * ed;
            friendPower = fh * fd;
            return enemyPower <= 1f ? 99f : friendPower / enemyPower;
        }

        // ------------------------------------------------------------ the picture
        /// <summary>The structure of its side most worth saving right now: enemy force
        /// within reach of each one, weighted by what it is (the Foundry and the bay
        /// first). Not a centroid over the whole base -- that once sent it to an outlying
        /// Bunkhouse while their Mech flattened the Foundry.</summary>
        bool HomeThreat(out Vector2 at, out float value)
        {
            at = Home;
            value = 0f;
            float bestScore = 0f;
            foreach (var s in w.units)
            {
                if (s == null || s.dying || s.team != team || !s.def.building) continue;
                float threat = 0f;
                Vector2 sum = Vector2.zero;
                foreach (var e in w.UnitsNear(s.pos, 30f + s.def.radius))
                {
                    if (e.team == team || e.team > 1 || e.Untargetable) continue;
                    // An enemy Mech Bay or tower raised beside its base is an attack too.
                    if (e.def.building && e.Type != UnitType.MechBay && e.Type != UnitType.Sentinel) continue;
                    if (!e.def.building && (!e.def.Armed || e.Type == UnitType.Worker)) continue;
                    float v = e.Type == UnitType.MechBay ? 600f : e.Type == UnitType.Sentinel ? 300f : Defs.ArmyValue(e.Type);
                    threat += v;
                    sum += e.pos * v;
                }
                if (threat <= 0f) continue;
                float weight = s.Type == UnitType.Foundry || s.Type == UnitType.MechBay ? 3f
                             : s.Type == UnitType.Garrison || s.Type == UnitType.Workshop ? 2f : 1f;
                float score = threat * weight;
                if (score > bestScore) { bestScore = score; value = threat; at = sum / threat; }
            }
            return bestScore > 0f;
        }

        /// <summary>How far a point is from the nearest structure of its side.</summary>
        float DistToBase(Vector2 p, out Unit nearest)
        {
            nearest = null;
            float best = float.MaxValue;
            foreach (var u in w.units)
            {
                if (u == null || u.dying || u.team != team || !u.def.building) continue;
                float d = (u.pos - p).magnitude - u.def.radius;
                if (d < best) { best = d; nearest = u; }
            }
            return best;
        }

        Vector2? ArmyCentre(int side, out float value)
        {
            Vector2 sum = Vector2.zero;
            value = 0f;
            foreach (var u in w.units)
            {
                if (u == null || u.dying || u.team != side || !u.def.IsArmy || u.deserted) continue;
                float v = Defs.ArmyValue(u.Type);
                sum += u.pos * v;
                value += v;
            }
            return value > 0f ? sum / value : (Vector2?)null;
        }

        /// <summary>Is its side's army on the attack: gathered and pushing onto the enemy's
        /// half, or in a fight away from home?</summary>
        bool ArmyAttacking(out Vector2 front, out float value)
        {
            front = default;
            var c = ArmyCentre(team, out value);
            if (c == null || value < 250f) return false;
            // Where most of it is: the units within 30 m of the weighted centre.
            Vector2 centre = c.Value;
            Vector2 sum = Vector2.zero;
            float v = 0f;
            foreach (var u in w.units)
            {
                if (u == null || u.dying || u.team != team || !u.def.IsArmy || u.deserted) continue;
                if ((u.pos - centre).magnitude > 30f) continue;
                float k = Defs.ArmyValue(u.Type);
                sum += u.pos * k;
                v += k;
            }
            if (v < 200f) return false;
            front = sum / v;
            float toFoe = (front - Foe).magnitude, toHome = (front - Home).magnitude;
            bool forward = toFoe < toHome * 0.95f;
            bool fighting = false;
            foreach (var e in w.UnitsNear(front, 26f))
                if (e.team != team && e.team < 2 && (e.def.Armed || e.def.building)) { fighting = true; break; }
            return forward || (fighting && toHome > 45f);
        }

        /// <summary>Somewhere worth going: an enemy force, expansion, worker line or Mech
        /// Bay that it (with anything of its side there) beats with a margin, not under
        /// a stack of towers, and not across the whole map when it is hurt.</summary>
        bool FindPrey(Unit m, MechCore core, float caution, out Vector2 at, out string what, out Unit key)
        {
            at = default; what = ""; key = null;
            float bestScore = 0f;
            weighed.Clear();
            weighedRatio.Clear();
            foreach (var e in w.units)
            {
                // Their deserters harm no one: not worth the walk.
                if (e == null || e.dying || e.team == team || e.team > 1 || e.Untargetable || e.deserted) continue;
                float value;
                if (e.Type == UnitType.MechBay) value = F.mechDropped && w.factions[1 - team].mechDropped ? 250f : 900f;
                else if (e.Type == UnitType.Foundry) value = 350f;
                else if (e.Type == UnitType.Worker) value = 40f;
                else if (e.def.building) value = e.def.Armed ? 60f : 150f;
                else value = Defs.ArmyValue(e.Type);
                if (value < 40f) continue;
                float dist = m.Dist(e);
                if (dist > 150f) continue;
                // Weigh the fight once per cluster: what sits near a place already weighed shares its odds.
                float r = -1f;
                for (int k = 0; k < weighed.Count; k++)
                    if ((weighed[k] - e.pos).sqrMagnitude < 12f * 12f) { r = weighedRatio[k]; break; }
                if (r < 0f)
                {
                    r = Ratio(e.pos, 30f, m, out _, out _);
                    weighed.Add(e.pos);
                    weighedRatio.Add(r);
                }
                if (r < caution * 1.25f) continue;
                float gain = value + ClusterValue(e.pos, 20f);
                float score = gain / (1f + dist / 60f) * Mathf.Min(3f, r / caution);
                if (score > bestScore)
                {
                    bestScore = score;
                    at = e.pos;
                    key = e.def.building ? e : null;
                    what = e.def.building ? $"their {e.def.displayName} {Where(e.pos)}" : $"{Describe(e.pos, 20f)} {Where(e.pos)}";
                }
            }
            return bestScore > 150f;
        }

        float ClusterValue(Vector2 at, float radius)
        {
            float v = 0f;
            foreach (var e in w.UnitsNear(at, radius))
                if (e.team != team && e.team < 2 && !e.def.building && !e.deserted) v += e.Type == UnitType.Worker ? 40f : Defs.ArmyValue(e.Type);
            return v;
        }

        /// <summary>Whether its bay can mend it: not in the enemy's reach -- their towers and
        /// army round it outweigh what stands with it there.</summary>
        bool BaySafe(Unit bay, Unit m)
        {
            if (HomeSide(bay)) return true;
            if (w.time < bayShunUntil) return false;
            // What can reach the gantry, not only what stands round it: a Mech or a Mauler
            // shelling it from 45 m kept the repairs at a trickle that a fixed 32 m never saw.
            float radius = 32f;
            foreach (var e in w.UnitsNear(bay.pos, 75f))
            {
                if (e.team == team || e.team > 1 || e.Untargetable) continue;
                float d = (e.pos - bay.pos).magnitude;
                if (d > radius && d < ThreatRange(e) + bay.def.radius + 6f) radius = d + 2f;
            }
            float r = Ratio(bay.pos, radius, m, out float enemy, out _, home: true);
            return enemy <= 0f || r > 1.2f;
        }

        /// <summary>A bay on its own half of the map (nearer its base than theirs).</summary>
        bool HomeSide(Unit bay) => (bay.pos - Foe).magnitude > (bay.pos - Home).magnitude;

        /// <summary>Where it waits: on the approach from the enemy, near the bay -- if the bay
        /// is on its own half of the map (one raised by the enemy's base would put the guard
        /// post in their half).</summary>
        Vector2 Guard(Unit bay)
        {
            bool bayHomeSide = bay != null && HomeSide(bay);
            Vector2 anchor = bayHomeSide ? Vector2.Lerp(Home, bay.pos, 0.5f) : Home;
            Vector2 g = anchor + Norm(Foe - Home) * 22f;
            return w.NearestReachable(Home, g, true);
        }

        /// <summary>Where it stands to be repaired: beside the bay, in the gantry's reach.</summary>
        Vector2 Dock(Unit bay, Unit m)
        {
            Vector2 d = Norm(m.pos - bay.pos);
            if (d.sqrMagnitude < 0.5f) d = Norm(Foe - bay.pos);
            // Clear of the gantry: a four-legged Mech's feet reach five metres from its middle.
            float legs = m.mech.design != null && m.mech.design.locomotion == MechLocomotion.Quad ? 5.5f : 4f;
            return w.NearestReachable(m, bay.pos + d * (bay.def.radius + legs + 1f));
        }

        // ------------------------------------------------------------ orders
        bool Spend(int n)
        {
            if (!Budget.Spend(n)) return false;
            Budget.Note(w.time);
            return true;
        }

        void MoveTo(Unit m, Vector2 dest, float slack)
        {
            // Unstick: a Mech that has not moved for a while on a move order gets a nudge aside.
            if (m.order == Order.Move && (m.pos - lastPos).sqrMagnitude < 0.04f && (m.orderPos - m.pos).magnitude > 4f)
            {
                stuckT += thinkEvery;   // thinks come twice as often in a fight
                if (stuckT > 3f)
                {
                    stuckT = 0f;
                    dest += new Vector2(rng.Range(-8f, 8f), rng.Range(-8f, 8f));
                    slack = 0f;
                }
            }
            else stuckT = 0f;
            lastPos = m.pos;
            goal = dest;
            bool idle = m.order != Order.Move || !m.Moving;
            if (!idle && (dest - m.orderPos).magnitude < slack) return;
            if (idle && (dest - m.pos).magnitude < Mathf.Max(2.5f, slack)) return;
            if (w.time - lastOrderT < 0.2f) return;
            if (!Spend(1)) return;
            lastOrderT = w.time;
            w.CmdMechMove(m, dest);
        }

        void Focus(Unit m, Unit target)
        {
            if (m.mech.focus == target) return;
            if (!Spend(1)) return;
            w.CmdMechFocus(m, target);
        }

        void SetIntent(MechCore core, MechIntent intent, string text)
        {
            mode = intent;
            core.intent = intent;
            core.intentText = text;
            lastText = text;   // the debug line is built only when someone reads it
        }

        // ------------------------------------------------------------ advice
        // What it says, and when: its side hears from it when it arrives, when it sees
        // the enemy coming (before they can), when it thinks the moment to strike has
        // come, when an attack is walking into more than it can take, when it goes to be
        // repaired -- never more than one of those every twenty seconds, apart from the
        // warnings -- and in between, to keep their spirits up, a line MechSpeech picks
        // for how the battle stands.

        void Say(MechAdviceKind kind, Vector2 at, string text, Unit about = null)
        {
            w.MechAdvise(team, kind, at, text, about);
            AdviceGiven++;
            AdviceByKind[(int)kind]++;
            nextAdvice = Mathf.Max(nextAdvice, w.time + 20f);
        }

        string Pick(params string[] lines) => lines[rng.IRange(0, lines.Length)];

        /// <summary>A line from MechSpeech: it does not hold back the calls (Say does), so
        /// the other side's Commander hears those when it would have without it.</summary>
        void Speak(Unit m, string text)
        {
            if (text == null) return;
            w.MechAdvise(team, MechAdviceKind.Morale, m.pos, text, m);
            AdviceGiven++;
            AdviceByKind[(int)MechAdviceKind.Morale]++;
        }

        void Advise(Unit m)
        {
            var d = F.design;
            if (d == null) return;
            // Its arrival.
            if (m != null && m.mech.Landed && !greeted)
            {
                greeted = true;
                Say(MechAdviceKind.Morale, m.pos, Pick(
                    $"{d.pilot} of {d.order}, callsign {d.callsign}, on the ground. My guns are sworn to your cause, Commander.",
                    $"{d.callsign} has made planetfall. {d.pilot}, {d.order}. Point me at the enemy -- or do not; I will find them.",
                    $"This is {d.pilot}. {d.order} honours its oath: {d.callsign} stands with you."), m);
                return;
            }
            if (m == null) return;
            float now = w.time;
            speech.Observe(m, mode);

            // Their Mech, the first time it is on the field.
            var foe = w.MechOf(1 - team);
            if (foe != null && foe.mech.Landed && !sawEnemyMech)
            {
                sawEnemyMech = true;
                var fd = foe.mech.design;
                Say(MechAdviceKind.Warning, foe.pos, $"Enemy Mech on the field: a {fd.className}, {WeaponsOf(fd)}. " +
                    (MechParts.Covers(fd, true) ? "Keep your infantry spread." : "It is weak against a swarm.") +
                    " Do not feed it units piecemeal.", foe);
                return;
            }
            if (foe == null && sawEnemyMech && w.factions[1 - team].mechLost && enemyMechsBefore != 0)
            {
                enemyMechsBefore = 0;
                Say(MechAdviceKind.Morale, m.pos, Pick(
                    "Their Mech is down! The field is ours -- press them while they are broken.",
                    "Their champion has fallen. Now we finish this."), m);
                return;
            }

            // A force coming at the base (warn early: it sees them before anyone else).
            if (now - lastDefendAdvice > 45f && IncomingForce(out Vector2 at, out float val, out string desc) && val >= 250f)
            {
                lastDefendAdvice = now;
                DistToBase(at, out Unit nearest);
                string aimedAt = nearest != null ? $"your {nearest.def.displayName}" : "the base";
                Say(MechAdviceKind.Defend, at, $"{desc} moving on {aimedAt} {Where(at)}. Pull your army back to meet them -- I am on my way.");
                return;
            }

            if (now < nextAdvice) return;

            // An attack walking into more than it can take.
            if (now - lastRegroupAdvice > 60f && ArmyAttacking(out Vector2 front, out float armyVal))
            {
                float r = Ratio(front, 36f, m, out float ep, out _);
                if (ep > 0f && r < 0.6f && (m.pos - front).magnitude > 30f)
                {
                    lastRegroupAdvice = now;
                    Say(MechAdviceKind.Regroup, front, Pick(
                        $"Your attack {Where(front)} is walking into {Describe(front, 36f)}. Pull back and regroup -- we strike together or not at all.",
                        $"They outnumber your forces {Where(front)}. Fall back to me; we will take them on our ground."));
                    return;
                }
            }

            // The moment to strike.
            if (now - lastAttackAdvice > 75f && StrikeNow(m, out Vector2 target, out string why))
            {
                lastAttackAdvice = now;
                Say(MechAdviceKind.Attack, target, why + " I will walk with you.");
                return;
            }

            // Otherwise, a word for how the battle stands (MechSpeech chooses it and when).
            Speak(m, speech.Next(m, d));
        }

        /// <summary>Its army is going at their Mech and cannot win that fight even with it:
        /// tell them to pull back (at most once a minute).</summary>
        void WarnOff(Vector2 front, Unit foeMech)
        {
            if (w.time - lastRegroupAdvice < 60f) return;
            lastRegroupAdvice = w.time;
            Say(MechAdviceKind.Regroup, front, Pick(
                $"Their {foeMech.mech.design.className} holds {Where(foeMech.pos)} and we cannot break it like this. Pull back and build up -- we go in together, with enough.",
                "Fall back! Their Mech is tearing through the attack. Mass the army first; I will be at the front when we go."), foeMech);
        }

        /// <summary>An enemy force on its way to the base: armed units on this side's half,
        /// closer to its base than to their own and heading its way.</summary>
        bool IncomingForce(out Vector2 at, out float value, out string desc)
        {
            at = default; value = 0f; desc = "";
            Vector2 sum = Vector2.zero;
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.team == team || e.team > 1 || !e.def.IsArmy || e.deserted) continue;
                float dHome = DistToBase(e.pos, out _);
                if (dHome > 75f || dHome < 12f) continue;       // at the door already is the base's business
                if ((e.pos - Home).magnitude > (e.pos - Foe).magnitude) continue;
                if (e.agent != null && e.agent.enabled)
                {
                    Vector3 v = e.agent.velocity;
                    Vector2 toHome = Norm(Home - e.pos);
                    if (new Vector2(v.x, v.z).sqrMagnitude > 0.5f && Vector2.Dot(new Vector2(v.x, v.z).normalized, toHome) < 0.2f) continue;
                }
                float k = Defs.ArmyValue(e.Type);
                value += k;
                sum += e.pos * k;
            }
            if (value <= 0f) return false;
            at = sum / value;
            desc = Capitalise(Describe(at, 35f));
            return true;
        }

        /// <summary>Is now the time for its side to attack? When the enemy's army is out of
        /// position (far from home) or weaker than its side's army and the Mech together.</summary>
        bool StrikeNow(Unit m, out Vector2 target, out string why)
        {
            target = Foe; why = "";
            var mine = ArmyCentre(team, out float myArmy);
            var theirs = ArmyCentre(1 - team, out float theirArmy);
            if (mine == null || myArmy < 300f) return false;
            // Something worth hitting: their Mech Bay before it drops, production, expansion.
            Unit best = null;
            float bestScore = 0f;
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.team == team || e.team > 1 || !e.def.building) continue;
                float v = e.Type == UnitType.MechBay && !w.factions[1 - team].mechDropped ? 4f
                        : e.Type == UnitType.Foundry ? 2.5f
                        : e.Type == UnitType.Workshop || e.Type == UnitType.Garrison ? 2f : 1f;
                float s = v / (1f + (e.pos - mine.Value).magnitude / 80f);
                if (s > bestScore) { bestScore = s; best = e; }
            }
            if (best == null) return false;
            target = best.pos;
            float mechPower = m.mech.EffectiveHp * m.mech.Dps(true) * 0.001f;
            float us = myArmy + mechPower * 0.5f, them = theirArmy + (w.MechOf(1 - team) != null ? 2500f : 0f);
            bool away = theirs != null && (theirs.Value - Foe).magnitude > 70f;
            if (away && us > them * 0.5f)
            {
                why = $"Their army is out in the field {Where(theirs.Value)}, far from home. Strike their {best.def.displayName} {Where(best.pos)} now, before they can turn back.";
                return true;
            }
            if (us > them * 1.6f)
            {
                why = best.Type == UnitType.MechBay
                    ? $"Their Mech Bay {Where(best.pos)} stands before their Mech has come down. Destroy it and it never will."
                    : $"We outgun them. Their {best.def.displayName} {Where(best.pos)} is open -- attack now.";
                return true;
            }
            return false;
        }

        void OnEvent(GameEvent e)
        {
            // Soldiers of its side ran: it gives its verdict in a few seconds (Punish).
            if (e.kind == GameEventKind.Desertion && e.team == team && !punishing && verdictAt < 0f)
            {
                verdictAt = w.time + punishRng.Range(4f, 9f);
                verdictUntil = w.time + 75f;
            }
            if (e.kind == GameEventKind.Death && e.team == 1 - team && e.unit != null && e.unit.Type == UnitType.MechBay && !w.factions[team].mechLost)
            {
                if (Mech != null)
                    Say(MechAdviceKind.Morale, e.pos, w.factions[1 - team].mechDropped
                        ? "Their Mech Bay is rubble. Their Mech has nowhere left to lick its wounds."
                        : "Their Mech Bay is down before their Mech could land. It will never come.");
            }
            if (e.kind == GameEventKind.Death && e.team == team && e.unit != null && e.unit.Type == UnitType.MechBay)
            {
                if (Mech != null) Say(MechAdviceKind.Warning, e.pos, "The bay is lost. I cannot be repaired now -- I will fight with more care.");
            }
            if (e.kind == GameEventKind.MechInbound && e.team == team && F.design != null)
                w.MechAdvise(team, MechAdviceKind.Status, new Vector2(e.pos.x, e.pos.z),
                    $"{F.design.pilot} inbound. Clear the pad beside the Mech Bay -- landing in {GameWorld.MechInboundLead:0} seconds.");
        }

        // ------------------------------------------------------------ deserters
        // Soldiers of its side who broke and ran (GameWorld.Morale) sit in a camp in the
        // corner behind its base. A few seconds after they run -- once it is whole enough
        // and nothing is on it -- it decides whether to go and put them down: a cold pilot
        // more often than a warm one, a proud one more often, and any of them more often
        // when the war is going badly (its mood). If it goes, it says so, walks there, and
        // its guns treat them as enemies until they are dead (GameWorld.Purging); it gives
        // up if it is needed at home, their Mech comes near, it goes for repairs, or after
        // two and a half minutes. Either way its side hears what it decided.

        /// <summary>For the trials: give the verdict now, and make it "go".</summary>
        public void PunishNow()
        {
            forcePunish = true;
            verdictAt = w.time;
            verdictUntil = w.time + 75f;
        }

        /// <summary>The verdict when it is due, and the walk to the camp: true if that is what
        /// it is doing this think.</summary>
        bool Punish(Unit m, MechCore core, float hpFrac)
        {
            var M = w.MoraleOf(team);
            if (verdictAt >= 0f && w.time >= verdictAt)
            {
                if (M.deserters.Count == 0 || w.time > verdictUntil) verdictAt = -1f;   // gone, or too late to matter
                else if (!forcePunish && (hpFrac < 0.6f || InFight(m) || (HomeThreat(out _, out float tv) && tv >= 120f)))
                    verdictAt = w.time + 5f;                                            // busy: later
                else { verdictAt = -1f; Verdict(m, M); }
            }
            if (!punishing) return false;

            if (w.time > punishUntil) { StopPunishing(m, Line(Aborted)); return false; }
            if (HomeThreat(out _, out float threat) && threat >= 120f) { StopPunishing(m, Line(Aborted)); return false; }
            var foe = w.MechOf(1 - team);
            if (foe != null && foe.mech.Landed && m.Dist(foe) < 60f) { StopPunishing(m, Line(FoeNear)); return false; }

            Unit prey = null;
            float best = float.MaxValue;
            foreach (var d in M.deserters)
            {
                if (!Unit.Live(d)) continue;
                float dd = (d.pos - m.pos).sqrMagnitude;
                if (dd < best) { best = dd; prey = d; }
            }
            if (prey == null) { StopPunishing(m, Line(speech.Temper < 0f ? DoneCold : DoneWarm)); return false; }

            w.SetPurge(team, true);
            SetIntent(core, MechIntent.Hunting, "Punishing deserters " + Where(M.camp));
            Focus(m, prey);
            float dist = m.Dist(prey) - prey.def.radius;
            if (!punishArrived && dist < core.FightRange)
            {
                punishArrived = true;
                Say(MechAdviceKind.Status, prey.pos, Line(speech.Temper < 0f ? ArriveCold : ArriveWarm), m);
            }
            // Close enough for every gun, not on top of them.
            float stand = Mathf.Clamp(core.ShortRange - 2f, 6f, Mathf.Max(6f, core.FightRange - 3f));
            if (dist > stand) MoveTo(m, prey.pos + Norm(m.pos - prey.pos) * stand, 2f);
            else if (m.order == Order.Move && m.Moving && Spend(1)) w.CmdMechHalt(m);
            return true;
        }

        void Verdict(Unit m, MoraleState M)
        {
            float mood = speech.Mood(m), temper = speech.Temper, pride = speech.Pride;
            float will = 0.12f + 0.4f * Mathf.Max(0f, -temper) - 0.1f * Mathf.Max(0f, temper)
                       + 0.15f * pride + 0.35f * Saturate(-mood * 2f);
            will = Mathf.Clamp(will, 0.03f, 0.9f);
            bool go = punishRng.F01() < will || forcePunish;
            LastVerdict = $"will {will:0.00} (temper {temper:+0.00;-0.00}, pride {pride:0.00}, mood {mood:+0.00;-0.00}){(forcePunish ? " forced" : "")}: {(go ? "punish" : "let them go")}";
            forcePunish = false;
            if (go)
            {
                punishing = true;
                punishArrived = false;
                punishUntil = w.time + 150f;
                Punishments++;
                string text = Line(temper < 0f ? GoCold : GoWarm).Replace("{where}", Where(M.camp)).Replace("{n}", M.deserters.Count.ToString());
                Say(MechAdviceKind.Status, M.camp, text, m);
            }
            else if (punishRng.F01() < 0.6f)
                Speak(m, Line(temper < 0f ? SpareCold : SpareWarm));
        }

        void StopPunishing(Unit m, string line)
        {
            punishing = false;
            punishArrived = false;
            w.SetPurge(team, false);
            if (line != null && m != null) Say(MechAdviceKind.Status, m.pos, line, m);
        }

        string Line(string[] lines)
        {
            string s = lines[punishRng.IRange(0, lines.Length)];
            var d = F.design;
            return d != null ? s.Replace("{callsign}", d.callsign) : s;
        }

        static readonly string[] GoCold =
        {
            "{n} of ours threw down their oath and ran -- they are hiding {where}. I will see to them. Nobody walks away from this war.",
            "Deserters, {where}. I am going to remind them what an oath costs.",
            "They ran from the enemy. They will not run from me. Turning back for the deserters {where}.",
        };
        static readonly string[] GoWarm =
        {
            "Some of ours have broken and fled {where}. I take no joy in this -- but if I let it stand, more will follow. Going.",
            "Deserters {where}. It falls to me to deal with them. I wish it did not.",
        };
        static readonly string[] ArriveCold =
        {
            "You ran from them. You cannot run from me.",
            "{callsign} to the deserters: there is no corner far enough.",
            "Stand where you are. This is what the oath was for.",
        };
        static readonly string[] ArriveWarm =
        {
            "Lay down your arms and face it. I am sorry it came to this.",
            "I am here. Do not make it worse by running.",
        };
        static readonly string[] DoneCold =
        {
            "It is done. Nobody else runs.",
            "The deserters are dealt with. Remember it, all of you.",
        };
        static readonly string[] DoneWarm =
        {
            "It is done. I hope never to do that again. Hold your lines.",
            "Finished. Let the rest of you stand where you stood.",
        };
        static readonly string[] Aborted =
        {
            "The deserters will keep. I am needed elsewhere.",
            "No time for deserters now -- I am turning back.",
        };
        static readonly string[] FoeNear =
        {
            "Their Mech is close. The deserters can wait.",
        };
        static readonly string[] SpareCold =
        {
            "Deserters, cowering in the corner. They are not worth a shell. Not today.",
            "Some of ours have run. Let them sit in the dirt -- the enemy is the one I came for.",
        };
        static readonly string[] SpareWarm =
        {
            "Some of ours have run for the corner. Let them go -- fear is not treason, and I am needed here.",
            "Deserters. I will not turn my guns on our own. Hold the line, the rest of you.",
        };

        // ------------------------------------------------------------ words
        string Where(Vector2 p)
        {
            Vector2 d = p - Home;
            if (d.magnitude < 20f) return "at the base";
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            string[] dirs = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            int i = Mathf.RoundToInt(((ang % 360f) + 360f) % 360f / 45f) % 8;
            bool theirSide = (p - Foe).magnitude < (p - Home).magnitude;
            return (theirSide ? "on their side, " : "") + dirs[i] + " of our base";
        }

        string Describe(Vector2 at, float radius)
        {
            int troopers = 0, maulers = 0, skimmers = 0, mechs = 0, other = 0;
            foreach (var e in w.UnitsNear(at, radius))
            {
                if (e.team == team || e.team > 1 || e.def.building) continue;
                switch (e.Type)
                {
                    case UnitType.Trooper: troopers++; break;
                    case UnitType.Mauler: maulers++; break;
                    case UnitType.Skimmer: skimmers++; break;
                    case UnitType.Mech: mechs++; break;
                    case UnitType.Worker: break;
                    default: other++; break;
                }
            }
            var parts = new List<string>();
            if (mechs > 0) parts.Add("their Mech");
            if (maulers > 0) parts.Add($"{maulers} Mauler{(maulers == 1 ? "" : "s")}");
            if (troopers > 0) parts.Add($"{troopers} Trooper{(troopers == 1 ? "" : "s")}");
            if (skimmers > 0) parts.Add($"{skimmers} Skimmer{(skimmers == 1 ? "" : "s")}");
            if (parts.Count == 0) return "an enemy force";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        static string WeaponsOf(MechDesign d)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < d.weapons.Count; i++)
            {
                if (i > 0) sb.Append(i == d.weapons.Count - 1 ? " and " : ", ");
                sb.Append(MechParts.Weapon(d.weapons[i]).name.ToLowerInvariant());
            }
            return "armed with " + sb;
        }

        static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
