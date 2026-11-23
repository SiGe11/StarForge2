#!/bin/zsh
# Send commands to the open Unity Editor through Assets/StarForge/Editor/AgentInbox.cs
# and print the answers. Each command blocks until it is done; none needs a sleep loop.
#
#   Tools/editor.sh compile                    refresh, wait out the compile and domain reload,
#                                              print the compiler errors (exit 1) or "compiled";
#                                              returns at once when nothing changed
#   Tools/editor.sh play [here]                open the Battlefield ("here": the open scene),
#                                              enter play, return once the match scene is up
#   Tools/editor.sh call Namespace.Type.Method run a public static no-argument method
#   Tools/editor.sh wait Type.Method [regex] [seconds]
#                                              call a staged trial's report every 2 s until it is
#                                              done: until its result matches regex, or without one,
#                                              until it stops reading staging/running/looking/...
#   Tools/editor.sh eval quick|full            run the AI evaluation and print its report when it
#                                              ends (5-6 / 27-35 min: run it in the background)
#   Tools/editor.sh menu "StarForge/Build All" | refresh | stop | state | ready
#
# Single commands wait up to $SF_EDITOR_TIMEOUT seconds (default 900) for the answer, a command
# queued behind a compile or a long one (Build All) included. Any command fails at once with no
# Editor running, and when an inbox that stamps a heartbeat (AgentInbox since 2026-11-23) has not
# polled for 5 minutes with no command running and no compile in the last 5 (a modal dialog, a
# hang, or AgentInbox not loaded because the Editor scripts do not compile). An older inbox
# stamps no heartbeat: then only the no-Editor check applies.
zmodload zsh/datetime
root="$(cd "$(dirname "$0")/.." && pwd)"
dir="$root/Temp/AgentInbox"
mkdir -p "$dir"

mtime() { stat -f %Fm "$1" 2>/dev/null || echo 0; }
elapsed() { printf '%.1f' $(( EPOCHREALTIME - $1 )); }
# This project's Editor: the Unity process naming the repo that is no -batchMode import worker
# (a worker, or another project's Editor, must not count as this one being up).
editor_pid() {
  local p a
  for p in $(pgrep -f "Unity.app/Contents/MacOS/Unity"); do
    a="$(ps -o args= -p $p 2>/dev/null)"
    [[ $a == *-batchMode* || $a != *" $root"* ]] && continue
    print -r -- $p; return 0
  done
  return 1
}
editor_up() { editor_pid >/dev/null; }
# CPU of the Editor and its import workers together (an import on focus runs in the workers while
# the Editor waits); C locale: under de_DE ps prints "12,3", which zsh's arithmetic misreads.
project_cpu() {
  local p a sum=0 c
  for p in $(pgrep -f "Unity.app/Contents/MacOS/Unity"); do
    a="$(ps -o args= -p $p 2>/dev/null)"
    [[ $a == *" $root"* ]] || continue
    c=$(LC_ALL=C ps -o %cpu= -p $p 2>/dev/null | tr -d ' ')
    (( sum += ${${c%.*}:-0} ))
  done
  print -r -- $sum
}

# Why nobody is answering, or nothing when someone still may. The inbox stamps "alive" every
# 2 s and "busy" while a command runs; a compile and its domain reload stop both for a while.
stalled() {
  editor_up || { echo "no Editor running"; return 0; }
  [[ -f "$dir/alive" && ! -f "$dir/busy" ]] || return 1
  # Minutes, not seconds: a slow but live Editor (an import, a reload, 8x frames under load) must
  # never end a wait or an evaluation.
  (( EPOCHREALTIME - $(mtime "$dir/compile.started") < 300 )) && return 1
  local quiet=$(( EPOCHREALTIME - $(mtime "$dir/alive") ))
  (( quiet > 300 )) || return 1
  # Busy without the inbox's knowing (Unity's own import on focus, a build started by hand or
  # through MCP) is not a stall: a modal dialog leaves the Editor idle, an import or build does not.
  (( $(project_cpu) >= 5 )) && return 1
  printf 'the Editor has not polled its inbox for %.0f s and runs no command: a modal dialog, a hang, or AgentInbox not loaded (Editor scripts not compiling?)\n' $quiet
  return 0
}

