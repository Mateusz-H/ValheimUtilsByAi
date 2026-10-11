using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Materials in built chests near the local player count for crafting, upgrading and building, and are taken from
// them after the player's own inventory.
//
// Counting is not taking. Every peer receives a chest's inventory with its ZDO, so the UI (requirement amounts, craft
// button, recipe and build lists) counts every chest in range, owned or not. The checks right before vanilla
// consumes (InventoryGui.DoCrafting, the place click in Player.UpdatePlacement), and checks made by any other code,
// count only chests this peer owns, and only those are edited: only the owner's edits are saved
// (Container.OnContainerChanged), and ownership never changes inside the synchronous check-and-consume.
//
// Ownership is asked for only on an action (the Craft click, selecting a build piece, a place click that falls short)
// and only for chests holding one of its materials. The request is vanilla's RPC_RequestOpen with the chest UI
// suppressed, so owners without the mod hand chests over too, under vanilla's rules: not while open, private chests
// only to their builder.
//
// A craft takes what the inventory lacks out of the chests on the Craft click (from the others as soon as they arrive)
// and holds it until vanilla consumes at the end of the craft timer, so no chest stays locked for the length of the
// craft. Whatever is left when the craft ends or is cancelled goes back to its chest, else the inventory, else the
// ground.
//
// Recipes that need only one of their ingredients are left vanilla: their consume path bypasses ConsumeResources.
internal static class CraftFromContainers
{
    private const string ChestBusyMessage = "Chest busy, try again";
    private const string FetchingMessage = "Fetching materials, click again";

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _radius = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "CraftFromContainers",
            true,
            "Crafting, upgrading and building also use materials from chests near you, after your own inventory. " +
            "A craft takes its materials out of the chests when you click Craft and puts back what is left if " +
            "you cancel. In multiplayer a chest another player has open is skipped until they close it; " +
            "you get a short message and can try again.");
        _radius = config.Bind(
            "Crafting",
            "CraftFromContainersRadius",
            15f,
            new ConfigDescription(
                "How far from you, in meters, a chest can be to be used.",
                new AcceptableValueRange<float>(1f, 50f)));
    }

    private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
        AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
    private static readonly Func<Container, bool> ContainerLoad =
        AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));
    private static readonly AccessTools.FieldRef<InventoryGui, float> CraftTimer =
        AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");
    private static readonly AccessTools.FieldRef<InventoryGui, Recipe> CraftRecipe =
        AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
    private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData?> CraftUpgradeItem =
        AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData?>("m_craftUpgradeItem");
    private static readonly AccessTools.FieldRef<InventoryGui, bool> MultiCrafting =
        AccessTools.FieldRefAccess<InventoryGui, bool>("m_multiCrafting");
    // m_selectedRecipe is a private struct type, so its Recipe is read by reflection.
    private static readonly FieldInfo SelectedRecipeField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
    private static readonly MethodInfo SelectedRecipeGetter = AccessTools.PropertyGetter(SelectedRecipeField.FieldType, "Recipe");

    // ---------------------------------------------------------------- chests in range

    private static readonly List<Container> Chests = new();

    // Player-built chests only: carts, ship storage, tombstones, the obliterator and loot crates are skipped, and so
    // are feeding troughs, which hold animal feed. Placement ghosts have no inventory. Registered regardless of the
    // config so toggling works at runtime.
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
            if (__instance.name.StartsWith(FeedingTrough.TroughPrefab, StringComparison.Ordinal)) return;
            Chests.Add(__instance);
        }
    }

    // Vanilla Container.CheckAccess: public chests for everyone, private ones for their builder. AutoStorage's link
    // tools use it too.
    internal static bool HasAccess(Container chest, long playerID) => chest.m_privacy switch
    {
        Container.PrivacySetting.Public => true,
        Container.PrivacySetting.Private => chest.GetComponent<Piece>().GetCreator() == playerID,
        _ => false,
    };

    private static readonly List<Container> InRange = new();
    private static int _inRangeFrame = -1;

    // Chests in range the local player may use, owned or not. Recomputed once per frame; validity and ownership are
    // checked on every use because network messages can be processed later in the same frame, and a chest removed
    // earlier in the frame loses its ZDO before its GameObject goes.
    private static List<Container> ChestsInRange()
    {
        if (_inRangeFrame == Time.frameCount) return InRange;
        _inRangeFrame = Time.frameCount;
        InRange.Clear();
        UsableChestsWithin(_radius.Value, InRange);
        return InRange;
    }

    // Adds the chests within radius of the local player that the player may use, owned or not. QuickStack and Restock
    // use it too.
    internal static void UsableChestsWithin(float radius, List<Container> result)
    {
        var player = Player.m_localPlayer;
        if (player == null) return;
        var playerID = player.GetPlayerID();
        var position = player.transform.position;
        for (var i = Chests.Count - 1; i >= 0; i--)
        {
            var chest = Chests[i];
            if (chest == null) { Chests.RemoveAt(i); RequestedByUs.Remove(chest!); continue; }
            var view = ContainerView(chest);
            if (!view.IsValid() || Vector3.Distance(chest.transform.position, position) > radius) continue;
            if (!chest.GetComponent<Piece>().IsPlacedByPlayer() || !HasAccess(chest, playerID)) continue;
            if (chest.m_checkGuardStone && !PrivateArea.CheckAccess(chest.transform.position, 0f, flash: false)) continue;
            result.Add(chest);
        }
    }

    internal static bool IsOwned(Container chest)
    {
        var view = ContainerView(chest);
        return view != null && view.IsValid() && view.IsOwner();
    }

    // Open in another player's UI: its contents are about to change and vanilla won't hand it over.
    private static bool OpenElsewhere(ZNetView view) => view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;

    // Not owned here and open in another player's UI: it can be neither edited nor asked for.
    internal static bool Busy(Container chest)
    {
        var view = ContainerView(chest);
        return view == null || !view.IsValid() || (!view.IsOwner() && OpenElsewhere(view));
    }

    private static bool Countable(Container chest, bool countNotOwned)
    {
        var view = ContainerView(chest);
        return view.IsValid() && (view.IsOwner() || (countNotOwned && !OpenElsewhere(view)));
    }

    // Load picks up data that arrived since the last Container.CheckForChanges (once a second); an owner's own edits
    // are always current.
    internal static Inventory LoadedInventory(Container chest)
    {
        LoadChest(chest);
        return chest.GetInventory();
    }

    private static readonly Action<Container> UpdateUseVisual =
        AccessTools.MethodDelegate<Action<Container>>(AccessTools.Method(typeof(Container), "UpdateUseVisual"));

    // Load only reports new data once per data revision, and CheckForChanges redraws the lid only when its own Load
    // reports it. Loading here first used up the revision that carried a closed lid, so a chest another player
    // closed kept showing open; redraw it the same way. An unchanged s_inUse is not written, so this never bumps the
    // revision. AutoStorage loads its chests through this too.
    internal static void LoadChest(Container chest)
    {
        if (ContainerLoad(chest)) UpdateUseVisual(chest);
    }

    private static bool HoldsAny(Inventory inventory, Piece.Requirement[] requirements)
    {
        foreach (var requirement in requirements)
        {
            if (requirement.m_resItem && LikedItems.CountAvailable(inventory, requirement.m_resItem.m_itemData.m_shared.m_name) > 0) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- counting

    private static int _countScope;
    // Above zero inside the UI, where chests this peer doesn't own count too.
    private static int _uiDepth;

    private static bool Active(Player player) =>
        _enabled.Value && player != null && player == Player.m_localPlayer && !player.NoCostCheat();

    // Scoped to the requirement checks and the requirement UI.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
    private static class AddChestCounts
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            if (_countScope == 0 || name == null) return;
            var player = Player.m_localPlayer;
            if (!Active(player) || __instance != player.GetInventory()) return;
            __result += CountHeld(name, quality, matchWorldLevel);
            var countNotOwned = _uiDepth > 0;
            foreach (var chest in ChestsInRange())
            {
                if (Countable(chest, countNotOwned)) __result += LikedItems.CountAvailable(LoadedInventory(chest), name, quality, matchWorldLevel);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
    private static class CountForRecipe
    {
        private static void Prefix(Recipe piece, out bool __state)
        {
            __state = !piece.m_requireOnlyOneIngredient;
            if (__state) _countScope++;
        }

        private static void Finalizer(bool __state)
        {
            if (__state) _countScope--;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    private static class CountForPiece
    {
        private static void Prefix() => _countScope++;
        private static void Finalizer() => _countScope--;
    }

    [HarmonyPatch]
    private static class CountChestsNotOwnedInUi
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(InventoryGui), "UpdateRecipe");
            yield return AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");
            yield return AccessTools.Method(typeof(Hud), "UpdateBuild");
            yield return AccessTools.Method(typeof(BuildUiPieceButton), nameof(BuildUiPieceButton.UpdateRequirements));
        }

        private static void Prefix() => _uiDepth++;
        private static void Finalizer() => _uiDepth--;
    }

    // Set while the crafting panel draws a one-ingredient recipe, which crafts from the inventory only.
    private static bool _drawingOneIngredientRecipe;

    [HarmonyPatch(typeof(InventoryGui), "SetupRequirementList")]
    private static class NoChestCountsForOneIngredientRecipe
    {
        private static void Prefix(InventoryGui __instance)
        {
            var recipe = SelectedRecipeGetter.Invoke(SelectedRecipeField.GetValue(__instance), null) as Recipe;
            _drawingOneIngredientRecipe = recipe != null && recipe.m_requireOnlyOneIngredient;
        }

        private static void Finalizer() => _drawingOneIngredientRecipe = false;
    }

    // Requirement icons in the crafting panel and the build HUD: the amount stops flashing red when chests cover
    // it, and reads "have/need" instead of vanilla's "need". The postfix runs before the finalizer, so its count
    // includes the chests too.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    private static class CountForDisplay
    {
        private static void Prefix(out bool __state)
        {
            __state = !_drawingOneIngredientRecipe;
            if (__state) _countScope++;
        }

        private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, int quality,
            int craftMultiplier, bool __result, bool __state)
        {
            if (!__state || !__result || !Active(player) || !req.m_resItem) return;
            var have = player.GetInventory().CountItems(req.m_resItem.m_itemData.m_shared.m_name);
            var need = req.GetAmount(quality) * craftMultiplier;
            elementRoot.Find("res_amount").GetComponent<TMP_Text>().text = $"{have}/{need}";
        }

        private static void Finalizer(bool __state)
        {
            if (__state) _countScope--;
        }
    }

    // ---------------------------------------------------------------- owned chests only right before consuming

    // Message for a shortfall that only chests this peer doesn't own could cover, while DoCrafting or UpdatePlacement
    // runs; null otherwise.
    private static string? _shortMessage;
    private static bool _showShortMessage;

    // DoCrafting runs inside UpdateRecipe, so the UI depth is parked while it runs; the UpdateCraftingPanel it calls
    // afterwards counts as UI again.
    private static int BeginOwnedOnly(string shortMessage)
    {
        var uiDepth = _uiDepth;
        _uiDepth = 0;
        _shortMessage = shortMessage;
        _showShortMessage = false;
        return uiDepth;
    }

    private static void EndOwnedOnly(int uiDepth)
    {
        // Shown last, so it replaces vanilla's "missing requirement".
        var player = Player.m_localPlayer;
        if (_showShortMessage && _shortMessage != null && player != null) player.Message(MessageHud.MessageType.Center, _shortMessage);
        _showShortMessage = false;
        _shortMessage = null;
        _uiDepth = uiDepth;
    }

    private static bool InOwnedOnlyCheck(Player player) => _shortMessage != null && _uiDepth == 0 && Active(player);

    private static bool PassesCountingChestsNotOwned(Func<bool> check)
    {
        _uiDepth++;
        try { return check(); }
        finally { _uiDepth--; }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    private static class CraftFromOwnedChestsOnly
    {
        private static void Prefix(out int __state) => __state = BeginOwnedOnly(ChestBusyMessage);

        // Runs however DoCrafting ended: what the craft didn't use goes back.
        private static void Finalizer(int __state)
        {
            EndOwnedOnly(__state);
            ReturnHeld();
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    private static class PlaceFromOwnedChestsOnly
    {
        private static void Prefix(Player __instance, out int __state)
        {
            RequestChestsForSelectedPiece(__instance);
            __state = BeginOwnedOnly(FetchingMessage);
        }

        private static void Finalizer(int __state) => EndOwnedOnly(__state);
    }

    // The craft failed only because chests this peer doesn't own hold the rest: the hand-over hasn't arrived, or
    // another game took them back. Vanilla fails silently here and takes nothing.
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Recipe), typeof(bool), typeof(int), typeof(int))]
    private static class ExplainBusyChestOnCraft
    {
        private static void Postfix(Player __instance, Recipe recipe, bool discover, int qualityLevel, int amount, bool __result)
        {
            if (__result || discover || !InOwnedOnlyCheck(__instance) || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)) return;
            _showShortMessage = PassesCountingChestsNotOwned(() => __instance.HaveRequirements(recipe, discover, qualityLevel, amount));
        }
    }

    // The place click fell short only because chests this peer doesn't own hold the rest: ask for them, so the next
    // click works.
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    private static class FetchChestsOnShortPlaceClick
    {
        private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, bool __result)
        {
            if (__result || mode != Player.RequirementMode.CanBuild || !InOwnedOnlyCheck(__instance)) return;
            if (!PassesCountingChestsNotOwned(() => __instance.HaveRequirements(piece, mode))) return;
            _showShortMessage = true;
            RequestChestsHolding(piece.m_resources);
        }
    }

    // ---------------------------------------------------------------- asking for ownership

    // Chests asked for with RPC_RequestOpen whose response hasn't arrived: it must not open the chest UI.
    private static readonly HashSet<Container> RequestedByUs = new();

    private static void RequestChestsHolding(Piece.Requirement[] requirements)
    {
        var player = Player.m_localPlayer;
        if (!Active(player)) return;
        foreach (var chest in ChestsInRange())
        {
            if (!Busy(chest) && !IsOwned(chest) && HoldsAny(LoadedInventory(chest), requirements)) RequestChest(chest);
        }
    }

    // Asks the chest's owner to hand it over, the way opening it does, without showing the chest. False when it is
    // not owned here and can't be asked for now. QuickStack and Restock ask through this too.
    internal static bool RequestChest(Container chest)
    {
        var player = Player.m_localPlayer;
        var view = ContainerView(chest);
        if (player == null || view == null || !view.IsValid()) return false;
        if (view.IsOwner()) return true;
        // Owner 0: the server assigns one within a few seconds, and an RPC to 0 would go to every peer.
        if (view.GetZDO().GetOwner() == 0 || OpenElsewhere(view)) return false;
        RequestedByUs.Add(chest);
        view.InvokeRPC("RPC_RequestOpen", player.GetPlayerID());
        return true;
    }

    private static Piece? _requestedForPiece;

    // Selecting a build piece, or taking out the tool, asks for its chests so the first click normally works.
    private static void RequestChestsForSelectedPiece(Player player)
    {
        if (player != Player.m_localPlayer) return;
        var piece = Active(player) && player.InPlaceMode() ? player.GetSelectedPiece() : null;
        if (piece == _requestedForPiece) return;
        _requestedForPiece = piece;
        if (piece != null) RequestChestsHolding(piece.m_resources);
    }

    [HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
    private static class HideResponseToOurRequest
    {
        private static bool Prefix(Container __instance) => !RequestedByUs.Remove(__instance);
    }

    // A real E press must open the chest even while our request for it is on its way.
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    private static class OpenOnRealInteract
    {
        private static void Prefix(Container __instance) => RequestedByUs.Remove(__instance);
    }

    // ---------------------------------------------------------------- holding a running craft's materials

    // A class, not a tuple: the game's runtime has no System.ValueTuple.
    private sealed class HeldItem
    {
        public readonly Container Chest;
        public readonly ItemDrop.ItemData Item;
        public HeldItem(Container chest, ItemDrop.ItemData item) { Chest = chest; Item = item; }
    }

    // Taken out of chests on the Craft click, with the chest each came from; in no inventory until DoCrafting consumes
    // them. Counted as the player's own by the requirement checks. Lost only if the game crashes during the craft.
    private static readonly List<HeldItem> Held = new();
    // What the running craft still lacks, by item name, until chests this peer doesn't own yet arrive; null while no
    // craft holds anything.
    private static Dictionary<string, int>? _stillMissing;

    // What the inventory lacks for these requirements, by item name, with the filters vanilla ConsumeResources uses:
    // at a station only its upgrader (or non-upgrader) resources, elsewhere no upgrader resources. Null when nothing.
    private static Dictionary<string, int>? Missing(Player player, Piece.Requirement[] requirements, int qualityLevel,
        int itemQuality, int multiplier)
    {
        Dictionary<string, int>? missing = null;
        var station = player.GetCurrentCraftingStation();
        var inventory = player.GetInventory();
        foreach (var requirement in requirements)
        {
            if (station != null && station.m_upgrader != requirement.m_upgraderResource) continue;
            if (station == null && requirement.m_upgraderResource) continue;
            if (!requirement.m_resItem) continue;
            var name = requirement.m_resItem.m_itemData.m_shared.m_name;
            var lacking = requirement.GetAmount(qualityLevel) * multiplier - inventory.CountItems(name, itemQuality);
            if (lacking <= 0) continue;
            missing ??= new Dictionary<string, int>();
            missing.TryGetValue(name, out var earlier);
            missing[name] = earlier + lacking;
        }
        return missing;
    }

    private static bool StillMissing(Piece.Requirement requirement) =>
        _stillMissing != null && requirement.m_resItem && _stillMissing.ContainsKey(requirement.m_resItem.m_itemData.m_shared.m_name);

    // Inventory.CountItems' filters over the held items.
    private static bool Matches(ItemDrop.ItemData item, string name, int quality, bool matchWorldLevel) =>
        item.m_shared.m_name == name && (quality < 0 || item.m_quality == quality) &&
        (!matchWorldLevel || item.m_worldLevel >= Game.m_worldLevel);

    private static int CountHeld(string name, int quality, bool matchWorldLevel)
    {
        var count = 0;
        foreach (var held in Held) if (Matches(held.Item, name, quality, matchWorldLevel)) count += held.Item.m_stack;
        return count;
    }

    // Moves what the craft still lacks out of the chests this peer owns, in range order.
    private static void HoldFromOwnedChests()
    {
        if (_stillMissing == null) return;
        foreach (var name in new List<string>(_stillMissing.Keys))
        {
            var missing = _stillMissing[name];
            foreach (var chest in ChestsInRange())
            {
                if (missing <= 0) break;
                if (!IsOwned(chest)) continue;
                var inventory = LoadedInventory(chest);
                foreach (var item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
                {
                    if (missing <= 0) break;
                    if (!Matches(item, name, -1, matchWorldLevel: true)) continue;
                    // A liked stack keeps its last unit, which marks the chest for quick stacking.
                    var take = Mathf.Min(missing, item.m_stack - LikedItems.Reserved(item));
                    if (take <= 0) continue;
                    var taken = item.Clone();
                    taken.m_stack = take;
                    LikedItems.Unlike(taken);
                    if (!inventory.RemoveItem(item, take)) continue;
                    Held.Add(new HeldItem(chest, taken));
                    missing -= take;
                }
            }
            if (missing > 0) _stillMissing[name] = missing;
            else _stillMissing.Remove(name);
        }
    }

    // Each held item goes back to its chest if this peer still owns it and it has room, else to the inventory, else
    // to the ground.
    private static void ReturnHeld()
    {
        _stillMissing = null;
        if (Held.Count == 0) return;
        var player = Player.m_localPlayer;
        // A client that lost the server (Game.FixedUpdate logs out, Shutdown runs this) can no longer send the chest or
        // a drop: the server peer is already gone when ZDOMan flushes, so a chest return was always lost. The
        // inventory is saved by SavePlayerProfile right after. The take reached the server long before the timeout
        // that ends the connection (ZDOs go out every 0.05 s), so this doubles the materials only if the connection
        // was already dead when Craft was clicked.
        var offline = ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.instance.GetServerPeer() == null;
        foreach (var held in Held)
        {
            var chest = held.Chest;
            var item = held.Item;
            if (!offline && IntoChest(chest, item)) continue;
            if (player != null && player.GetInventory().CanAddItem(item) && player.GetInventory().AddItem(item)) continue;
            if (offline)
            {
                Plugin.Log.LogWarning($"CraftFromContainers: {item.m_stack} x {item.m_shared.m_name} could not be returned while disconnected");
                continue;
            }
            var position = player != null ? player.transform.position + Vector3.up : chest != null ? chest.transform.position + Vector3.up : (Vector3?)null;
            if (position == null) { Plugin.Log.LogWarning($"CraftFromContainers: {item.m_stack} x {item.m_shared.m_name} could not be returned"); continue; }
            ItemDrop.DropItem(item, item.m_stack, position.Value, Quaternion.identity);
        }
        Held.Clear();
    }

    private static bool IntoChest(Container chest, ItemDrop.ItemData item)
    {
        if (chest == null) return false;
        var view = ContainerView(chest);
        if (view == null || !view.IsValid() || !view.IsOwner()) return false;
        var inventory = LoadedInventory(chest);
        return inventory.CanAddItem(item) && inventory.AddItem(item);
    }

    // Vanilla starts the craft timer here and consumes at its end; the materials leave the chests now instead, so a
    // chest is only busy for the hand-over, not for the whole craft. Not for one-ingredient recipes, which never use
    // chests.
    [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
    private static class HoldOnCraftClick
    {
        private static void Postfix(InventoryGui __instance)
        {
            var recipe = CraftRecipe(__instance);
            var player = Player.m_localPlayer;
            if (CraftTimer(__instance) < 0f || recipe == null || recipe.m_requireOnlyOneIngredient || !Active(player)) return;
            ReturnHeld();
            if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)) return;
            var upgrade = CraftUpgradeItem(__instance);
            var quality = upgrade == null ? 1 : upgrade.m_quality + 1;
            var multiplier = MultiCrafting(__instance) ? __instance.m_multiCraftAmount : 1;
            _stillMissing = Missing(player, recipe.m_resources, quality, -1, multiplier) ?? new Dictionary<string, int>();
            HoldFromOwnedChests();
            // Only chests holding what is still missing: asking takes them from other players and linked pieces.
            if (_stillMissing.Count > 0) RequestChestsHolding(Array.FindAll(recipe.m_resources, StillMissing));
        }
    }

    // Takes from chests as they arrive, and returns everything once the craft timer stopped without DoCrafting:
    // cancelled, the inventory closed, the player died or walked away.
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    private static class FollowHeldCraft
    {
        private static void Postfix(InventoryGui __instance)
        {
            if (_stillMissing == null && Held.Count == 0) return;
            if (CraftTimer(__instance) < 0f) ReturnHeld();
            else if (_stillMissing is { Count: > 0 } && Active(Player.m_localPlayer)) HoldFromOwnedChests();
        }
    }

    [HarmonyPatch(typeof(Game), "Shutdown")]
    private static class ReturnHeldOnShutdown
    {
        private static void Prefix() => ReturnHeld();
    }

    // ---------------------------------------------------------------- consuming

    // The prefix measures what the inventory lacks with the same filters RemoveItem uses; vanilla then takes what
    // the inventory has and the postfix takes the rest from the held materials, then from owned chests.
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    private static class TakeFromChests
    {
        private static void Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality,
            int multiplier, out Dictionary<string, int>? __state)
        {
            __state = Active(__instance) ? Missing(__instance, requirements, qualityLevel, itemQuality, multiplier) : null;
        }

        private static void Postfix(int itemQuality, Dictionary<string, int>? __state)
        {
            if (__state == null) return;
            foreach (var pair in __state)
            {
                var name = pair.Key;
                var missing = TakeHeld(name, itemQuality, pair.Value);
                foreach (var chest in ChestsInRange())
                {
                    if (missing <= 0) break;
                    if (!IsOwned(chest)) continue;
                    var inventory = LoadedInventory(chest);
                    var take = Mathf.Min(missing, LikedItems.CountAvailable(inventory, name, itemQuality));
                    if (take <= 0) continue;
                    LikedItems.RemoveAvailable(inventory, name, take, itemQuality);
                    missing -= take;
                }
                // The owned-only check before consuming counted these, so the check and this loop disagree (another
                // mod's check, or a game change) and the craft or piece was only partly paid.
                if (missing > 0) Plugin.Log.LogWarning($"CraftFromContainers: {missing} x {name} could not be taken from nearby chests");
            }
        }
    }

    // Consumes up to amount of the held materials with Inventory.RemoveItem's filters; returns what is still owed.
    private static int TakeHeld(string name, int itemQuality, int amount)
    {
        for (var i = 0; i < Held.Count && amount > 0; i++)
        {
            var item = Held[i].Item;
            if (!Matches(item, name, itemQuality, matchWorldLevel: true)) continue;
            var take = Mathf.Min(amount, item.m_stack);
            item.m_stack -= take;
            amount -= take;
        }
        Held.RemoveAll(held => held.Item.m_stack <= 0);
        return amount;
    }
}
