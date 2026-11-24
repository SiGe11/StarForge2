// Unit.SideArms.cs -- what a soldier does with what he carries besides his gun (TacticalRules; the
// world's side is GameWorld.Grenades.cs). Nobody orders it. No magazines: the pistol is for when the
// main gun cannot be used, on the run or too close for it, and the knife for arm's length.
using UnityEngine;
using StarForge.Sim;
using static StarForge.SFMath;

namespace StarForge.World
{
    public sealed partial class Unit
    {
        /// <summary>What he has left of each.</summary>
        [System.NonSerialized] public int frags, smokes, flashes;
        [System.NonSerialized] bool loadoutGiven;
        /// <summary>Winding up to throw it, and when each last happened, for his body to show.</summary>
        [System.NonSerialized] public Thrown throwing;
        [System.NonSerialized] public float thrownAtT = -99f, stabbedAtT = -99f, pistolAtT = -99f;
        [System.NonSerialized] Vector2 throwAt;
        [System.NonSerialized] float throwReleaseAt, nextThrowLook, restockAt;
        [System.NonSerialized] float pistolReadyAt, pistolLookAt, knifeReadyAt;
        [System.NonSerialized] Unit pistolTarget;
        /// <summary>His last look for cover found none: smoke is what he has.</summary>
        [System.NonSerialized] bool noCoverLast;
        /// <summary>A flash in his eyes: he notices nothing until then.</summary>
        [System.NonSerialized] public float blindUntil = -99f;
        /// <summary>His own frag is in the air: he stays where he is until it has gone off.</summary>
        [System.NonSerialized] public float fragWaitUntil = -99f;
        /// <summary>Getting away from a frag: he lies flat where that gets him, and it is no cover.</summary>
        [System.NonSerialized] bool diving;
        [System.NonSerialized] float friendsLookAt;
        [System.NonSerialized] bool friendsClear = true;

        /// <summary>Rounds left in the magazine, and when the reload under way is done. Ammunition never runs out.</summary>
        [System.NonSerialized] public int rounds;
        [System.NonSerialized] public float reloadAtT = -99f;
        [System.NonSerialized] float reloadDoneAt, rashLookAt;
        [System.NonSerialized] Unit nervousOf;
        public bool Reloading => reloadDoneAt > 0f && world.time < reloadDoneAt;
        /// <summary>The rank that counts when he decides: his own, or the oldest hand with him.</summary>
        public int Experience => squad != null ? Mathf.Max(rank, squad.veteran) : rank;

        const float ThrowLookEvery = 1f, ThrowWind = 0.5f, ThrowChance = 0.5f, RestockEvery = 10f;
        const float SmokeSquadWaits = 15f, ThrowSquadWaits = 6f;

        void TickSideArms()
        {
            if (!loadoutGiven)
            {
                loadoutGiven = true;
                TacticalRules.Loadout(Type, out frags, out smokes, out flashes);
                rounds = def.magazine;
                restockAt = world.time + RestockEvery;
            }
            if (throwing != Thrown.None && world.time >= throwReleaseAt) ReleaseThrow();
            if (reloadDoneAt > 0f && world.time >= reloadDoneAt)
            {
                reloadDoneAt = 0f;
                rounds = def.magazine;
            }
            // A lull with half a magazine gone: he tops up while he can.
            else if (reloadDoneAt <= 0f && def.magazine > 1 && rounds < def.magazine / 2 && world.time - firedAt > 4f && world.time - lastFiredAtT > 3f) BeginReload();
            if (world.time >= restockAt)
            {
                restockAt = world.time + RestockEvery;
                Restock();
            }
        }

        void SpendRound()
        {
            if (noMind || def.magazine <= 0 || !loadoutGiven) return;
            if (reloadDoneAt > 0f && world.time >= reloadDoneAt) { reloadDoneAt = 0f; rounds = def.magazine; }
            if (--rounds <= 0) BeginReload();
        }

        void BeginReload()
        {
            reloadAtT = world.time;
            // After the last round's own wait: a magazine and its reload take magazine x cooldown + reload,
            // the cycle the numbers were set for (overlapped, a Marksman fired 19% faster than before).
            reloadDoneAt = world.time + Mathf.Max(0f, cooldown) + def.reloadTime * (mode == SoldierMode.Pinned ? 1.4f : 1f);
            world.Reloads++;
        }

        void Restock()
        {
            TacticalRules.Loadout(Type, out int f, out int s, out int l);
            if (frags >= f && smokes >= s && flashes >= l) return;
            if (!world.NearOwnDepot(this)) return;
            if (frags < f) frags++;
            else if (smokes < s) smokes++;
            else flashes++;
        }

