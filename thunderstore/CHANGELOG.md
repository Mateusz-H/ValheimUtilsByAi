## Unreleased

## 0.0.22

- Build at traders and spawn: place portals and other pieces and use the hoe and pickaxe next to Haldor, Hildir, the Bog Witch and the spawn stones; the traders' safe zone still keeps monsters out (`[Building] BuildInNoBuildZones`).
- Quick stack repairs at nearby stations: quick stacking (P, the Q button or Quick stack in a chest) also repairs worn items at crafting stations within `[Crafting] AutoRepairNearbyMeters` (`[Inventory] QuickStackRepair`).

## 0.0.21

- Higher network bandwidth: the server and players send world updates at up to 400 KB/s instead of vanilla's 150 KB/s (`[Network] NetworkSendRate`), so a base full of animals no longer makes creatures jump around.

## 0.0.20

- Plant spacing: a sapling or seed can't be planted within its grow radius of another sapling, grown crop or planted mushroom or bush, nor next to a bigger sapling it would stunt, also in PlantEasily grids; vanilla let carrots go right on top of mushrooms.
- Farm everything: every vanilla sapling (crops, Jotun puffs, magecap, trees, vines) gets a grow time setting in minutes, `FarmEverything<sapling>Minutes`, by default half of vanilla (carrots 33 to 41 minutes instead of 67 to 83).
- Farm everything: planted bushes, mushrooms, thistle, dandelion and fiddlehead regrow in half the wild time by default (120 and 150 minutes, was 360 and 450). An existing config keeps its values; delete the `FarmEverything<Prefab>Minutes` lines to get the new defaults.

## 0.0.19

- Server config sync: on a server running the mod, every setting except keys and the main-menu mod list comes from the server's config for all players; your own file applies again on disconnect, in single player and on servers without the mod.

- Liked items: like a stack with Alt + right click instead of Alt + left click, which MyLittleUI uses to post the item in chat.
- Sort items, quick stack and store all leave the rows other mods add below your inventory alone, such as Better Archery's quiver: sorting no longer empties the quiver or hides items in its hidden row, and quick stack no longer sends quiver arrows to chests.
- Trash can: equipped gear can't be trashed; unequip it first. Trashing an equipped Better Archery quiver hid the arrows in it.
- Farm everything: removing a grown planted bush with the Hammer drops its harvest too, not only its seed.
- AutoStorage: a cooking station gets no new raw food while none of its output chests can be used, so food no longer burns while the output chest is open or not loaded.
- Feeding trough: animals use a trough again after the player who had it open crashed; they ignored it until someone opened and closed it.

## 0.0.18

- Larger crafting station build range: the default is now 100 m (was 40 m) and the maximum 128 m (was 64 m). An existing config keeps its value; set `StationBuildRangeMeters` to change it.
- Bigger chests: chests get twice their vanilla rows (reinforced chest 6x8, black metal chest 8x8, ...); the multiplier and the list of chests are configurable, and turning it off or removing the mod keeps every filled row.
- Auto repair at nearby stations: opening a crafting station also repairs what stations within `AutoRepairNearbyMeters` (default 15 m) can repair, such as the forge next to your workbench.
- Liked items: Alt + left click likes a stack. In your inventory quick stack, store all, sort and trash leave it alone; in a chest crafting from chests, AutoStorage and restock always leave its last item. Likes survive death.
- Quick stack: P or a button puts stackable items into nearby chests that already hold them, nearest first, merges partial stacks there and reports how many went. Replaces vanilla's Place stacks and hold-E stacking.
- Store all: a button in chests moves everything except the hotbar, equipped and liked items into the open chest.
- Restock: L or a button fills partial stacks of ammo, food and potions from nearby chests.
- Sort items: O or a button sorts the inventory or the open chest by kind and name and merges partial stacks; the hotbar and liked items keep their slots.
- Trash can: destroy a dragged item with the trash can in the inventory panel or the Delete key, after a confirmation.
- Craft and build from nearby containers: a liked chest stack keeps its last item. A chest the game still marks as open after its user crashed is asked for again instead of being skipped (its owner still refuses while it really is open).

## 0.0.17

- Mod versions in main menu: the main menu shows this mod's version and the number of installed mods, and a Mods button lists every installed mod with its version.
- Version check: a client and a server that both run this mod must now run the same version, otherwise Jötunn refuses the connection and shows both versions.

## 0.0.16

- Fix: a chest another player opened and closed no longer keeps showing an open lid while you craft or build near it, or while an AutoStorage piece uses it.

## 0.0.15

