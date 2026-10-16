# ValheimUtils

> **AI generated.** This mod is written by an AI (Claude) under human direction and testing.

Quality-of-life tweaks for Valheim. Every feature has its own switch in
`BepInEx/config/mh.ValheimUtilsByAi.cfg` (generated on first launch). Each feature states
whether it runs on the client, the server, or both.

On a server that runs this mod, the server's config applies to every player: switches, ranges, multipliers and lists
come from the server's `mh.ValheimUtilsByAi.cfg` when you connect and whenever it changes. Admins can change them in
game with a configuration manager (F1); for everyone else they are locked while connected. Keys (quick stack, sort,
trash, ...) and the main-menu mod list stay your own. Your own config file is never overwritten: on disconnect, in
single player and on servers without the mod your own values apply. Edit the server's file while it is stopped, or
restart it afterwards.

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

Adds **AutoStorage Link input** and **AutoStorage Link output** to the vanilla Hammer's Misc tab (copper ore and copper
bar icons): tools like the Hammer's Repair entry. Free, nothing is placed.

Pick one in the Hammer menu, left-click a processing piece, then left-click any chest built by a player (wood chest,
reinforced chest, black metal chest, ...; not carts, ships or tombstones). With **Link input** the piece takes its
inputs from that chest; with **Link output** it puts its products into it. A piece can have several input and output
chests, used in the order you linked them, and one chest can be both its input and its output. There is no pickup
radius or area: you choose every connection. Click the same piece and chest again to unlink; click a chest with
nothing selected to unlink it from every piece that uses it as input (or as output, with Link output). While a link
tool is selected, lines show the existing links of its kind (blue for inputs, green for outputs) and a yellow line
follows the selected piece. Pick any other Hammer entry to build again.

You can only link or unlink a chest you could open: a private chest only its builder, a chest inside a ward only
players the ward lets in. A piece inside a ward can only be selected by players the ward lets in.

A piece with only input chests still drops or keeps its products as in vanilla, and one with only output chests still
needs its inputs added by hand. A chest linked as both input and output also feeds a piece its own products: a
windmill then turns oat seeds into oats and those oats into oat flour. Use separate input and output chests to keep
the oats.

| Piece | Takes from input chests | Puts into output chests |
| --- | --- | --- |
| Charcoal kiln | wood (respects the kiln wood filter) | coal |
| Smelter, blast furnace, eitr refinery | ore / scrap / soft tissue, coal or sap | bars, eitr |
| Spinning wheel | flax | linen thread |
| Windmill | barley, oat seeds, oats | barley flour, oats, oat flour |
| Frigid kiln | ice | liquid frost |
| Hot tub | wood | - |
| Beehive, birds' nest, sap extractor | - | honey, feathers, sap |
| Cooking station, iron cooking station, stone oven | raw food, wood for the oven | cooked (or burnt) food |
| Frost foundry | casts, liquid frost | the finished weapons, armor and staffs |
| Fermenter | base mead | mead |

A piece never takes the last item of a liked stack in its input chests (see Liked items), so a chest can keep one ore
or log as a marker for quick stacking.

Vanilla rules stay: fire, fuel, roof, cover and biome requirements are untouched (Craft without roof lifts roof
and cover), and nothing moves while a player has the chest open. When no output chest has room, the kilns, smelters,
furnaces, refinery, spinning wheel and windmill drop their products on the ground as usual; a beehive, birds' nest or
sap extractor keeps filling up to its limit, a fermenter keeps its mead, and a cooking station leaves the food on the
station, where it burns as in vanilla. A cooking station gets no new raw food while none of its output chests can be
used (open, not loaded, being handed over, removed or out of range), so only what is already on it can burn. Food that AutoStorage takes off a cooking station gives no cooking skill and no
bonus portions, since no player picks it up, and frost foundry gear gets no crafter name.
`[Crafting] AutoStorageMaxLinkDistance` (default 100 m) caps the link length. Pieces are only simulated near a
player, so a chest more than roughly 64 m from its piece may not be loaded and the piece then idles.

