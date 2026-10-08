#!/usr/bin/env bash
# Decompile assembly_valheim.dll into obj/decompiled/ once (grep callers there), or print one type.
# Usage: scripts/decompile.sh            full dump to obj/decompiled/ (skipped if present)
#        scripts/decompile.sh <Type>     print decompiled C# of one type
#        scripts/decompile.sh --refresh  redo the full dump (after a game update)
set -euo pipefail
cd "$(dirname "$0")/.."

managed="${VALHEIM_MANAGED:-$(sed -n "s/.*<ValheimManaged [^>]*>\([^<]*\)<.*/\1/p" ValheimUtilsByAi.csproj)}"
dll="$managed/assembly_valheim.dll"
[ -f "$dll" ] || { echo "ERROR: $dll missing" >&2; exit 1; }
command -v ilspycmd >/dev/null || dotnet tool install -g ilspycmd >/dev/null

out=obj/decompiled
case "${1:-}" in
  "") [ -d "$out" ] && { echo "$out already exists, use --refresh"; exit 0; }
      mkdir -p "$out" && ilspycmd -p -o "$out" "$dll" >/dev/null && echo "decompiled to $out" ;;
  --refresh) rm -rf "$out" && mkdir -p "$out" && ilspycmd -p -o "$out" "$dll" >/dev/null && echo "decompiled to $out" ;;
  *) ilspycmd -t "$1" "$dll" ;;
esac
