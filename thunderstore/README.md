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

Adds **AutoStorage Link** to the vanilla Hammer's Misc tab: a tool like the Hammer's Repair entry. Free, nothing
is placed.

Select AutoStorage Link in the Hammer menu and left-click a processing piece, then left-click any chest built by a player
(wood chest, reinforced chest, black metal chest, ...; not carts, ships or tombstones) to link them. Linked pieces take their inputs from that chest and put their products into it, with no pickup
radius or area: you choose every connection. Click the same piece and chest again to unlink; click a chest with
nothing selected to unlink every piece connected to it. While AutoStorage Link is selected, green lines show
existing links and a yellow line follows the selected piece. Pick any other Hammer entry to build again.

The separate AutoStorage Hammer from earlier versions is gone: any copy in your inventory turns into its materials
(3 Wood, 1 Surtling Core) when you log in or pick it up.

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

In multiplayer only the game that owns a chest edits it. A linked piece asks the chest's owner to hand it over and
waits until it does; the owner hands it over only while nobody has the chest open, so two players never write to
the same chest at once.

The custom AutoStorage chest from earlier versions is removed. **Empty your AutoStorage chests before updating:**
placed ones disappear from the world, along with their contents.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client, otherwise the Link tool does
not exist for them and their chests are never handed over. Turning the feature off stops linking and automation;
links are kept.

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

The Escape menu gets a button below Settings that shows the current power (e.g. **Forsaken power: Eikthyr**).
Each click switches to the next Forsaken power, wherever you are. It works like using a guardian stone: it only
changes which power `F` will use. A power that is already running keeps going and the cooldown is not reset, so
switching can't skip the cooldown.

You can only pick powers this character has already selected at a guardian stone at least once (the game
remembers them). New powers still have to be unlocked at the stones as usual. The button is hidden until you
have picked at least one.

Side: Client. Other players and the server don't need the mod.

### Auto repair at crafting stations (`[Crafting] AutoRepair`, default: on)

Opening a crafting station (workbench, forge, black forge, ...) repairs every worn item it can repair, one per frame,
so you no longer click the hammer button once per item. Which station repairs which item, the station level
requirement and the crafting skill gain are vanilla.

Side: Client. Other players and the server don't need the mod.

### Larger crafting station build range (`[Building] StationBuildRange`, default: on)

You can build farther from a workbench, forge or any other crafting station: its build range is
`[Building] StationBuildRangeMeters` (default 40 m; vanilla is 20 m, max 100). The circle shown around the station grows
with it. Changes apply within two seconds. Any bonus from station upgrades, the station's own area effect, crafting
distance and upgrade rules stay vanilla.

Side: Client. Only the player who builds needs the mod; other players and the server don't.

### Faster loot from dead creatures (`[Combat] FastCorpseDrop`, default: on)

A killed creature's body vanishes in a puff and drops its loot after `[Combat] FastCorpseDropSeconds` (default 0.5 s,
range 0.1-10) instead of lying there for several seconds. Bodies that vanish sooner in vanilla are left alone, and
the loot itself, the death animation start and the puff effect stay vanilla.

Side: Client. It applies to creatures your game controls when they die: always in single player; in multiplayer
usually the ones near you. Other players and the server don't need the mod; a body controlled by a player without
it keeps the vanilla delay.

### Craft and build from nearby containers (`[Crafting] CraftFromContainers`, default: on)

Crafting, upgrading and building count materials in chests built by any player within `[Crafting] CraftFromContainersRadius`
(default 15 m) of you. Your own inventory is used first, then the chests. The requirement numbers in the crafting
panel and build menu show what you have (inventory plus chests) and what is needed, for example `25/10`, and stop
flashing red when the chests have enough.

What stays vanilla: carts, ships, tombstones, other players' private chests and chests behind a ward you can't use
are ignored. The item you upgrade must still be in your inventory. Recipes that take only one of several
ingredients still use your inventory only. Recipe discovery still comes only from what you carry, and no-cost mode
never touches chests.

Multiplayer: a chest is used only while your game owns it, so nothing is duplicated or lost. If another player's game
owns it, yours asks for it, and their game hands it over only while nobody has it open. A chest someone is looking
into is skipped until they close it. If two players craft from the same chest at the same time, it can take a
second or two before the chest counts for you.

Side: Both. Chests owned by a player without the mod are never handed over to you, so they are not used.

### Comfort range (`[Building] ComfortRange`, default: on)

Beds, chairs, rugs, banners, fires and every other comfort piece count towards your comfort level from up to
`[Building] ComfortRangeMeters` away (default 20 m; vanilla is 10 m, max 100). Which pieces stack, their comfort values
and the shelter requirement stay vanilla.

Side: Client. Only players with the mod get the longer range; the rested buff itself is unchanged for everyone else.

### Station extension range (`[Building] ExtensionRange`, default: on)

Chopping blocks, tanning racks, bellows, anvils and every other station extension upgrade a crafting station from up
to `[Building] ExtensionRangeMeters` away (default 10 m; vanilla is 5 m, max 50), and the hammer lets you place them
that far from the station. As in vanilla, one extension upgrades every station of its kind within range, each type
counts once per station, and extensions still need their vanilla spacing from each other.

Side: Client. Every player who wants the higher station level or the relaxed placement needs the mod; the station
level is computed on each client.

### Portal map travel (`[World] PortalMapTravel`, default: on)

Stepping into any portal opens the large map with a portal pin for every portal in the world, named by its tag.
Click a pin to travel there; close the map to stay. Portal tags and connections no longer matter, so a single portal
per outpost is enough. Everything else stays vanilla: the same item restrictions (`$msg_noteleport`), the
`NoPortals` / `NoBossPortals` world modifiers, the loading screen and the exit spot in front of the target portal.
The map pins are temporary and never saved.

Side: Both. The client part works alone but only shows portals your game has already seen; with the mod on the
server every portal in the world is listed. Other players without the mod keep vanilla portals.

### Farm everything (`[World] FarmEverything`, default: on)

Wild raspberry, blueberry and cloudberry bushes, the three mushrooms, thistle and dandelion can be taken home.
With the Cultivator out, look at one and press the Remove button (middle mouse, hammer "remove" bind): the plant is
uprooted and drops one seed item (for example **Raspberries seeds**, with the berry icon; not edible). Each seed is a
new Cultivator piece (**Raspberries plant**, ...), placed on cultivated ground like any sapling. A planted one starts
bare and grows its harvest after `[World] FarmEverything<Prefab>Minutes` (defaults: 450 for berry bushes, 360 for
mushrooms, thistle and dandelion, i.e. 1.5x the wild 300 / 240), then regrows at that pace after every pick. The
harvest itself is vanilla. Uproot a planted one with the Cultivator to get its seed back (hammer remove refunds it too).
Wild plants and their respawn are untouched unless you uproot them.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client: the seeds and planted pieces are
new prefabs, and players without them see errors. Turning the feature off stops uprooting and hides the pieces from the
Cultivator; already planted ones keep growing on their configured time.

## Install

Drop `ValheimUtilsByAi.dll` into `BepInEx/plugins` or install with a mod manager. Requires
[Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