Products that still landed on the ground (output chests full, open or not loaded at that moment) are picked up later:
once a minute each piece with an output chest moves items of the kinds it makes lying within
`[Crafting] AutoStorageCollectRadius` (default 5 m, 0 turns it off) into its output chests. Items are matched by kind,
not by where they came from: a cooking station, which makes coal from burnt food, also takes coal from a nearby kiln.
Items placed as decoration, such as food on tables, are never taken, and neither is anything a player carried and
dropped, so bars dropped next to a smelter to hand them over stay on the ground. A drop is only taken by the game that owns it,
exactly like a vanilla pickup, and leaves the ground only as far as it went into a chest, so nothing is duplicated or
lost.

In multiplayer only the game that owns a chest edits it. A linked piece asks the chest's owner to hand it over and
waits until it does; the owner hands it over only while nobody has the chest open, so two players never write to the
same chest at once. A player opening a chest always sees what the linked pieces put into it, even a moment before.
The game that controls a piece (in multiplayer usually the first player who came near it) runs its automation with its
settings, which are the server's when the server runs the mod.

Links made with the single AutoStorage Link tool of 0.0.10 and earlier are removed: link those pieces again with Link
input and Link output. The separate AutoStorage Hammer from earlier versions is gone: any copy in your inventory turns
into its materials (3 Wood, 1 Surtling Core) when you log in or pick it up. The custom AutoStorage chest from earlier
versions is removed. **Empty your AutoStorage chests before updating:** placed ones disappear from the world, along
with their contents.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client, otherwise the link tools do
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
or not) that the station can repair, all at once with one repair sound, so you no longer click the hammer
button once per item. Torches can't be repaired, as in vanilla. Which station repairs which item, the station
level requirement and the crafting skill gain are vanilla too.

When the station you open has a repair button, crafting stations within `[Crafting] AutoRepairNearbyMeters` of you
(default 15 m, range 0-50) repair at the same time, so opening the workbench also repairs the armor only the forge
next to it can repair. Each station follows its
own vanilla rules: its item types, its level (counting its upgrades), and a roof or a lit fire where it needs one. Each
station that repaired something plays its repair sound. Set it to 0 to repair only at the station you open.

Side: Client. Other players and the server don't need the mod.

### Larger crafting station build range (`[Building] StationBuildRange`, default: on)

You can build farther from a workbench, forge or other crafting station: its build range is raised to
`[Building] StationBuildRangeMeters` (default 100 m; vanilla is 20 m, artisan table 40 m, max 128), and the circle
shown around the station grows with it. The setting never shrinks a range, so the artisan table only gains above
40 m. As in vanilla, removing and repairing pieces use the same range, measured horizontally from where you stand.
Changes apply within two seconds. Any bonus from station upgrades, the station's own area effect, crafting distance
and upgrade rules stay vanilla.

A station only counts while your game has it loaded. The default Simulation Distance (graphics settings) loads at
least 128 m around you; at the lowest setting it is 64 m, so a station farther away than that does not let you build
until you walk closer.

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
flashing red when the chests have enough. A liked stack in a chest (see Liked items) always keeps its last item, which
is not counted.

What stays vanilla: carts, ships, tombstones, feeding troughs, other players' private chests and chests behind a ward
you can't use are ignored. The item you upgrade must still be in your inventory. Recipes that take only one of
several ingredients still use your inventory only, and their amounts don't count chests. Recipe discovery still comes
only from what you carry, and no-cost mode never touches chests.

