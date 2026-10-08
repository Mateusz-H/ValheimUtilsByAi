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

### Auto storage (`[Crafting] AutoStorage`, default: on)

Adds two craftables:

- **AutoStorage** chest (Hammer > Misc, workbench): 10 Fine Wood, 5 Greydwarf Eyes, 5 Bronze. 10 slots.
- **AutoStorage Hammer** (workbench): 3 Wood, 1 Surtling Core.

Hold the AutoStorage Hammer and left-click a processing piece, then left-click an AutoStorage chest to link them.
Linked pieces take their inputs from that chest and put their products into it, with no pickup radius or area:
you choose every connection. Click the same piece and chest again to unlink; click a chest with nothing
selected to unlink every piece connected to it. While the hammer is held, green lines show existing links and a
yellow line follows the selected piece. The hammer never builds or repairs.

| Piece | Takes from chest | Puts into chest |
| --- | --- | --- |
| Charcoal kiln | wood (respects the kiln wood filter) | coal |
| Smelter, blast furnace, eitr refinery | ore / scrap / soft tissue, coal or sap | bars, eitr |
| Spinning wheel, windmill | flax, barley | linen thread, barley flour |
| Beehive, sap extractor | - | honey, sap |
| Cooking station, iron cooking station, stone oven | raw food, wood for the oven | cooked (or burnt) food |
| Fermenter | base mead | mead |

Vanilla rules stay: fire, fuel, roof, cover and biome requirements are untouched, the chest must have room or
the product drops on the ground as usual, and nothing moves while a player has the chest open.
`[Crafting] AutoStorageMaxLinkDistance` (default 100 m) caps the link length. Pieces are only simulated near a
player, so a chest more than roughly 64 m from its piece may not be loaded and the piece then idles.

Products that still landed on the ground (chest full, open or not loaded at that moment) are picked up later: once a
minute each linked piece moves its own products lying within `[Crafting] AutoStorageCollectRadius` (default 5 m,
0 turns it off) into its chest. Only that piece's products are taken, never other items or food placed on tables.
A drop is only taken by the game that owns it, exactly like a vanilla pickup, and leaves the ground only as far as
it went into the chest, so nothing is duplicated or lost.

Chests built with 0.0.3 had 32 slots. Their items are kept: everything is moved into the 10 visible slots, and
anything beyond that stays stored in the chest, is still used by linked pieces, and shows up once slots free up.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client, otherwise the custom
chest and hammer do not exist for them. Turning the feature off keeps both craftable but stops linking and
automation.

### Auto map sync (`[World] AutoMapSync`, default: on)

All players share one map without building a cartography table. Your explored areas and pins are sent to the
server 30 seconds after you join and then every 5 minutes (only when something changed); the server passes them
on to everyone else, and new players get the shared map when they join. Merging works exactly like reading a
vanilla cartography table: shared areas show as "explored by others", death markers are not shared, and the
table itself still works by hand.

Side: Both. The server and every player who wants the shared map need the mod. The server keeps the map only in
memory; after a restart it is rebuilt from the players' next uploads.

### Feeding trough (`[World] FeedingTrough`, default: on)

Adds the **Feeding Trough** (Hammer > Misc, workbench): 10 Wood, 2 Resin, a 10-slot chest. Put food in it and
hungry tamed animals within `[World] FeedingTroughRange` (default 15 m) walk to it and eat one item at a time, just
like food dropped on the ground. Each animal only takes what it eats in vanilla.

Vanilla rules stay: animals only look for food when hungry, food lying on the ground nearby is still eaten first,
and nothing is taken while a player has the trough open. Untamed animals ignore the trough, so it does not tame.
The trough is only checked when the game's own food search runs (every 10 s per hungry tamed animal), without
physics queries, so idle troughs and fed animals cost nothing.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client, otherwise the trough does
not exist for them. Animals are simulated by a nearby player's game, so that player needs the mod for feeding to
happen. Turning the feature off keeps the trough craftable but animals stop using it.

### Switch Forsaken power anywhere (`[Combat] ForsakenPowerAnywhere`, default: on)

Press `[Combat] ForsakenPowerAnywhereKey` (default `H`) anywhere to switch to the next Forsaken power. The
switch works like using a guardian stone: it only changes which power `F` will use. A power that is already
running keeps going and the cooldown is not reset, so you can't use the hotkey to skip the cooldown.

You can only pick powers this character has already selected at a guardian stone at least once (the game
remembers them). New powers still have to be unlocked at the stones as usual.

Side: Client. Other players and the server don't need the mod.

## Install

Drop `ValheimUtilsByAi.dll` into `BepInEx/plugins` or install with a mod manager. Requires
[Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