        /// <summary>Is there something worth throwing, and is it safe to? One a squad at a time.</summary>
        void TryThrow(Unit threat)
        {
            if (hurry || throwing != Thrown.None || world.time < nextThrowLook || threat.def.flying) return;
            if (mode == SoldierMode.Panic || mode == SoldierMode.ToCover) return;
            nextThrowLook = world.time + ThrowLookEvery;
            float d = Dist(threat);
            var q = squad;
            bool squadWaits = q != null && world.time < q.thrownUntil;

            // Pinned, or caught in the open with nowhere to go: smoke between him and them.
            if (smokes > 0 && suppression > 0.3f && d > 8f && !squadWaits &&
                (mode == SoldierMode.Pinned || (mode == SoldierMode.Free && noCoverLast && (TacticalRules.LegacySmoke || suppression >= TacticalRules.SmokeFreeSuppression))) &&
                !world.SmokeNear(pos, TacticalRules.SmokeRadius * 1.5f))
            {
                Vector2 to = (threat.pos - pos) / d;
                BeginThrow(Thrown.Smoke, pos + to * Mathf.Min(d * 0.4f, mode == SoldierMode.Pinned ? 3.5f : 8f));
                return;
            }
            if (mode == SoldierMode.Pinned || squadWaits) return;
            // At men, not at armour or walls; at what his rifle cannot reach, or at more than one of them.
            if (threat.def.Heavy || threat.def.building) return;
            bool noLine = target == threat && fireBlocked && !hiding;
            // (A nervous hand throws at one man in the open: judged once for each man he faces.)
            if (!noLine && world.EnemiesRound(threat, team, 5f) < 2)
            {
                if (nervousOf == threat) return;
                nervousOf = threat;
                if (!world.Errs(this, 0.4f)) return;
            }
            if (frags <= 0 && flashes <= 0) return;
            if (world.CombatRoll() > ThrowChance) return;
            float secs = GameWorld.ThrowFlight(d);
            float fragSecs = secs + TacticalRules.FragFuse;
            bool canFlash = flashes > 0 && threat.def.bodyRadius > 0f && d >= TacticalRules.FlashMin && d <= TacticalRules.FlashMax &&
                            world.FriendsClear(this, threat.pos, TacticalRules.FlashReach, secs);
            // Going in on men he has no line to, a flash first: they cannot shoot while he closes.
            if (canFlash && noLine && (order == Order.Attack || order == Order.AttackMove))
            {
                BeginThrow(Thrown.Flash, threat.pos);
                return;
            }
            if (frags > 0 && d >= TacticalRules.FragMin && d <= TacticalRules.FragMax &&
                world.FriendsClear(this, threat.pos, TacticalRules.FragReach + GameWorld.ThrowErrorMax, fragSecs))
            {
                BeginThrow(Thrown.Frag, threat.pos);
                return;
            }
            // Too near his own for a frag, or none left: a flash hurts no one, and at worst dazes a friend.
            if (canFlash) BeginThrow(Thrown.Flash, threat.pos);
        }

        void BeginThrow(Thrown what, Vector2 at)
        {
            throwing = what;
            throwAt = at;
            thrownAtT = world.time;
            throwReleaseAt = world.time + (mode == SoldierMode.Pinned ? 0.15f : ThrowWind);
            if (mode == SoldierMode.Free && Moving)
            {
                Halt();
                resumeMove = order == Order.Move;
            }
            if (squad != null) squad.thrownUntil = world.time + (what == Thrown.Smoke ? (TacticalRules.LegacySmoke ? SmokeSquadWaits : TacticalRules.SmokeSquadWait) : ThrowSquadWaits);
        }

        /// <summary>It leaves his hand, unless his friends have walked to where it would land.</summary>
        void ReleaseThrow()
        {
            var what = throwing;
            throwing = Thrown.None;
            if (mode == SoldierMode.Panic) return;
            float d = Dist(throwAt);
            if (what != Thrown.Smoke)
            {
                float reach = what == Thrown.Frag ? TacticalRules.FragReach + GameWorld.ThrowErrorMax : TacticalRules.FlashReach;
                float secs = GameWorld.ThrowFlight(d) + (what == Thrown.Frag ? TacticalRules.FragFuse : TacticalRules.FlashFuse);
                if (!world.FriendsClear(this, throwAt, reach, secs)) return;
                // Nobody who knows it is coming walks into it.
                if (what == Thrown.Frag)
                {
                    fragWaitUntil = world.time + secs;
                    if (squad != null) squad.fragUntil = world.time + secs;
                }
            }
            if (what == Thrown.Frag) frags--;
            else if (what == Thrown.Smoke) smokes--;
            else flashes--;
            world.Throw(this, what, throwAt);
        }

        /// <summary>A frag has landed by him: away from it, whatever he was doing.</summary>
        public void DiveFrom(Vector2 at)
        {
            if (noMind || deserted || hurry || mode == SoldierMode.Panic) return;
            Vector2 away = pos - at;
            float len = away.magnitude;
            away = len > 0.2f ? away / len : new Vector2(Mathf.Cos(id * 2.4f), Mathf.Sin(id * 2.4f));
            Vector2 spot = world.NearestReachable(this, pos + away * (TacticalRules.FragReach + 1.5f - len));
            throwing = Thrown.None;
            diving = true;
            coverAt = spot;
            coverBy = world.time + 1.4f;
            mode = SoldierMode.ToCover;
            rushing = true;
            hiding = false;
            StopLeaning();
            posture = Posture.Stand;
            MoveTo(spot);
        }

