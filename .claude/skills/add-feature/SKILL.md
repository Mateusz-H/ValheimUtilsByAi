---
name: add-feature
description: Add a new toggleable feature to ValheimUtilsByAi from a plain-language description. Scaffolding, registration, docs stubs, smoke test and branch are scripted; only the Harmony patch and three sentences of prose are written by hand. Use for "/add-feature <description>", "add a feature that ...", "make the mod also ...".
---

Feature request: `$ARGUMENTS`. Follow the steps in order. Scripts do the mechanical work; keep your own output to the code and prose the scripts leave as TODO.

## 1. Name it

Pick `<PascalName>` (the config key and class), `<Section>` (gameplay area: `Crafting`, `Building`, `Combat`, `Inventory`, `World`, ...) and a short `<Title>`. If the request is ambiguous in a way that changes which game method to patch, ask one question first. Otherwise do not ask.

## 2. Scaffold

```
bash scripts/new-feature.sh <PascalName> <Section> "<Title>"
```

Creates the branch `feat/mh/<slug>`, `Features/<PascalName>.cs` with the config binding, registers it in `Plugin.cs`, adds a README stub and a changelog bullet. Everything left for you is marked `TODO`.

## 3. Find the patch target

```
bash scripts/decompile.sh                 # one-time full dump to obj/decompiled/
bash scripts/decompile.sh <GameType>      # print one type
grep -rn "<Method>(" obj/decompiled       # every caller
```

Patch the single shared method all game paths go through, never each caller. Decide the side: Client (local player, UI, input), Server (ZDO owner, `ZNet.instance.IsServer()`, spawning), or Both.

Small feature (one or two game types): read the decompile yourself. Anything bigger (several game types, custom
prefabs, ZDO persistence, input handling) goes to a research subagent so the decompile dumps stay out of your
context. Launch one `general-purpose` agent with `model: opus` and `effort: max`, read-only, with: the `obj/decompiled/` path, a one-paragraph
description of the feature, and a numbered list of concrete questions per game type (fields, update loop and
its owner check, ZDO keys, exact signatures and private/public, every caller of the spawn/output method, which
Unity module DLL holds a needed type). Ask for quoted snippets and line references; forbid file edits. Write
the patch only from that report, then run step 5. If the report leaves a signature uncertain, print that one
type with `scripts/decompile.sh <GameType>` rather than guessing. Jötunn (CLAUDE.md feature rule 5) is a
dependency decision, make it before the research so the questions match (prefab registration questions are
moot with Jötunn).

## 4. Fill the TODOs

- `Features/<PascalName>.cs`: the `[HarmonyPatch]` target and body. Read `_enabled.Value` on every call. If game state must change for the duration of a call, save it in `__state` and restore it in a postfix. No helpers, base classes or new dependencies beyond what CLAUDE.md feature rule 5 allows.
- Config description: one or two sentences a player understands.
- `thunderstore/README.md` stub: what changes, what stays vanilla, Side, whether other players need the mod.
- `thunderstore/CHANGELOG.md` bullet: one line.
- First server-side feature only: add `"server-side"` to categories in `thunderstore.toml`. New gameplay area: add its slug (`building`, `gear`, `enemies`, ...).

## 5. Verify

```
dotnet build -c Release
bash scripts/smoke-test.sh
```

Both must pass. The smoke test loads the DLL in the headless server; Harmony throws there if the patch target does not exist. Fix and rerun until green.

Then run the `review-feature` skill on `<PascalName>`. The smoke test never touches UI or multiplayer, so this is the
only check for soft-locks, interrupted flows and item loss. Fix what it confirms, rebuild, rerun the smoke test.

## 6. Hand over

```
git add -A && git commit -m "<Title>" && git push -u origin HEAD
```

Report: config key and default, side, exact in-game steps to test and what vanilla did, and the compare URL
`https://github.com/Mateusz-H/ValheimUtilsByAi/compare/main...feat/mh/<slug>`. Do not merge and do not run `/deploy`.
