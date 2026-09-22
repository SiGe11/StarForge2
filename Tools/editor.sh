#!/bin/zsh
# Send one command to the open Unity Editor through Assets/StarForge/Editor/AgentInbox.cs
# and print its answer. Usage:
#   Tools/editor.sh menu "StarForge/Build All"
#   Tools/editor.sh call StarForge.EditorTools.MapChecks.Report
#   Tools/editor.sh refresh | play | stop | state
# Waits up to $SF_EDITOR_TIMEOUT seconds (default 900) for the answer.
dir="$(cd "$(dirname "$0")/.." && pwd)/Temp/AgentInbox"
mkdir -p "$dir"
id="$(date +%s%N)"
print -r -- "$*" > "$dir/$id.tmp" && mv "$dir/$id.tmp" "$dir/$id.cmd"
limit=${SF_EDITOR_TIMEOUT:-900}
for (( i = 0; i < limit * 5; i++ )); do
  if [[ -f "$dir/$id.out" ]]; then
    cat "$dir/$id.out"; rm -f "$dir/$id.out"
    exit 0
  fi
  sleep 0.2
done
echo "timeout: the Editor did not answer '$*' within ${limit}s (compiling, a modal dialog, or AgentInbox not loaded)"
rm -f "$dir/$id.cmd"
exit 1
