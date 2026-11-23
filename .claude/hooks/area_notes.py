#!/usr/bin/env python3
"""Inject StarForge area notes (folder CLAUDE.md files) when a tool touches that area.

Claude Code's own on-demand loading of nested CLAUDE.md files did not reach the desktop
session, so this makes it deterministic: on Read/Edit/Write/Grep calls on a file in an
area, Bash commands that read, run or edit something there, and Unity MCP calls, the
area's notes are added to the context, once per session and once per subagent (again
after a compaction or /clear; not after a resume, whose transcript still holds them).
Each additionalContext is capped at 10,000 chars, so long files go out in chunks, one
chunk per hook call (Pre and Post both run, so two per tool call). Never blocks: any
error = no output.

What does not trigger (each cost ~50k tokens of notes a review needed none of,
2026-11-23): a path only listed, counted, moved or committed (ls, wc, stat, find, git
status/add/commit/log, ...), a directory named by such a command, a Grep pattern's
words, and reading AgentInbox.cs/BugTrials.cs (editing them still brings the World and
View notes their trials test). An area counts as read only after a full Read of its
CLAUDE.md, not a mention, a grep or a partial read.
"""
import fcntl, json, os, re, shlex, sys

CAP = 9000
FILE_TOOLS = ("Read", "Edit", "Write", "MultiEdit", "NotebookEdit", "Grep", "Glob")
EDIT_TOOLS = ("Edit", "Write", "MultiEdit", "NotebookEdit")

# extra (regex on repo-relative path) -> area dirs, beyond the folder ancestors;
# the third field limits a rule to editing tools
PATH_EXTRA = [
    (r"Scripts/View/(MechView|MechWreck|FXDirector\.Mech)", ["Assets/StarForge/Scripts/World/Mech"], False),
    (r"Scripts/AI/Mech", ["Assets/StarForge/Scripts/World/Mech"], False),
    (r"Scripts/View/(AdaptiveResolution|QualityController)", ["Assets/StarForge/Scripts/Game"], False),
    (r"Shaders/SF_(Wind|Tree|Grass)", ["Assets/StarForge/Scripts/View", "Assets/StarForge/Scripts/World"], False),
    (r"Editor/(MechTrials|MechKitBuilder)", ["Assets/StarForge/Scripts/World/Mech", "Assets/StarForge/Scripts/AI"], False),
    (r"Editor/(MapChecks|MapBuilder)", ["Assets/StarForge/Scripts/World/MapGen"], False),
    (r"Editor/(AgentInbox|BugTrials)", ["Assets/StarForge/Scripts/World", "Assets/StarForge/Scripts/View"], True),
    (r"Tools/make_audio", ["Assets/StarForge/Scripts/View"], False),
]
# words in a command that runs something (editor.sh, the player, a Tools script) or a Unity MCP
# call -> area dirs; a grep or a heredoc that only mentions them is no trial run
KEYWORDS = [
    (r"AgentPlay", ["Assets/StarForge/Scripts/World", "Assets/StarForge/Scripts/View"]),
    (r"MechTrials|MechDuel|BayEdgeCases|MechShowcase|MechCombatLook|MechTrackRide|MechDesigns|MechSpeech",
     ["Assets/StarForge/Scripts/World/Mech", "Assets/StarForge/Scripts/AI"]),
    (r"MapChecks|GroveBatch|GroveGrid|MapGenerator", ["Assets/StarForge/Scripts/World/MapGen", "Assets/StarForge/Scripts/World"]),
    (r"AIEval|Evaluate AI|sfbench|sfplay|sfgallery|sfburst", ["Assets/StarForge/Scripts/Game"]),
    (r"make_audio|fetch_assets|check_licences|measure_music", ["Tools"]),
]
PATH_IN_TEXT = re.compile(r"(?:Assets/StarForge|Tools)/[\w./\-]+")
RUNS = re.compile(r"editor\.sh|StarForge\.app|Tools/[\w/]+\.py")
# Commands that only list, count or record files: the paths they name were not read or written.
# (cp, mv, rm and a redirect write into an area, so they still bring its notes: copying a staged
# file into Assets/ is how a change lands.)
META = {"ls", "wc", "stat", "du", "find", "mkdir", "touch", "shasum", "md5", "file", "test", "[",
        "basename", "dirname", "realpath", "readlink", "which", "cd", "pushd", "popd", "true", "xattr"}
GIT_META = {"status", "log", "add", "commit", "check-ignore", "mv", "rm", "ls-files", "rev-parse", "branch",
            "fetch", "push", "pull", "lfs", "update-index", "write-tree", "read-tree", "commit-tree", "update-ref",
            "hash-object", "cat-file", "check-attr"}


def bash_text(cmd):
    """The parts of a Bash command that read, run or edit something (meta-only segments dropped)."""
    keep = []
    for seg in re.split(r"&&|\|\||[;|\n]", cmd):
        try:
            words = shlex.split(seg, comments=True)
        except ValueError:
            words = seg.split()
        while words and re.match(r"^\w+=", words[0]):     # VAR=value prefixes
            words = words[1:]
        if words and words[0] in ("env", "time", "nice", "command"):
            words = words[1:]
        if not words:
            continue
        prog = os.path.basename(words[0])
        if prog in META and not (prog == "find" and any(w in ("-exec", "-execdir", "-delete") for w in words)) \
                and not re.search(r"[^<>]>|>>", seg):
            continue
        if prog == "git":
            sub = next((w for w in words[1:] if not w.startswith("-")), "")
            if sub in GIT_META or (sub == "diff" and any(w in ("--stat", "--name-only", "--name-status", "--shortstat")
                                                         for w in words)):
                continue
        keep.append(seg)
    return "\n".join(keep)