        void EndDive()
        {
            diving = false;
            rushing = false;
            mode = SoldierMode.Free;
            posture = Posture.Prone;
            Halt();
            resumeMove = order == Order.Move;
        }

        /// <summary>Stunned: an order waits for him to come round (Release takes it up).</summary>
        public bool Stunned => mode == SoldierMode.Panic && world.time < blindUntil;

        /// <summary>A flash: he sees nothing and does nothing for a moment. Fire and orders wait.</summary>
        public void Stun(float seconds)
        {
            if (noMind || deserted) return;
            diving = false;
            blindUntil = Mathf.Max(blindUntil, world.time + seconds);
            Suppress(0.35f);
            throwing = Thrown.None;
            mode = SoldierMode.Panic;
            panicUntil = Mathf.Max(panicUntil, world.time + seconds);
            rushing = false;
            hiding = false;
            StopLeaning();
            posture = Posture.Crouch;
            Halt();
        }

        void FirePistolAt(Unit t)
        {
            pistolReadyAt = world.time + TacticalRules.PistolCooldown;
            pistolAtT = world.time;
            world.FirePistol(this, t);
        }

        /// <summary>On the run a man cannot use his main gun, but he can loose off a pistol at what is close.</summary>
        void RunningShot()
        {
            if (world.time < pistolReadyAt) return;
            if (world.time >= pistolLookAt)
            {
                pistolLookAt = world.time + 0.5f;
                var t = world.NearestThreat(this, TacticalRules.PistolRange);
                pistolTarget = t != null && !t.def.flying && !t.def.Heavy && !t.def.building && world.LineOfFire(this, t) ? t : null;
            }
            if (Live(pistolTarget) && world.Sees(team, pistolTarget)) FirePistolAt(pistolTarget);
        }

        /// <summary>Knife at arm's length; the pistol where his main gun is no use. True if that is all he
        /// does about this target this tick.</summary>
        bool CloseQuarters(float dt)
        {
            if (target.def.flying || target.Untargetable) return false;
            float d = Dist(target) - target.def.radius;
            bool known = world.Sees(team, target);
            float want = Mathf.Atan2(target.pos.x - pos.x, target.pos.y - pos.y);
            if (target.def.bodyRadius > 0f && d <= TacticalRules.KnifeReach && known && LineOfFireNow())
            {
                yaw = ApproachAngle(yaw, want, def.turnRate * dt);
                if (world.time >= knifeReadyAt && Mathf.Abs(WrapAngle(want - yaw)) < 0.6f)
                {
                    knifeReadyAt = world.time + TacticalRules.KnifeCooldown;
                    stabbedAtT = world.time;
                    world.Stab(this, target);
                }
                return true;
            }
            bool inside = def.pistolInside > 0f && d < def.pistolInside;
            bool beyond = def.range < TacticalRules.PistolRange && d > def.range && d <= TacticalRules.PistolRange;
            // His main gun out of it for now, and they are close: the pistol while he reloads.
            bool clearing = Reloading && d <= TacticalRules.PistolRange;
            if ((!inside && !beyond && !clearing) || hiding || !known) return false;
            if (mode == SoldierMode.InCover && leanSide == GameWorld.RiseOver && !risen) return false;
            // For men and light things: at armour or a wall his main gun, or nothing, is better.
            if (target.def.Heavy || target.def.building) return false;
            // On a plain Move he shoots only while stopped.
            bool walking = Moving;
            if (walking && order == Order.Move) return false;
            if (!walking) yaw = ApproachAngle(yaw, want, def.turnRate * dt);
            bool line = LineOfFireNow();
            if (line && world.time >= pistolReadyAt && (walking || Mathf.Abs(WrapAngle(want - yaw)) < 0.6f)) FirePistolAt(target);
            // No way there: he is not done, and looks for what else he can shoot.
            return (inside || clearing) && line;
        }

        /// <summary>A shell or rocket that could catch his own: he holds it until they are clear.</summary>
        bool FriendsAllow()
        {
            // A tank's shell (the Mauler's): its own men by the target hold it, as a mortar's do.
            bool shell = Type == UnitType.Mauler;
            if (def.bodyRadius <= 0f && !shell) return true;
            if (!shell && def.weapon != WeaponKind.Mortar && def.weapon != WeaponKind.Bazooka) return true;
            if (world.time < friendsLookAt) return friendsClear;
            friendsLookAt = world.time + 0.5f;
            float reach = def.splash + 1.5f;
            if (def.weapon == WeaponKind.Bazooka || shell)
            {
                friendsClear = world.FriendsClear(this, target.pos, reach, 0.8f);
                return friendsClear;
            }
            // A mortar leads what it fires at (FireRoster): round there too.
            Vector3 v = target.Velocity;
            float flight = Mathf.Clamp(Dist(target) / 13f, 1.3f, 2.6f);
            Vector2 led = target.pos + new Vector2(v.x, v.z) * (flight * 0.5f);
            friendsClear = world.FriendsClear(this, target.pos, reach, flight) && world.FriendsClear(this, led, reach, flight);
            return friendsClear;
        }
    }
}
