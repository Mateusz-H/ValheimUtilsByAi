# Review checklist

Every item is a question about a concrete situation. Answer it from the feature's code and the vanilla code in
`obj/decompiled/`, not from what the feature is meant to do. A finding is a scenario: steps a player can take, the
wrong outcome, the line in the feature and the vanilla lines that make it happen.

## 1. Forcing what vanilla never allows

The 0.0.13 soft-lock: PortalMapTravel opened the large map while the inventory was open. `Minimap.Update` ignores
input while `InventoryGui.IsVisible()` and `InventoryGui.Update` ignores input while `Minimap.IsOpen()`, so neither
could be closed. Vanilla never reaches that state, so nobody had checked it.

- Does the feature open, close or show UI, move or teleport the player, change input or start an action itself
  (not as a reaction to the player's own click)? If so, find the vanilla code that normally does it and list the
  conditions vanilla checks first. Each condition the feature skips is a state to test.
- UI that blocks other UI: `InventoryGui`, `Minimap` (large), `Menu`, `Chat` focus, `Console`, `TextInput` (signs,
  portal tags), `TextViewer`, `StoreGui` (trader), container open, `Hud` piece selection and radial, split dialog,
  `UnifiedPopup`. Grep each `IsVisible()`/`IsOpen()` to see who blocks whom.
- Player states: dead, teleporting, in cutscene or intro, sleeping, sitting, attached (chair, ship, cart, saddle),
  in a dungeon or other interior, in place mode, gamepad active, encumbered, debug fly or free cam.
- For each forced change: can the player still undo it with the normal keys in every state above?

## 2. Interrupted flows

List every multi-step action the feature takes part in (a craft, a teleport choice, a hand-over between chests,
an open door) and every way it can end before it finishes:

- teleport or portal (including another feature opening the map), death, `InventoryGui.Hide`, walking out of range,
  the station, chest or piece destroyed or unloaded, recipe switched, the toggle turned off at runtime.
- logout, disconnect from the server, Alt+F4 (`Game.OnApplicationQuit` -> `Shutdown` -> `SavePlayerProfile`),
  process kill or crash (nothing saved since the last autosave), server restart.

For each: where does every item and every piece of state end up? Is anything left half-done: a door left open, a
chest left `inUse`, an RPC answer swallowed, a static list never cleared?

## 3. Items

- Only the ZDO owner's edits are saved. Every place that removes an item from one inventory and adds it to another:
  does this peer own both, can ownership change in between, can another peer edit the same inventory meanwhile?
- Is the check and the consume one synchronous step, or can a network message or a frame land between them?
- Profile and world are saved at different times. Can an item exist in neither (lost on crash) or in both (duped)
  at the moment either one is saved?
- Inventory full, chest full, stack limits, world level and quality filters, items with the same name but a
  different prefab.
- Items on the ground: only the owner of the drop may take it.

## 4. Multiplayer

- Which peer runs each patch: the local player's client, the ZDO owner, the server? Is that the peer whose change
  is saved and sent?
- Mixed install: the server or some players without the mod. What does each of them see, can anything desync or be
  overwritten by a vanilla peer?
- RPCs: `InvokeRPC` to owner 0 goes to every peer; RPC responses the feature suppresses; RPCs that vanilla owners
  reject.
- Dedicated server: `Player.m_localPlayer`, `Minimap.instance`, `InventoryGui.instance`, `Hud.instance` are null.

## 5. Config and game state

- The toggle and every other setting work when changed in game, both ways.
- Prefab fields, shared statics, `ObjectDB` or `ZNetScene` changed by the feature: restored when the toggle goes off?
  Restored when the patched method throws (a postfix does not run on an exception; use a `Finalizer`)?
- `__state` saved in a prefix and restored only in a postfix.

## 6. Exceptions and runtime

- Null and destroyed objects: `ZNetView.IsValid()`, Unity `== null` on destroyed objects, `GetComponent` on prefabs
  of other mods, `ZoneSystem.instance` and friends before the world loads.
- No tuples: the game's runtime has no `System.ValueTuple` (CLAUDE.md).
- An exception in a patch of a per-frame method spams the log every frame and may stop vanilla code after it.

## 7. Cost

- Work in per-frame or per-object update methods: physics overlaps, `FindObjectsOfType`, LINQ and allocations,
  loops over every ZDO or every chest. How does it scale with 100 chests, 50 portals, 10 players?
