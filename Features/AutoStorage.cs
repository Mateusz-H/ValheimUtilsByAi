using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// "AutoStorage Link input" and "AutoStorage Link output" hammer tools. They link a processing piece (smelter family,
// beehive, sap collector, cooking station, fermenter) to player-built chests: the piece's ZDO owner pulls inputs from
// its input chests and pushes products into its output chests on the piece's own vanilla update tick.
//
// Links are stored on the piece as the chests' world positions, one list per direction: ZDOIDs are reassigned on
// every world load, so they cannot be persisted. Chest edits only stick on the chest's ZDO owner, so the piece owner
// asks the chest's owner to hand it over and skips that chest until it owns it, and while a player has it open.
internal static class AutoStorage
{
    private enum Direction { Input, Output }

    private const string HammerPrefab = "AutoStorageHammer";
    private const string HammerToken = "$item_autostoragehammer";
    private const float ChestMatchRadius = 0.5f;

    private static readonly int InputChestsKey = "mh_AutoStorageInputs".GetStableHashCode();
    private static readonly int OutputChestsKey = "mh_AutoStorageOutputs".GetStableHashCode();
    // The single two-way link of 0.0.10 and earlier. Cleared, not migrated: inputs and outputs are linked separately now.
    private static readonly int LegacyLinkKey = "mh_AutoStorageLink".GetStableHashCode();

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _maxLinkDistance = null!;
    private static ConfigEntry<float> _collectRadius = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "AutoStorage",
            true,
            "Adds the AutoStorage Link input and AutoStorage Link output hammer tools. A processing piece (kiln, " +
            "smelter, blast furnace, spinning wheel, windmill, eitr refinery, frigid kiln, hot tub, beehive, birds' " +
            "nest, sap extractor, cooking stations, frost foundry, fermenter) takes its inputs from the chests linked " +
            "as its input and puts its products into the chests linked as its output; one chest can be both. " +
            "When this is off the automation and linking stop; links are kept. In multiplayer the setting of the game " +
            "that controls the piece applies.");
        _maxLinkDistance = config.Bind(
            "Crafting",
            "AutoStorageMaxLinkDistance",
            100f,
            "Maximum distance in meters between a piece and a chest linked to it. " +
            "Pieces farther than roughly 64 m from any player are not simulated by the game, so a chest beyond " +
            "that distance from the piece may simply not be loaded.");
        _collectRadius = config.Bind(
            "Crafting",
            "AutoStorageCollectRadius",
            5f,
            "Once a minute, items of the kinds a linked piece makes lying on the ground within this many meters of it " +
            "(for example because its output chests were full or open) are moved into its output chests. " +
            "0 turns this off.");

        PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefabs;
    }

    private static Piece? _inputTool;
    private static Piece? _outputTool;

    private static void RegisterPrefabs()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefabs;

        _inputTool = AddLinkTool("AutoStorageLinkInput", "piece_autostoragelinkinput", "AutoStorage Link input", "CopperOre",
            "Click a processing piece, then a chest it should take its inputs from (ore, wood, fuel, raw food, " +
            "mead base). Click the pair again to unlink. Click a chest with nothing selected to unlink it from every " +
            "piece's inputs.");
        _outputTool = AddLinkTool("AutoStorageLinkOutput", "piece_autostoragelinkoutput", "AutoStorage Link output", "Copper",
            "Click a processing piece, then a chest it should put its products into. Click the pair again to unlink. " +
            "Click a chest with nothing selected to unlink it from every piece's outputs.");

        // Replaced by the link tools: kept without a recipe so saved copies still load and get refunded.
        var hammer = new CustomItem(HammerPrefab, "Hammer", new ItemConfig
        {
            Name = HammerToken,
            Description = "$item_autostoragehammer_description",
        });
        var shared = hammer.ItemDrop.m_itemData.m_shared;
        shared.m_buildPieces = null;
        shared.m_useDurability = false;
        ItemManager.Instance.AddItem(hammer);

        var localization = LocalizationManager.Instance.GetLocalization();
        localization.AddTranslation(HammerToken.TrimStart('$'), "AutoStorage Hammer");
        localization.AddTranslation("item_autostoragehammer_description",
            "Replaced by AutoStorage Link input and AutoStorage Link output in the Hammer's Misc tab. Refunded automatically.");
    }

    // A normal Misc piece cloned from the Repair tool. Repair/remove flags would make the build menu treat it as a
    // second special button; the ghost and the click are handled by the patches below. Each tool shows an item's icon
    // (ore in, bar out) so the two can be told apart; the Repair icon stays if the item is missing.
    private static Piece AddLinkTool(string prefabName, string token, string name, string iconItem, string description)
    {
        var tool = new CustomPiece(prefabName, "piece_repair", new PieceConfig
        {
            Name = "$" + token,
            Description = "$" + token + "_description",
            PieceTable = PieceTables.Hammer,
            Category = PieceCategories.Misc,
            Usage = new[] { PieceUsages.Misc },
            Icon = ItemIcon(iconItem),
        });
        tool.Piece.m_repairPiece = false;
        tool.Piece.m_removePiece = false;
        tool.Piece.m_canRotate = false;
        tool.Piece.m_resources = Array.Empty<Piece.Requirement>();
        tool.Piece.m_craftingStation = null;
        PieceManager.Instance.AddPiece(tool);

        var localization = LocalizationManager.Instance.GetLocalization();
        localization.AddTranslation(token, name);
        localization.AddTranslation(token + "_description", description);
        return tool.Piece;
    }

    private static Sprite? ItemIcon(string itemPrefab)
    {
        var prefab = PrefabManager.Instance.GetPrefab(itemPrefab);
        var item = prefab == null ? null : prefab.GetComponent<ItemDrop>();
        return item == null ? null : item.m_itemData.GetIcon();
    }

    // ---------------------------------------------------------------- chest registry and link resolution

    private const string RequestOwnRpc = "mh_AutoStorage_RequestOwn";

    private static readonly List<Container> Chests = new();

    // Player-built chests only: carts, ship storage, tombstones, the obliterator and loot crates are skipped.
    // Placement ghosts have no inventory.
    [HarmonyPatch(typeof(Container), "Awake")]
    private static class RegisterChest
    {
        private static void Postfix(Container __instance)
        {
            if (__instance.GetInventory() == null) return;
            var view = ContainerView(__instance);
            if (view == null || !view.IsValid()) return;
            // "Built by any player" (not world-generated) is checked on use: the creator is read in Piece.Awake, which
            // may run after this, and a newly built chest only gets its creator after Awake.
            if (__instance.GetComponent<Piece>() == null) return;
            if (__instance.m_wagon != null || __instance.m_rootObjectOverride != null || __instance.m_autoDestroyEmpty) return;
            if (__instance.GetComponentInParent<Ship>() != null || __instance.GetComponentInParent<Incinerator>() != null) return;

            var chest = __instance;
            view.Register(RequestOwnRpc, sender => RPC_RequestOwn(chest, sender));
            Chests.Add(chest);
        }
    }

    private static readonly AccessTools.FieldRef<InventoryGui, Container?> CurrentContainer =
        AccessTools.FieldRefAccess<InventoryGui, Container?>("m_currentContainer");

    // Runs on the chest's owner, which hands the chest over only while nobody has it open, like vanilla
    // Container.RPC_RequestOpen. Only the owner hands over, so two peers never edit the chest at once. A chest this
    // player is opening counts as open too: it is marked in use only by the first InventoryGui frame after the open
    // answer, and handing it over in between hid its panel.
    private static void RPC_RequestOwn(Container chest, long sender)
    {
        var view = ContainerView(chest);
        if (!view.IsValid() || !view.IsOwner() || chest.IsInUse()) return;
        var gui = InventoryGui.instance;
        if (gui != null && CurrentContainer(gui) == chest) return;
        ZDOMan.instance.ForceSendZDO(sender, view.GetZDO().m_uid);
        view.GetZDO().SetOwner(sender);
    }

    // A chest reloads its contents from the ZDO only once a second (Container.CheckForChanges) and never while open, so
    // a player opening a chest that a linked piece filled in the last second saw it without those items, and their
    // first move saved that view over them. Reloading right before it counts as open closes the gap. Only on the owner,
    // whose own edits are always saved at once, so nothing unsaved is dropped. Not gated on the config: it only makes
    // the chest show what it holds.
    [HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
    private static class FreshContentsOnOpen
    {
        private static void Prefix(Container __instance, bool inUse)
        {
            if (inUse && !__instance.IsInUse() && __instance.IsOwner()) ContainerLoad(__instance);
        }
    }

    private static readonly Dictionary<Container, float> NextRequest = new();

    private static bool RequestDue(Container chest) => !NextRequest.TryGetValue(chest, out var next) || Time.time >= next;

    private static void RequestOwnership(Container chest, ZNetView view)
    {
        var owner = view.GetZDO().GetOwner();
        // No owner: the server assigns one within a few seconds.
        if (owner == 0 || !RequestDue(chest)) return;
        NextRequest[chest] = Time.time + 5f;
        view.InvokeRPC(owner, RequestOwnRpc);
    }

    private static Container? FindChestAt(Vector3 position)
    {
        for (var i = Chests.Count - 1; i >= 0; i--)
        {
            var chest = Chests[i];
            if (chest == null) { Chests.RemoveAt(i); NextRequest.Remove(chest!); continue; }
            if (Vector3.Distance(chest.transform.position, position) <= ChestMatchRadius) return chest;
        }
        return null;
    }

    private static int ChestsKey(Direction direction) => direction == Direction.Input ? InputChestsKey : OutputChestsKey;

    // Null when the piece has no chests in this direction.
    private static List<Vector3>? ReadChestPositions(ZDO zdo, Direction direction)
    {
        var bytes = zdo.GetByteArray(ChestsKey(direction));
        if (bytes == null) return null;
        var package = new ZPackage(bytes);
        var count = package.ReadInt();
        var positions = new List<Vector3>(count);
        for (var i = 0; i < count; i++) positions.Add(package.ReadVector3());
        return positions;
    }

    // Set even when empty, never removed: a removed ZDO key is not sent to the other peers.
    private static void WriteChestPositions(ZDO zdo, Direction direction, List<Vector3> positions)
    {
        var package = new ZPackage();
        package.Write(positions.Count);
        foreach (var position in positions) package.Write(position);
        zdo.Set(ChestsKey(direction), package.GetArray());
    }

    private const string SetLinkRpc = "mh_AutoStorage_SetLink";

    [HarmonyPatch(typeof(ZNet), "Awake")]
    private static class RegisterSetLinkRpc
    {
        private static void Postfix() =>
            ZRoutedRpc.instance.Register<ZDOID, int, bool, Vector3>(SetLinkRpc, RPC_SetLink);
    }

    // A link edit goes to the piece's owner instead of claiming the piece: the owner's copy has a newer data revision
    // (the piece's update tick writes its ZDO every second), so ZDOMan drops a claimer's older data and the edit was
    // lost; when the claimer's copy won instead, what the owner had just queued was lost. The owner applies the edit
    // to its current list, so two quick edits from different players both stick.
    private static void SendLinkEdit(ZNetView piece, Direction direction, bool add, Vector3 chestPosition)
    {
        var zdo = piece.GetZDO();
        // No owner: nobody simulates the piece, so claiming it loses nothing.
        if (zdo.GetOwner() == 0L) piece.ClaimOwnership();
        ZRoutedRpc.instance.InvokeRoutedRPC(zdo.GetOwner(), SetLinkRpc, zdo.m_uid, (int)direction, add, chestPosition);
    }

    // Dropped when ownership moved on while the edit was on its way; the player sees the link missing and clicks again.
    private static void RPC_SetLink(long sender, ZDOID pieceId, int direction, bool add, Vector3 chestPosition)
    {
        var zdo = ZDOMan.instance.GetZDO(pieceId);
        if (zdo == null || !zdo.IsOwner()) return;
        var positions = ReadChestPositions(zdo, (Direction)direction) ?? new List<Vector3>();
        var removed = RemoveChest(positions, chestPosition);
        if (add) positions.Add(chestPosition);
        if (add || removed) WriteChestPositions(zdo, (Direction)direction, positions);
    }

    private static bool RemoveChest(List<Vector3> positions, Vector3 chestPosition) =>
        positions.RemoveAll(position => Vector3.Distance(position, chestPosition) <= ChestMatchRadius) > 0;

    private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
        AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
    private static readonly Func<Container, bool> ContainerLoad =
        AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));

    // Inventories of the piece's chests in this direction that this peer can edit right now, in link order; none while
    // the feature is off or another peer simulates the piece. A chest that is missing, too far or open is skipped. One
    // owned by another peer is skipped too, and asked for only when wanted says the piece has work for it, judged from
    // the chest's contents as last synced to this peer: an idle piece never pulls chests around, so two pieces run by
    // different peers only pass a shared chest back and forth while both have work for it, at most every 5 s. Items a
    // player puts into a chest reach every peer with the chest, so the piece asks for it on its next tick. Only the
    // owner's edits are saved by Container.OnContainerChanged. Open means IsInUse on the owner, not the s_inUse ZDO
    // flag: that flag stays set after a player crashes with the chest open, and the owner refuses the hand-over while
    // the chest really is open.
    private static IReadOnlyList<Inventory> LinkedInventories(ZNetView pieceView, Direction direction, Func<Inventory, bool> wanted)
    {
        var positions = LinkedPositions(pieceView, direction);
        if (positions == null) return Array.Empty<Inventory>();

        var inventories = new List<Inventory>(positions.Count);
        foreach (var position in positions)
        {
            if (Vector3.Distance(position, pieceView.transform.position) > _maxLinkDistance.Value) continue;
            var chest = FindChestAt(position);
            if (chest == null || chest.IsInUse()) continue;
            var chestView = ContainerView(chest);
            if (chestView == null || !chestView.IsValid()) continue;
            if (chestView.IsOwner())
            {
                CraftFromContainers.LoadChest(chest);
                inventories.Add(chest.GetInventory());
            }
            else if (wanted != Never && RequestDue(chest))
            {
                CraftFromContainers.LoadChest(chest);
                if (wanted(chest.GetInventory())) RequestOwnership(chest, chestView);
            }
        }
        return inventories;
    }

    private static readonly Func<Inventory, bool> Never = _ => false;

    // Null while the feature is off, another peer simulates the piece or it has no chests in this direction.
    private static List<Vector3>? LinkedPositions(ZNetView pieceView, Direction direction)
    {
        if (!_enabled.Value || !pieceView.IsValid() || !pieceView.IsOwner()) return null;
        var zdo = pieceView.GetZDO();
        if (zdo.GetVec3(LegacyLinkKey, Vector3.zero) != Vector3.zero) zdo.Set(LegacyLinkKey, Vector3.zero);
        var positions = ReadChestPositions(zdo, direction);
        return positions == null || positions.Count == 0 ? null : positions;
    }

    // An output chest worth asking for: Store would put this product into it. A chest without room for it is never
    // asked for, so pieces that can't store anything don't pull it around.
    private static bool CanStore(Inventory inventory, GameObject? prefab, int amount) =>
        prefab != null && amount > 0 && inventory.CanAddItem(Product(prefab, amount));

    // A chest worth asking for to feed the piece: it holds fuel the piece lacks, or something FindCookableItem would take
    // while the piece has room. The last unit of a liked stack doesn't count, so a marker never pulls a chest over.
    private static bool HasInput<TPiece>(Inventory inventory, ItemDrop? fuelItem, bool needFuel, bool needItems, TPiece piece,
        Func<TPiece, Inventory, ItemDrop.ItemData?> findCookable)
    {
        if (needFuel && fuelItem != null && LikedItems.CountAvailable(inventory, fuelItem.m_itemData.m_shared.m_name) > 0) return true;
        return needItems && LikedItems.SkippingReserved(() => findCookable(piece, inventory)) != null;
    }

    private static bool TakeOne(IReadOnlyList<Inventory> inputs, ItemDrop item)
    {
        var name = item.m_itemData.m_shared.m_name;
        foreach (var inventory in inputs)
        {
            if (LikedItems.RemoveAvailable(inventory, name, 1) == 1) return true;
        }
        return false;
    }

    // Removes and returns the first item the piece's vanilla FindCookableItem picks from the input chests, in link
    // order. Going through FindCookableItem keeps KilnWoodFilter's narrowing of the kiln's list. The last unit of a
    // liked stack stays: FindCookableItem looks items up with Inventory.GetItem, which skips it in this scope.
    private static ItemDrop.ItemData? TakeCookable<TPiece>(IReadOnlyList<Inventory> inputs, TPiece piece,
        Func<TPiece, Inventory, ItemDrop.ItemData?> findCookable)
    {
        foreach (var inventory in inputs)
        {
            var item = LikedItems.SkippingReserved(() => findCookable(piece, inventory));
            if (item != null && inventory.RemoveOneItem(item)) return item;
        }
        return null;
    }

    // All or nothing, into the first output chest with room for the whole amount. One item is used for both the room
    // check and the add: CanAddItem(GameObject) checks room at the prefab's world level 0 while AddItem(GameObject)
    // adds at the current one, and a partial add returns false after adding, so in new game+ worlds the callers
    // delivered the products a second time.
    private static bool Store(IReadOnlyList<Inventory> outputs, GameObject prefab, int amount)
    {
        if (amount <= 0) return false;
        var item = Product(prefab, amount);
        foreach (var inventory in outputs)
        {
            if (inventory.CanAddItem(item)) return inventory.AddItem(item);
        }
        return false;
    }

    private static ItemDrop.ItemData Product(GameObject prefab, int amount)
    {
        var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_dropPrefab = prefab;
        item.m_stack = amount;
        item.m_worldLevel = (byte)Game.m_worldLevel;
        return item;
    }

    // ---------------------------------------------------------------- collecting stray products

    private const float CollectInterval = 60f;

    private static readonly Dictionary<ZNetView, float> NextCollect = new();
    private static readonly Collider[] ItemHits = new Collider[128];
    private static int _itemMask;

    private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> ItemView =
        AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");

    // Cheap per-tick gate; the product list is only built by the caller when this says a sweep is due.
    private static bool CollectDue(ZNetView pieceView)
    {
        if (_collectRadius.Value <= 0f) return false;
        var now = Time.time;
        if (NextCollect.TryGetValue(pieceView, out var next)) { if (now < next) return false; }
        else foreach (var stale in NextCollect.Keys.Where(view => view == null).ToList()) NextCollect.Remove(stale);
        NextCollect[pieceView] = now + CollectInterval;
        return true;
    }

    // Once a minute per piece with output chests, move items of the kinds it makes lying within the collect radius
    // into those chests. Products end up on the ground whenever the chests were full, open or unloaded at the moment
    // they came out. An output chest is only asked for when a stray fits into it, and a drop only when an owned output
    // chest has room for it, so two pieces never pass a drop or a full chest back and forth. After asking, one sweep
    // follows 2 s later, once the hand-over arrived, instead of a minute later.
    //
    // No duplication: a drop is only touched while this peer owns its ZDO. Ownership is handed over only by the
    // current owner (ItemDrop.RPC_RequestOwn), so exactly one peer can take it, the same rule vanilla pickup relies
    // on; otherwise we request ownership and retry on the next sweep. Each chest's count is measured before and after
    // adding, and the drop loses exactly what went in: destroyed when the whole stack fit, shrunk when only part did.
    private const float CollectRetry = 2f;
    private static readonly HashSet<ZNetView> CollectRetried = new();

    private static void CollectStrays(ZNetView pieceView, IEnumerable<ItemDrop?> products)
    {
        if (LinkedPositions(pieceView, Direction.Output) == null || !CollectDue(pieceView)) return;
        var asked = SweepStrays(pieceView, products);
        if (asked && CollectRetried.Add(pieceView)) NextCollect[pieceView] = Time.time + CollectRetry;
        else CollectRetried.Remove(pieceView);
    }

    // True when it asked for a chest or a drop.
    private static bool SweepStrays(ZNetView pieceView, IEnumerable<ItemDrop?> products)
    {
        var names = new HashSet<string>(products.Where(product => product != null).Select(product => product!.m_itemData.m_shared.m_name));
        if (names.Count == 0) return false;
        if (_itemMask == 0) _itemMask = LayerMask.GetMask("item");
        var hits = Physics.OverlapSphereNonAlloc(pieceView.transform.position, _collectRadius.Value, ItemHits, _itemMask);
        var strays = new List<ItemDrop>();
        for (var i = 0; i < hits; i++)
        {
            var body = ItemHits[i].attachedRigidbody;
            var drop = body == null ? null : body.GetComponent<ItemDrop>();
            if (drop == null || drop.IsPiece()) continue;
            var view = ItemView(drop);
            if (view == null || !view.IsValid() || !names.Contains(drop.m_itemData.m_shared.m_name)) continue;
            // Something a player had in their inventory and dropped on purpose (handing it over, sorting) stays put;
            // fresh products have never been picked up. m_pickedUp is saved in the drop's ZDO, so every peer agrees,
            // and Load only reads it.
            drop.Load();
            if (!drop.m_itemData.m_pickedUp) strays.Add(drop);
        }
        if (strays.Count == 0) return false;
        var outputs = LinkedInventories(pieceView, Direction.Output,
            inventory => strays.Exists(drop => inventory.CanAddItem(drop.m_itemData, 1)));
        // None here yet: those with room were just asked for.
        if (outputs.Count == 0) return true;
        var asked = false;
        foreach (var drop in strays)
        {
            if (!outputs.Any(inventory => inventory.CanAddItem(drop.m_itemData, 1))) continue;
            var view = ItemView(drop);
            if (!view.IsOwner())
            {
                drop.RequestOwn();
                asked = true;
                continue;
            }
            if (!drop.CanPickup()) continue;
            drop.Load();
            if (drop.m_itemData.m_pickedUp) continue;
            var name = drop.m_itemData.m_shared.m_name;
            var stack = drop.m_itemData.m_stack;
            var left = stack;
            foreach (var inventory in outputs)
            {
                var item = drop.m_itemData.Clone();
                item.m_stack = left;
                var before = inventory.CountItems(name, matchWorldLevel: false);
                inventory.AddItem(item);
                left -= inventory.CountItems(name, matchWorldLevel: false) - before;
                if (left <= 0) break;
            }
            if (left <= 0) ZNetScene.instance.Destroy(drop.gameObject);
            else if (left < stack) drop.SetStack(left);
        }
        return asked;
    }

    // ---------------------------------------------------------------- smelter family

    private static readonly AccessTools.FieldRef<Smelter, ZNetView> SmelterView =
        AccessTools.FieldRefAccess<Smelter, ZNetView>("m_nview");
    private static readonly Func<Smelter, Inventory, ItemDrop.ItemData?> SmelterFindCookable =
        AccessTools.MethodDelegate<Func<Smelter, Inventory, ItemDrop.ItemData?>>(AccessTools.Method(typeof(Smelter), "FindCookableItem"));
    private static readonly Action<Smelter, string, bool> SmelterQueueOre =
        AccessTools.MethodDelegate<Action<Smelter, string, bool>>(AccessTools.Method(typeof(Smelter), "QueueOre"));
    private static readonly Func<Smelter, string, Smelter.ItemConversion?> SmelterConversion =
        AccessTools.MethodDelegate<Func<Smelter, string, Smelter.ItemConversion?>>(AccessTools.Method(typeof(Smelter), "GetItemConversion"));

    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    private static class FeedSmelter
    {
        private static void Postfix(Smelter __instance)
        {
            var nview = SmelterView(__instance);
            if (nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();
            var queued = zdo.GetInt(ZDOVars.s_queued);
            var fuel = zdo.GetFloat(ZDOVars.s_fuel);
            // Shortly before a product comes out the output chests are asked for, so they are here in time. A smelter
            // that makes no progress (no fuel, no roof, blocked smoke, no wind) never asks.
            var amount = 0;
            var product = FinishingSoon(__instance, zdo, queued) ? FinishingProduct(__instance, zdo, out amount) : null;
            LinkedInventories(nview, Direction.Output, product != null ? inventory => CanStore(inventory, product, amount) : Never);
            CollectStrays(nview, __instance.m_conversion.Select(conversion => conversion.m_to));

            var usesFuel = __instance.m_maxFuel > 0 && __instance.m_fuelItem != null;
            var needFuel = usesFuel && fuel <= __instance.m_maxFuel - 1;
            var needOre = queued < __instance.m_maxOre;
            var inputs = LinkedInventories(nview, Direction.Input, inventory =>
                HasInput(inventory, __instance.m_fuelItem, needFuel, needOre, __instance, SmelterFindCookable));
            if (inputs.Count == 0) return;
            if (usesFuel)
            {
                while (fuel <= __instance.m_maxFuel - 1 && TakeOne(inputs, __instance.m_fuelItem!)) fuel += 1f;
                zdo.Set(ZDOVars.s_fuel, fuel);
            }

            while (zdo.GetInt(ZDOVars.s_queued) < __instance.m_maxOre)
            {
                var ore = TakeCookable(inputs, __instance, SmelterFindCookable);
                if (ore == null) return;
                SmelterQueueOre(__instance, ore.m_dropPrefab.name, ore.m_cheated);
            }
        }
    }

    private const float OutputLead = 10f;

    // Vanilla's progress test in UpdateSmelter, plus the bake timer within OutputLead of finishing the current item.
    private static bool FinishingSoon(Smelter smelter, ZDO zdo, int queued)
    {
        if (smelter.m_maxOre == 0 || queued == 0 || smelter.m_secPerProduct <= 0f || !smelter.IsActive()) return false;
        if (smelter.m_windmill != null && smelter.m_windmill.GetPowerOutput() <= 0f) return false;
        return zdo.GetFloat(ZDOVars.s_bakeTimer) >= smelter.m_secPerProduct - OutputLead;
    }

    // What finishing the current item spawns, as Smelter.QueueProcessed does: one product, or for pieces that stack
    // their output (windmill, spinning wheel) the waiting stack once it is full or another item takes over. Null when
    // it spawns nothing.
    private static GameObject? FinishingProduct(Smelter smelter, ZDO zdo, out int amount)
    {
        amount = 1;
        var ore = zdo.GetString(ZDOVars.s_item0);
        if (!smelter.m_spawnStack) return SmelterConversion(smelter, ore)?.m_to?.gameObject;
        var waitingOre = zdo.GetString(ZDOVars.s_spawnOre);
        var waiting = zdo.GetInt(ZDOVars.s_spawnAmount);
        if (waitingOre.Length == 0) return null;
        if (waitingOre != ore)
        {
            amount = waiting;
            return SmelterConversion(smelter, waitingOre)?.m_to?.gameObject;
        }
        var product = SmelterConversion(smelter, ore)?.m_to;
        amount = waiting + 1;
        return product != null && amount >= product.m_itemData.m_shared.m_maxStackSize ? product.gameObject : null;
    }

    // Single choke point for every smelter output path; falls back to the vanilla drop when no output chest has room.
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    private static class CollectSmelterOutput
    {
        private static bool Prefix(Smelter __instance, string ore, int stack)
        {
            var nview = SmelterView(__instance);
            if (nview == null) return true;
            var product = SmelterConversion(__instance, ore)?.m_to;
            if (product == null) return true;
            var outputs = LinkedInventories(nview, Direction.Output, inventory => CanStore(inventory, product.gameObject, stack));
            return outputs.Count == 0 || !Store(outputs, product.gameObject, stack);
        }
    }

    // ---------------------------------------------------------------- beehive and sap collector

    private static readonly AccessTools.FieldRef<Beehive, ZNetView> BeehiveView =
        AccessTools.FieldRefAccess<Beehive, ZNetView>("m_nview");
    private static readonly AccessTools.FieldRef<SapCollector, ZNetView> SapCollectorView =
        AccessTools.FieldRefAccess<SapCollector, ZNetView>("m_nview");

    private static void CollectLevel(ZNetView nview, ItemDrop item)
    {
        if (!nview.IsValid()) return;
        CollectStrays(nview, new[] { item });
        var zdo = nview.GetZDO();
        var level = zdo.GetInt(ZDOVars.s_level);
        if (level <= 0) return;
        var amount = level * Game.instance.ScaleDrops(item.m_itemData, 1);
        var outputs = LinkedInventories(nview, Direction.Output, inventory => CanStore(inventory, item.gameObject, amount));
        if (outputs.Count == 0) return;
        if (Store(outputs, item.gameObject, amount)) zdo.Set(ZDOVars.s_level, 0);
    }

    [HarmonyPatch(typeof(Beehive), "UpdateBees")]
    private static class CollectHoney
    {
        private static void Postfix(Beehive __instance)
        {
            var nview = BeehiveView(__instance);
            if (nview != null && __instance.m_honeyItem != null) CollectLevel(nview, __instance.m_honeyItem);
        }
    }

    [HarmonyPatch(typeof(SapCollector), "UpdateTick")]
    private static class CollectSap
    {
        private static void Postfix(SapCollector __instance)
        {
            var nview = SapCollectorView(__instance);
            if (nview == null || __instance.m_spawnItem == null) return;
            var before = nview.IsValid() ? nview.GetZDO().GetInt(ZDOVars.s_level) : 0;
            CollectLevel(nview, __instance.m_spawnItem);
            if (before > 0 && nview.GetZDO().GetInt(ZDOVars.s_level) == 0) nview.InvokeRPC(ZNetView.Everybody, "RPC_UpdateEffects");
        }
    }

    // ---------------------------------------------------------------- cooking station

    private static readonly AccessTools.FieldRef<CookingStation, ZNetView> CookingView =
        AccessTools.FieldRefAccess<CookingStation, ZNetView>("m_nview");
    private static readonly Func<CookingStation, bool> CookingIsFireLit =
        AccessTools.MethodDelegate<Func<CookingStation, bool>>(AccessTools.Method(typeof(CookingStation), "IsFireLit"));
    private static readonly Func<CookingStation, int> CookingFreeSlot =
        AccessTools.MethodDelegate<Func<CookingStation, int>>(AccessTools.Method(typeof(CookingStation), "GetFreeSlot"));
    private static readonly Func<CookingStation, Inventory, ItemDrop.ItemData?> CookingFindCookable =
        AccessTools.MethodDelegate<Func<CookingStation, Inventory, ItemDrop.ItemData?>>(AccessTools.Method(typeof(CookingStation), "FindCookableItem"));
    private static readonly Func<CookingStation, string, bool> CookingIsItemDone =
        AccessTools.MethodDelegate<Func<CookingStation, string, bool>>(AccessTools.Method(typeof(CookingStation), "IsItemDone"));
    private static readonly MethodInfo CookingSetSlot = AccessTools.Method(typeof(CookingStation), "SetSlot");
    private static readonly Type CookingStatus = AccessTools.Inner(typeof(CookingStation), "Status");

    [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
    private static class FeedCookingStation
    {
        private static void Postfix(CookingStation __instance)
        {
            var nview = CookingView(__instance);
            if (nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();
            CollectStrays(nview, __instance.m_conversion.Select(conversion => conversion.m_to).Concat(new[] { __instance.m_overCookedItem }));
            // The output chests are asked for once something on the station is done; a done item burns only after
            // another cooking time, far longer than the hand-over.
            var done = new List<GameObject>();
            for (var slot = 0; slot < __instance.m_slots.Length; slot++)
            {
                var itemName = zdo.GetString("slot" + slot);
                if (itemName == "" || !CookingIsItemDone(__instance, itemName)) continue;
                var prefab = ObjectDB.instance.GetItemPrefab(itemName);
                if (prefab != null) done.Add(prefab);
            }
            var outputs = LinkedInventories(nview, Direction.Output,
                done.Count > 0 ? inventory => done.Exists(prefab => CanStore(inventory, prefab, 1)) : Never);
            if (outputs.Count > 0)
            {
                for (var slot = 0; slot < __instance.m_slots.Length; slot++)
                {
                    var itemName = zdo.GetString("slot" + slot);
                    if (itemName == "" || !CookingIsItemDone(__instance, itemName)) continue;
                    var prefab = ObjectDB.instance.GetItemPrefab(itemName);
                    if (prefab == null || !Store(outputs, prefab, 1)) continue;
                    CookingSetSlot.Invoke(__instance, new object[] { slot, "", 0f, Enum.ToObject(CookingStatus, 0), false });
                    nview.InvokeRPC(ZNetView.Everybody, "RPC_SetSlotVisual", slot, "");
                }
            }

            var usesFuel = __instance.m_useFuel && __instance.m_fuelItem != null;
            var needFuel = usesFuel && zdo.GetFloat(ZDOVars.s_fuel) <= __instance.m_maxFuel - 1;
            // The fire test is a physics query: only made for a linked chest worth asking for.
            var inputs = LinkedInventories(nview, Direction.Input, inventory => HasInput(inventory, __instance.m_fuelItem,
                needFuel, (!__instance.m_requireFire || CookingIsFireLit(__instance)) && CookingFreeSlot(__instance) >= 0,
                __instance, CookingFindCookable));
            if (inputs.Count == 0) return;
            if (usesFuel)
            {
                var fuel = zdo.GetFloat(ZDOVars.s_fuel);
                while (fuel <= __instance.m_maxFuel - 1 && TakeOne(inputs, __instance.m_fuelItem!)) fuel += 1f;
                zdo.Set(ZDOVars.s_fuel, fuel);
            }

            if (__instance.m_requireFire && !CookingIsFireLit(__instance)) return;
            while (CookingFreeSlot(__instance) >= 0)
            {
                var raw = TakeCookable(inputs, __instance, CookingFindCookable);
                if (raw == null) return;
                nview.InvokeRPC("RPC_AddItem", raw.m_dropPrefab.name, raw.m_cheated);
            }
        }
    }

    // ---------------------------------------------------------------- fermenter

    private static readonly AccessTools.FieldRef<Fermenter, ZNetView> FermenterView =
        AccessTools.FieldRefAccess<Fermenter, ZNetView>("m_nview");
    private static readonly Func<Fermenter, Inventory, ItemDrop.ItemData?> FermenterFindCookable =
        AccessTools.MethodDelegate<Func<Fermenter, Inventory, ItemDrop.ItemData?>>(AccessTools.Method(typeof(Fermenter), "FindCookableItem"));

    [HarmonyPatch(typeof(Fermenter), "SlowUpdate")]
    private static class FeedFermenter
    {
        private static void Postfix(Fermenter __instance)
        {
            var nview = FermenterView(__instance);
            if (nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();
            var products = __instance.m_conversion.Select(conversion => conversion.m_to);
            CollectStrays(nview, products);

            var content = zdo.GetInt(ZDOVars.s_content);
            if (content == 0)
            {
                // Asked for only while empty, so a barrel fermenting for 40 minutes doesn't keep pulling its input
                // chests away from other players.
                var inputs = LinkedInventories(nview, Direction.Input, inventory =>
                    HasInput(inventory, null, false, true, __instance, FermenterFindCookable));
                var raw = TakeCookable(inputs, __instance, FermenterFindCookable);
                if (raw != null) nview.InvokeRPC("RPC_AddItem", raw.m_dropPrefab.name.GetStableHashCode(), raw.m_cheated);
                return;
            }

            // The output chests are asked for once the mead is ready, not during the fermentation before.
            var startTicks = zdo.GetLong(ZDOVars.s_startTime);
            if (startTicks == 0) return;
            var fermentedSeconds = (ZNet.instance.GetTime() - new DateTime(startTicks)).TotalSeconds;
            if (fermentedSeconds <= __instance.m_fermentationDuration) return;
            foreach (var conversion in __instance.m_conversion)
            {
                if (conversion.m_from == null || conversion.m_from.gameObject.name.GetStableHashCode() != content) continue;
                var mead = conversion.m_to.gameObject;
                var outputs = LinkedInventories(nview, Direction.Output,
                    inventory => CanStore(inventory, mead, conversion.m_producedItems));
                if (outputs.Count == 0 || !Store(outputs, mead, conversion.m_producedItems)) return;
                zdo.Set(ZDOVars.s_content, 0);
                zdo.Set(ZDOVars.s_startTime, 0L);
                zdo.Set(ZDOVars.s_cheatedQueued, false);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- hammer: linking

    private static ZNetView? _selectedPiece;
    // The link tool held in the previous frame; switching tools drops the selection.
    private static Direction? _toolInHand;

    private static Direction? HeldLinkTool(Player player)
    {
        if (!player.InPlaceMode()) return null;
        var selected = player.GetSelectedPiece();
        if (selected == null) return null;
        if (selected == _inputTool) return Direction.Input;
        if (selected == _outputTool) return Direction.Output;
        return null;
    }

    private static bool IsLinkTool(Piece? piece) => piece != null && (piece == _inputTool || piece == _outputTool);

    // Beehives and sap collectors only make products, so they have no inputs to link.
    private static ZNetView? LinkablePiece(GameObject hovered, Direction direction)
    {
        if (hovered.GetComponentInParent<SiegeMachine>() != null) return null;
        Component? piece = hovered.GetComponentInParent<Smelter>();
        piece ??= hovered.GetComponentInParent<CookingStation>();
        piece ??= hovered.GetComponentInParent<Fermenter>();
        if (direction == Direction.Output)
        {
            piece ??= hovered.GetComponentInParent<Beehive>();
            piece ??= hovered.GetComponentInParent<SapCollector>();
        }
        return piece == null ? null : piece.GetComponentInParent<ZNetView>();
    }

    private static Container? HoveredChest(GameObject hovered)
    {
        var container = hovered.GetComponentInParent<Container>();
        return container != null && Chests.Contains(container) && container.GetComponent<Piece>().IsPlacedByPlayer() ? container : null;
    }

    // Vanilla Container.Interact's rules: a private chest only for its builder, a chest that checks wards only for
    // players the ward lets in.
    private static bool CanOpen(Player player, Container chest)
    {
        if (!CraftFromContainers.HasAccess(chest, player.GetPlayerID()))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_cantopen");
            return false;
        }
        if (!chest.m_checkGuardStone || PrivateArea.CheckAccess(chest.transform.position)) return true;
        player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
        return false;
    }

    private static string Kind(Direction direction) => direction == Direction.Input ? "input" : "output";

    private static void HandleHammerClick(Player player, Direction direction)
    {
        // In build mode the player has no hover object; the hammer's hovered piece is the one it highlights.
        var hoveredPiece = player.GetHoveringPiece();
        if (hoveredPiece == null)
        {
            player.Message(MessageHud.MessageType.Center, "Aim at a processing piece or chest within reach");
            return;
        }
        var hovered = hoveredPiece.gameObject;

        var chest = HoveredChest(hovered);
        if (chest != null)
        {
            if (!CanOpen(player, chest)) return;
            var chestPosition = chest.transform.position;
            if (_selectedPiece == null || !_selectedPiece.IsValid())
            {
                UnlinkFromEveryPiece(player, chestPosition, direction);
                return;
            }

            var selected = _selectedPiece;
            _selectedPiece = null;
            if (Vector3.Distance(selected.transform.position, chestPosition) > _maxLinkDistance.Value)
            {
                player.Message(MessageHud.MessageType.Center, $"Too far, max {_maxLinkDistance.Value:0} m");
                return;
            }
            var positions = ReadChestPositions(selected.GetZDO(), direction) ?? new List<Vector3>();
            var unlinked = RemoveChest(positions, chestPosition);
            SendLinkEdit(selected, direction, !unlinked, chestPosition);
            player.Message(MessageHud.MessageType.Center, unlinked ? $"Unlinked {Kind(direction)} chest" : $"Linked {Kind(direction)} chest");
            return;
        }

        var piece = LinkablePiece(hovered, direction);
        if (piece == null || !piece.IsValid())
        {
            var takesNoInput = direction == Direction.Input && LinkablePiece(hovered, Direction.Output) != null;
            player.Message(MessageHud.MessageType.Center, takesNoInput ? "This piece takes no input" : "Not a processing piece or chest");
            return;
        }
        if (_selectedPiece == piece)
        {
            _selectedPiece = null;
            player.Message(MessageHud.MessageType.Center, "Selection cleared");
            return;
        }
        if (!PrivateArea.CheckAccess(piece.transform.position))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            return;
        }
        _selectedPiece = piece;
        player.Message(MessageHud.MessageType.Center, $"Selected, now click its {Kind(direction)} chest");
    }

    private static void UnlinkFromEveryPiece(Player player, Vector3 chestPosition, Direction direction)
    {
        var unlinked = 0;
        var pieces = new List<Piece>();
        Piece.GetAllPiecesInRadius(chestPosition, _maxLinkDistance.Value, pieces);
        foreach (var candidate in pieces)
        {
            var view = candidate.GetComponentInParent<ZNetView>();
            if (view == null || !view.IsValid()) continue;
            var positions = ReadChestPositions(view.GetZDO(), direction);
            if (positions == null || !RemoveChest(positions, chestPosition)) continue;
            SendLinkEdit(view, direction, false, chestPosition);
            unlinked++;
        }
        player.Message(MessageHud.MessageType.Center,
            unlinked == 0 ? $"No piece uses this chest as {Kind(direction)}" : $"Unlinked {unlinked} piece(s) from this {Kind(direction)} chest");
    }

    private static readonly AccessTools.FieldRef<Player, GameObject?> PlacementGhost =
        AccessTools.FieldRefAccess<Player, GameObject?>("m_placementGhost");

    // Without a ghost vanilla's place branch does nothing: no cost, stamina, durability or effects. Not gated on
    // the config: the link tools must never be placeable.
    [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
    private static class NoLinkGhost
    {
        private static bool Prefix(Player __instance)
        {
            if (!IsLinkTool(__instance.GetSelectedPiece())) return true;
            ref var ghost = ref PlacementGhost(__instance);
            if (ghost != null) UnityEngine.Object.Destroy(ghost);
            ghost = null;
            return false;
        }
    }

    // Same input gates as vanilla's place click.
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    private static class LinkClick
    {
        private static void Postfix(Player __instance, bool takeInput)
        {
            if (__instance != Player.m_localPlayer || !takeInput || __instance.IsDead()) return;
            var tool = HeldLinkTool(__instance);
            if (tool == null) return;
            if (Hud.IsPieceSelectionVisible() || Hud.InRadial() || ZInput.GetButton("JoyAltKeys")) return;
            if (!ZInput.GetButtonDown("Attack") && !ZInput.GetButtonDown("JoyPlace")) return;
            if (!_enabled.Value)
            {
                __instance.Message(MessageHud.MessageType.Center, "AutoStorage is disabled in the config");
                return;
            }
            HandleHammerClick(__instance, tool.Value);
        }
    }

    // The link tools can't be built, so they must not count towards the "build every piece" achievement.
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetAllBuildPieces))]
    private static class HideLinkToolsFromAchievement
    {
        private static void Postfix(List<Piece> __result)
        {
            if (_inputTool != null) __result.Remove(_inputTool);
            if (_outputTool != null) __result.Remove(_outputTool);
        }
    }

    // ---------------------------------------------------------------- old AutoStorage Hammer refund

    private static bool _checkOldHammers;

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    private static class OldHammerOnSpawn
    {
        private static void Postfix(Player __instance) => _checkOldHammers |= __instance == Player.m_localPlayer;
    }

    // Also catches hammers taken from chests, the ground or a tombstone. Only flags: changing the inventory
    // inside its own change callback is unsafe, so the swap happens on the next Player.Update.
    [HarmonyPatch(typeof(Player), "OnInventoryChanged")]
    private static class OldHammerOnChange
    {
        private static void Postfix(Player __instance) => _checkOldHammers |= __instance == Player.m_localPlayer;
    }

    private static void RefundOldHammers(Player player)
    {
        var inventory = player.GetInventory();
        var old = inventory.GetAllItems().Where(i => i.m_shared.m_name == HammerToken).ToList();
        foreach (var item in old)
        {
            player.RemoveEquipAction(item);
            player.UnequipItem(item, triggerEquipEffects: false);
            if (!inventory.RemoveItem(item)) continue;
            Refund(player, inventory, "Wood", 3 * item.m_stack);
            Refund(player, inventory, "SurtlingCore", item.m_stack);
        }
        if (old.Count > 0)
        {
            player.Message(MessageHud.MessageType.Center, "AutoStorage Hammer refunded, use AutoStorage Link input/output (Hammer > Misc)");
        }
    }

    private static void Refund(Player player, Inventory inventory, string prefabName, int amount)
    {
        var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
        if (prefab == null || amount <= 0) return;
        var data = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        data.m_dropPrefab = prefab;
        data.m_stack = amount;
        data.m_worldLevel = (byte)Game.m_worldLevel;
        data.m_pickedUp = true;
        if (inventory.AddItem(data)) return;
        // AddItem leaves the part that did not fit in m_stack.
        var t = player.transform;
        ItemDrop.DropItem(data, data.m_stack, t.position + t.forward + t.up, t.rotation);
    }

    // ---------------------------------------------------------------- link tools: lines

    private static readonly Color InputLineColor = new(0.25f, 0.55f, 1f);
    private static readonly Color OutputLineColor = Color.green;

    private static readonly List<LineRenderer> Lines = new();
    private static readonly List<Piece> NearbyPieces = new();
    private static readonly List<Vector3> LinkSegments = new();
    private static float _nextLinkScan;
    private static Material? _lineMaterial;

    [HarmonyPatch(typeof(Player), "Update")]
    private static class DrawLinks
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (_checkOldHammers)
            {
                _checkOldHammers = false;
                RefundOldHammers(__instance);
            }

            var tool = _enabled.Value ? HeldLinkTool(__instance) : null;
            if (tool != _toolInHand)
            {
                _toolInHand = tool;
                _selectedPiece = null;
                _nextLinkScan = 0f;
            }
            if (tool == null)
            {
                HideLines(0);
                return;
            }

            if (Time.time >= _nextLinkScan)
            {
                _nextLinkScan = Time.time + 0.5f;
                ScanLinks(__instance.transform.position, tool.Value);
            }

            var count = 0;
            var color = tool == Direction.Input ? InputLineColor : OutputLineColor;
            for (var i = 0; i + 1 < LinkSegments.Count; i += 2) DrawLine(count++, LinkSegments[i], LinkSegments[i + 1], color);
            if (_selectedPiece != null && _selectedPiece.IsValid())
            {
                DrawLine(count++, _selectedPiece.transform.position + Vector3.up * 0.5f, __instance.transform.position + Vector3.up * 1.2f, Color.yellow);
            }
            HideLines(count);
        }
    }

    private static void ScanLinks(Vector3 around, Direction direction)
    {
        LinkSegments.Clear();
        NearbyPieces.Clear();
        Piece.GetAllPiecesInRadius(around, _maxLinkDistance.Value, NearbyPieces);
        foreach (var piece in NearbyPieces)
        {
            var view = piece.GetComponentInParent<ZNetView>();
            if (view == null || !view.IsValid()) continue;
            var chests = ReadChestPositions(view.GetZDO(), direction);
            if (chests == null) continue;
            foreach (var chest in chests)
            {
                LinkSegments.Add(view.transform.position + Vector3.up * 0.5f);
                LinkSegments.Add(chest + Vector3.up * 0.5f);
            }
        }
    }

    private static void DrawLine(int index, Vector3 from, Vector3 to, Color color)
    {
        for (var i = Lines.Count - 1; i >= 0; i--) if (Lines[i] == null) Lines.RemoveAt(i);
        while (Lines.Count <= index)
        {
            var line = new GameObject("mh_AutoStorageLink").AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.06f;
            if (_lineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                if (shader != null) _lineMaterial = new Material(shader);
            }
            if (_lineMaterial != null) line.material = _lineMaterial;
            Lines.Add(line);
        }
        var renderer = Lines[index];
        renderer.enabled = true;
        renderer.startColor = renderer.endColor = color;
        renderer.SetPosition(0, from);
        renderer.SetPosition(1, to);
    }

    private static void HideLines(int fromIndex)
    {
        for (var i = fromIndex; i < Lines.Count; i++) if (Lines[i] != null) Lines[i].enabled = false;
    }
}
