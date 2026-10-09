# ValheimUtils

> **AI generated.** This mod is written by an AI (Claude) under human direction and testing.

Quality-of-life tweaks for Valheim. Every feature has its own switch in
`BepInEx/config/mh.ValheimUtilsByAi.cfg` (generated on first launch). Each feature states
whether it runs on the client, the server, or both.

## Features

### Craft without roof (`[Crafting] CraftWithoutRoof`, default: on)

Vanilla refuses to use the workbench, forge, stonecutter, artisan table, black forge, galdr table, food preparation
table, fermenter, spinning wheel and beds without a roof, or when they are "too exposed" (not enough walls and roof
around them). With this on they all work in the open: the fermenter can be filled and keeps fermenting, the spinning
wheel keeps spinning, and a bed can be claimed, set as your spawn point and slept in.

Everything else stays vanilla: the cauldron and mead ketill never needed a roof and still need a fire, stations still
need their extensions in range, and build pieces still need a station nearby. A bed still needs a fire nearby and no
enemies around, and you can't sleep while wet, so rain still keeps you out of an open-air bed.

Side: Client. For crafting stations and beds only the player using them needs the mod. A fermenter or spinning wheel
works in the open only while the game that controls it has the mod: always in single player; in multiplayer usually
the first player who came near it. While a player without the mod controls it, it stalls outdoors as in vanilla, and a
fermenter starts over. The server doesn't need the mod; works on vanilla servers.

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

Vanilla rules stay: fire, fuel, roof, cover and biome requirements are untouched (Craft without roof lifts roof
and cover), the chest must have room or the product drops on the ground as usual, and nothing moves while a player
has the chest open.
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

All players share one map without building a cartography table. When you join, your explored areas and pins are
sent to the server, which merges them into one shared map and sends you everything the others have found. After that
your map is sent at most every 5 minutes, and only when you explored something new or added, removed or ticked off one
of your own pins; the server passes whatever is new on to everyone else.

The shared map works like one cartography table that everybody writes to: shared areas show as "explored by others",
death markers are not shared, and a real table still works by hand. A pin belongs to the player who placed it, and
only they can remove it for everyone. If you delete another player's pin it comes back with the next sync, ticking a
pin off is not passed on to players who already have it, and `resetsharedmap` is refilled by the next sync. Syncing
never shows pin types you have hidden, and nothing is synced in worlds without a map.

