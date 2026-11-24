#!/usr/bin/env python3
"""Replay the AI's plan selector (StrategySelector.Update) offline over a plan trace.

A trace (AuditTrials.EvalTracedFull) holds, for every think of every evaluation game, the selector's
inputs: the 20 features, the legality of each plan and its inputs, the match's per-plan bias, and the
advantage inputs the bandit learns from. This script steps the selector over those rows with any
doctrine and reports the plan shares it would have chosen -- first order only: a different plan would
have played a different match, so the features themselves would differ. Validate first: with the
doctrine the trace was recorded under it should agree with the recorded plan most of the time, and
after 420 s (no exploration noise) its scores should match the recorded ones.

  /usr/bin/python3 Tools/plan_replay.py TRACE_DIR [--varied | --block Name] [--set Plan.Feature=v ...]
                     [--setfile FILE] [--truth] [--draws 5] [--legal-rule --rule k=v] [--feint-cap RUN,REST]
                     [--clamp D] [--baseline K] [--fade T,S] [--score-cap D] [--lr k] [--quiet]
--feint-cap and --clamp are StrategySelector's FeintRun/FeintRest and LearnReach (read from a trace header's feintcap and
learnreach stamps when replaying the varied doctrine); --baseline, --fade, --score-cap and --lr are replay-only
experiments (2026-11-24: none of them is in the game).
Traces: Art/Source/staged/aiaudit/plans/traces/ (git-ignored). Run from the repo root.
"""
import argparse, csv, glob, math, os, random, re, sys

PLANS = ["Eco", "TrooperRush", "Harass", "TimingPush", "TurtleTech", "Expand", "CounterAttack", "Feint"]
NAMES = ["Economy", "Trooper Rush", "Harass", "Timing Push", "Turtle/Tech", "Expand", "Counter-Attack", "Feint"]
FEATS = ["Time", "ArmyRatio", "SupplyFill", "Ore", "Workers", "Army", "EArmy", "PRush", "PMacro", "PTurtle",
         "PHarass", "PExpand", "PTech", "Aggression", "Expansion", "Defensive", "ScoutConf", "Entropy",
         "ArmyAway", "Bias"]
S, NF = len(PLANS), len(FEATS)
KINDS = ["Rusher", "Macro", "Turtle", "Harasser"]
TRUTH = {"Rusher": 7, "Macro": 8, "Turtle": 9, "Harasser": 10}   # feature index of the true style's belief


def doctrine_from_cs(path):
    """Every W(Strategy.X, F.Y, v) call in the file, the last one of each pair winning: right only for a file
    with one doctrine (before 2026-11-23). A file with `DoctrineLegacy`/`DoctrineVaried` is read by block
    (main picks the legacy one unless --varied)."""
    src = open(path).read()
    w = [[0.0] * NF for _ in range(S)]
    for m in re.finditer(r"W\(Strategy\.(\w+),\s*F\.(\w+),\s*(-?[\d.]+)f\)", src):
        w[PLANS.index(m.group(1))][FEATS.index(m.group(2))] = float(m.group(3))
    return w


def doctrine_block(path, name):
    src = open(path).read()
    m = re.search(r"void " + name + r"\b.*?\n        \}\n", src, re.S)
    if not m:
        sys.exit(f"no doctrine block {name} in {path}")
    w = [[0.0] * NF for _ in range(S)]
    for mm in re.finditer(r"W\(Strategy\.(\w+),\s*F\.(\w+),\s*(-?[\d.]+)f\)", m.group(0)):
        w[PLANS.index(mm.group(1))][FEATS.index(mm.group(2))] = float(mm.group(3))
    return w


def load(trace_dir):
    games = []
    for path in sorted(glob.glob(os.path.join(trace_dir, "game_*.csv"))):
        rows, header, meta = [], None, ""
        for line in open(path):
            if line.startswith("#"):
                meta = line.strip()
                continue
            if header is None:
                header = line.strip().split(",")
                continue
            v = line.strip().split(",")
            if len(v) != len(header):
                continue
            rows.append(dict(zip(header, v)))
        games.append((os.path.basename(path), meta, rows))
    return games


