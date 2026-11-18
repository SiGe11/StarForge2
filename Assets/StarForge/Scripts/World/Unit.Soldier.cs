// Unit.Soldier.cs -- how a soldier carries out his order under the new combat (TacticalRules).
//
// The order stays the player's: Move, Attack, Hold. How he goes about it is his own, thought over four
// times a second (never by what the camera sees): fire near him adds suppression, which makes him
// crouch, then pins him flat, sooner the less brave he is. In contact he looks for cover against what
// threatens him: behind rocks, trunks, walls and over rises, the way a round would really be stopped.
// He runs to it, stays low there and shows himself only as far as his gun needs, and on an attack he
// bounds from cover to cover. A timid man under heavy fire may freeze or bolt for cover, whatever the
// order, for a few seconds. When it eases he picks the order up again.
using UnityEngine;
using StarForge.Sim;

namespace StarForge.World
{
    public enum SoldierMode : byte { Free, ToCover, InCover, Pinned, Panic }

    public sealed partial class Unit
    {
        /// <summary>How hard he is being shot at, 0 up. Near misses and hits raise it; it ebbs once they stop.</summary>
        [System.NonSerialized] public float suppression;
        [System.NonSerialized] public float lastFiredAtT = -99f;
        [System.NonSerialized] public SoldierMode mode;
        /// <summary>Where he is going for cover, or has it.</summary>
        [System.NonSerialized] public Vector2 coverAt;
        [System.NonSerialized] public bool rushing;
        [System.NonSerialized] float mindAt, coverBy, coverStayUntil, nextCoverLook, panicUntil, panicRollAt;
        [System.NonSerialized] float bravery = -1f;
        [System.NonSerialized] float postureLookAt;
        /// <summary>Who he is with (GameWorld.Squads), and which half of them he is in for taking turns.</summary>
        [System.NonSerialized] public Squad squad;
        [System.NonSerialized] public int fireTeam;
        [System.NonSerialized] public int squadMark;
        /// <summary>A Move stopped for his turn: the way goes on from here (KeepGoing would read the stop as the end).</summary>
        [System.NonSerialized] bool resumeMove;
        /// <summary>Double right-click: he runs, looks for no cover and only being pinned stops him.</summary>
        [System.NonSerialized] public bool hurry;
        /// <summary>The order he last read: a new one takes him out of his cover.</summary>
        [System.NonSerialized] Order readOrder;
        [System.NonSerialized] Vector2 readOrderAt;
        /// <summary>A trial's A/B: this one does none of his own thinking, and walks and shoots as told.</summary>
        [System.NonSerialized] public bool noMind;
        /// <summary>Down behind his cover: he shows nothing and fires nothing.</summary>
        [System.NonSerialized] public bool hiding;
        /// <summary>Behind a low wall: up over it for a few rounds, then down behind it again.</summary>
        [System.NonSerialized] public bool risen;
        /// <summary>Pinned where he had taken cover: rounds stopped by it suppress him at the cover's half rate, and
        /// when it eases he comes back up into it (out into the open beside it, he stood over his own wall).</summary>
        [System.NonSerialized] public bool pinnedInCover;
        [System.NonSerialized] float riseFlipAt;
        /// <summary>The share of his walking pace his posture leaves him; a rush is over 1.</summary>
        [System.NonSerialized] float soldierPace = 1f;
        /// <summary>Over a low wall: he slows to climb it, and the rig plays the vault (vaultAtT).</summary>
        [System.NonSerialized] public bool vaulting;
        [System.NonSerialized] public float vaultAtT = -99f;
        const float VaultPace = 0.5f;

        const float MindEvery = 0.25f;
        const float SuppressionEbb = 0.2f, EbbAfter = 1.5f;
        const float RushPace = 6f / 3.5f, CrouchPace = 0.65f, CrawlPace = 0.27f;
        const float CoverReach = 12f;
        const float NewOrderFrom = 6f;
        const float FollowPoint = 0.8f, PointLead = 12f, CoverIsThere = 6f;

        /// <summary>He is busy with something that takes him out of the order for now.</summary>
        bool SoldierBusy => mode != SoldierMode.Free || throwing != Thrown.None;

