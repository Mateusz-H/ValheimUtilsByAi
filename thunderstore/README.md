# ValheimUtils

> **AI generated.** This mod is written by an AI (Claude) under human direction and testing.

Quality-of-life tweaks for Valheim. Every feature has its own switch in
`BepInEx/config/mh.ValheimUtilsByAi.cfg` (generated on first launch). Each feature states
whether it runs on the client, the server, or both.

## Features

### Craft without roof (`[Crafting] CraftWithoutRoof`, default: on)

The vanilla game refuses to open a workbench, forge or any other crafting station that is
not under a roof or is "too exposed". This removes that check. Everything else is untouched:
a forge still needs fire, stations still need their extensions in range, build pieces still
need a station nearby.

Side: Client. Other players do not need the mod. Works on vanilla servers.

### Kiln wood filter (`[Crafting] KilnWoodFilter`, default: on)

The charcoal kiln only accepts the wood types listed in `[Crafting] KilnAllowedWood`
(comma-separated item prefab names, default: `Wood`). Any other wood is refused with the vanilla
"won't work" message, and pressing E with only unlisted wood in the inventory says there is nothing to
process. Smelters and blast furnaces are untouched. Vanilla kiln wood: `Wood`, `FineWood`, `RoundLog`, `Blackwood`.

Side: Client. Only players running the mod are restricted; a player without it can still add any wood.

## Install

Drop `ValheimUtilsByAi.dll` into `BepInEx/plugins` or install with a mod manager.
