#!/usr/bin/env bash
# Load the Release DLL in the headless dedicated server with BepInEx and fail on plugin load / Harmony errors.
# Usage: scripts/smoke-test.sh          (VALHEIM_SERVER overrides the server directory)
set -euo pipefail
cd "$(dirname "$0")/.."

server="${VALHEIM_SERVER:-C:/Users/mateu/valheim_server}"
dll=bin/Release/ValheimUtilsByAi.dll
log="$server/BepInEx/LogOutput.log"
die() { echo "ERROR: $*" >&2; exit 1; }

[ -f "$dll" ] || die "$dll missing, run: dotnet build -c Release"
[ -f "$server/winhttp.dll" ] || die "BepInEx not installed in $server"

rm -f "$server"/BepInEx/plugins/ValheimUtilsByAi.dll "$log"
cp "$dll" "$server/BepInEx/plugins/"

(cd "$server" && ./valheim_server.exe -nographics -batchmode -name smoke -port 2456 -world smoke \
  -password smoketest123 -savedir "$server/saves" -public 0 >/dev/null 2>&1 &)
for _ in $(seq 1 60); do
  sleep 2
  grep -q "Chainloader startup complete" "$log" 2>/dev/null && break
done
# With Jötunn present, custom prefabs register only once the world loads; wait for that so errors there are caught.
if [ -f "$server/BepInEx/plugins/Jotunn.dll" ]; then
  for _ in $(seq 1 90); do
    sleep 2
    grep -q "Registering .* custom RPCs" "$log" 2>/dev/null && break
  done
fi
sleep 2
taskkill //F //IM valheim_server.exe >/dev/null 2>&1 || true

grep -q "Chainloader startup complete" "$log" || die "server never finished loading plugins, see $log"
grep -q "Loading \[ValheimUtilsByAi" "$log" || die "plugin not loaded, see $log"
if grep -iE "harmony|exception|\[Error  *:(ValheimUtilsByAi|Jotunn)" "$log" | grep -vi "AsyncResourceUpload"; then
  die "errors in $log"
fi
grep -E "Loading \[ValheimUtilsByAi|Chainloader startup complete|Adding .* custom|Registering .* custom" "$log"
echo "SMOKE TEST OK"
