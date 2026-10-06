# Claude Code setup for StarForge

For the humans maintaining it. Claude reads `CLAUDE.md`, the area notes, the skills and the agents; nothing loads this file.

```
CLAUDE.md                          always loaded (every session and subagent): cross-cutting rules
Assets/StarForge/**/CLAUDE.md,     area notes: rules + measured targets per area, injected the first
Tools/**/CLAUDE.md                   time a tool call touches the area (hooks/area_notes.py)
.claude/
  settings.json                    hooks, read-denies, git deny/ask rules, Bash output cap
  hooks/area_notes.py              area-note injector, state per session *and* per subagent
  agents/scout.md                  read-only investigator (Sonnet)
  agents/reviewer.md               read-only adversarial reviewer (Opus)
  skills/verify-change/            levels, check per area, Editor-slot protocol (+ new-trial.md)
  skills/measure-performance/      player benchmark, burst, gallery, AI evaluation
  skills/checkpoint/               any commit the user asks for: this session's files only
.github/workflows/licences.yml     CI: offline licence check on every push to main and every PR
```
Auto-memory (`~/.claude/projects/-Users-sige-starforge/memory/`) holds session state such as feature progress. It reaches the main conversation only, never a subagent, so no rule may live only there.

## Why it is shaped like this
- **Parallelism is at the session level, not inside one.** The user runs several sessions, one per feature, in one working tree with one Unity Editor. A `.cs` write recompiles under every session's play mode, so within a session one agent writes and subagents only read. That is why there are no gameplay, Unity, UI, QA or performance *implementer* agents. The domain knowledge such agents would carry already lives in the area notes, which reach any agent that touches the area.
- **Two agents, each earning its keep.** `scout` keeps wide reads out of the main context, and unlike built-in Explore it loads `CLAUDE.md` (grep-first discipline, invariants). `reviewer` gives a fresh, skeptical context, checks StarForge's own failure lenses, and returns file:line findings tied to the check that would expose each.
- **Skills hold procedures**, loaded on demand: `verify-change` (correctness in the Editor), `measure-performance` (fps and evaluation), `checkpoint` (path-scoped commits).
- **No git worktrees.** A worktree is a second Unity project with its own `Library` to import, and the open Editor cannot drive it. Isolation comes from path-scoped commits and the deny/ask rules instead.
- **No Agent Teams.** They are experimental, and while on they turn named subagents into teammates writing the same tree. Cross-session `SendMessage` already covers coordination between the user's sessions.
- **The licence rule has a backstop outside Claude.** Instructions are not enforcement, and the repository is public. CI runs `check_licences.py --offline` on exactly what was pushed (paths only, so LFS content is not fetched). A clean-clone simulation of `21713ac` found the committed register naming two flipbook PNGs that were never committed.
- **Area-note hook state is keyed on session_id + agent_id.** A subagent's tool calls carry the parent's `session_id`. Keyed on that alone, a subagent never got notes the parent already had (and a subagent's first touch hid them from the parent).

## Using it
- Ask normally; the session implements. For "how does X work" or "what would adding Y touch", it should send `scout` (or ask for it: "use the scout agent").
- Before a gameplay, AI, View, shader or generator change is reported done, the session runs `verify-change` (named check, numbers against the area-note target) and then `reviewer`. Ask for either by name at any time.
- `/checkpoint [subject]` commits only this session's files with their `.meta` and lists the peers' files it left alone.
- New or edited agent definitions load at session start: open a new session after changing `.claude/agents/`. Skills reload live.

## Maintaining it
- **A new lesson** goes in the area `CLAUDE.md` it belongs to (rule + one-clause reason + target), not here and not in an agent.
- **A new skill** only after a workflow has been done by hand twice and needed re-explaining (candidates: adding a unit type, adding a Mech part). Capture the touch-point list `scout` produced then.
- **A new agent** only for a distinct read-only role with its own context needs. Never an implementer while one Editor is shared.
- **Audit every few weeks:** `/doctor prompt-audit` in a terminal `claude` session (duplicates and contradictions across CLAUDE.md, rules, skills, agents); check that descriptions still trigger, that the reviewer's lenses match the area notes, and that the `verify-change` table names checks that still exist (`grep -n "public static string" Assets/StarForge/Editor/*.cs`).
- **Test the hook after editing it** (it runs on every tool call of every session and fails silently): pipe a fake event in, once without and once with `agent_id`, and expect notes both times:
  `echo '{"session_id":"t","hook_event_name":"PreToolUse","tool_name":"Read","tool_input":{"file_path":"Assets/StarForge/Scripts/Game/GameBootstrap.cs"}}' | CLAUDE_PROJECT_DIR=$PWD python3 .claude/hooks/area_notes.py`, then the same with `"agent_id":"x"`; then `rm Temp/sf_area_notes_t*`.

## Known limits
- Bash deny/ask rules match the command as written: `git -C <dir> reset --hard` or a script slips past them. They catch the common form, and `CLAUDE.md` states the rule.
- `checkout`/`restore` of a single file prompts too (the Blender notes restore FBXs that way): approve it when it is the session's own file.
- Unverified: if a subagent's own compaction fires `PostCompact` without `agent_id`, the hook resets the parent's state instead. The cost is notes sent twice, never lost. To check, log the stdin keys on `PostCompact` while a long subagent compacts.
- A `SessionStart` (startup, clear, compact, and apparently a working-directory change in the desktop app) resets the injected-notes state, so the next touch re-injects an area's notes (up to ~15 KB for World).
