#!/usr/bin/env bash
# Release: bump version, finalize changelog, build, commit, tag, push. CI does the publishing.
# Usage: scripts/release.sh [patch|minor|major|X.Y.Z]   (default: patch)
set -euo pipefail
cd "$(dirname "$0")/.."

csproj=ValheimUtilsByAi.csproj
changelog=thunderstore/CHANGELOG.md
die() { echo "ERROR: $*" >&2; exit 1; }

[ "$(git branch --show-current)" = main ] || die "not on main"
[ -z "$(git status --porcelain)" ] || die "working tree not clean"
git fetch -q origin
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || die "main differs from origin/main, pull or push first"

bullets=$(awk '/^## Unreleased/{p=1;next} /^## /{p=0} p' "$changelog" | grep -c '^- ' || true)
[ "$bullets" -gt 0 ] || die "nothing under '## Unreleased' in $changelog"

current=$(sed -n 's/.*<Version>\([0-9][0-9.]*\)<\/Version>.*/\1/p' "$csproj")
[ -n "$current" ] || die "no <Version> in $csproj"
IFS=. read -r major minor patch <<<"$current"
case "${1:-patch}" in
  patch) new="$major.$minor.$((patch + 1))" ;;
  minor) new="$major.$((minor + 1)).0" ;;
  major) new="$((major + 1)).0.0" ;;
  [0-9]*.[0-9]*.[0-9]*) new="$1" ;;
  *) die "usage: $0 [patch|minor|major|X.Y.Z]" ;;
esac

latest_tag=$(git tag --list 'v*' --sort=-v:refname | head -1)
latest=${latest_tag#v}
if [ -n "$latest" ]; then
  [ "$new" != "$latest" ] || die "version $new already tagged"
  [ "$(printf '%s\n%s\n' "$latest" "$new" | sort -V | tail -1)" = "$new" ] || die "$new is not greater than latest tag $latest"
fi

sed -i "s|<Version>$current</Version>|<Version>$new</Version>|" "$csproj"
sed -i "0,/^## Unreleased/s//## Unreleased\n\n## $new/" "$changelog"

dotnet build -c Release --nologo -v q
zip="dist/ValheimUtilsByAi-$new.zip"
[ -f "$zip" ] || die "$zip not produced"
unzip -p "$zip" manifest.json | grep -q "\"version_number\": \"$new\"" || die "manifest.json in $zip does not say $new"

git add "$csproj" "$changelog"
git commit -q -m "Release $new"
git tag "v$new"
git push -q origin main "v$new"

echo "Released v$new"
echo "Workflow:  https://github.com/Mateusz-H/ValheimUtilsByAi/actions"
echo "Package:   https://thunderstore.io/package/ByAi/ValheimUtils/"
echo
awk -v v="$new" '/^## /{p = ($2 == v)} p' "$changelog"