        /// <summary>Fire near him. <paramref name="amount"/> at the line's closest pass.</summary>
        public void Suppress(float amount)
        {
            if (noMind) return;   // a plain soldier feels nothing, in his aim or anywhere
            suppression = Mathf.Min(1.5f, suppression + amount);
            lastFiredAtT = world.time;
        }

        /// <summary>His own nerve, from who he is, what he has seen and who is with him.</summary>
        public float Courage()
        {
            if (bravery < 0f)
            {
                uint h = unchecked((uint)id * 2654435761u);
                h ^= h >> 13;
                bravery = 0.25f + 0.75f * ((h & 0xFFFF) / 65535f);
            }
            float c = bravery + 0.12f * rank;
            if (suppression > 0.05f)
            {
                world.Company(this, out int friends, out int lost);
                c += 0.05f * Mathf.Min(friends, 4) - 0.15f * Mathf.Min(lost, 3);
            }
            return Mathf.Clamp(c, 0f, 1.2f);
        }

        /// <summary>A share of his suppression at which he is pinned: the brave stand far more.</summary>
        float PinAt(float courage) => 0.45f + 0.45f * courage;

        void SetPace()
        {
            soldierPace = rushing || (hurry && mode == SoldierMode.Free) ? RushPace : posture == Posture.Prone ? CrawlPace : posture == Posture.Crouch ? CrouchPace : 1f;
            // On the way with nothing in sight, a man goes ahead and the others follow a little behind,
            // never so far that he walks out of the squad.
            if (!rushing && !hurry && squad != null && squad.men.Count >= 3 && !squad.contact && order == Order.AttackMove &&
                squad.point != this && Live(squad.point) && Dist(squad.point) < PointLead)
                soldierPace *= FollowPoint;
        }

        /// <summary>Walking up or running to cover on an attack: what his squad counts as moving in contact.</summary>
        public bool MayWalkNow => !SoldierBusy && SquadLetsMove();
        public bool IsMovingUnderOrder => !hurry && Moving && (order == Order.Attack || order == Order.AttackMove || order == Order.Move || order == Order.Idle);

        /// <summary>A soldier sent somewhere chooses how he gets there; only a hurried one looks at nothing.</summary>
        bool Careful => world.Tactical && def.bodyRadius > 0f && !noMind && !hurry;

        /// <summary>In contact, half a squad moves while the other half shoots: is it his turn, with room?</summary>
        bool SquadLetsMove()
        {
            if (!hurry && world.time < fragWaitUntil) return false;
            var q = squad;
            if (q != null && !hurry && world.time < q.fragUntil) return false;
            if (q == null || !q.contact || hurry) return true;
            if (q.blunder == SquadBlunder.Rush) return true;
            if (q.blunder == SquadBlunder.Stall) return false;
            if (q.men.Count >= 2 && fireTeam != q.movingTeam) return false;
            return IsMovingUnderOrder || q.moving < Mathf.Max(1, q.men.Count / 2);
        }

        /// <summary>He is walking up out of his turn: stop and shoot (not a man already running for cover).</summary>
        public bool StopForTurn()
        {
            if (mode != SoldierMode.Free || !IsMovingUnderOrder) return false;
            Halt();
            resumeMove = order == Order.Move;
            return true;
        }

        void StopLeaning()
        {
            leanSide = 0;
            leanDir = Vector2.zero;
        }

        /// <summary>A new order is obeyed at once, unless he is pinned or frozen, and then when it eases.
        /// An order running out to Idle is no new order, and neither is the same one asked again about
        /// the same place (the AI repeats its orders every pass, and he would never stay in cover).</summary>
        void ReadOrder()
        {
            bool sameKind = order == readOrder || order == Order.Idle;
            bool samePlace = (orderPos - readOrderAt).sqrMagnitude < NewOrderFrom * NewOrderFrom;
            bool moved = !sameKind || !samePlace;
            readOrder = order;
            readOrderAt = orderPos;
            if (order == Order.Idle || order == Order.Hold) hurry = false;
            if (!moved && !hurry) return;
            if (order == Order.Idle) return;
            lineOnlyUntil = moved ? 0f : lineOnlyUntil;
            if (mode != SoldierMode.ToCover && mode != SoldierMode.InCover) return;
            diving = false;
            mode = SoldierMode.Free;
            rushing = false;
            hiding = false;
            StopLeaning();
            nextCoverLook = world.time + 1f;
            if (order == Order.Move || order == Order.AttackMove) MoveTo(orderPos);
        }