Multiplayer: materials are only taken from a chest while your game holds it, so nothing is duplicated. When you click
Craft or pick a build piece, your game asks for the chests holding its materials, the same way the game asks when you
open a chest. A craft takes what your inventory lacks out of the chests right when you click Craft (from chests your
game didn't hold yet as soon as they arrive, usually a fraction of a second) and holds it until the craft finishes, so
other players can open those chests during the craft. If you cancel the craft, close the inventory or walk away, the
materials go back to their chest; if that chest is full or another game holds it now, into your inventory, and if
that is full too, onto the ground. If you lose the connection during the craft they go into your inventory, since the
server can no longer receive them back into the chest; only a crash, or a full inventory at that moment, loses them.
A chest another player has open is not counted until they close it. If a chest you need never arrives, your craft
says "Chest busy, try again" and takes nothing; a build click that needs a chest your game doesn't hold yet says
"Fetching materials, click again". The next try works.

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
Click a pin to travel there; close the map to stay. Moving more than 5 m away from the portal closes the map, so
you travel only from the portal you stepped into. Portal tags and connections no longer matter, so a single portal
per outpost is enough. Everything else stays vanilla: the same item restrictions (`$msg_noteleport`), the
`NoPortals` / `NoBossPortals` world modifiers, the loading screen and the exit spot in front of the target portal.
The map pins are temporary and never saved. They use their own portal icon, pulse gently, stay drawn above every
other pin and ignore the map's icon filter. With `[World] PortalsOnMap` (default: on) the same portal pins are also
shown every time you open the large map (M), so you always see where portals are; they don't pulse and clicking them
does nothing, travel still starts by stepping into a portal. This works even with `PortalMapTravel` off. Because one portal now reaches every other, the portal and the stone
portal cost more: `[World] PortalCostMultiplier` (default: 1.5, range 0.1-10) multiplies every material, rounded up,
so the portal takes 30 fine wood, 15 greydwarf eyes and 3 surtling cores. Set it to 1 for vanilla prices. The new price
applies to portals built from then on; destroying a portal refunds the price at the time the world loaded, so a portal
built before the change can refund more than it cost. Without a map (the `NoMap` world modifier or the `nomap` console
command) portals keep their vanilla tag links. Pins can only be picked with a mouse: on a controller, stepping into a
portal currently just opens a map you cannot pick from, so turn the feature off to keep vanilla portals.

Side: Both. The client part works alone but only shows portals your game has already seen; with the mod on the
server every portal in the world is listed. Other players without the mod keep vanilla portals. The build price is
checked by each player's own game, so every player pays the price from their config (the server's when it runs the
mod), and players without the mod pay the vanilla price.

### Farm everything (`[World] FarmEverything`, default: on)

Wild raspberry, blueberry, cloudberry and lingonberry bushes, the four mushrooms (incl. Ashlands smoke puffs), thistle,
dandelion and Ashlands fiddlehead can be taken home. Magecap and Jotun puffs stay vanilla (already farmable).
With the Cultivator out, look at one and press the Remove button (middle mouse, hammer "remove" bind): the plant is
uprooted and drops one seed item (for example **Raspberries seeds**, with the berry icon; not edible), plus its harvest
if it still has one. Uprooting costs stamina and Cultivator durability, like removing a piece with the hammer. Each
seed is a new Cultivator piece (**Raspberries plant**, ...), placed on cultivated ground like any sapling. A planted one
starts bare (mushrooms, thistle, dandelion and fiddlehead as a small version of the plant) and grows its harvest after
`[World] FarmEverything<Prefab>Minutes` (1 to 10000; defaults: 150 for berry bushes and fiddlehead, 120 for mushrooms,
thistle and dandelion, i.e. half the wild 300 / 240), then regrows at that pace after every pick. The harvest itself is
vanilla. Uproot a planted one with the Cultivator to get its seed back; for planted berry bushes, hammer remove refunds
it too. Unlike saplings, planted ones are never raid targets and grow in any biome without needing free space around
them, and the minutes setting of their owner (the player whose game is simulating them; the server's setting when it
runs the mod) decides the regrow time. Wild plants and their respawn are untouched unless you uproot them.

Vanilla saplings get a grow time setting too: `[World] FarmEverything<sapling>Minutes` (1 to 10000), for example
`FarmEverythingsapling_carrotMinutes`, for every crop and its seed version, Jotun puffs, magecap, tree sapling and vine.
It is the earliest the sapling grows. Each sapling still picks its own time in the vanilla range, which scales with the
setting: carrots take 67 to 83 minutes in vanilla, 30 makes it 30 to 37.5. The defaults are half of vanilla: crops,
Jotun puffs and magecap 33, beech, fir and pine 25, birch 42, oak 50, vines 2 (each description lists its vanilla
time). A changed value also applies to saplings already in the ground. As with the regrow time, the owner's setting
decides when a sapling grows; a player without the mod grows the ones they own at vanilla speed. Plant growth timer
shows the time from your own setting, which on a server without the mod can differ from the owner's.

