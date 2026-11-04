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
        /// <summary>The order he last read: a new one takes him out of his cover.</summary>
        [System.NonSerialized] Order readOrder;
        [System.NonSerialized] Vector2 readOrderAt;
        /// <summary>A trial's A/B: this one does none of his own thinking, and walks and shoots as told.</summary>
        [System.NonSerialized] public bool noMind;
        /// <summary>Down behind his cover: he shows nothing and fires nothing.</summary>
        [System.NonSerialized] public bool hiding;
        /// <summary>The share of his walking pace his posture leaves him; a rush is over 1.</summary>
        [System.NonSerialized] float soldierPace = 1f;

        const float MindEvery = 0.25f;
        const float SuppressionEbb = 0.2f, EbbAfter = 1.5f;
        const float RushPace = 6f / 3.5f, CrouchPace = 0.65f, CrawlPace = 0.27f;
        const float CoverReach = 12f;
        const float NewOrderFrom = 6f;

        /// <summary>He is busy with something that takes him out of the order for now.</summary>
        bool SoldierBusy => mode != SoldierMode.Free;

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
            soldierPace = rushing ? RushPace : posture == Posture.Prone ? CrawlPace : posture == Posture.Crouch ? CrouchPace : 1f;
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
            if (!moved && order != Order.Move) return;
            if (order == Order.Idle) return;
            lineOnlyUntil = moved ? 0f : lineOnlyUntil;
            if (mode != SoldierMode.ToCover && mode != SoldierMode.InCover) return;
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
            StopLeaning();
            posture = Posture.Stand;
            soldierPace = 1f;
        }

        void TickSoldier(float dt)
        {
            if (noMind) return;
            ReadOrder();
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
            mode = SoldierMode.Free;
            rushing = false;
            hiding = false;
            StopLeaning();
            posture = Posture.Stand;
            if (order == Order.Move || order == Order.AttackMove) MoveTo(orderPos);
        }

        void Think()
        {
            var threat = world.NearestThreat(this, def.sight);
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
                if (Dist(coverAt) < 1.1f || world.time >= coverBy || (!Moving && world.time >= coverBy - 0.5f))
                {
                    mode = SoldierMode.InCover;
                    rushing = false;
                    Halt();
                    coverStayUntil = world.time + 2.5f + 2.5f * ((id * 7 + (int)world.time) % 8) / 8f;
                }
                return;
            }

            // Told to go, he goes: he only stops when pinned. On Hold he keeps his ground, and his head down.
            if (order == Order.Move) { posture = Posture.Stand; return; }
            bool mayMove = order == Order.Attack || order == Order.AttackMove || order == Order.Idle;

            if (threat == null || threat.def.flying)
            {
                // Nothing to shoot at; a moment to be sure, then up.
                if (world.time - lastFiredAtT > 3f)
                {
                    if (mode != SoldierMode.Free) Release();
                    else posture = Posture.Stand;
                }
                return;
            }

            float range = Dist(threat);
            if (mode == SoldierMode.InCover)
            {
                // And in cover the same order asked again does not walk him out of it.
                if (Moving) Halt();
                ChooseFiringPosture(threat);
                if (!mayMove) return;
                bool flanked = !world.CoversFrom(coverAt, threat);
                bool onward = order != Order.Idle && suppression < 0.3f && world.time >= coverStayUntil &&
                              (range > def.range * 0.75f || fireBlocked);
                if ((flanked || onward) && world.time >= nextCoverLook) LookForCover(threat, onward, false);
                return;
            }

            // In the open, in contact.
            ChooseFiringPosture(threat);
            if (mayMove && (suppression > 0.05f || range < def.range * 1.15f) && world.time >= nextCoverLook)
                LookForCover(threat, order != Order.Idle, hp < MaxHp * 0.35f && suppression > 0.3f);
        }

        /// <summary>As low as his gun has a way from, unless the fire is too heavy to show himself at all.</summary>
        void ChooseFiringPosture(Unit threat)
        {
            if (mode == SoldierMode.Free && Dist(threat) < 12f && suppression < 0.1f) { posture = Posture.Stand; return; }
            // Too hot: right back behind it, showing nothing and firing nothing, until it eases.
            bool hot = mode == SoldierMode.InCover && suppression > (hiding ? 0.2f : 0.35f);
            if (hot != hiding) { hiding = hot; fireLookAt = -99f; }
            if (hiding) { posture = Posture.Crouch; StopLeaning(); return; }
            // The lines below are asked of every soldier in contact: once a second is enough to choose by.
            if (world.time < postureLookAt) return;
            postureLookAt = world.time + 1f;
            // Stopped in the open with an enemy far enough off to lie down for, he lies down: half the
            // target. Under fire he lies down wherever he is.
            if (mode == SoldierMode.Free && world.Sees(team, threat) && (suppression > 0.25f || (!Moving && Dist(threat) > 15f)))
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
        void LookForCover(Unit threat, bool onward, bool back)
        {
            if (!world.TakeCoverLook()) return;
            nextCoverLook = world.time + 2.5f;
            if (!world.FindCover(this, threat, CoverReach, onward, back, out var spot))
            {
                // Nowhere better: an onward bound with no cover to reach ends in the open, walking up.
                if (mode == SoldierMode.InCover && onward) { mode = SoldierMode.Free; posture = Posture.Crouch; }
                return;
            }
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
