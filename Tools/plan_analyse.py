#!/usr/bin/env python3
"""Why a plan is or is not chosen, from a plan trace (AuditTrials.EvalTracedFull).

Per opponent and plan: the share of thinks it was legal, chosen, and its mean gap to the best plan
while legal (recorded scores, noise included). For the plan in force, which features carry its lead
over the runner-up (doctrine weights x features; the bandit's in-match learning is left out).

  /usr/bin/python3 Tools/plan_analyse.py TRACE_DIR [--cs StrategySelector.cs] [--varied | --block Name]  (legacy by default)
"""
import argparse, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from plan_replay import load, kind_of, doctrine_from_cs, doctrine_block, PLANS, NAMES, FEATS, S, NF


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trace")
    ap.add_argument("--cs", default="Assets/StarForge/Scripts/AI/StrategySelector.cs")
    ap.add_argument("--block", help="a named doctrine method (DoctrineLegacy, DoctrineVaried)")
    ap.add_argument("--varied", action="store_true", help="the varied doctrine (default: the legacy one)")
    a = ap.parse_args()
    if not a.block and "void DoctrineLegacy" in open(a.cs).read():
        a.block = "DoctrineVaried" if a.varied else "DoctrineLegacy"   # the file holds both doctrines
    w = doctrine_block(a.cs, a.block) if a.block else doctrine_from_cs(a.cs)
    games = load(a.trace)
    n = len(games)
    stats = {}
    lead = {}
    for gi, (name, meta, rows) in enumerate(games):
        k = kind_of(gi, n)
        st = stats.setdefault(k, {"n": 0, "legal": [0] * S, "chosen": [0] * S, "gap": [0.0] * S, "gapn": [0] * S, "early": [0] * S, "earlyn": 0})
        for r in rows:
            st["n"] += 1
            sc = [float(r["s%d" % i]) for i in range(S)]
            legal = [r["l%d" % i] == "1" for i in range(S)]
            best = max(sc[i] for i in range(S) if legal[i])
            cur = int(r["cur"])
            st["chosen"][cur] += 1
            t = float(r["t"])
            for i in range(S):
                if legal[i]:
                    st["legal"][i] += 1
                    st["gap"][i] += best - sc[i]
                    st["gapn"][i] += 1
            # who leads, and on what
            order = sorted([i for i in range(S) if legal[i]], key=lambda i: -sc[i])
            if len(order) >= 2:
                top, nxt = order[0], order[1]
                f = [float(r["f%d" % j]) for j in range(NF)]
                key = (k, PLANS[top], PLANS[nxt])
                d = lead.setdefault(key, [0, [0.0] * NF])
                d[0] += 1
                for j in range(NF):
                    d[1][j] += w[top][j] * f[j] - w[nxt][j] * f[j]
    for k, st in stats.items():
        print(f"== {k}: {st['n']} thinks")
        for i in range(S):
            lg = st["legal"][i] / st["n"]
            ch = st["chosen"][i] / st["n"]
            gp = st["gap"][i] / st["gapn"][i] if st["gapn"][i] else float("nan")
            print(f"   {NAMES[i]:15} legal {100*lg:5.1f}%  chosen {100*ch:5.1f}%  mean gap to the best while legal {gp:6.2f}")
    print("== the most common leader / runner-up pairs, and the features that carry the lead (mean, doctrine weights)")
    for key, (cnt, acc) in sorted(lead.items(), key=lambda kv: -kv[1][0])[:14]:
        parts = sorted(range(NF), key=lambda j: -abs(acc[j]))[:6]
        print(f"   {key[0]:8} {key[1]:13} over {key[2]:13} {cnt:6} thinks: " +
              ", ".join(f"{FEATS[j]} {acc[j]/cnt:+.2f}" for j in parts))


if __name__ == "__main__":
    main()
