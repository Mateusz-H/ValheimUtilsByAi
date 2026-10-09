## Unreleased

- Fix: the kiln wood filter no longer deletes wood that other players put into a kiln you own. `KilnAllowedWood` now ignores letter case and logs a warning for names that are not kiln wood.
- Craft without roof: the fermenter, spinning wheel and beds now also work without a roof or cover.
- Fix: faster loot from dead creatures no longer makes player corpses and bosses vanish early; both keep the vanilla timing.
- Fix: switch Forsaken power anywhere only offers powers whose boss has been killed in the current world, so a character from another world or server no longer gets every power it picked there.
- Fix: auto repair at crafting stations repairs all worn items at once with a single sound, instead of piling up one sound per item.
- Fix: auto map sync no longer deletes the pins of players who are offline (after a server restart, a new character or `resetsharedmap`). The server now merges everyone's map instead of passing on the last upload, new players get the whole shared map when they join, and only a pin's owner can remove it for everyone. **Update the server too**; players on older versions keep syncing with an updated server.
- Fix: auto map sync sends your map only after you explored something or changed your own pins, instead of re-sending everything others shared, so it uses far less network and CPU. It no longer turns hidden pin types back on, no longer leaks one world's map into another world on a listen server, and skips worlds without a map and servers without the mod.
- Fix: feeding troughs could lose or duplicate items in multiplayer when an animal ate while another player was using the trough. The animal now waits at the trough until the game that holds it hands it over, which only happens while nobody has it open. **All players and the server should update**: older versions still take the trough without asking, and a trough held by an older version is never handed over, so an animal gives up on it after 30 s.
- Fix: Lox and Moose now eat from feeding troughs. Animals no longer eat from a trough through a gate that closed on their way and give up after 30 s if they can't reach it, summoned creatures leave the troughs alone, and `FeedingTroughRange` is limited to 1-50 m.

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