An existing config keeps the regrow minutes it already has (360 and 450 in earlier versions). Delete those
`FarmEverything<Prefab>Minutes` lines, or set them, to get the new defaults.

Side: Both. The mod (and Jötunn) must be installed on the server and on every client: the seeds and planted pieces are
new prefabs, and players without them see errors. Turning the feature off stops uprooting, hides the pieces from the
Cultivator and puts saplings back on vanilla grow times; already planted ones keep growing on their configured time.
### Plant growth timer (`[World] PlantGrowthTimer`, default: on)

Hovering a planted sapling (carrots, turnips, onions, barley, flax, trees, Mistlands mushrooms, ...) adds a line
under the vanilla "( Healthy )" text: `grows in 1h 05m`. Planted bushes and mushrooms from Farm everything show
`<name>` and the same line while they are bare (vanilla shows nothing there). Wild plants are untouched. The time is
the real growth time of that exact plant, so it already includes the random part of the vanilla grow time.

Side: Client. Only you see the text; other players do not need the mod.

### Carts and ships on map (`[World] VehiclesOnMap`, default: on)

Every cart and ship in the world (raft, karve, longship, drakkar and modded ones) gets a small pin on the large map and
the minimap, using the same icon as in the build menu. Pins follow the vehicle as it moves, disappear when it is
destroyed, and ignore the map's icon filter. They are view-only: they are not saved to your map, can't be removed with
right click and are not shared through map tables. Your own saved pins and the rest of the map stay vanilla.

Side: Both. The server sends every cart and ship to all players so they are visible anywhere in the world. Without the
mod on the server you only see vehicles you've been near this session, at the last place you saw them. Players without
the mod see vanilla maps.

### Higher breeding limit (`[World] BreedingLimit`, default: on)

Tamed boars, wolves, lox, chickens and every other breedable animal keep breeding until `[World] BreedingLimitCount`
animals of their kind, adults and young together, stand within 10 m (default 20, max 100); vanilla stops at a handful.
Everything else stays vanilla: animals must still be tamed, fed and not alerted, need a partner close by, and the
love points, pregnancy time and 10 m counting radius are unchanged. A herd spread wider than 10 m can still grow past
the limit, as in vanilla.

Side: Both. Breeding is decided by whoever owns the animal, usually the game of the nearest player, so install it on
the server and on the client of every player who keeps animals.

### Auto-opening doors (`[Building] AutoDoors`, default: on)

Doors and gates built by players open by themselves when you come within 2.5 m, swinging away from you, and close
again once nobody is within 4 m. A door you close by hand while standing next to it stays closed until you walk away.
Doors the game generated (dungeons, crypts, Fuling villages and other locations), doors that need a key and doors
inside someone else's ward stay vanilla and still need E.

Side: Client. Opening uses the same request as pressing E, so other players see the doors move without having the
mod, and the server doesn't need it.

### Mod versions in main menu (`[Interface] ModVersions`, default: on)

The main menu shows a second line under the game version, for example `ValheimUtilsByAi 0.0.17 (12 mods)`, and gets
a Mods button under Settings. It opens a list of every mod BepInEx loaded, with its version. Copy puts the list,
headed by the game version, on the clipboard, so friends can compare their setups. Close, Escape or gamepad B close
the list. The version shown is the one the mod reports to BepInEx, which can differ from its Thunderstore version.
Mods that failed to load are not listed. The Escape menu in game stays vanilla.

Side: Client. Nobody else needs it.

### Liked items (`[Inventory] LikedItems`, default: on)

Alt + right click on an item in your inventory or in an open chest likes that stack: it gets a gold border and a line
in its tooltip. Alt + right click again unlikes it. Alt + left click stays free for other mods, such as MyLittleUI's
chat item links.