# One command to the inbox; prints its answer ("ok" or "error", the result, then the console
# after a "--- console" line).
send() {
  local id="$(date +%s%N)_$$" limit=${SF_EDITOR_TIMEOUT:-900} i why
  editor_up || { echo "no Editor running"; return 1; }
  print -r -- "$*" > "$dir/$id.tmp" && mv "$dir/$id.tmp" "$dir/$id.cmd"
  for (( i = 1; i <= limit * 5; i++ )); do
    if [[ -f "$dir/$id.out" ]]; then
      cat "$dir/$id.out"; rm -f "$dir/$id.out"
      return 0
    fi
    if (( i % 50 == 0 )) && why=$(stalled); then
      echo "error: '$*' not answered: $why"; rm -f "$dir/$id.cmd"
      return 1
    fi
    sleep 0.2
  done
  echo "timeout: the Editor did not answer '$*' within ${limit}s (compiling, a modal dialog, or AgentInbox not loaded)"
  rm -f "$dir/$id.cmd"
  return 1
}

# The result line of an answer, and the answer without its console part.
result() { print -r -- "$1" | sed -n 2p; }
answer() { print -r -- "$1" | sed '/^--- console$/,$d'; }
# Exceptions logged to Editor.log since a byte offset (a restarted Editor starts a new log).
exceptions_since() {
  local log="$root/Logs/Editor.log" from=$1
  (( $(stat -f %z "$log" 2>/dev/null || echo 0) < from )) && from=0
  tail -c +$(( from + 1 )) "$log" | grep -aE '^([A-Za-z.+]*Exception|Unhandled Exception)(:| )' | grep -v SuperProxy
}

case "$1" in
compile)
  t0=$EPOCHREALTIME
  limit=${SF_EDITOR_TIMEOUT:-300}
  editor_up || { echo "no Editor running"; exit 1; }
  st=$(SF_EDITOR_TIMEOUT=$limit send state) || { print -r -- "$st"; exit 1; }
  # A compile in play mode reloads under the running match (and wrecks a peer's trial).
  [[ $(result "$st") == *playing=True* ]] && { echo "in play mode: stop first, then compile"; exit 1; }
  # The newest script, or folder (a deleted or renamed script shows only on its folder).
  newest=0
  for f in "$root"/Assets/**/*.cs(N.om[1]) "$root"/Assets/**/(N/om[1]); do
    m=$(mtime "$f"); (( m > newest )) && newest=$m
  done
  refreshed=0; quiet=0; idle=0
  while :; do
    started=$(mtime "$dir/compile.started"); done_=$(mtime "$dir/compile.done"); reloaded=$(mtime "$dir/reloaded")
    if (( started > newest && done_ >= started )); then
      # The last compile began after the newest change and has finished.
      if (( $(head -1 "$dir/compile.done") > 0 )); then
        echo "COMPILE ERRORS ($(head -1 "$dir/compile.done")):"; tail -n +2 "$dir/compile.done"; echo; exit 1
      fi
      if (( reloaded >= done_ )); then
        if (( done_ < t0 )); then echo "up to date: the compile of $(date -r ${done_%.*} +%H:%M:%S) has every change, 0 errors"
        else echo "compiled: $(printf '%.1f' $(( done_ - started ))) s compile, domain reloaded; $(elapsed $t0) s in all"; fi
        exit 0
      elif (( newest <= reloaded )); then
        # A compile finished with no reload after it and no script newer than the last reload: it was not the
        # Editor's own (a player build compiles the scripts for its target, writes compile.done and never reloads
        # the domain; `compile` then waited out its whole limit for a reload that was not coming). Nothing is left
        # to load once the Editor is idle: three quiet answers in a row, as the branch below asks for.
        r=$(SF_EDITOR_TIMEOUT=$limit send state | sed -n 2p)
        if [[ $r == *compiling=False* ]]; then
          if (( ++idle >= 3 )); then
            echo "up to date: the compile of $(date -r ${done_%.*} +%H:%M:%S) was no reload of this domain (a player build?) and no script is newer than the last reload ($(date -r ${reloaded%.*} +%H:%M:%S)); $(elapsed $t0) s"; exit 0
          fi
        else idle=0; fi
      fi
    elif (( ! refreshed )); then
      ans=$(SF_EDITOR_TIMEOUT=$limit send refresh) || { print -r -- "$ans"; exit 1; }
      refreshed=1; continue
    elif (( started <= newest )); then
      # No compile has begun since the newest change: is one coming? (Unity compiles nothing
      # when no script's content changed, and a folder also changes when a .meta is written.)
      # If not, the last compile's result stands.
      r=$(SF_EDITOR_TIMEOUT=$limit send state | sed -n 2p)
      if [[ $r == *compiling=False* ]]; then
        if (( ++quiet >= 3 )); then
          if [[ -f "$dir/compile.done" ]] && (( $(head -1 "$dir/compile.done") > 0 )); then
            echo "COMPILE ERRORS (from the last compile; nothing newer to compile):"; tail -n +2 "$dir/compile.done"; echo; exit 1
          fi
          echo "up to date: nothing new to compile ($(elapsed $t0) s)"; exit 0
        fi
      else quiet=0; fi
    fi
    (( EPOCHREALTIME - t0 > limit )) && { echo "compile: not finished within ${limit}s"; exit 1; }
    sleep 0.5
  done
  ;;