        /// <summary>He has thrown down his oath: nothing of how he was fighting comes with him.</summary>
        void StandDown()
        {
            mode = SoldierMode.Free;
            rushing = false;
            hiding = false;
            diving = false;
            throwing = Thrown.None;
            StopLeaning();
            posture = Posture.Stand;
            soldierPace = 1f;
            risen = false;
            vaulting = false;
            pinnedInCover = false;
        }

        void TickSoldier(float dt)
        {
            // Over a low wall he climbs it, slowly: going across it, not along beside a fallen trunk.
            bool over = false;
            if (Moving && world.Walls.Count > 0 && world.Sight != null)
            {
                var w = world.Sight.WallUnder(pos, 0.3f);
                if (w != null)
                {
                    Vector3 v = Velocity;
                    Vector2 ab = w.b - w.a;
                    float across = Mathf.Abs(v.x * ab.y - v.z * ab.x), speed = new Vector2(v.x, v.z).magnitude * ab.magnitude;
                    over = across > 0.5f * speed;
                }
            }
            if (over && !vaulting) vaultAtT = world.time;
            vaulting = over;
            if (noMind) return;
            ReadOrder();
            // Up over a wall only while in cover behind it: out of it, his lines are his posture's again.
            if (risen && mode != SoldierMode.InCover) risen = false;
            TickSideArms();
            if (world.time - lastFiredAtT > EbbAfter) suppression = Mathf.MoveTowards(suppression, 0f, SuppressionEbb * dt);
            if (world.time >= mindAt)
            {
                mindAt = world.time + MindEvery;
                Think();
                SetPace();
            }
        }

        void Release()
        {
            bool back = pinnedInCover && mode == SoldierMode.Pinned && !hurry && Dist(coverAt) < 1.5f;
            pinnedInCover = false;
            if (back)
            {
                var th = world.NearestThreat(this, def.sight);
                if (th == null || world.CoversFrom(coverAt, th))
                {
                    mode = SoldierMode.InCover;
                    rushing = false;
                    hiding = false;
                    StopLeaning();
                    posture = Posture.Crouch;
                    coverStayUntil = world.time + 2.5f;
                    return;
                }
            }
            mode = SoldierMode.Free;
            rushing = false;
            hiding = false;
            StopLeaning();
            posture = Posture.Stand;
            if (order == Order.Move || order == Order.AttackMove) MoveTo(orderPos);
        }