def kind_of(i, n):
    if n > 0 and n % 4 == 0:   # the runner plays each opponent's games together: 5 a Full, 3 the other openings, 1 a Quick
        return KINDS[i // (n // 4)]
    return "?"


def adv(r, legacy=False):
    ore = float(r["ore"])
    return 0.0020 * (float(r["army"]) - float(r["earmy"])) + 0.030 * float(r["workers"]) + 0.0008 * (ore if legacy else min(ore, 400.0))


def legal_rule(p, r, rules):
    """The C# Legal() from the trace's inputs, with thresholds from `rules`."""
    army, ore, workers = float(r["army"]), float(r["ore"]), float(r["workers"])
    if p == 1: return float(r["garrisons"]) >= 1 and float(r["troopers"]) >= rules["rush_troopers"]
    if p == 2: return float(r["troopers"]) + float(r["skimmers"]) >= rules["harass_units"]
    if p == 3: return army >= rules["push_army"]
    if p == 6: return army >= rules["counter_army"] and float(r["eaway"]) > rules["counter_away"]
    if p == 7: return army >= rules["feint_army"] and r["ebaseknown"] == "1"
    if p == 5: return ore >= rules["expand_ore"] and workers >= rules["expand_workers"]
    return True


RULES = dict(rush_troopers=4, harass_units=3, push_army=300, counter_army=200, counter_away=60, feint_army=400,
             expand_ore=350, expand_workers=10)


def replay(rows, w0, rng, truth_idx=None, legal_mode="recorded", rules=RULES, extra=None, feint_cap=None, baseline=0.0,
           clamp=None, fade=None, score_cap=None, lr_scale=1.0):
    """Step StrategySelector.Update (the audit's code, LegacyLearning off) over a game's rows. feint_cap (run, rest):
    a Feint run ends after `run` s and Feint is barred for `rest` s (StrategySelector's FeintRun/FeintRest)."""
    w = [row[:] for row in w0]
    cur = pend = 0
    commitT = evalT = lastAdv = 0.0
    haveAdv = False
    runTime = [0.0] * S
    runPay = [0.0] * S
    fatigue = [0.0] * S
    timeIn = [0.0] * S
    lateIn = [0.0] * S
    feintRestUntil = -1.0
    lastFeat = [0.0] * NF
    agree = 0
    resid = []
    picks = []
    base = [0.0]   # --baseline, a replay-only experiment: the trend of the reward, per second
    tnow = [0.0]
    def reward(r, c, window=18.0):
        if math.isnan(r) or math.isinf(r): return
        if baseline > 0:
            window = max(1.0, window)
            rr = r - base[0] * window
            base[0] += baseline * (window / 18.0) * (max(-1.0, min(1.0, r)) / window - base[0])
            r = rr
        g = max(-1.0, min(1.0, r))
        lr = 0.05 * lr_scale   # StrategySelector's learning rate (0.05), scaled by --lr
        if fade is not None:   # --fade, a replay-only experiment: full rate to fade[0] s, none by fade[0] + fade[1] s
            lr *= max(0.0, 1.0 - max(0.0, tnow[0] - fade[0]) / fade[1])
        for i in range(NF):
            v = max(-4.0, min(4.0, w[c][i] + lr * g * lastFeat[i]))
            if clamp is not None:   # StrategySelector.LearnReach: a weight moves at most clamp from where the match began
                v = max(w0[c][i] - clamp, min(w0[c][i] + clamp, v))
            w[c][i] = v
        runPay[c] += g
    for r in rows:
        t = float(r["t"]); dt = float(r["dt"]); tnow[0] = t
        feat = [float(r["f%d" % i]) for i in range(NF)]
        if truth_idx is not None:
            for i in range(7, 13):
                feat[i] = 0.02
            feat[truth_idx] = 0.90
        timeIn[cur] += dt; runTime[cur] += dt
        if t > 480: lateIn[cur] += dt
        if feint_cap and cur == 7 and runTime[7] > feint_cap[0]: feintRestUntil = t + feint_cap[1]
        for i in range(S):
            if i != cur: fatigue[i] = max(0.0, fatigue[i] - dt * 0.002)
        evalT += dt
        if evalT >= 18.0:
            a = adv(r)
            if haveAdv: reward(a - lastAdv, cur, evalT)
            lastAdv = a; haveAdv = True; evalT = 0.0
        lastFeat = feat[:]
        best, second, bestS = -1e30, -1e30, 0
        sc = [None] * S
        for si in range(S):
            legal = (r["l%d" % si] == "1") if legal_mode == "recorded" else legal_rule(si, r, rules)
            if not legal or (si == 7 and t < feintRestUntil):
                continue
            acc = sum(w[si][i] * feat[i] for i in range(NF))
            if score_cap is not None:   # --score-cap, a replay-only experiment: learning moves a plan's score at most this
                d0 = sum(w0[si][i] * feat[i] for i in range(NF))
                acc = d0 + max(-score_cap, min(score_cap, acc - d0))
            acc += rng.uniform(-1, 1) * 0.10 * (1 - min(1.0, t / 420.0))
            b = float(r["b%d" % si])
            if extra is not None:
                b += extra[si]
            acc += b * (1.0 + (0.5 - 1.0) * min(1.0, t / 480.0))
            if si == cur and runTime[si] > 120 and runPay[si] <= 0.05:
                acc -= min(0.35, (runTime[si] - 120) * 0.004)
            acc -= fatigue[si]
            sc[si] = acc
            if acc > best: second, best, bestS = best, acc, si
            elif acc > second: second = acc
        if t > 425 and truth_idx is None and extra is None and legal_mode == "recorded":
            for si in range(S):
                if sc[si] is not None:
                    resid.append(abs(sc[si] - float(r["s%d" % si])))
        commitT -= dt
        if bestS != cur:
            if bestS == pend:
                if commitT <= 0:
                    if haveAdv: reward(adv(r) - lastAdv, cur, evalT)
                    was = cur
                    if runTime[was] > 120 and runPay[was] <= 0.05: fatigue[was] = min(0.25, fatigue[was] + 0.12)
                    runTime[was] = 0; runPay[was] = 0
                    cur = bestS; commitT = 14.0
                    evalT = 0.0; lastAdv = adv(r); haveAdv = True
            else:
                pend = bestS
                if commitT <= 0: commitT = 2.5
        else:
            pend = bestS
        if cur == int(r["cur"]): agree += 1
        picks.append(cur)
    tot = sum(timeIn) or 1.0
    late = sum(lateIn)
    return [x / tot for x in timeIn], agree / max(1, len(rows)), resid, ([x / late for x in lateIn] if late > 0 else None)


def entropy(sh):
    return -sum(p * math.log2(p) for p in sh if p > 1e-9)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trace")
    ap.add_argument("--cs", default="Assets/StarForge/Scripts/AI/StrategySelector.cs")
    ap.add_argument("--block", help="a named doctrine method in --cs (DoctrineLegacy, DoctrineVaried)")
    ap.add_argument("--varied", action="store_true", help="the varied doctrine (default: the legacy one)")
    ap.add_argument("--set", action="append", default=[], help="Plan.Feature=v (on top of the doctrine)")
    ap.add_argument("--setfile", help="a file of Plan.Feature=v lines (# comments)")
    ap.add_argument("--truth", action="store_true", help="beliefs replaced by the opponent's true style")
    ap.add_argument("--draws", type=int, default=5)
    ap.add_argument("--legal-rule", action="store_true", help="legality from the trace's inputs and RULES")
    ap.add_argument("--rule", action="append", default=[], help="name=v for RULES")
    ap.add_argument("--feint-cap", help="RUN,REST: a Feint run ends after RUN s, then Feint is barred REST s (default: the "
                    "trace header's feintcap stamp; 1e9,0 turns it off). The 2026-11-23 22:55 trace has no stamp and wrote "
                    "the bar into its legality: replay it with --legal-rule")
    ap.add_argument("--baseline", type=float, default=0.0, help="K: pay a plan its reward less the trend (an average of "
                    "recent rewards per second, moved K of the way each 18 s window); replay-only experiment")
    ap.add_argument("--clamp", type=float, help="D: in-match learning moves a weight at most D from its start (LearnReach; "
                    "default: the trace header's learnreach stamp when replaying the varied doctrine; 99 turns it off)")
    ap.add_argument("--fade", help="T,S: the learning rate fades from T s to nothing at T+S s; replay-only experiment")
    ap.add_argument("--score-cap", type=float, help="D: what in-match learning adds to a plan's score is held to +-D; replay-only experiment")
    ap.add_argument("--lr", type=float, default=1.0, help="k: the learning rate x k (0.05 x k); replay-only experiment")
    ap.add_argument("--quiet", action="store_true")
    a = ap.parse_args()
    if not a.block and "void DoctrineLegacy" in open(a.cs).read():
        a.block = "DoctrineVaried" if a.varied else "DoctrineLegacy"   # the file holds both doctrines
    w = doctrine_block(a.cs, a.block) if a.block else doctrine_from_cs(a.cs)
    if a.setfile:
        a.set = [l.split("#")[0].strip() for l in open(a.setfile) if l.split("#")[0].strip()] + a.set
    for s in a.set:
        k, v = s.split("=")
        p, f = k.split(".")
        w[PLANS.index(p)][FEATS.index(f)] = float(v)
    rules = dict(RULES)
    for s in a.rule:
        k, v = s.split("=")
        rules[k] = float(v)
    games = load(a.trace)
    n = len(games)
    meta = " ".join(m for _, m, _ in games[:1])
    varied = a.block == "DoctrineVaried"   # the game applies the cap and the reach with the varied doctrine only
    stamp = re.search(r" feintcap (\d+)/(\d+)", meta) if varied else None
    cap = tuple(float(x) for x in a.feint_cap.split(",")) if a.feint_cap else \
          ((float(stamp.group(1)), float(stamp.group(2))) if stamp else None)
    if stamp and not a.feint_cap:
        print(f"Feint cap from the trace header: {stamp.group(1)}/{stamp.group(2)} (--feint-cap 1e9,0 turns it off)")
    reach = re.search(r" learnreach ([\d.]+)", meta) if varied else None
    if reach and a.clamp is None:
        a.clamp = float(reach.group(1))
        print(f"learning reach from the trace header: {a.clamp} (--clamp 99 turns it off)")
    if " doctrine " in meta and (("doctrine varied" in meta) != (a.block == "DoctrineVaried")):
        print(f"WARNING: the trace was recorded under another doctrine ({meta.split(' doctrine ')[1].split()[0]}) than replayed ({a.block})")
    print(f"{n} games from {a.trace}; doctrine {a.block or os.path.basename(a.cs)}{'; ' + ', '.join(a.set) if a.set else ''}"
          f"{'; TRUE READ' if a.truth else ''}{'; legality by rule ' + str(rules) if a.legal_rule else ''}"
          f"{f'; Feint capped {cap[0]:.0f} s, rest {cap[1]:.0f} s' if cap else ''}{f'; credit baseline {a.baseline}' if a.baseline else ''}{f'; learning clamp {a.clamp}' if a.clamp is not None else ''}{f'; score cap {a.score_cap}' if a.score_cap is not None else ''}{f'; learning rate x{a.lr}' if a.lr != 1.0 else ''}{f'; learning fade {a.fade}' if a.fade else ''}")
    allsh = [0.0] * S
    bykind = {}
    latek = {}
    agrees, resids = [], []
    for gi, (name, meta, rows) in enumerate(games):
        kind = kind_of(gi, n)
        sh = [0.0] * S; ag = 0.0
        for d in range(a.draws):
            s_, g_, res, lt = replay(rows, w, random.Random(1000 + 7 * gi + d), TRUTH.get(kind) if a.truth else None,
                                     "rule" if a.legal_rule else "recorded", rules, feint_cap=cap, baseline=a.baseline,
                                     clamp=a.clamp, score_cap=a.score_cap, lr_scale=a.lr, fade=tuple(float(x) for x in a.fade.split(",")) if a.fade else None)
            sh = [x + y / a.draws for x, y in zip(sh, s_)]
            if lt is not None:
                lk = latek.setdefault(kind, [0.0] * (S + 1))
                for i in range(S): lk[i] += lt[i]
                lk[S] += 1
            ag += g_ / a.draws
            resids += res
        # the recorded shares
        rec = [0.0] * S
        for r in rows:
            rec[int(r["cur"])] += float(r["dt"])
        tot = sum(rec) or 1
        rec = [x / tot for x in rec]
        agrees.append(ag)
        for i in range(S): allsh[i] += sh[i] / n
        bk = bykind.setdefault(kind, [0.0] * S)
        for i in range(S): bk[i] += sh[i]
        if not a.quiet:
            fmt = lambda v: " ".join(f"{NAMES[i][:6]:>6}{100*v[i]:4.0f}" for i in range(S))
            print(f"  {kind:8} {name}: replay {fmt(sh)} | agree {100*ag:3.0f}%")
            print(f"  {'':8} {'':11}  record {fmt(rec)}")
    print("mean share over the games: " + ", ".join(f"{NAMES[i]} {100*allsh[i]:.1f}%" for i in range(S)))
    print(f"plan-time entropy of the pooled shares {entropy(allsh):.2f} bits (of {math.log2(S):.2f}); plans at 0%: "
          f"{sum(1 for x in allsh if x < 0.0005)}; Timing Push {100*allsh[3]:.1f}%")
    for k, v in bykind.items():
        cnt = sum(1 for gi in range(n) if kind_of(gi, n) == k)
        print(f"  {k:8}: " + ", ".join(f"{NAMES[i]} {100*v[i]/cnt:.0f}%" for i in range(S) if v[i] / cnt >= 0.005))
        lk = latek.get(k)
        if lk and lk[S]:
            print(f"  {'':8}  after 480 s: " + ", ".join(f"{NAMES[i]} {100*lk[i]/lk[S]:.0f}%" for i in range(S) if lk[i] / lk[S] >= 0.005))
    if not a.set and a.block in (None, "DoctrineLegacy", "DoctrineVaried") and not a.truth and not a.legal_rule:
        print(f"validation: agreement with the recorded plan {100*sum(agrees)/max(1,len(agrees)):.1f}% (mean over games); "
              f"score residual after 425 s: mean {sum(resids)/max(1,len(resids)):.4f}, max {max(resids) if resids else 0:.4f}")


if __name__ == "__main__":
    main()