- In your inventory a liked stack is left alone by quick stack, store all, sort (it keeps its slot) and the trash can.
- In a chest a liked stack always keeps at least one item: crafting and building from nearby chests, AutoStorage
  pieces and restock never take its last one. The chest keeps holding that item even after a craft used up the rest,
  so quick stack keeps sending it there. Sort leaves it in its slot and fills it up first.

The like is stored in the item itself, so it survives logging out and dying: what you take back from your tombstone is
still liked, also when it merges into a stack you picked up since (that stack becomes liked). Splitting a liked stack
gives two liked stacks, and upgrading a liked item keeps the like. Moving a liked item between your inventory and a
chest by hand unlikes it, since a like in one place means nothing in the other. Items you pick up merge into a liked
stack as usual; once it is full the rest lands in a normal stack, which quick stack stores.

Side: Client. Nobody else needs the mod. Players without it don't see the likes, and their own crafting takes from
liked chest stacks like from any other.

### Quick stack (`[Inventory] QuickStack`, default: on)

`[Inventory] QuickStackKey` (default P, works with the inventory closed) or the Q button under your weight moves
stackable items from your inventory into chests within `[Inventory] QuickStackRadius` (default 10 m) that already hold
the same item. The nearest such chest is filled first; what doesn't fit goes to the next chest holding that item.
Items no chest holds stay with you, and the message says how much went, for example `Stored 12 of 23 items`. Partial
stacks in those chests are merged, so a chest doesn't keep two half stacks of the same item.

With a chest open, P and the Quick stack button in the container panel fill that chest only. Quick stack replaces
vanilla's Place stacks button, and holding E on a chest follows the same rules.