        void Think()
        {
            if (diving)
            {
                if (Dist(coverAt) < 1.1f || world.time >= coverBy) EndDive();
                return;
            }
            var threat = world.NearestThreat(this, def.sight);
            if (throwing != Thrown.None) return;
            if (threat != null) TryThrow(threat);
            if (throwing != Thrown.None) return;
            float courage = Courage();

            if (mode == SoldierMode.Panic)
            {
                if (world.time < panicUntil) return;
                Release();
            }

            // Pinned: flat, and no further until it eases. He still shoots, badly (GameWorld.ShotSpread).
            float pin = PinAt(courage);
            if (mode == SoldierMode.Pinned)
            {
                if (suppression < pin * 0.6f) Release();
                else if (courage < 0.5f && suppression > pin + 0.2f && world.time >= panicRollAt) RollPanic(threat);
                else return;
                if (mode != SoldierMode.Free) return;
            }
            else if (suppression >= pin)
            {
                pinnedInCover = mode == SoldierMode.InCover;
                mode = SoldierMode.Pinned;
                rushing = false;
                posture = Posture.Prone;
                Halt();
                return;
            }

            if (mode == SoldierMode.ToCover)
            {
                // The same order asked again sets him walking to its goal: his way is still to cover.
                if (agent != null && agent.enabled && agent.isOnNavMesh && !agent.pathPending &&
                    (new Vector2(agent.destination.x, agent.destination.z) - coverAt).sqrMagnitude > 1f) MoveTo(coverAt);
                // Not on a wall's own band, where he is in nothing's shelter (vaulting he is slower than coverBy
                // allowed): three seconds more to get off it.
                bool onWall = world.Sight != null && world.Sight.WallUnder(pos, 0f) != null;
                if ((!onWall && (Dist(coverAt) < 1.1f || world.time >= coverBy || (!Moving && world.time >= coverBy - 0.5f))) ||
                    world.time >= coverBy + 3f)
                {
                    mode = SoldierMode.InCover;
                    rushing = false;
                    // Under heavy fire he throws himself down into it (the rig dives, from the run); he comes
                    // up again at his next look.
                    if (suppression > 0.4f && Velocity.sqrMagnitude > 4f) posture = Posture.Prone;
                    Halt();
                    coverStayUntil = world.time + 2.5f + 2.5f * ((id * 7 + (int)world.time) % 8) / 8f;
                }
                return;
            }

            // Told to hurry, he goes: he only stops when pinned. On Hold he keeps his ground, and his head down.
            if (hurry) { posture = Posture.Stand; return; }
            // At arm's length he does not turn his back and run: it is the knife (CloseQuarters).
            if (threat != null && threat.def.bodyRadius > 0f && mode == SoldierMode.Free &&
                Dist(threat) - threat.def.radius <= TacticalRules.KnifeReach + 0.5f)
            {
                posture = Posture.Stand;
                if (Moving && order != Order.Move) Halt();
                return;
            }
            // In cover near where he was sent, he is there: not out into the open for the last metres.
            if (order == Order.Move && mode == SoldierMode.InCover && Dist(orderPos) < CoverIsThere) order = Order.Idle;
            bool mayMove = order == Order.Attack || order == Order.AttackMove || order == Order.Move || order == Order.Idle;

            if (threat == null || threat.def.flying)
            {
                // Nothing to shoot at; a moment to be sure, then up.
                if (world.time - lastFiredAtT > 3f)
                {
                    if (mode != SoldierMode.Free) Release();
                    // A man on his own goes carefully; with others about, upright.
                    else posture = order == Order.AttackMove && (squad == null || squad.men.Count == 1) ? Posture.Crouch : Posture.Stand;
                }
                return;
            }

            float range = Dist(threat);
            if (mode == SoldierMode.InCover)
            {
                // And in cover the same order asked again does not walk him out of it.
                if (Moving) Halt();
                ChooseFiringPosture(threat);
                RiseAndDuck();
                if (!mayMove) return;
                bool flanked = !world.CoversFrom(coverAt, threat);
                bool due = order != Order.Idle && world.time >= coverStayUntil && (order == Order.Move || range > def.range * 0.75f || fireBlocked);
                // A rash man goes on under fire he should have waited out.
                bool rash = false;
                if (due && suppression >= 0.3f && world.time >= rashLookAt && world.time >= nextCoverLook)
                {
                    rashLookAt = world.time + 2.5f;
                    rash = world.Errs(this, 0.5f);
                }
                bool onward = due && (suppression < 0.3f || rash);
                if ((flanked || onward) && world.time >= nextCoverLook) LookForCover(threat, onward, false, flanked);
                return;
            }

            // In the open, in contact.
            ChooseFiringPosture(threat);
            if (mayMove && (suppression > 0.05f || range < def.range * 1.15f) && world.time >= nextCoverLook)
                LookForCover(threat, order != Order.Idle, hp < MaxHp * 0.35f && suppression > 0.3f);
        }

        /// <summary>As low as his gun has a way from, unless the fire is too heavy to show himself at all.</summary>
        /// <summary>Behind a low wall he fires standing, so he comes up for a round or two and goes down again
        /// between them: up 0.9 s (a slow gun's round and some), down 1.2 (the lean's rule, slower, so it
        /// does not read as a twitch). Too hot, he stays down (hiding).</summary>
        void RiseAndDuck()
        {
            if (mode != SoldierMode.InCover || hiding || leanSide != GameWorld.RiseOver) { risen = false; return; }
            if (world.time >= riseFlipAt)
            {
                risen = !risen;
                float jitter = 0.85f + 0.3f * ((id * 2654435761u >> 8) & 0xFF) / 255f;
                riseFlipAt = world.time + (risen ? Mathf.Max(0.9f, def.cooldown + 0.3f) : 1.2f) * jitter;
            }
            posture = risen ? Posture.Stand : Posture.Crouch;
        }