play)
  out=$(send play ${2:+"$2"}) || { print -r -- "$out"; exit 1; }
  print -r -- "$out" | tail -n +2 | grep -v '^$'
  [[ $out == error* ]] && exit 1
  t0=$EPOCHREALTIME
  limit=${SF_PLAY_TIMEOUT:-180}
  while :; do
    ans=$(SF_EDITOR_TIMEOUT=$limit send ready) || { print -r -- "$ans"; exit 1; }
    r=$(result "$ans")
    case $r in
      ready:*) echo "$r ($(elapsed $t0) s)"; exit 0;;
      "no match"*) echo "$r"; exit 1;;
      "not playing") (( EPOCHREALTIME - t0 > 15 )) && { echo "play mode did not start (compile errors? a modal dialog?)"; exit 1; };;
    esac
    (( EPOCHREALTIME - t0 > limit )) && { echo "play: not ready within ${limit}s (last: $r)"; exit 1; }
    sleep 0.5
  done
  ;;
wait)
  [[ -n $2 ]] || { echo "usage: editor.sh wait Namespace.Type.Method [done-regex] [seconds]"; exit 2; }
  target=$2; pattern=$3; limit=${4:-540}
  # Progress texts: the six words, and the ones trials print without them (AIReview's waits,
  # Duels' fights, CampSeeds' count, the batches' maps).
  busy='^(staging|running|looking|starting|shooting|watching|waiting)|^fight [0-9]+:|^[0-9]+ chars so far$|^map [0-9]+(: loading)?$'
  from=$(stat -f %z "$root/Logs/Editor.log" 2>/dev/null || echo 0)   # a byte offset: no rescan of the whole log every 2 s
  t0=$EPOCHREALTIME
  while :; do
    out=$(SF_EDITOR_TIMEOUT=60 send call "$target") || { print -r -- "$out"; exit 1; }
    # A trial whose coroutine threw reads "staging" for ever: say so instead of waiting it out.
    exc=$(exceptions_since $from | head -3)
    [[ -n $exc ]] && { print -r -- "$out" | tail -n +2; echo "wait: exception in the Editor log since the wait began (the run goes on; wait again to keep waiting):"; print -r -- "$exc"; exit 1; }
    r=$(result "$out")
    # No trial to wait for, in any of the trials' wordings ("none", "no probe running", "no long match run yet")
    [[ $out == error* || $r == (not playing|not running|none|no trial running|no *running|no *run yet) ]] && { print -r -- "$out"; exit 1; }
    # A BugTrial whose Wait gave up (its world replaced, match time frozen) says so first.
    [[ $r == INCOMPLETE* ]] && { print -r -- "$out" | tail -n +2; exit 1; }
    if [[ -n $pattern ]]; then
      # the result only: a console line must not end the wait
      answer "$out" | tail -n +2 | grep -qE -- "$pattern" && { print -r -- "$out" | tail -n +2; exit 0; }
    else
      print -r -- "$r" | grep -qE "$busy" || { print -r -- "$out" | tail -n +2; exit 0; }
    fi
    (( EPOCHREALTIME - t0 > limit )) && { print -r -- "$out" | tail -n +2; echo "wait: not done after ${limit}s"; exit 1; }
    sleep 2
  done
  ;;