- Fix: craft without roof, station extension range, station build range and breeding limit restore the vanilla value even when the game method they adjust fails (for example because of another mod), so turning them off always takes effect.
- Fix: portal map travel closes the map once you move more than 5 m from the portal, so you can no longer step into a portal, walk off and travel from anywhere later.
- Fix: craft and build from nearby containers no longer loses the materials of a craft that is running when the connection to the server drops; they go into your inventory.
- Fix: AutoStorage no longer sweeps items a player dropped on purpose next to a linked piece into its output chest.
- Fix: AutoStorage links made on a piece another player's game runs (for example a smelter they stand next to) no longer silently disappear.
- Fix: auto-opening doors close behind you when you die, go through a portal or log out next to them, and when the feature is turned off.

## 0.0.14

- Fix: being pushed into a portal with the inventory or a crafting station open no longer locks the screen; the inventory closes and the map opens.

## 0.0.13

- Auto-opening doors: player-built doors and gates open as you walk up and close behind you; dungeon doors stay manual.

- Craft and build from nearby containers: a craft now takes its materials out of the chests the moment you click Craft and puts back what is left if you cancel, so other players can open those chests during your craft instead of getting "Someone else is using this".

- Fix: AutoStorage no longer loses items (honey, bars, coal, ...) when a player opens an output chest right after a piece put something into it.

- AutoStorage: clicking with a link tool while aiming at nothing in reach now says so instead of doing nothing.

- Higher breeding limit: tamed animals keep breeding until 20 of their kind stand within 10 m instead of the vanilla handful. Set the number with `[World] BreedingLimitCount`, turn it off with `[World] BreedingLimit`.

- Carts and ships on map: every cart and ship in the world shows on the map and minimap with its build-menu icon. Turn it off with `[World] VehiclesOnMap`.

- Portal map travel: every portal is now shown on the large map (M) all the time, not only after stepping into a portal. These pins are for viewing only. Turn it off with `[World] PortalsOnMap`.

## 0.0.12

- Portal map travel: portal pins have their own icon, pulse slowly and are always drawn above other pins. Hiding a pin type in the map filter no longer hides them, and choosing a portal no longer turns a hidden filter back on.
- Portal map travel: the portal and stone portal cost 1.5x the vanilla materials (30 fine wood, 15 greydwarf eyes, 3 surtling cores for the portal). Change it with `[World] PortalCostMultiplier`; 1 keeps vanilla prices.

## 0.0.11

- Fix: the kiln wood filter no longer deletes wood that other players put into a kiln you own. `KilnAllowedWood` now ignores letter case and logs a warning for names that are not kiln wood.
- Craft without roof: the fermenter, spinning wheel and beds now also work without a roof or cover.
- Fix: faster loot from dead creatures no longer makes player corpses and bosses vanish early; both keep the vanilla timing.
- Fix: switch Forsaken power anywhere only offers powers whose boss has been killed in the current world, so a character from another world or server no longer gets every power it picked there.
- Fix: auto repair at crafting stations repairs all worn items at once with a single sound, instead of piling up one sound per item.
- Fix: auto map sync no longer deletes the pins of players who are offline (after a server restart, a new character or `resetsharedmap`). The server now merges everyone's map instead of passing on the last upload, new players get the whole shared map when they join, and only a pin's owner can remove it for everyone. **Update the server too**; players on older versions keep syncing with an updated server.
- Fix: auto map sync sends your map only after you explored something or changed your own pins, instead of re-sending everything others shared, so it uses far less network and CPU. It no longer turns hidden pin types back on, no longer leaks one world's map into another world on a listen server, and skips worlds without a map and servers without the mod.
- Fix: feeding troughs could lose or duplicate items in multiplayer when an animal ate while another player was using the trough. The animal now waits at the trough until the game that holds it hands it over, which only happens while nobody has it open. **All players and the server should update**: older versions still take the trough without asking, and a trough held by an older version is never handed over, so an animal gives up on it after 30 s.
- Fix: Lox and Moose now eat from feeding troughs. Animals no longer eat from a trough through a gate that closed on their way and give up after 30 s if they can't reach it, summoned creatures leave the troughs alone, and `FeedingTroughRange` is limited to 1-50 m.
- Fix: craft from containers no longer makes nearby chests flip back and forth between players while two of you have the inventory open or are building, which made crafts fail without a word, the amounts flicker and chests sometimes refuse to open. Your game now asks for a chest only when you click Craft or pick a build piece, and only for chests holding its materials. A chest another player is crafting from says "Chest busy, try again" and takes nothing.
- Fix: craft from containers ignores feeding troughs, so crafting and planting no longer use up animal feed, and one-ingredient recipes no longer show chest amounts they can't use. Nobody else needs the mod anymore: chests held by players without it are used too. **Everyone should update**: requests from 0.0.9 and 0.0.10 to an updated player are ignored, so an older game only uses chests it already holds.
- Auto storage: **AutoStorage Link is split into AutoStorage Link input and AutoStorage Link output** (Hammer > Misc). Link input picks the chests a piece takes its ore, wood, fuel, raw food or mead base from, Link output the chests it puts its products into; a piece can have several of each, and one chest can be both. **All existing links are removed: link your pieces again with the new tools.** A chest linked both ways also feeds a piece its own products, so a windmill grinds its oats on into oat flour; use separate chests to keep the oats.
- Auto storage: the frigid kiln, hot tub, birds' nest and frost foundry work with it too (they already did; now documented).
- Fix: auto storage respects wards and private chests. You can only link or unlink a chest you could open and only pick a piece inside a ward that lets you in, so nobody can empty your chests or take your honey through a link anymore.
- Fix: auto storage no longer stops using a chest for good after a player crashed or lost connection with it open, and no longer takes a chest away from a player who is crafting from it.
- Fix: auto storage no longer duplicates products in new game+ worlds (world level above 0) when an output chest is nearly full.
- Auto storage: **everyone should update, players and server.** Older versions don't know the new links, so pieces they control don't run them, and their old links are removed as soon as an updated game controls the piece.