        void ChooseFiringPosture(Unit threat)
        {
            if (mode == SoldierMode.Free && Dist(threat) < 12f && suppression < 0.1f) { posture = Posture.Stand; return; }
            // Too hot: right back behind it, showing nothing and firing nothing, until it eases.
            bool hot = mode == SoldierMode.InCover && suppression > (hiding ? 0.2f : 0.35f);
            if (hot != hiding) { hiding = hot; fireLookAt = -99f; }
            // Down behind it; one who dived in stays flat.
            if (hiding) { if (posture != Posture.Prone) posture = Posture.Crouch; StopLeaning(); return; }
            // The lines below are asked of every soldier in contact: once a second is enough to choose by.
            // (Up off the ground at once when he sets off again, or he crawls the first of his turn.)
            if (world.time < postureLookAt && !(posture == Posture.Prone && Moving && suppression < 0.25f)) return;
            postureLookAt = world.time + 1f;
            // Stopped in the open with an enemy far enough off to lie down for, he lies down: half the
            // target. Under fire he lies down wherever he is.
            // On his way to somewhere else he keeps on, low, rather than crawl it.
            bool flat = Moving ? suppression > 0.25f && order != Order.Move : suppression > 0.25f || Dist(threat) > 15f;
            if (mode == SoldierMode.Free && world.Sees(team, threat) && flat)
            {
                posture = Posture.Prone;
                if (world.LineOfFire(this, threat)) return;
            }
            posture = Posture.Crouch;
            if (!world.Sees(team, threat) || world.LineOfFire(this, threat)) return;
            posture = Posture.Stand;
            if (!world.LineOfFire(this, threat)) posture = Posture.Crouch;
        }

        /// <summary>Find a place out of what threatens him, and run for it. Spends one of the world's looks;
        /// with none to spare he asks again next time. Onward means nearer the enemy; back, further off.</summary>
        void LookForCover(Unit threat, bool onward, bool back, bool flanked = false)
        {
            // His turn, unless what he is behind no longer covers him.
            if (!back && !flanked && !SquadLetsMove()) return;
            if (!world.TakeCoverLook()) return;
            nextCoverLook = world.time + 2.5f;
            // Getting it wrong: he stays where he is, or takes a worse place than there was to take.
            bool poor = false;
            if (!back && !flanked && world.Errs(this))
            {
                if (world.CombatRoll() < 0.5f) return;
                poor = true;
            }
            noCoverLast = !world.FindCover(this, threat, CoverReach, onward, back, out var spot, poor);
            if (noCoverLast)
            {
                // Nowhere better: an onward bound with no cover to reach ends in the open, walking up.
                if (mode == SoldierMode.InCover && onward)
                {
                    mode = SoldierMode.Free;
                    posture = Posture.Crouch;
                    if (order == Order.Move) MoveTo(orderPos);
                }
                // Already behind something (a wall a few steps in front): he takes it where he stands, and
                // comes up over it for his rounds rather than stand in the open to fire.
                else if (mode == SoldierMode.Free && !onward && !Moving && world.CoversFrom(pos, threat) &&
                         (world.Sight == null || world.Sight.WallUnder(pos, 0f) == null))
                {
                    noCoverLast = false;
                    coverAt = pos;
                    mode = SoldierMode.InCover;
                    rushing = false;
                    coverStayUntil = world.time + 2.5f;
                }
                return;
            }
            if (squad != null) squad.moving++;
            coverAt = spot;
            hiding = false;
            StopLeaning();
            coverBy = world.time + Dist(spot) / (def.speed * RushPace) + 1.5f;
            mode = SoldierMode.ToCover;
            rushing = true;
            posture = Posture.Stand;
            MoveTo(spot);
        }

        void RollPanic(Unit threat)
        {
            panicRollAt = world.time + 6f;
            if (world.CombatRoll() > 0.35f) return;
            mode = SoldierMode.Panic;
            panicUntil = world.time + 2f + 2f * world.CombatRoll();
            if (threat != null && !threat.def.flying && world.CombatRoll() < 0.5f && world.FindCover(this, threat, CoverReach, false, true, out var spot))
            {
                coverAt = spot;
                rushing = true;
                posture = Posture.Stand;
                MoveTo(spot);
                return;
            }
            posture = Posture.Prone;
            Halt();
        }
    }
}
