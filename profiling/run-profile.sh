#!/usr/bin/env bash
# Launch Cold Audit and collect dotnet-counters + sampled stack traces.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/profiling"
BIN="$ROOT/src/bin/Release/net10.0/ColdAudit"
DURATION="${1:-00:01:30}"
export PATH="$PATH:$HOME/.dotnet/tools"

mkdir -p "$OUT"
cd "$(dirname "$BIN")"

echo "Starting Cold Audit..."
./ColdAudit >"$OUT/game.log" 2>&1 &
GAME_PID=$!
echo "PID=$GAME_PID"

# Wait for window / diagnostics port to come up
for i in $(seq 1 30); do
  if kill -0 "$GAME_PID" 2>/dev/null; then
    if dotnet-trace ps 2>/dev/null | grep -q "$GAME_PID"; then
      break
    fi
  else
    echo "Game exited early. See $OUT/game.log"
    exit 1
  fi
  sleep 0.5
done

echo "Collecting for $DURATION - play the game now (move, interact, look around)."
echo "Tools will stop automatically when the timer ends."

dotnet-counters collect -p "$GAME_PID" \
  --counters System.Runtime \
  --format csv \
  -o "$OUT/counters.csv" \
  --duration "$DURATION" >"$OUT/counters.log" 2>&1 &
COUNTERS_PID=$!

dotnet-trace collect -p "$GAME_PID" \
  --profile dotnet-sampled-thread-time \
  --format Speedscope \
  -o "$OUT/cpu.nettrace" \
  --duration "$DURATION" >"$OUT/trace.log" 2>&1 &
TRACE_PID=$!

wait "$COUNTERS_PID" "$TRACE_PID" || true

echo "Collection finished."
ls -lh "$OUT"/counters.csv "$OUT"/cpu.nettrace* 2>/dev/null || true
echo "Game still running (PID $GAME_PID). Close the window when done."
echo "Artifacts in $OUT"
