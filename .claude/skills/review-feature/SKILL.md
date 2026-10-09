---
name: review-feature
description: Review ValheimUtilsByAi features for bugs a smoke test and a logic-only review miss - UI soft-locks from forcing state vanilla never allows, interrupted flows, item loss or duplication, multiplayer ownership, runtime toggles, exceptions, cost. Read-only research agents check every item of checklist.md against the decompiled game; findings are verified before they are reported. Use for "/review-feature", "/review-feature PortalMapTravel AutoDoors", "/review-feature all", "review the feature", "audit the mod", "are other features broken too".
---

Target: `$ARGUMENTS`. Read-only until the user asks for fixes.

## 1. Scope

- Names given (`PortalMapTravel AutoDoors`): those `Features/<Name>.cs` files.
- `all`: every file in `Features/`.
- Empty: the features changed on this branch, `git diff --name-only main...HEAD -- Features/`; if none, ask which.

Make sure `obj/decompiled/` exists (`bash scripts/decompile.sh` once). Read `checklist.md` in this skill's directory.

## 2. Research

Group the features: a feature over ~300 lines gets its own agent, smaller ones share one, at most 5 agents in total.
Launch all groups in one message, each a `general-purpose` agent with `model: opus`, with this prompt filled in:

> Review these features of the BepInEx/HarmonyX Valheim mod in C:/Users/mateu/Desktop/ValheimAI/ValheimUtilsByAi
> for bugs: <files>. READ-ONLY: do not edit, create or delete files, do not run git, do not build.
> Intended behaviour and side (Client/Server/Both) are in thunderstore/README.md; project rules in CLAUDE.md.
> Decompiled game source: obj/decompiled/<Type>.cs.
> Work through every section of .claude/skills/review-feature/checklist.md for each feature. <Extra focus, if the
> user named a symptom or the feature has an obvious risk.>
> Verify every claim in obj/decompiled and quote the vanilla lines with line numbers. Report only findings you can
> write as a scenario: steps -> wrong outcome. For each: severity (soft-lock, item loss/dupe, exception, multiplayer
> desync, permanent state change, cost, cosmetic), file:line, the vanilla code that causes it, a minimal fix, and
> how to reproduce it in game. Then list, per checklist section, what you checked and found safe. Under ~900 words.

## 3. Verify

For every finding, open the cited feature line and vanilla lines yourself and confirm the scenario actually reaches
them (guards earlier in the method, who calls it, which peer runs it). Drop what does not hold and say so. If a
claim needs more than reading a few lines, print the type with `bash scripts/decompile.sh <Type>`, don't guess.

## 4. Report

To the user, in their language:

- Confirmed findings, most severe first: one line of what goes wrong for the player, file:line, the fix in one line,
  how to reproduce in game.
- Dropped findings, one line each with why.
- Features found clean.

Offer to fix the confirmed ones on `fix/mh/<slug>` with a changelog bullet under `## Unreleased`. Do not edit before
the user agrees.
