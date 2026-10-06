#!/usr/bin/env python3
"""Inject StarForge area notes (folder CLAUDE.md files) when a tool touches that area.

Claude Code's own on-demand loading of nested CLAUDE.md files did not reach the desktop
session, so this makes it deterministic: on Read/Edit/Write/Grep/Bash/Unity MCP calls that
name a file or a known helper in an area, the area's notes are added to the context, once
per session and once per subagent (again after a compaction/clear: SessionStart resets the
state). Each
additionalContext is capped at 10,000 chars, so long files go out in chunks, one chunk per
hook call (Pre and Post both run, so two per tool call). Never blocks: any error = no output.
"""
import json, os, re, sys

CAP = 9000
FILE_TOOLS = ("Read", "Edit", "Write", "MultiEdit", "NotebookEdit", "Grep", "Glob")

# extra (regex on repo-relative path or on text) -> area dirs, beyond the folder ancestors
PATH_EXTRA = [
    (r"Scripts/View/(MechView|MechWreck|FXDirector\.Mech)", ["Assets/StarForge/Scripts/World/Mech"]),
    (r"Scripts/AI/Mech", ["Assets/StarForge/Scripts/World/Mech"]),
    (r"Scripts/View/(AdaptiveResolution|QualityController)", ["Assets/StarForge/Scripts/Game"]),
    (r"Shaders/SF_(Wind|Tree|Grass)", ["Assets/StarForge/Scripts/View", "Assets/StarForge/Scripts/World"]),
    (r"Editor/(MechTrials|MechKitBuilder)", ["Assets/StarForge/Scripts/World/Mech", "Assets/StarForge/Scripts/AI"]),
    (r"Editor/(MapChecks|MapBuilder)", ["Assets/StarForge/Scripts/World/MapGen"]),
    (r"Editor/(AgentInbox|BugTrials)", ["Assets/StarForge/Scripts/World", "Assets/StarForge/Scripts/View"]),
    (r"Tools/make_audio", ["Assets/StarForge/Scripts/View"]),
]
KEYWORDS = [
    (r"AgentPlay", ["Assets/StarForge/Scripts/World", "Assets/StarForge/Scripts/View"]),
    (r"MechTrials|MechDuel|BayEdgeCases|MechShowcase|MechCombatLook|MechTrackRide|MechDesigns|MechSpeech",
     ["Assets/StarForge/Scripts/World/Mech", "Assets/StarForge/Scripts/AI"]),
    (r"MapChecks|GroveBatch|GroveGrid|MapGenerator", ["Assets/StarForge/Scripts/World/MapGen", "Assets/StarForge/Scripts/World"]),
    (r"AIEval|Evaluate AI|sfbench|sfplay|sfgallery|sfburst", ["Assets/StarForge/Scripts/Game"]),
    (r"make_audio|fetch_assets|check_licences|measure_music", ["Tools"]),
]
PATH_IN_TEXT = re.compile(r"(?:Assets/StarForge|Tools)/[\w./\-]+")


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
        try:
            os.remove(state_path)
        except OSError:
            pass
        return
    tool = d.get("tool_name", "")
    ti = d.get("tool_input") or {}
    rels, texts = [], []
    if tool in FILE_TOOLS:
        for k in ("file_path", "notebook_path", "path"):
            if isinstance(ti.get(k), str):
                rels.append(ti[k])
        texts.append(str(ti.get("pattern", "")))
    elif tool == "Bash":
        texts.append(str(ti.get("command", "")))
    elif tool.startswith("mcp__unity"):
        texts.append(json.dumps(ti))
    else:
        return
    for t in texts:
        rels += [m for m in PATH_IN_TEXT.findall(t)
                 if not m.endswith(("editor.sh", "analyse_burst.py"))]

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
        for rx, extra in PATH_EXTRA:
            if re.search(rx, rel):
                for a in extra:
                    add(a)
    blob = " ".join(texts)
    for rx, extra in KEYWORDS:
        if re.search(rx, blob):
            for a in extra:
                add(a)
    if not areas:
        return

    try:
        state = json.load(open(state_path))
    except Exception:
        state = {}
    def save():  # write-then-rename: a parallel hook call never reads a half-written file
        try:
            os.makedirs(os.path.dirname(state_path), exist_ok=True)
            tmp = f"{state_path}.{os.getpid()}"
            with open(tmp, "w") as f:
                json.dump(state, f)
            os.replace(tmp, state_path)
        except OSError:
            pass

    # reading an area file itself counts as having it
    marked = False
    for p in rels:
        if os.path.basename(p) == "CLAUDE.md":
            key = os.path.relpath(os.path.dirname(os.path.abspath(p)), root)
            marked |= state.get(key) != 10**6
            state[key] = 10**6

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