def full_read(ti, path):
    """A Read that took the whole file (no window, or a window covering it)."""
    off, lim = ti.get("offset"), ti.get("limit")
    if not off and not lim:
        return True
    try:
        n = sum(1 for _ in open(path, encoding="utf-8", errors="replace"))
    except OSError:
        return False
    return (off or 1) <= 1 and (lim is None or lim >= n)


def main():
    d = json.load(sys.stdin)
    root = os.environ.get("CLAUDE_PROJECT_DIR") or d.get("cwd") or os.getcwd()
    sid = re.sub(r"\W", "", str(d.get("session_id", "x")))
    # a subagent's calls carry the parent's session_id plus its own agent_id, and it starts
    # with a fresh context: track it apart, or it never gets notes the parent already had
    aid = re.sub(r"\W", "", str(d.get("agent_id") or ""))
    state_path = os.path.join(root, "Temp", f"sf_area_notes_{sid}{'_' + aid if aid else ''}.json")
    ev = d.get("hook_event_name", "")
    if ev in ("SessionStart", "PostCompact"):
        # A resumed session's transcript still holds the notes it was given; only a new
        # context (startup, /clear, compaction) needs them again.
        if ev == "SessionStart" and d.get("source") == "resume":
            return
        try:
            os.remove(state_path)
        except OSError:
            pass
        return
    tool = d.get("tool_name", "")
    ti = d.get("tool_input") or {}
    rels, texts, keyword_text = [], [], ""
    editing = tool in EDIT_TOOLS
    if tool in FILE_TOOLS:
        for k in ("file_path", "notebook_path", "path"):
            if isinstance(ti.get(k), str):
                rels.append(ti[k])
    elif tool == "Bash":
        cmd = bash_text(str(ti.get("command", "")))
        texts.append(cmd)
        if RUNS.search(cmd):
            keyword_text = cmd
    elif tool.startswith("mcp__unity"):
        texts.append(json.dumps(ti))
        keyword_text = texts[-1]
    else:
        return
    for t in texts:
        rels += [m for m in PATH_IN_TEXT.findall(t)
                 if not m.endswith(("editor.sh", "analyse_burst.py", "CLAUDE.md"))]

    areas = []

    def add(a):
        if a not in areas and os.path.isfile(os.path.join(root, a, "CLAUDE.md")):
            areas.append(a)

    for p in rels:
        p = os.path.normpath(os.path.join(root, p) if not os.path.isabs(p) else p)
        rel = os.path.relpath(p, root)
        if rel.startswith(".."):
            continue
        cur = rel if os.path.isdir(p) else os.path.dirname(rel)
        while cur and cur != ".":
            add(cur)
            cur = os.path.dirname(cur)
        for rx, extra, edit_only in PATH_EXTRA:
            if re.search(rx, rel) and (editing or not edit_only):
                for a in extra:
                    add(a)
    for rx, extra in KEYWORDS:
        if keyword_text and re.search(rx, keyword_text):
            for a in extra:
                add(a)
    # reading an area file in full counts as having it
    read_area = None
    if tool == "Read" and rels and os.path.basename(rels[0]) == "CLAUDE.md":
        p = rels[0] if os.path.isabs(rels[0]) else os.path.join(root, rels[0])
        if full_read(ti, p):
            read_area = os.path.relpath(os.path.dirname(os.path.abspath(p)), root)
    if not areas and not read_area:
        return

    os.makedirs(os.path.dirname(state_path), exist_ok=True)
    # Pre and Post hooks and parallel tool calls run at once: hold a lock over the
    # read-modify-write, or two calls hand out the same chunk.
    with open(state_path + ".lock", "w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            state = json.load(open(state_path))
        except Exception:
            state = {}

        def save():  # write-then-rename: a reader never sees a half-written file
            try:
                tmp = f"{state_path}.{os.getpid()}"
                with open(tmp, "w") as f:
                    json.dump(state, f)
                os.replace(tmp, state_path)
            except OSError:
                pass

        marked = False
        if read_area is not None:
            marked = state.get(read_area) != 10**6
            state[read_area] = 10**6

        for a in areas:
            text = open(os.path.join(root, a, "CLAUDE.md"), encoding="utf-8").read().strip()
            parts, cur = [], ""
            for sec in re.split(r"\n(?=## )", text):
                while len(sec) > CAP:  # a single huge section: split on blank lines
                    cut = sec.rfind("\n\n", 0, CAP)
                    cut = cut if cut > 0 else CAP
                    parts.append(sec[:cut]); sec = sec[cut:].lstrip("\n")
                if len(cur) + len(sec) + 2 > CAP and cur:
                    parts.append(cur); cur = sec
                else:
                    cur = (cur + "\n\n" + sec) if cur else sec
            if cur:
                parts.append(cur)
            i = state.get(a, 0)
            if i >= len(parts):
                continue
            state[a] = i + 1
            head = f"[StarForge area notes: {a}/CLAUDE.md, part {i + 1}/{len(parts)}; injected by .claude/hooks/area_notes.py because this call touches that area. Follow them.]\n"
            save()
            print(json.dumps({"hookSpecificOutput": {"hookEventName": ev, "additionalContext": head + parts[i]}}))
            return
        if marked:
            save()


try:
    main()
except Exception:
    pass
sys.exit(0)
