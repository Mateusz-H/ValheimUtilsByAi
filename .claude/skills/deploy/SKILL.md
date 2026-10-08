---
name: deploy
description: Release ValheimUtilsByAi. Runs scripts/release.sh which bumps the version, finalizes the changelog, commits, tags and pushes; GitHub Actions builds and publishes to GitHub Releases and Thunderstore. Use for "/deploy", "/deploy minor", "/deploy 1.2.0", "release", "publish new version".
---

Run exactly this and nothing else first:

```
bash scripts/release.sh $ARGUMENTS
```

`$ARGUMENTS` is empty (patch), `minor`, `major` or `X.Y.Z`.

- Exit 0: relay the script output to the user verbatim (version, workflow URL, package URL, changelog section). Done.
- Non-zero: the script printed one `ERROR:` line stating the precondition that failed. Relay it. Fix only if the fix is mechanical and obviously what the user wants (for example `git pull` when main is behind origin), then rerun. Otherwise stop and let the user decide.

Do not bump the version, edit the changelog, tag or upload by hand. The script is the only release path.