eval)
  case $2 in
    quick) menu=Quick; limit=${3:-2400};;
    full) menu=Full; limit=${3:-7200};;
    *) echo "usage: editor.sh eval quick|full [seconds]"; exit 2;;
  esac
  report="$HOME/Library/Application Support/StarForge/StarForge/starforge_ai_eval.txt"
  st=$(SF_EDITOR_TIMEOUT=60 send state) || { print -r -- "$st"; exit 1; }
  [[ $(result "$st") == *playing=True* ]] && { echo "already in play mode: stop first"; exit 1; }
  # Behind another app at Interaction Mode Default a run goes ~25% slower (Scripts/Game/CLAUDE.md).
  th=$(SF_EDITOR_TIMEOUT=60 send call StarForge.EditorTools.EditorThrottle.State | sed -n 2p)
  [[ $th == *NoThrottling* ]] || echo "warning: $th, not NoThrottling: behind another app the run is slower (editor.sh call StarForge.EditorTools.EditorThrottle.NoThrottling)"
  before=$(mtime "$report")
  from=$(stat -f %z "$root/Logs/Editor.log" 2>/dev/null || echo 0)
  # The display sleeping stalls the Editor (root CLAUDE.md): hold it awake while this waits.
  caffeinate -d -i -w $$ &
  out=$(send call StarForge.EditorTools.AIEvalMenu.$menu) || { print -r -- "$out"; exit 1; }
  [[ $out == error* ]] && { print -r -- "$out"; exit 1; }
  echo "evaluation $2 started $(date +%H:%M:%S)"
  t0=$EPOCHREALTIME; stopped=0; n=0
  while :; do
    sleep 10; (( n++ ))
    if (( $(mtime "$report") > before )); then
      # The report, then each game's line cut to 120 characters (whole lines: the report file).
      sed '/^per game:$/,$d' "$report"
      if grep -q '^per game:' "$report"; then
        echo "per game (whole lines in $report):"; sed '1,/^per game:$/d' "$report" | cut -c1-120
      else
        echo "per game:"; log="$root/Logs/Editor.log"
        tail -c +$(( from + 1 )) "$log" | grep -a '^\[AIEval\] vs' | cut -c1-120
      fi
      echo "exceptions: $(exceptions_since $from | wc -l | tr -d ' ')"
      echo "waited $(elapsed $t0) s"
      exit 0
    fi
    # A run stopped part-way writes no report: two looks a minute apart outside play end the wait.
    if (( n % 6 == 0 )); then
      [[ $(SF_EDITOR_TIMEOUT=60 send state | sed -n 2p) == *playing=False* ]] && (( ++stopped >= 2 )) && { echo "the evaluation stopped without a report"; exit 1; }
    fi
    (( EPOCHREALTIME - t0 > limit )) && { echo "eval: no report within ${limit}s"; exit 1; }
  done
  ;;
*)
  # An "error" answer exits 1 too: callers go by the exit status.
  out=$(send "$@"); rc=$?
  print -r -- "$out"
  (( rc == 0 )) && [[ $out != error* ]]
  ;;
esac
