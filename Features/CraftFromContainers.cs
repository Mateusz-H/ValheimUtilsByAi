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
// only to their builder. An owner with the mod also refuses while its own craft is running and the chest holds one of
// that recipe's materials, so a hand-over never breaks a craft in progress.
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
            "In multiplayer a chest another player has open or is crafting from is skipped until they finish; " +
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

    // Vanilla Container.CheckAccess: public chests for everyone, private ones for their builder.
    private static bool HasAccess(Container chest, long playerID) => chest.m_privacy switch
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
        var player = Player.m_localPlayer;
        if (player == null) return InRange;
        var playerID = player.GetPlayerID();
        var position = player.transform.position;
        for (var i = Chests.Count - 1; i >= 0; i--)
        {
            var chest = Chests[i];
            if (chest == null) { Chests.RemoveAt(i); RequestedByUs.Remove(chest!); continue; }
            var view = ContainerView(chest);
            if (!view.IsValid() || Vector3.Distance(chest.transform.position, position) > _radius.Value) continue;
            if (!chest.GetComponent<Piece>().IsPlacedByPlayer() || !HasAccess(chest, playerID)) continue;
            if (chest.m_checkGuardStone && !PrivateArea.CheckAccess(chest.transform.position, 0f, flash: false)) continue;
            InRange.Add(chest);
        }
        return InRange;
    }

    private static bool IsOwned(Container chest) => ContainerView(chest).IsOwner();

    // Open in another player's UI: its contents are about to change and vanilla won't hand it over.
    private static bool OpenElsewhere(ZNetView view) => view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;

    private static bool Countable(Container chest, bool countNotOwned)
    {
        var view = ContainerView(chest);
        return view.IsValid() && (view.IsOwner() || (countNotOwned && !OpenElsewhere(view)));
    }

    // Load picks up data that arrived since the last Container.CheckForChanges (once a second); an owner's own edits
    // are always current.
    private static Inventory LoadedInventory(Container chest)
    {
        ContainerLoad(chest);
        return chest.GetInventory();
    }

    private static bool HoldsAny(Inventory inventory, Piece.Requirement[] requirements)
    {
        foreach (var requirement in requirements)
        {
            if (requirement.m_resItem && inventory.HaveItem(requirement.m_resItem.m_itemData.m_shared.m_name)) return true;
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
            var countNotOwned = _uiDepth > 0;
            foreach (var chest in ChestsInRange())
            {
                if (Countable(chest, countNotOwned)) __result += LoadedInventory(chest).CountItems(name, quality, matchWorldLevel);
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
        private static void Finalizer(int __state) => EndOwnedOnly(__state);
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

    // The craft failed only because chests this peer doesn't own hold the rest: their owner is crafting from them, or
    // the hand-over hasn't arrived. Vanilla fails silently here and takes nothing.
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
            var view = ContainerView(chest);
            // Owner 0: the server assigns one within a few seconds, and an RPC to 0 would go to every peer.
            if (!view.IsValid() || view.IsOwner() || view.GetZDO().GetOwner() == 0 || OpenElsewhere(view)) continue;
            if (!HoldsAny(LoadedInventory(chest), requirements)) continue;
            RequestedByUs.Add(chest);
            view.InvokeRPC("RPC_RequestOpen", player.GetPlayerID());
        }
    }

    // The craft timer (0.8-2 s, longer for multi-craft and upgrades) normally covers the hand-over.
    [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
    private static class RequestChestsForCraft
    {
        private static void Postfix(InventoryGui __instance)
        {
            var recipe = CraftRecipe(__instance);
            if (CraftTimer(__instance) >= 0f && recipe != null) RequestChestsHolding(recipe.m_resources);
        }
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

    // ---------------------------------------------------------------- owner side

    // True while the local player's craft is running and this chest, in range, holds one of that recipe's materials:
    // handing it over now would fail the craft when its timer ends. AutoStorage asks for chests with its own RPC and
    // should refuse in this case too.
    internal static bool IsCraftingFrom(Container chest)
    {
        var gui = InventoryGui.instance;
        if (!Active(Player.m_localPlayer) || gui == null || CraftTimer(gui) < 0f) return false;
        var recipe = CraftRecipe(gui);
        return recipe != null && ChestsInRange().Contains(chest) && HoldsAny(chest.GetInventory(), recipe.m_resources);
    }

    // Vanilla already refuses while the chest is open. uid is the asking peer; asking ourselves stays vanilla.
    [HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
    private static class KeepChestWhileCrafting
    {
        private static bool Prefix(Container __instance, long uid)
        {
            var view = ContainerView(__instance);
            if (uid == ZNet.GetUID() || !view.IsValid() || !view.IsOwner() || !IsCraftingFrom(__instance)) return true;
            view.InvokeRPC(uid, "RPC_OpenResponse", false);
            return false;
        }
    }

    // ---------------------------------------------------------------- consuming

    // The prefix measures what the inventory lacks with the same filters RemoveItem uses; vanilla then takes what
    // the inventory has and the postfix takes the rest from owned chests. Same requirement filter as vanilla.
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    private static class TakeFromChests
    {
        private static void Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality,
            int multiplier, out Dictionary<string, int>? __state)
        {
            __state = null;
            if (!Active(__instance)) return;
            var station = __instance.GetCurrentCraftingStation();
            var inventory = __instance.GetInventory();
            foreach (var requirement in requirements)
            {
                if (station != null && station.m_upgrader != requirement.m_upgraderResource) continue;
                if (station == null && requirement.m_upgraderResource) continue;
                if (!requirement.m_resItem) continue;
                var name = requirement.m_resItem.m_itemData.m_shared.m_name;
                var missing = requirement.GetAmount(qualityLevel) * multiplier - inventory.CountItems(name, itemQuality);
                if (missing <= 0) continue;
                __state ??= new Dictionary<string, int>();
                __state.TryGetValue(name, out var earlier);
                __state[name] = earlier + missing;
            }
        }

        private static void Postfix(int itemQuality, Dictionary<string, int>? __state)
        {
            if (__state == null) return;
            foreach (var pair in __state)
            {
                var name = pair.Key;
                var missing = pair.Value;
                foreach (var chest in ChestsInRange())
                {
                    if (missing <= 0) break;
                    if (!IsOwned(chest)) continue;
                    var inventory = LoadedInventory(chest);
                    var take = Mathf.Min(missing, inventory.CountItems(name, itemQuality));
                    if (take <= 0) continue;
                    inventory.RemoveItem(name, take, itemQuality);
                    missing -= take;
                }
                // The owned-only check before consuming counted these, so the check and this loop disagree (another
                // mod's check, or a game change) and the craft or piece was only partly paid.
                if (missing > 0) Plugin.Log.LogWarning($"CraftFromContainers: {missing} x {name} could not be taken from nearby chests");
            }
        }
    }
}
