# Writing a new play-mode check

Model: `TargetingTrial` in `Assets/StarForge/Editor/BugTrials.cs` (staged trial) and `BugTrials.PauseSpeed` (answers at once). Put a new check in the Editor file of its family (`BugTrials`, `MoraleTrials`, `MechTrials`, or `AgentPlay` in `AgentInbox.cs` for World/View helpers), never in runtime scripts.

## Shape
- **Answers at once** (reads state, flips a setting and back): a `public static string Name()` that returns `"not playing"` unless `EditorApplication.isPlaying`, then the numbers.
- **Takes match time:** a `public sealed class NameTrial : BugTrial` with `IEnumerator Start()`, plus `public static string Name() => Stage<NameTrial>("Name")` and `NameReport() => Report<NameTrial>()`. `Stage` replaces any earlier instance; `report` holds "staging" until the trial writes it.
- Inside `Start`: `yield return EnsureMatch()` (starts a match with memory off if none runs), `if (!Ready) { report = "no match running"; yield break; }`, then `Wait(seconds)` in match time. `FindField(rng, ...)` finds open dry ground away from both bases; `Remove(u)` kills a staged unit.

## Rules
- **Save and restore every static you touch in `try/finally`:** `Commander.Suspended` (silence the opponent so it does not interfere), `Time.timeScale`, A/B switches like `Unit.StickyTargets`. Statics survive play sessions; one left on skewed a whole evaluation.
- **Spawn and order through the same API the game uses** (`world.Spawn`, `world.Cmd*`), and observe through `world.Event` (unsubscribe at the end). Do not poke private state to make the scenario happen.
- **Own seeded randomness** (`new System.Random(<constant>)`), never `UnityEngine.Random`, so the trial repeats.
- **Report numbers next to the target**, plus A/B when a switch exists ("before"/"after" in one run). One line per case; say what "good" is in the doc comment (`/// <summary>`), and add the check and its target to the area notes of the code it tests.
- Look-helpers (captures) never restart a running match; write PNGs to `Temp/<name>_*.png`; judge them by reading the PNG and, where possible, a number.
- Writing the trial is a `.cs` write: take the Editor slot first (SKILL.md step 3), wait for `compiling=False` before `play`, or the Editor enters play on the old assembly without your trial.
