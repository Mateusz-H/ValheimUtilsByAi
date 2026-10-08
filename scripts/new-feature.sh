#!/usr/bin/env bash
# Scaffold a feature: branch, Features/<Name>.cs with config binding, registration in Plugin.cs,
# README section stub and changelog bullet. The Harmony patch and the prose are left as TODO.
# Usage: scripts/new-feature.sh <PascalName> <ConfigSection> "<Human title>"
#   e.g. scripts/new-feature.sh CraftWithoutRoof Crafting "Craft without roof"
set -euo pipefail
cd "$(dirname "$0")/.."

name="${1:?PascalName}"; section="${2:?ConfigSection}"; title="${3:?Human title}"
die() { echo "ERROR: $*" >&2; exit 1; }
[[ "$name" =~ ^[A-Z][A-Za-z0-9]*$ ]] || die "name must be PascalCase"
[[ "$section" =~ ^[A-Z][A-Za-z0-9]*$ ]] || die "section must be PascalCase"
file="Features/$name.cs"
[ ! -e "$file" ] || die "$file exists"
[ -z "$(git status --porcelain)" ] || die "working tree not clean"

slug=$(echo "$name" | sed 's/\([A-Z]\)/-\L\1/g; s/^-//')
git checkout -q main && git checkout -q -b "feat/mh/$slug"

cat > "$file" <<EOF
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

internal static class $name
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "$section",
            "$name",
            true,
            "TODO one or two sentences a player understands.");
    }

    // TODO: read _enabled.Value on every call; if game state must change for the call, restore it in a postfix via __state.
    [HarmonyPatch(typeof(TODO_GameType), nameof(TODO_GameType.TODO_Method))]
    private static class Patch
    {
        private static void Prefix()
        {
            if (!_enabled.Value) return;
        }
    }
}
EOF

awk -v line="        $name.Bind(Config);" '
  { lines[NR] = $0; if ($0 ~ /\.Bind\(Config\);/) last = NR }
  END { for (i = 1; i <= NR; i++) { print lines[i]; if (i == last) print line } }
' Plugin.cs > Plugin.cs.tmp && mv Plugin.cs.tmp Plugin.cs
grep -q "$name.Bind(Config);" Plugin.cs || die "failed to register in Plugin.cs"

awk -v block="### $title (\`[$section] $name\`, default: on)

TODO: what changes, what stays vanilla.

Side: TODO Client / Server / Both. TODO whether other players need the mod.
" '/^## Install/ { print block } { print }' thunderstore/README.md > README.tmp && mv README.tmp thunderstore/README.md

sed -i "0,/^## Unreleased/s//## Unreleased\n\n- $title: TODO one line./" thunderstore/CHANGELOG.md

echo "branch feat/mh/$slug"
echo "fill the TODOs in: $file  thunderstore/README.md  thunderstore/CHANGELOG.md"
