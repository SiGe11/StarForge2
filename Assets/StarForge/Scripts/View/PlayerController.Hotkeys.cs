// PlayerController.Hotkeys.cs — the keys that move the eye rather than give orders. Every
// letter but WASD and QE is a command card's, so these sit on Option + digits, F-keys, Tab
// and Backspace. A MacBook's F-row sends brightness and media unless fn is held, so the
// camera spots answer Option + 1-4 as well as StarCraft II's F5-F8 (the user's call, 2026-11-24).
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.View
{
    public sealed partial class PlayerController
    {
        public const int CameraSpots = 4;
        readonly RTSCamera.Shot[] spots = new RTSCamera.Shot[CameraSpots];
        readonly bool[] spotSet = new bool[CameraSpots];

        /// <summary>Supplied by the HUD: the place of the newest alert that has one (step 0), the one
        /// before it (step 1) and so on; null past the oldest kept.</summary>
        public Func<int, Vector2?> AlertPlace;

        /// <summary>A line of feedback that is not the world's news (a camera spot saved, nothing idle): the HUD shows it.</summary>
        public event Action<string> Hint;

        int alertStep;
        float alertStepAt = -9f;
        Unit lastFoundry, lastIdle;

        void HandleEyeKeys(Keyboard kb, bool ctrl, bool alt)
        {
            for (int i = 0; i < CameraSpots; i++)
            {
                bool digit = alt && kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame;
                bool fkey = kb[(Key)((int)Key.F5 + i)].wasPressedThisFrame;
                if (!digit && !fkey) continue;
                // Saving takes Ctrl; with Option Cmd does too. Not Cmd + F5: that is macOS's VoiceOver.
                // macOS's keyboard-navigation shortcuts may also keep Ctrl + F5-F8 (on by default):
                // Ctrl + Option + 1-4 is the save that always arrives.
                bool save = digit ? ctrl : kb.ctrlKey.isPressed;
                if (save) SaveCameraSpot(i);
                else RecallCameraSpot(i);
            }
            if (kb.tabKey.wasPressedThisFrame) JumpToAlert();
            if (kb.backspaceKey.wasPressedThisFrame) NextFoundry();
            // All of them with Shift or Ctrl (not Cmd: Cmd + F1 is the Mac's display mirroring, and
            // macOS may keep Ctrl + F1 for keyboard access, so Shift is the one that always arrives).
            if (kb.f1Key.wasPressedThisFrame)
            {
                if (kb.shiftKey.isPressed || kb.ctrlKey.isPressed) SelectIdleDiggers();
                else NextIdleDigger();
            }
        }

        // ------------------------------------------------------------ camera spots
        public bool CameraSpotSet(int i) => i >= 0 && i < CameraSpots && spotSet[i];

        /// <summary>Ctrl + Option + 1-4 or Ctrl + F5-F8: the view as it is (place, turn, zoom) kept in spot i.</summary>
        public void SaveCameraSpot(int i)
        {
            if (rig == null || i < 0 || i >= CameraSpots) return;
            spots[i] = rig.Capture();
            spotSet[i] = true;
            Hint?.Invoke($"Camera spot {i + 1} saved");
        }

        /// <summary>Option + 1-4 or F5-F8: back to spot i.</summary>
        public bool RecallCameraSpot(int i)
        {
            if (rig == null || i < 0 || i >= CameraSpots) return false;
            if (!spotSet[i])
            {
                Hint?.Invoke($"Camera spot {i + 1} is empty: Ctrl + Option + {i + 1} saves the view there");
                return false;
            }
            rig.GoTo(spots[i]);
            return true;
        }

        // ------------------------------------------------------------ the latest alert
        /// <summary>Tab: to where the newest alert happened; again within 3 s, the one before, and round.</summary>
        public bool JumpToAlert()
        {
            if (AlertPlace == null || rig == null) return false;
            alertStep = Time.unscaledTime - alertStepAt < 3f ? alertStep + 1 : 0;
            alertStepAt = Time.unscaledTime;
            var at = AlertPlace(alertStep);
            if (at == null && alertStep > 0) { alertStep = 0; at = AlertPlace(0); }
            if (at == null) { Hint?.Invoke("No alert to look at yet"); return false; }
            rig.CenterOn(at.Value);
            return true;
        }

        // ------------------------------------------------------------ the Foundry
        /// <summary>Backspace (StarCraft II's base key): select the next Foundry of ours and look at it.</summary>
        public bool NextFoundry()
        {
            var next = NextAfter(lastFoundry, u => u.Type == UnitType.Foundry && u.team == team);
            if (next == null) { Hint?.Invoke("No Foundry standing"); return false; }
            lastFoundry = next;
            SelectOnly(next);
            if (rig != null) rig.CenterOn(next.pos);
            return true;
        }

        // ------------------------------------------------------------ idle Diggers
        /// <summary>A Digger of ours with nothing to do: not mining, carrying, building, moving or running.</summary>
        public bool IdleDigger(Unit u) =>
            Unit.Live(u) && u.Type == UnitType.Worker && u.team == team && !u.deserted && u.order == Order.Idle && u.Complete;

        /// <summary>How many Diggers stand idle (the HUD's idle button). No allocation.</summary>
        public int IdleDiggerCount()
        {
            int n = 0;
            foreach (var u in world.units) if (IdleDigger(u)) n++;
            return n;
        }

        /// <summary>F1 or the idle button: select the next idle Digger and look at it.</summary>
        public bool NextIdleDigger()
        {
            var next = NextAfter(lastIdle, IdleDigger);
            if (next == null) { Hint?.Invoke("No idle Diggers"); return false; }
            lastIdle = next;
            SelectOnly(next);
            if (rig != null) rig.CenterOn(next.pos);
            return true;
        }

        /// <summary>Shift (or Ctrl) + F1 or Ctrl-click on the idle button: every idle Digger, the camera where it is.</summary>
        public int SelectIdleDiggers()
        {
            Selection.Clear();
            foreach (var u in world.units) if (IdleDigger(u)) Selection.Add(u);
            SelectionChanged?.Invoke();
            if (Selection.Count == 0) Hint?.Invoke("No idle Diggers");
            return Selection.Count;
        }

        /// <summary>The first unit matching after <paramref name="last"/> in the world's list, round to the start.</summary>
        Unit NextAfter(Unit last, Func<Unit, bool> match)
        {
            Unit first = null;
            bool passed = last == null;
            foreach (var u in world.units)
            {
                if (ReferenceEquals(u, last)) { passed = true; continue; }
                if (!Unit.Live(u) || !match(u)) continue;
                if (passed) return u;
                if (first == null) first = u;
            }
            // Past the end (or the last one gone): round to the start; the last one again if it is all there is.
            if (first != null) return first;
            return Unit.Live(last) && match(last) ? last : null;
        }
    }
}
