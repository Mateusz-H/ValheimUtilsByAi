## Unreleased

- Farm everything: uproot wild berry bushes, mushrooms, thistle, dandelion and fiddlehead with the Cultivator for seeds and plant them on cultivated ground; planted ones regrow in `FarmEverything<Prefab>Minutes` (default 1.5x wild).

- Portal map travel: stepping into a portal opens the map with every portal pinned; click one to travel there.

- Station extension range: extensions upgrade stations from up to `ExtensionRangeMeters` away (default 10 m, vanilla 5 m) and can be placed that far.

- Comfort range: comfort pieces count from up to `ComfortRangeMeters` away (default 20 m, vanilla 10 m).
- Larger crafting station build range: config is now `StationBuildRangeMeters` (default 40 m) instead of a multiplier; the old `StationBuildRangeMultiplier` key is ignored.

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
