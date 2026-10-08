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

// Custom "AutoStorage" chest plus an "AutoStorageHammer" tool. The hammer links a processing piece (smelter
// family, beehive, sap collector, cooking station, fermenter) to one AutoStorage chest. The piece's ZDO owner
// then pulls inputs from that chest and pushes outputs into it on the piece's own vanilla update tick.
//
// The link is stored on the piece as the chest's world position: ZDOIDs are reassigned on every world load,
// so they cannot be persisted. Chest edits only stick on the chest's ZDO owner, so the piece owner claims the
// chest before writing and skips the tick while a player has the chest open.
internal static class AutoStorage
{
    private const string ChestPrefab = "AutoStorage";
    private const string HammerPrefab = "AutoStorageHammer";
    private const string HammerToken = "$item_autostoragehammer";
    private const string ChestToken = "$piece_autostorage";
    private const float ChestMatchRadius = 0.5f;
    private const int ChestWidth = 5;
    private const int ChestHeight = 2;
    // Chests built with 0.0.3 were 8x4; their saved items can sit outside the current grid.
    private const int LegacyChestWidth = 8;

    private static readonly int LinkKey = "mh_AutoStorageLink".GetStableHashCode();

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _maxLinkDistance = null!;
    private static ConfigEntry<float> _collectRadius = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "AutoStorage",
            true,
            "Adds the AutoStorage chest and the AutoStorage Hammer. Pieces linked to a chest with the hammer " +
            "(kiln, smelter, blast furnace, spinning wheel, windmill, eitr refinery, beehive, sap collector, " +
            "cooking stations, fermenter) take their inputs from that chest and put their products into it. " +
            "The chest and hammer stay craftable when this is off; only the automation and linking stop.");
        _maxLinkDistance = config.Bind(
            "Crafting",
            "AutoStorageMaxLinkDistance",
            100f,
            "Maximum distance in meters between a piece and the AutoStorage chest it is linked to. " +
            "Pieces farther than roughly 64 m from any player are not simulated by the game, so a chest beyond " +
            "that distance from the piece may simply not be loaded.");
        _collectRadius = config.Bind(
            "Crafting",
            "AutoStorageCollectRadius",
            5f,
            "Once a minute, products of a linked piece lying on the ground within this many meters of it (for " +
            "example because the chest was full or open) are moved into its chest. 0 turns this off.");

        PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefabs;
    }

    private static void RegisterPrefabs()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefabs;

        var chestConfig = new PieceConfig
        {
            Name = ChestToken,
            Description = "$piece_autostorage_description",
            PieceTable = PieceTables.Hammer,
            Category = PieceCategories.Misc,
            CraftingStation = CraftingStations.Workbench,
        };
        chestConfig.AddRequirement("FineWood", 10, true);
        chestConfig.AddRequirement("GreydwarfEye", 5, true);
        chestConfig.AddRequirement("Bronze", 5, true);
        var chest = new CustomPiece(ChestPrefab, "piece_chest", chestConfig);
        var container = chest.PiecePrefab.GetComponent<Container>();
        container.m_name = ChestToken;
        container.m_width = ChestWidth;
        container.m_height = ChestHeight;
        PieceManager.Instance.AddPiece(chest);

        var hammerConfig = new ItemConfig
        {
            Name = HammerToken,
            Description = "$item_autostoragehammer_description",
            CraftingStation = CraftingStations.Workbench,
        };
        hammerConfig.AddRequirement("Wood", 3);
        hammerConfig.AddRequirement("SurtlingCore", 1);
        var hammer = new CustomItem(HammerPrefab, "Hammer", hammerConfig);
        var shared = hammer.ItemDrop.m_itemData.m_shared;
        shared.m_buildPieces = null;
        shared.m_useDurability = false;
        ItemManager.Instance.AddItem(hammer);

        var localization = LocalizationManager.Instance.GetLocalization();
        localization.AddTranslation(ChestToken.TrimStart('$'), "AutoStorage");
        localization.AddTranslation("piece_autostorage_description",
            "Link kilns, smelters, beehives, cooking stations and fermenters to this chest with the AutoStorage Hammer.");
        localization.AddTranslation(HammerToken.TrimStart('$'), "AutoStorage Hammer");
        localization.AddTranslation("item_autostoragehammer_description",
            "Click a processing piece, then an AutoStorage chest to link them. Click the pair again to unlink. " +
            "Click a chest with nothing selected to unlink everything from it.");
    }

    // ---------------------------------------------------------------- chest registry and link resolution

    private static readonly List<Container> Chests = new();

    [HarmonyPatch(typeof(Container), "Awake")]
    private static class RegisterChest
    {
        private static void Postfix(Container __instance)
        {
            if (__instance.name.StartsWith(ChestPrefab, StringComparison.Ordinal)) Chests.Add(__instance);
        }
    }

    private static Container? FindChestAt(Vector3 position)
    {
        for (var i = Chests.Count - 1; i >= 0; i--)
        {
            var chest = Chests[i];
            if (chest == null) { Chests.RemoveAt(i); continue; }
            if (Vector3.Distance(chest.transform.position, position) <= ChestMatchRadius) return chest;
        }
        return null;
    }

    private static Container? LinkedChest(ZNetView nview)
    {
        if (!nview.IsValid()) return null;
        var position = nview.GetZDO().GetVec3(LinkKey, Vector3.zero);
        return position == Vector3.zero ? null : FindChestAt(position);
    }

    private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
        AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
    private static readonly Func<Container, bool> ContainerLoad =
        AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));

    private static readonly AccessTools.FieldRef<Inventory, int> InventoryWidth =
        AccessTools.FieldRefAccess<Inventory, int>("m_width");
    private static readonly Func<Inventory, bool, Vector2i> InventoryFindEmptySlot =
        AccessTools.MethodDelegate<Func<Inventory, bool, Vector2i>>(AccessTools.Method(typeof(Inventory), "FindEmptySlot"));

    // Vanilla Inventory.Load silently drops saved items whose column is outside the grid, so a 0.0.3 chest (8x4)
    // would lose columns 5-7 now that the chest is 5x2. Load with the old width, then move every item outside the
    // grid into a free slot. Items are only repositioned inside the same inventory, never added or removed; what
    // does not fit stays saved outside the grid (still used by the automation) and moves in on a later load.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load), typeof(ZPackage))]
    private static class MigrateLegacyChest
    {
        private static void Prefix(Inventory __instance, out int __state)
        {
            __state = InventoryWidth(__instance);
            if (__instance.GetName() == ChestToken && __state < LegacyChestWidth) InventoryWidth(__instance) = LegacyChestWidth;
        }

        private static void Postfix(Inventory __instance, int __state)
        {
            if (InventoryWidth(__instance) == __state) return;
            InventoryWidth(__instance) = __state;
            var height = __instance.GetHeight();
            foreach (var item in __instance.GetAllItems())
            {
                if (item.m_gridPos.x < __state && item.m_gridPos.y < height) continue;
                var slot = InventoryFindEmptySlot(__instance, false);
                if (slot.x < 0) return;
                item.m_gridPos = slot;
            }
        }
    }

    // Inventory of the linked chest, ready to edit from this peer, or null when the chest is missing, too far,
    // open in someone's UI, or not loaded. Claiming ownership is what makes Container.Save persist our edits.
    private static Inventory? WritableInventory(ZNetView pieceView)
    {
        if (!_enabled.Value || !pieceView.IsValid() || !pieceView.IsOwner()) return null;
        var chest = LinkedChest(pieceView);
        if (chest == null) return null;
        if (Vector3.Distance(chest.transform.position, pieceView.transform.position) > _maxLinkDistance.Value) return null;
        var chestView = ContainerView(chest);
        if (chestView == null || !chestView.IsValid()) return null;
        if (chest.IsInUse() || chestView.GetZDO().GetInt(ZDOVars.s_inUse) == 1) return null;
        if (!chestView.IsOwner()) chestView.ClaimOwnership();
        ContainerLoad(chest);
        return chest.GetInventory();
    }

    private static bool TakeOne(Inventory inventory, ItemDrop item)
    {
        var name = item.m_itemData.m_shared.m_name;
        if (inventory.CountItems(name) < 1) return false;
        inventory.RemoveItem(name, 1);
        return true;
    }

    private static bool Store(Inventory inventory, GameObject prefab, int amount) =>
        amount > 0 && inventory.CanAddItem(prefab, amount) && inventory.AddItem(prefab, amount);

    // ---------------------------------------------------------------- collecting stray products

    private const float CollectInterval = 60f;

    private static readonly Dictionary<ZNetView, float> NextCollect = new();
    private static readonly Collider[] ItemHits = new Collider[128];
    private static int _itemMask;

    private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> ItemView =
        AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");

    // Once a minute per linked piece, move its products lying within the collect radius into the chest. Products
    // end up on the ground whenever the chest was full, open or unloaded at the moment they came out.
    //
    // No duplication: a drop is only touched while this peer owns its ZDO. Ownership is handed over only by the
    // current owner (ItemDrop.RPC_RequestOwn), so exactly one peer can take it, the same rule vanilla pickup relies
    // on; otherwise we request ownership and retry on the next sweep. The chest count is measured before and after
    // adding, and the drop loses exactly what went in: destroyed when the whole stack fit, shrunk when only part did.
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

    private static void CollectStrays(ZNetView pieceView, Inventory inventory, IEnumerable<ItemDrop?> products)
    {
        var names = new HashSet<string>(products.Where(product => product != null).Select(product => product!.m_itemData.m_shared.m_name));
        if (names.Count == 0) return;
        if (_itemMask == 0) _itemMask = LayerMask.GetMask("item");
        var hits = Physics.OverlapSphereNonAlloc(pieceView.transform.position, _collectRadius.Value, ItemHits, _itemMask);
        for (var i = 0; i < hits; i++)
        {
            var body = ItemHits[i].attachedRigidbody;
            var drop = body == null ? null : body.GetComponent<ItemDrop>();
            if (drop == null || drop.IsPiece()) continue;
            var view = ItemView(drop);
            if (view == null || !view.IsValid()) continue;
            var name = drop.m_itemData.m_shared.m_name;
            if (!names.Contains(name)) continue;
            if (!view.IsOwner()) { drop.RequestOwn(); continue; }
            if (!drop.CanPickup()) continue;

            drop.Load();
            var stack = drop.m_itemData.m_stack;
            var before = inventory.CountItems(name, matchWorldLevel: false);
            inventory.AddItem(drop.m_itemData.Clone());
            var added = inventory.CountItems(name, matchWorldLevel: false) - before;
            if (added >= stack) ZNetScene.instance.Destroy(drop.gameObject);
            else if (added > 0) drop.SetStack(stack - added);
        }
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
            if (nview == null) return;
            var inventory = WritableInventory(nview);
            if (inventory == null) return;
            var zdo = nview.GetZDO();
            if (CollectDue(nview)) CollectStrays(nview, inventory, __instance.m_conversion.Select(conversion => conversion.m_to));

            if (__instance.m_maxFuel > 0 && __instance.m_fuelItem != null)
            {
                var fuel = zdo.GetFloat(ZDOVars.s_fuel);
                while (fuel <= __instance.m_maxFuel - 1 && TakeOne(inventory, __instance.m_fuelItem)) fuel += 1f;
                zdo.Set(ZDOVars.s_fuel, fuel);
            }

            while (zdo.GetInt(ZDOVars.s_queued) < __instance.m_maxOre)
            {
                var ore = SmelterFindCookable(__instance, inventory);
                if (ore == null || !inventory.RemoveOneItem(ore)) return;
                SmelterQueueOre(__instance, ore.m_dropPrefab.name, ore.m_cheated);
            }
        }
    }

    // Single choke point for every smelter output path; falls back to the vanilla drop when the chest is full.
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    private static class CollectSmelterOutput
    {
        private static bool Prefix(Smelter __instance, string ore, int stack)
        {
            var nview = SmelterView(__instance);
            if (nview == null) return true;
            var inventory = WritableInventory(nview);
            if (inventory == null) return true;
            var product = SmelterConversion(__instance, ore)?.m_to;
            return product == null || !Store(inventory, product.gameObject, stack);
        }
    }

    // ---------------------------------------------------------------- beehive and sap collector

    private static readonly AccessTools.FieldRef<Beehive, ZNetView> BeehiveView =
        AccessTools.FieldRefAccess<Beehive, ZNetView>("m_nview");
    private static readonly AccessTools.FieldRef<SapCollector, ZNetView> SapCollectorView =
        AccessTools.FieldRefAccess<SapCollector, ZNetView>("m_nview");

    private static void CollectLevel(ZNetView nview, ItemDrop item)
    {
        var inventory = WritableInventory(nview);
        if (inventory == null) return;
        var zdo = nview.GetZDO();
        if (CollectDue(nview)) CollectStrays(nview, inventory, new[] { item });
        var level = zdo.GetInt(ZDOVars.s_level);
        if (level <= 0) return;
        var amount = level * Game.instance.ScaleDrops(item.m_itemData, 1);
        if (Store(inventory, item.gameObject, amount)) zdo.Set(ZDOVars.s_level, 0);
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
            if (nview == null) return;
            var inventory = WritableInventory(nview);
            if (inventory == null) return;
            var zdo = nview.GetZDO();
            if (CollectDue(nview))
            {
                CollectStrays(nview, inventory, __instance.m_conversion.Select(conversion => conversion.m_to).Concat(new[] { __instance.m_overCookedItem }));
            }

            for (var slot = 0; slot < __instance.m_slots.Length; slot++)
            {
                var itemName = zdo.GetString("slot" + slot);
                if (itemName == "" || !CookingIsItemDone(__instance, itemName)) continue;
                var prefab = ObjectDB.instance.GetItemPrefab(itemName);
                if (prefab == null || !Store(inventory, prefab, 1)) continue;
                CookingSetSlot.Invoke(__instance, new object[] { slot, "", 0f, Enum.ToObject(CookingStatus, 0), false });
                nview.InvokeRPC(ZNetView.Everybody, "RPC_SetSlotVisual", slot, "");
            }

            if (__instance.m_useFuel && __instance.m_fuelItem != null)
            {
                var fuel = zdo.GetFloat(ZDOVars.s_fuel);
                while (fuel <= __instance.m_maxFuel - 1 && TakeOne(inventory, __instance.m_fuelItem)) fuel += 1f;
                zdo.Set(ZDOVars.s_fuel, fuel);
            }

            if (__instance.m_requireFire && !CookingIsFireLit(__instance)) return;
            while (CookingFreeSlot(__instance) >= 0)
            {
                var raw = CookingFindCookable(__instance, inventory);
                if (raw == null || !inventory.RemoveOneItem(raw)) return;
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
            if (nview == null) return;
            var inventory = WritableInventory(nview);
            if (inventory == null) return;
            var zdo = nview.GetZDO();
            if (CollectDue(nview)) CollectStrays(nview, inventory, __instance.m_conversion.Select(conversion => conversion.m_to));

            var content = zdo.GetInt(ZDOVars.s_content);
            if (content == 0)
            {
                var raw = FermenterFindCookable(__instance, inventory);
                if (raw == null || !inventory.RemoveOneItem(raw)) return;
                nview.InvokeRPC("RPC_AddItem", raw.m_dropPrefab.name.GetStableHashCode(), raw.m_cheated);
                return;
            }

            var startTicks = zdo.GetLong(ZDOVars.s_startTime);
            if (startTicks == 0) return;
            var fermentedSeconds = (ZNet.instance.GetTime() - new DateTime(startTicks)).TotalSeconds;
            if (fermentedSeconds <= __instance.m_fermentationDuration) return;

            foreach (var conversion in __instance.m_conversion)
            {
                if (conversion.m_from == null || conversion.m_from.gameObject.name.GetStableHashCode() != content) continue;
                if (!Store(inventory, conversion.m_to.gameObject, conversion.m_producedItems)) return;
                zdo.Set(ZDOVars.s_content, 0);
                zdo.Set(ZDOVars.s_startTime, 0L);
                zdo.Set(ZDOVars.s_cheatedQueued, false);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- hammer: linking

    private static ZNetView? _selectedPiece;
    private static bool _clickHeld;

    private static bool HoldingHammer(Humanoid humanoid) => humanoid.RightItem?.m_shared.m_name == HammerToken;

    private static ZNetView? LinkablePiece(GameObject hovered)
    {
        if (hovered.GetComponentInParent<SiegeMachine>() != null) return null;
        Component? piece = hovered.GetComponentInParent<Smelter>();
        piece ??= hovered.GetComponentInParent<Beehive>();
        piece ??= hovered.GetComponentInParent<SapCollector>();
        piece ??= hovered.GetComponentInParent<CookingStation>();
        piece ??= hovered.GetComponentInParent<Fermenter>();
        return piece == null ? null : piece.GetComponentInParent<ZNetView>();
    }

    private static Container? HoveredChest(GameObject hovered)
    {
        var container = hovered.GetComponentInParent<Container>();
        return container != null && Chests.Contains(container) ? container : null;
    }

    private static void SetLink(ZNetView piece, Vector3 chestPosition)
    {
        piece.ClaimOwnership();
        piece.GetZDO().Set(LinkKey, chestPosition);
    }

    private static void HandleHammerClick(Player player)
    {
        var hovered = player.GetHoverObject();
        if (hovered == null) return;

        var chest = HoveredChest(hovered);
        if (chest != null)
        {
            var chestPosition = chest.transform.position;
            if (_selectedPiece == null || !_selectedPiece.IsValid())
            {
                var unlinked = 0;
                var pieces = new List<Piece>();
                Piece.GetAllPiecesInRadius(chestPosition, _maxLinkDistance.Value, pieces);
                foreach (var candidate in pieces)
                {
                    var view = candidate.GetComponentInParent<ZNetView>();
                    if (view == null || !view.IsValid()) continue;
                    if (Vector3.Distance(view.GetZDO().GetVec3(LinkKey, Vector3.zero), chestPosition) > ChestMatchRadius) continue;
                    SetLink(view, Vector3.zero);
                    unlinked++;
                }
                player.Message(MessageHud.MessageType.Center, unlinked == 0 ? "Nothing linked to this chest" : $"Unlinked {unlinked} piece(s)");
                return;
            }

            var selected = _selectedPiece;
            _selectedPiece = null;
            if (Vector3.Distance(selected.transform.position, chestPosition) > _maxLinkDistance.Value)
            {
                player.Message(MessageHud.MessageType.Center, $"Too far, max {_maxLinkDistance.Value:0} m");
                return;
            }
            var alreadyLinked = Vector3.Distance(selected.GetZDO().GetVec3(LinkKey, Vector3.zero), chestPosition) <= ChestMatchRadius;
            SetLink(selected, alreadyLinked ? Vector3.zero : chestPosition);
            player.Message(MessageHud.MessageType.Center, alreadyLinked ? "Unlinked" : "Linked");
            return;
        }

        var piece = LinkablePiece(hovered);
        if (piece == null || !piece.IsValid())
        {
            player.Message(MessageHud.MessageType.Center, "Not a processing piece or AutoStorage chest");
            return;
        }
        if (_selectedPiece == piece)
        {
            _selectedPiece = null;
            player.Message(MessageHud.MessageType.Center, "Selection cleared");
            return;
        }
        _selectedPiece = piece;
        player.Message(MessageHud.MessageType.Center, "Selected, now click an AutoStorage chest");
    }

    // The tool is not a weapon, so vanilla would punch. Consuming the attack (result true) clears the queued
    // attack timer; while the button is held vanilla keeps calling this, hence the own edge detection.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    private static class HammerClick
    {
        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
        {
            if (!_enabled.Value || __instance != Player.m_localPlayer || !HoldingHammer(__instance)) return true;
            __result = true;
            if (secondaryAttack || _clickHeld) return false;
            _clickHeld = true;
            HandleHammerClick((Player)__instance);
            return false;
        }
    }

    // ---------------------------------------------------------------- hammer: link lines

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
            if (!ZInput.GetButton("Attack") && !ZInput.GetButton("JoyAttack")) _clickHeld = false;

            var show = _enabled.Value && HoldingHammer(__instance);
            if (!show)
            {
                _selectedPiece = null;
                HideLines(0);
                return;
            }

            if (Time.time >= _nextLinkScan)
            {
                _nextLinkScan = Time.time + 0.5f;
                ScanLinks(__instance.transform.position);
            }

            var count = 0;
            for (var i = 0; i + 1 < LinkSegments.Count; i += 2) DrawLine(count++, LinkSegments[i], LinkSegments[i + 1], Color.green);
            if (_selectedPiece != null && _selectedPiece.IsValid())
            {
                DrawLine(count++, _selectedPiece.transform.position + Vector3.up * 0.5f, __instance.transform.position + Vector3.up * 1.2f, Color.yellow);
            }
            HideLines(count);
        }
    }

    private static void ScanLinks(Vector3 around)
    {
        LinkSegments.Clear();
        NearbyPieces.Clear();
        Piece.GetAllPiecesInRadius(around, _maxLinkDistance.Value, NearbyPieces);
        foreach (var piece in NearbyPieces)
        {
            var view = piece.GetComponentInParent<ZNetView>();
            if (view == null || !view.IsValid()) continue;
            var target = view.GetZDO().GetVec3(LinkKey, Vector3.zero);
            if (target == Vector3.zero) continue;
            LinkSegments.Add(view.transform.position + Vector3.up * 0.5f);
            LinkSegments.Add(target + Vector3.up * 0.5f);
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
