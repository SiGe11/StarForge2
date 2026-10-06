---
name: checkpoint
description: How to commit in the shared StarForge working tree - this session's files only, explicit paths with their .meta and LFS files, peers' work untouched, after verification and review. Use whenever the user asks to commit, checkpoint or push; never commit unasked.
argument-hint: "[commit subject]"
allowed-tools: Bash(git status *) Bash(git diff *) Bash(git add -- *) Bash(git commit -m *) Bash(git show *) Bash(git log *) Bash(git fetch origin) Bash(git rev-list *) Bash(git lfs status *)
---

# Checkpoint this session's work

The working tree is shared by several sessions, each with uncommitted work. A commit must hold this session's files and nothing else (one session's work once landed inside another's "Mech part 1" commit).

## 1. Scope
- **Mine:** every file this session wrote or edited (your Edit/Write calls), the outputs of generators this session ran, and the `.meta` of each new or deleted file under `Assets/`.
- `git status --short`. Every changed path not in "mine" belongs to a peer: leave it out and list it in the report.
- **Shared files** (touched by this session and a peer, e.g. `README.md`, `CLAUDE.md`, `GameWorld.cs`, a regenerated scene): `git diff -- <file>`. If any hunk is not yours, stop and ask the user (commit it together with the peer's agreement, or leave it out); there is no interactive staging here.
- Never: `git add -A`/`.`/`-u`, `git commit -a`, `--amend`, `stash`, `reset`, `checkout`/`restore` of files you did not change, `clean`, branch switches (they move every session's files and make the open Editor reimport). The project settings ask the user before these.

## 2. Gate
- `git fetch origin` and `git rev-list --count HEAD..origin/main`: if the remote is ahead, stop and ask the user (no merge or rebase in a shared tree).
- The change met its level in the `verify-change` skill; at level 3+ the `reviewer` verdict is not CHANGES REQUIRED, or the user accepted the open findings.
- Anything under `Assets/`, `Art/` or `Tools/` that came from outside: `python3 Tools/check_licences.py` is clean, and the asset is in `fetch_assets.py`, `THIRD-PARTY.md` and the README in this same commit.
- `git diff -- <paths>`: read it once more for debug leftovers, stray `Debug.Log`, a static left on, edits to generated output instead of its generator.

## 3. Commit
```bash
git add -- <new files and their .meta>
git commit -m "<subject>" -m "<body>" -- <every path in scope>
git show --stat HEAD
```
`git commit -- <paths>` commits exactly those paths even if a peer staged something else. Subject: `$ARGUMENTS` if given, else a short line naming the feature or fix. Body: what changed, and the verification run with its numbers (trial, evaluation line, bench). End with the co-author trailer from the session's attribution instructions.
Check `git show --stat HEAD` lists only the scoped paths. Binaries matching `.gitattributes` (png, jpg, fbx, wav, unity, ...) go through Git LFS: `git lfs status` before committing should list new ones as LFS objects, not as plain blobs.
Push only when the user asks: the repository is public, a push uploads the LFS objects too, and CI (`.github/workflows/licences.yml`) runs the offline licence check on what was pushed. After a push, `gh run list -R SiGe11/StarForge2 --limit 1` shows the check's result.

## 4. Report and tidy
Report the commit hash, the files in it, and the peers' files left out. Update auto-memory notes that call this work "uncommitted".
