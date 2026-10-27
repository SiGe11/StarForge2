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
#                                              done: until it matches regex, or without one, until
#                                              it stops reading staging/running/looking/...
#   Tools/editor.sh eval quick|full            run the AI evaluation and print its report when it
#                                              ends (5-6 / 27-35 min: run it in the background)
#   Tools/editor.sh menu "StarForge/Build All" | refresh | stop | state | ready
#
# Single commands wait up to $SF_EDITOR_TIMEOUT seconds (default 900) for the answer.
zmodload zsh/datetime
root="$(cd "$(dirname "$0")/.." && pwd)"
dir="$root/Temp/AgentInbox"
mkdir -p "$dir"

# One command to the inbox; prints its answer ("ok" or "error", the result, the console).
send() {
  local id="$(date +%s%N)" limit=${SF_EDITOR_TIMEOUT:-900} i
  print -r -- "$*" > "$dir/$id.tmp" && mv "$dir/$id.tmp" "$dir/$id.cmd"
  for (( i = 0; i < limit * 5; i++ )); do
    if [[ -f "$dir/$id.out" ]]; then
      cat "$dir/$id.out"; rm -f "$dir/$id.out"
      return 0
    fi
    sleep 0.2
  done
  echo "timeout: the Editor did not answer '$*' within ${limit}s (compiling, a modal dialog, or AgentInbox not loaded)"
  rm -f "$dir/$id.cmd"
  return 1
}

# The result line of an answer.
result() { print -r -- "$1" | sed -n 2p; }
mtime() { stat -f %Fm "$1" 2>/dev/null || echo 0; }
elapsed() { printf '%.1f' $(( EPOCHREALTIME - $1 )); }

case "$1" in
compile)
  t0=$EPOCHREALTIME
  limit=${SF_EDITOR_TIMEOUT:-300}
  pgrep -qf "Unity.app/Contents/MacOS/Unity" || { echo "no Editor running"; exit 1; }
  st=$(SF_EDITOR_TIMEOUT=$limit send state) || { print -r -- "$st"; exit 1; }
  # A compile in play mode reloads under the running match (and wrecks a peer's trial).
  [[ $(result "$st") == *playing=True* ]] && { echo "in play mode: stop first, then compile"; exit 1; }
  # The newest script, or folder (a deleted or renamed script shows only on its folder).
  newest=0
  for f in "$root"/Assets/**/*.cs(N.om[1]) "$root"/Assets/**/(N/om[1]); do
    m=$(mtime "$f"); (( m > newest )) && newest=$m
  done
  refreshed=0; quiet=0
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
    r=$(SF_EDITOR_TIMEOUT=60 send ready | sed -n 2p)
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
  busy='^(staging|running|looking|starting|shooting|watching)'
  log="$root/Logs/Editor.log"; from=$(( $(wc -l < "$log") + 1 ))
  t0=$EPOCHREALTIME
  while :; do
    out=$(SF_EDITOR_TIMEOUT=60 send call "$target") || { print -r -- "$out"; exit 1; }
    # A trial whose coroutine threw reads "staging" for ever: say so instead of waiting it out.
    exc=$(tail -n +$from "$log" | grep -aE '^([A-Za-z.+]*Exception|Unhandled Exception)(:| )' | grep -v SuperProxy | head -3)
    [[ -n $exc ]] && { print -r -- "$out" | tail -n +2; echo "wait: exception in the Editor log since the wait began (the run goes on; wait again to keep waiting):"; print -r -- "$exc"; exit 1; }
    r=$(result "$out")
    [[ $out == error* || $r == "not playing" || $r == "no trial running" ]] && { print -r -- "$out"; exit 1; }
    if [[ -n $pattern ]]; then
      print -r -- "$out" | grep -qE -- "$pattern" && { print -r -- "$out" | tail -n +2; exit 0; }
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
  log="$root/Logs/Editor.log"
  st=$(SF_EDITOR_TIMEOUT=60 send state) || { print -r -- "$st"; exit 1; }
  [[ $(result "$st") == *playing=True* ]] && { echo "already in play mode: stop first"; exit 1; }
  before=$(mtime "$report")
  from=$(( $(wc -l < "$log") + 1 ))
  # The display sleeping stalls the Editor (root CLAUDE.md): hold it awake while this waits.
  caffeinate -d -i -w $$ &
  out=$(send call StarForge.EditorTools.AIEvalMenu.$menu) || { print -r -- "$out"; exit 1; }
  [[ $out == error* ]] && { print -r -- "$out"; exit 1; }
  echo "evaluation $2 started $(date +%H:%M:%S)"
  t0=$EPOCHREALTIME; stopped=0; n=0
  while :; do
    sleep 10; (( n++ ))
    if (( $(mtime "$report") > before )); then
      cat "$report"
      echo "per game:"; tail -n +$from "$log" | grep -a '^\[AIEval\] vs' | cut -c1-120
      echo "exceptions: $(tail -n +$from "$log" | grep -aE '^([A-Za-z.+]*Exception|Unhandled Exception)(:| )' | grep -vc SuperProxy)"
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
  send "$@"
  ;;
esac