Never moved: the hotbar (first row), rows other mods add below your inventory (Better Archery's quiver), equipped
items, liked stacks, and items that don't stack (weapons, armor, tools), even when the chest holds the same one; Store
all moves those. Carts, ships, tombstones, other players' private chests
and chests behind a ward you can't use are skipped, as for crafting from chests.

The buttons sit where QuickStackStore puts them: small Q, R and S buttons under the weight in the inventory panel, and
a column with Quick stack, Take all, Store all, Restock and Sort in the container panel. Uninstall QuickStackStore
when using these features: both mods would add buttons and react to the same keys.

Multiplayer: a chest is only filled while your game holds it. P asks for the chests holding your items, the way
crafting from chests does, and stores once they arrive (usually a fraction of a second, at most 2 s). A chest another
player has open is skipped and counted in the message, for example `(1 chest busy)`.

Side: Client. Nobody else needs the mod.

### Store all (`[Inventory] StoreAll`, default: on)

A Store all button in the container panel moves everything from your inventory into the open chest, including items
that don't stack. It fills existing stacks first and merges partial stacks afterwards. The hotbar, rows other mods add
(Better Archery's quiver), equipped items, quest items and liked stacks stay. `[Inventory] StoreAllKey` (unbound by default) does the same while a chest is open.

Side: Client. Nobody else needs the mod.

### Restock (`[Inventory] Restock`, default: on)

`[Inventory] RestockKey` (default L, works with the inventory closed) or the R button under your weight fills partial
stacks of ammo, food, potions and other consumables in your inventory, hotbar and equipped ammo included. It takes
from chests within `[Inventory] RestockRadius` (default 10 m), nearest first. With a chest open, L and the Restock
button in the container panel take from that chest only. Only stacks that would merge are used (same item and
quality), and liked chest stacks keep their last item. Multiplayer works as for quick stack.

Side: Client. Nobody else needs the mod.

### Sort items (`[Inventory] SortItems`, default: on)

`[Inventory] SortKey` (default O, with the inventory open) or the S button under your weight sorts your inventory.
Partial stacks of the same item are merged, then items are laid out from the top left by kind (weapons, shields,
tools, armor, utility, ammo, food, fish, materials, trophies, the rest) and by name. With a chest open, O and the Sort
button in the container panel sort the chest instead. The hotbar, rows other mods add below your inventory (Better
Archery's quiver) and liked stacks keep their slots; equipped items move and stay equipped. Sorting twice changes
nothing.

Side: Client. Nobody else needs the mod.

### Trash can (`[Inventory] TrashItems`, default: on)

Adds a trash can under the armor value in the inventory panel. Drag an item from your inventory and click the can, or
press `[Inventory] TrashKey` (default Delete) while dragging, then confirm to destroy it. The dragged amount goes, so
split a stack first to destroy part of it. Liked and quest items can't be trashed, and neither can equipped gear
(unequip it first; equipped ammo can be trashed). Items from a chest go into your inventory first.

Side: Client. Nobody else needs the mod.

### Bigger chests (`[Inventory] BiggerChests`, default: on)

Chests get more rows, so each one holds more: their vanilla rows times `[Inventory] BiggerChestsRowMultiplier`
(default 2, max 5). With the default a chest is 5x4 instead of 5x2, a reinforced chest 6x8 instead of 6x4, a black
metal chest 8x8, a grausten chest 8x10, a barrel 6x4 and a personal chest 3x4. The panel scrolls when the rows don't
fit, as it does for the vanilla wardrobe. `[Inventory] BiggerChestsPrefabs` lists the containers that grow
(comma-separated prefab names, default the six chests above); the wardrobe (`piece_chest_warderobe`), carts (`Cart`)
and ships (`Karve`, `VikingShip`, ...) can be added. Items in a cart still slow it down as in vanilla, but the crates
shown on a cart count the added rows, so a cart filled to its vanilla size looks half loaded. Columns, stack sizes,
the tombstone and every container not listed stay vanilla.

Nothing is lost when a chest gets smaller again. Turning the feature off, lowering the multiplier, removing the mod
or opening the chest in a game without it keeps every row down to the last one that holds an item, as vanilla already
does for its own chests; only the empty rows below it disappear until the feature adds them back.

Side: Client. Each game sizes chests for itself, so the server doesn't need the mod. A player without it sees a
bigger chest down to its last filled row and can take everything out, but can't use the empty rows below that.

### Plant spacing (`[World] PlantSpacing`, default: on)

A sapling or seed can't be planted where another planted thing reaches into its own grow radius: 0.5 m for crops and
Jotun puffs, 0.8 m for magecap, 2 m for most tree saplings, 3 m for oak, 0.5 m for the bushes and mushrooms of Farm
everything. Nor can it go inside the grow radius of a bigger sapling nearby, so carrots stay 3 m from an oak sapling,
which would otherwise never grow. Planted things are saplings, grown crops (carrots, turnips, Jotun puffs, magecap, ...)
and Farm everything's bushes and mushrooms, also while bare. The ghost turns red and placing shows vanilla's message for
too little space. Vanilla lets you plant anywhere and only marks a crowded sapling "Not enough space" after planting,
and its check ignores mushrooms, so carrots could go right on top of grown Jotun puffs or planted mushrooms and still
grow. Wild pickables (stones, branches, berry bushes, mushrooms) don't count, so tree saplings still go anywhere in the
wild; wild Jotun puffs and magecap do, since the game uses the same object for wild and grown ones. Vanilla's own check
after planting (roof, other objects nearby) is unchanged.

With PlantEasily, every ghost of its grid gets the same check: crowded ones turn red and are not planted. That needs
PlantEasily's `PreventInvalidPlanting` (on by default). With it off, a crowded cursor ghost still refuses the whole
click, but when the cursor one is free PlantEasily plants the red grid ghosts too.

Side: Client. Your game checks your own planting; other players and the server don't need the mod.

### Higher network bandwidth (`[Network] NetworkBandwidth`, default: on)

World updates (where animals and other creatures are, doors, chest contents, built pieces) travel between each game
and the server at up to `[Network] NetworkSendRate` KB/s per player (default 400, 150-1024) instead of vanilla's fixed
150 KB/s. How much the game may have on its way at once grows with it, from 10 KB to the rate divided by 15 (about
27 KB at the default), the same ratio as vanilla. That 10 KB, not the rate, is what holds vanilla back: with a 50 ms
ping a player gets about 70 KB/s of updates, enough for some 20 animals on the move before the far ones start to
freeze and jump. With the default it is about 200 KB/s, with 1024 about 500 KB/s.

The rate is a ceiling: Steam only sends that much while there is something to send. The server's upload carries it
once per player online, so 4 players at the default can need up to about 13 Mbit/s; lower it on a home connection
with a slow upload. The same rate applies to what players send: a player whose upload is slower than the rate and
whose game runs a busy base (usually the first one there) can lag, and only the server's value counts, so lower it
for everyone then. A changed value applies to open connections right away.

What stays vanilla: how often updates are sent (at most 20 times a second), which objects are sent and in what order,
and no compression. Other mods that wait for room on the network, such as Jötunn's and ServerSync's settings sync,
see as much room as in vanilla. Servers started with `-crossplay` use a different network layer without a rate to
raise, so there the feature changes nothing. Don't combine it with another mod that raises the same limits (Better
Networking, Network): their windows add up and the last one to set the rate wins.

Side: Both. Each game speeds up only what it sends: the server what goes to players, each player what goes to the
server. Players without the mod can still join a server running it and get its faster updates, but what they send
stays at vanilla speed, so animals whose game runs on such a player update at vanilla speed for everyone.

### Quick stack repairs at nearby stations (`[Inventory] QuickStackRepair`, default: on)

Quick stacking (`[Inventory] QuickStackKey`, the Q button under your weight or the Quick stack button in a chest) also
repairs your worn items, equipped or not, at the crafting stations within `[Crafting] AutoRepairNearbyMeters` of you
(default 15 m), without opening one. In vanilla you repair only at the station you open, one item per click. Each
station follows its vanilla repair rules, as for auto repair: its item types, its level, and a roof or a lit fire where
it needs one; each one that repaired something plays its repair sound. The repair message shows top left so the quick
stack message stays readable.

Needs `[Inventory] QuickStack` on; `[Crafting] AutoRepair` can be off. `AutoRepairNearbyMeters` 0 repairs nothing here.

Side: Client. Nobody else needs the mod.

### Build at traders and spawn (`[Building] BuildInNoBuildZones`, default: on)

Vanilla forbids building, removing pieces and changing the ground around the traders (Haldor, Hildir, the Bog Witch)
and at the spawn stones. With this on they work like anywhere else: put a portal next to a trader, set up a workbench,
level the ground with the hoe or dig with the pickaxe. Most pieces still need a workbench nearby.

Everything else stays vanilla: the traders' safe zone still keeps monsters out (it is separate from the no-build zone),
the traders' own fire pits and the Bog Witch's candles can't be removed with the hammer, and the other no-build zones
(the Fader's arena, Hildir's towers, the Deep North) stay locked. The 5 m around the respawn point in the middle of
the spawn stones stay locked too, so nobody without a bed respawns inside a building. Ground you dig or level there
stays as it is after you turn this off.

Side: Client. Only the player who builds needs the mod, and what they build works for everyone. A player without the
mod can't remove those pieces with the hammer, as vanilla still blocks it for them. The server doesn't need the mod;
works on vanilla servers.

### Auto shield with one-handed weapons (`[Combat] AutoShield`, default: on)

Equipping a one-handed weapon (sword, axe, mace, knife, spear, ...) from the hotbar or the inventory while nothing is
in your left hand also takes up a shield from your hotbar: the shield you used last, or else the leftmost shield on
the hotbar. Unequipping that weapon the same way puts the shield away too. Vanilla left the shield on the hotbar and
kept it in hand after the weapon was put away.

A torch or anything else already in your left hand stays there, switching to another one-handed weapon keeps the
shield, and shields in your bag or with no durability left are never taken. Two-handed weapons, bows, tools, hiding
your weapons with R and swimming work as in vanilla.

Side: Client. Other players and the server don't need the mod.

## Install

Drop `ValheimUtilsByAi.dll` into `BepInEx/plugins` or install with a mod manager. Requires
[Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).

When both you and the server run this mod, the versions must match exactly. Otherwise Jötunn refuses the connection
and shows a window with both versions, so update the server and every player together. Players without the mod, or
servers without it, can still connect.
