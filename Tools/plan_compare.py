#!/usr/bin/env python3
"""The retune's gate: the baseline Full (old doctrine) against the retune's Full, from the per-game
[AIEval] lines and the plan traces. Run from the repo root:

  /usr/bin/python3 Tools/plan_compare.py BASE_LINES BASE_TRACE NEW_LINES NEW_TRACE
"""
import re, sys, os, glob, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from plan_replay import load, kind_of, NAMES, S

KINDS = ["Rusher", "Macro", "Turtle", "Harasser"]


def games(path):
    out = []
    for l in open(path, errors="replace"):
        if not l.startswith("[AIEval] vs "):
            continue
        g = lambda p, d="?": (re.search(p, l).group(1) if re.search(p, l) else d)
        out.append(dict(
            kind=g(r"vs (\w+) game"), res=g(r": (AI won|AI lost|draw|time cap)"), t=int(g(r"at (\d+)s", "0")),
            waves=int(g(r"(\d+) attack waves", "0")), beaten=int(g(r"beaten (\d+) \(", "0")),
            off=int(g(r"waves called off (\d+)", "0")), massed=int(g(r"massed waves (\d+)", "0")),
            massing="never massing" not in l, away=int(g(r"theirs away (\d+)", "0")), hurt=int(g(r"theirs hurt (\d+)", "0")),
            mechkills=g(r"opponent bay [^,]*, mech [^;]*?(\d+) kills", ""), mined=int(g(r"AI \d+ structures, \d+ Diggers, (\d+) ore mined", "0")),
            dlost=int(g(r"AI Diggers lost (\d+)", "0")), exc=("Exception" in l)))
    return out


def trace_stats(trace):
    gs = load(trace)
    n = len(gs)
    share = collections.defaultdict(lambda: [0.0] * S)
    late = collections.defaultdict(lambda: [0.0] * S)
    diggers = collections.defaultdict(lambda: collections.defaultdict(list))
    for gi, (name, meta, rows) in enumerate(gs):
        k = kind_of(gi, n)
        marks = {180: None, 240: None, 300: None}
        for r in rows:
            t = float(r["t"]); dt = float(r["dt"]); c = int(r["cur"])
            share[k][c] += dt
            if t > 480: late[k][c] += dt
            for m in marks:
                if marks[m] is None and t >= m: marks[m] = int(r["workers"])
        for m, v in marks.items():
            if v is not None: diggers[k][m].append(v)
    return share, late, diggers


def pct(v):
    tot = sum(v) or 1
    return [100 * x / tot for x in v]


def main():
    bl, bt, nl, nt = sys.argv[1:5]
    B, N = games(bl), games(nl)
    for label, G in (("baseline (old doctrine)", B), ("retune (varied doctrine)", N)):
        won = sum(g["res"] == "AI won" for g in G); lost = sum(g["res"] == "AI lost" for g in G); cap = sum(g["res"] == "time cap" for g in G)
        print(f"== {label}: {won}/{len(G)} won, {lost} lost, {cap} caps")
        for k in KINDS:
            gk = [g for g in G if g["kind"] == k]
            if not gk: continue
            print(f"   {k:8} won {sum(g['res']=='AI won' for g in gk)}/{len(gk)}, caps {sum(g['res']=='time cap' for g in gk)}, "
                  f"lengths {[g['t'] for g in gk]}, waves {sum(g['waves'] for g in gk)}, beaten {sum(g['beaten'] for g in gk)}, "
                  f"called off {sum(g['off'] for g in gk)}, massing in {sum(g['massing'] for g in gk)}, massed waves {sum(g['massed'] for g in gk)}, "
                  f"Mech-away waves {sum(g['away'] for g in gk)}, hurt-Mech waves {sum(g['hurt'] for g in gk)}, "
                  f"ore mined {sum(g['mined'] for g in gk)//max(1,len(gk))}/game, Diggers lost {sum(g['dlost'] for g in gk)}")
    for label, tr in (("baseline", bt), ("retune", nt)):
        share, late, dig = trace_stats(tr)
        allv = [0.0] * S
        print(f"== plan time, {label} (from the trace)")
        for k in KINDS:
            for i in range(S): allv[i] += share[k][i]
            p = pct(share[k]); pl = pct(late[k])
            print(f"   {k:8} " + ", ".join(f"{NAMES[i]} {p[i]:.0f}%" for i in range(S) if p[i] >= 0.5) +
                  f" | after 480 s: Feint {pl[7]:.0f}%, Timing Push {pl[3]:.0f}% | Diggers at 180/240/300 s: " +
                  " / ".join(f"{sum(dig[k][m])/max(1,len(dig[k][m])):.1f}" for m in (180, 240, 300)))
        p = pct(allv)
        print("   all      " + ", ".join(f"{NAMES[i]} {p[i]:.1f}%" for i in range(S)) + f"; plans at 0%: {sum(1 for x in p if x < 0.05)}")


if __name__ == "__main__":
    main()