- Farm everything: uproot wild berry bushes, mushrooms, thistle, dandelion and fiddlehead with the Cultivator for seeds and plant them on cultivated ground; planted ones regrow in `FarmEverything<Prefab>Minutes` (default 1.5x wild, 1-10000). Bare planted mushrooms, thistle, dandelion and fiddlehead show as a small plant. Uprooting costs stamina and Cultivator durability like hammer removal, and a plant that still has its harvest drops it first.
- Plant growth timer: hovering a sapling or a bare planted bush shows how long until it grows.

- Portal map travel: stepping into a portal opens the map with every portal pinned; click one to travel there (mouse only; vanilla item restrictions and portal world modifiers still apply).

- Station extension range: extensions upgrade stations from up to `ExtensionRangeMeters` away (default 10 m; vanilla 2-5 m depending on the extension, never shrunk) and can be placed that far.

- Comfort range: comfort pieces count from up to `ComfortRangeMeters` away (default 20 m, max 64 m, vanilla 10 m); resting still needs a burning fire nearby.
- Larger crafting station build range: config is now `StationBuildRangeMeters` (default 40 m, max 64 m, never below a station's vanilla range: 20 m, artisan table 40 m) instead of a multiplier; the old `StationBuildRangeMultiplier` key is ignored. At the default the artisan table keeps its vanilla 40 m (0.0.10 doubled it to 80 m).

## 0.0.10

- Fix: craft from containers and AutoStorage Link ignored newly built chests (and some older ones at random).
- Craft from containers: requirement amounts show have/need (inventory plus nearby chests), e.g. `25/10`.

## 0.0.9

- Craft and build from nearby containers: crafting, upgrading and building use materials from chests within 15 m (`CraftFromContainersRadius`), multiplayer safe.
- Auto storage: AutoStorage Link now links to any chest you built. The custom AutoStorage chest is removed; **empty it before updating**, placed ones disappear with their contents.
- Auto storage: a linked piece now asks the chest owner to hand the chest over instead of taking it, so two players never write to one chest at once.

## 0.0.8

- Larger crafting station build range: build up to 2x farther from workbenches and other stations (configurable).
- Faster loot from dead creatures: bodies vanish and drop loot after half a second (configurable).

## 0.0.7

- Auto storage: linking moved into the vanilla Hammer as AutoStorage Link (Misc tab); the AutoStorage Hammer item is removed and refunded as 3 Wood and 1 Surtling Core.

## 0.0.6

- Auto repair at crafting stations: opening a station repairs all items it can repair, no clicking.

## 0.0.5

- Switch Forsaken power anywhere: the button text is always in English, whatever the game language.

## 0.0.4

- Switch Forsaken power anywhere: a button in the Escape menu cycles through the powers you already picked at a guardian stone, without touching the cooldown.

- Auto storage: once a minute linked pieces collect their own products lying within 5 m into the chest (`AutoStorageCollectRadius`).
- Auto storage: the chest has 10 slots instead of 32; items in existing chests are kept.
- Auto storage: chest recipe is now 10 Fine Wood, 5 Greydwarf Eyes, 5 Bronze (no Surtling Cores).
- Feeding trough: craftable chest that hungry tamed animals nearby eat from (toggleable).
- Auto map sync: all players share explored map and pins through the server, no cartography table needed (toggleable).

## 0.0.3

- Auto storage: craftable AutoStorage chest and AutoStorage Hammer; linked kilns, smelters, beehives, sap extractors, cooking stations and fermenters pull inputs from and push products into the chest (toggleable). Adds Jötunn as a dependency.

## 0.0.2

- Kiln wood filter: the charcoal kiln only accepts configured wood types, default plain wood only (toggleable).

## 0.0.1

- Craft without roof: crafting stations no longer require a roof or cover (toggleable).