Side: Both. The server and every player who wants the shared map need the mod; update the server together with the
players (an older server still passes on single maps, which can remove other players' pins). The server keeps the
shared map only in memory: after a restart it is rebuilt as players join, because each player's own map still holds
everything that was shared before.

### Feeding trough (`[World] FeedingTrough`, default: on)

Adds the **Feeding Trough** (Hammer > Storage, also under Furniture; workbench): 10 Wood, 2 Resin, a 10-slot chest.
Put food in it and hungry tamed animals within `[World] FeedingTroughRange` (default 15 m, 1-50 m) walk to it and
eat one item at a time, just like food dropped on the ground. Each animal only takes what it eats in vanilla. Big
animals such as Lox and Moose use it too.

Vanilla rules stay: animals only look for food when hungry, food lying on the ground nearby is still eaten first,
and nothing is taken while a player has the trough open. Untamed animals ignore the trough, so it does not tame, and
summoned creatures ignore it, so they don't eat the livestock's feed. An animal only eats once it has really reached
the trough (not through a gate that closed on its way) and gives up after 30 s if it can't get there.
The trough is only checked when the game's own food search runs (every 10 s per hungry tamed animal), without
physics queries, so idle troughs and fed animals cost nothing.

In multiplayer only the game that owns the trough takes food out of it. An animal controlled by another player's game
waits at the trough and asks the owner to hand it over; the owner hands it over only while nobody has the trough
open, so two players never change the trough at once.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client, otherwise the trough does
not exist for them and is never handed over. Animals are simulated by a nearby player's game, so that player needs
the mod for feeding to happen. Turning the feature off keeps the trough craftable but animals stop using it.

### Switch Forsaken power anywhere (`[Combat] ForsakenPowerAnywhere`, default: on)

The Escape menu gets a button below Settings that shows the current power (e.g. **Forsaken power: Eikthyr**).
Each click switches to the next Forsaken power, wherever you are. It works like using a guardian stone: it only
changes which power `F` will use. A power that is already running keeps going and the cooldown is not reset, so
switching can't skip the cooldown.

You can only pick powers this character has already selected at a guardian stone at least once (the game
remembers them). Only powers whose boss has been killed in the current world are offered (a trophy carried in
from another world does not count). New powers still have to be unlocked at the stones as usual. The button is
hidden until you have picked at least one.

Side: Client. Other players and the server don't need the mod.

### Auto repair at crafting stations (`[Crafting] AutoRepair`, default: on)

Opening a crafting station (workbench, forge, black forge, ...) repairs every worn item in your inventory (equipped
or not) that the station can repair, all at once with a single repair sound, so you no longer click the hammer
button once per item. Torches can't be repaired, as in vanilla. Which station repairs which item, the station
level requirement and the crafting skill gain are vanilla too.

Side: Client. Other players and the server don't need the mod.

### Larger crafting station build range (`[Building] StationBuildRange`, default: on)

You can build farther from a workbench, forge or other crafting station: its build range is raised to
`[Building] StationBuildRangeMeters` (default 40 m; vanilla is 20 m, artisan table 40 m, max 64), and the circle shown
around the station grows with it. The setting never shrinks a range, so the artisan table only gains above 40 m. As
in vanilla, removing and repairing pieces use the same range, measured horizontally from where you stand. Changes
apply within two seconds. Any bonus from station upgrades, the station's own area effect, crafting distance and
upgrade rules stay vanilla.

Side: Client. Only the player who builds needs the mod; other players and the server don't.

### Faster loot from dead creatures (`[Combat] FastCorpseDrop`, default: on)

A killed creature's body vanishes in a puff and drops its loot after `[Combat] FastCorpseDropSeconds` (default 0.5 s,
range 0.1-10) instead of lying there for several seconds. Bodies that vanish sooner in vanilla are left alone, and
the loot itself, the death animation start and the puff effect stay vanilla. Player bodies and bosses keep the
vanilla timing. Creatures that leave no body, such as skeletons, blobs, Seekers, Bonemass, Moder and the Queen,
already drop their loot instantly, so nothing changes for them. When Thungr falls in the Zil & Thungr fight, his
trophy drops at once as in vanilla; only his body vanishes sooner.

Side: Client. It applies to creatures your game controls when they die: always in single player; in multiplayer
usually the ones near you. Other players and the server don't need the mod; a body controlled by a player without
it keeps the vanilla delay.

### Craft and build from nearby containers (`[Crafting] CraftFromContainers`, default: on)

Crafting, upgrading and building count materials in chests built by any player within `[Crafting] CraftFromContainersRadius`
(default 15 m) of you. Your own inventory is used first, then the chests. The requirement numbers in the crafting
panel and build menu show what you have (inventory plus chests) and what is needed, for example `25/10`, and stop
flashing red when the chests have enough.

What stays vanilla: carts, ships, tombstones, feeding troughs, other players' private chests and chests behind a ward
you can't use are ignored. The item you upgrade must still be in your inventory. Recipes that take only one of
several ingredients still use your inventory only, and their amounts don't count chests. Recipe discovery still comes
only from what you carry, and no-cost mode never touches chests.

Multiplayer: materials are only taken from a chest while your game holds it, so nothing is duplicated or lost. When
you click Craft or pick a build piece, your game asks for the chests holding its materials, the same way the game
asks when you open a chest. A chest another player has open is not counted until they close it. If another player is
crafting from a chest you need, your craft says "Chest busy, try again" and takes nothing; a build click that needs a
chest your game doesn't hold yet says "Fetching materials, click again". The next try works.

Side: Client. Works for you alone; nobody else needs the mod (owners without the mod hand chests over through the
vanilla open request).

### Comfort range (`[Building] ComfortRange`, default: on)

Beds, chairs, rugs, banners, fires and every other comfort piece count towards your comfort level from up to
`[Building] ComfortRangeMeters` away (default 20 m; vanilla is 10 m, max 64). Which pieces stack, their comfort values
and the shelter requirement stay vanilla. As in vanilla, pieces count through walls and floors, from any player, and
meads count as a chair. Resting still starts only near a burning fire, sitting or under a roof; the longer range only
raises the comfort level, which sets how long Rested lasts.

Side: Client. Only players with the mod get the longer range; the rested buff itself is unchanged for everyone else.

### Station extension range (`[Building] ExtensionRange`, default: on)

Chopping blocks, tanning racks, bellows, anvils and every other station extension upgrade a crafting station from up
to `[Building] ExtensionRangeMeters` away (default 10 m, max 50; vanilla is 2-5 m depending on the extension, and the
setting never shrinks it), and the hammer lets you place them that far from the station. As in vanilla, one extension
upgrades every station of its kind within range, each type counts once per station, and extensions still need their
vanilla spacing from each other. Distant extensions also give the vanilla upgrade bonus: each extension type adds
3-4 m to the station's build radius and to the area around it where enemies don't spawn (the cauldron has only the
build radius).

Side: Client. Every player who wants the higher station level or the relaxed placement needs the mod; the station
level is computed on each client.

### Portal map travel (`[World] PortalMapTravel`, default: on)

Stepping into any portal opens the large map with a portal pin for every portal in the world, named by its tag.
Click a pin to travel there; close the map to stay. Portal tags and connections no longer matter, so a single portal
per outpost is enough. Everything else stays vanilla: the same item restrictions (`$msg_noteleport`), the
`NoPortals` / `NoBossPortals` world modifiers, the loading screen and the exit spot in front of the target portal.
The map pins are temporary and never saved. Without a map (the `NoMap` world modifier or the `nomap` console
command) portals keep their vanilla tag links. Pins can only be picked with a mouse: on a controller, stepping into a
portal currently just opens a map you cannot pick from, so turn the feature off to keep vanilla portals.

Side: Both. The client part works alone but only shows portals your game has already seen; with the mod on the
server every portal in the world is listed. Other players without the mod keep vanilla portals.

### Farm everything (`[World] FarmEverything`, default: on)

Wild raspberry, blueberry, cloudberry and lingonberry bushes, the four mushrooms (incl. Ashlands smoke puffs), thistle,
dandelion and Ashlands fiddlehead can be taken home. Magecap and Jotun puffs stay vanilla (already farmable).
With the Cultivator out, look at one and press the Remove button (middle mouse, hammer "remove" bind): the plant is
uprooted and drops one seed item (for example **Raspberries seeds**, with the berry icon; not edible), plus its harvest
if it still has one. Uprooting costs stamina and Cultivator durability, like removing a piece with the hammer. Each
seed is a new Cultivator piece (**Raspberries plant**, ...), placed on cultivated ground like any sapling. A planted one
starts bare (mushrooms, thistle, dandelion and fiddlehead as a small version of the plant) and grows its harvest after
`[World] FarmEverything<Prefab>Minutes` (1 to 10000; defaults: 450 for berry bushes and fiddlehead, 360 for mushrooms,
thistle and dandelion, i.e. 1.5x the wild 300 / 240), then regrows at that pace after every pick. The harvest itself is
vanilla. Uproot a planted one with the Cultivator to get its seed back; for planted berry bushes, hammer remove refunds
it too. Unlike saplings, planted ones are never raid targets and grow in any biome without needing free space around
them, and the minutes setting of their owner (the player whose game is simulating them) decides the regrow time. Wild
plants and their respawn are untouched unless you uproot them.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client: the seeds and planted pieces are
new prefabs, and players without them see errors. Turning the feature off stops uprooting and hides the pieces from the
Cultivator; already planted ones keep growing on their configured time.
### Plant growth timer (`[World] PlantGrowthTimer`, default: on)

Hovering a planted sapling (carrots, turnips, onions, barley, flax, trees, Mistlands mushrooms, ...) adds a line
under the vanilla "( Healthy )" text: `grows in 1h 05m`. Planted bushes and mushrooms from Farm everything show
`<name>` and the same line while they are bare (vanilla shows nothing there). Wild plants are untouched. The time is
the real growth time of that exact plant, so it already includes the random part of the vanilla grow time.

Side: Client. Only you see the text; other players do not need the mod.

## Install

Drop `ValheimUtilsByAi.dll` into `BepInEx/plugins` or install with a mod manager. Requires
[Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
