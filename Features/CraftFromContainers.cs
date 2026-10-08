using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Materials in built chests near the local player count for crafting, upgrading and building, and are taken from
// them after the player's own inventory.
//
// No duplication: a chest is only counted or edited while this peer owns its ZDO, because only the owner's edits
// are saved (Container.OnContainerChanged) and ownership never changes inside one synchronous check-and-consume
// (InventoryGui.DoCrafting, Player.UpdatePlacement). Ownership of a chest owned by someone else is requested
// with an RPC, and that owner hands it over only while nobody has the chest open, the same rule as vanilla
// Container.RPC_RequestOpen. An owner without the mod ignores the request, so its chests are simply not used.
//
// Counting goes through Inventory.CountItems, scoped to the requirement checks and the requirement UI. Recipes
// that need only one of their ingredients are left vanilla: their consume path bypasses ConsumeResources.
internal static class CraftFromContainers
{
    private const string RequestOwnRpc = "mh_CraftFromContainers_RequestOwn";
    private const float MinRetry = 1f;
    private const float MaxRetry = 30f;

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _radius = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "CraftFromContainers",
            true,
            "Crafting, upgrading and building also use materials from chests near you, after your own inventory. " +
            "In multiplayer a chest someone has open is skipped until they close it.");
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

    // ---------------------------------------------------------------- chest registry and ownership hand-over

    private static readonly List<Container> Chests = new();

    // Player-built chests only: carts, ship storage, tombstones, the obliterator and loot crates are skipped.
    // Placement ghosts have no inventory. Registered regardless of the config so toggling works at runtime.
    [HarmonyPatch(typeof(Container), "Awake")]
    private static class RegisterChest
    {
        private static void Postfix(Container __instance)
        {
            if (__instance.GetInventory() == null) return;
            var view = ContainerView(__instance);
            if (view == null || !view.IsValid()) return;
            // Who built it is checked on use: Piece.Awake may run after this, and a new chest gets its creator after Awake.
            if (__instance.GetComponent<Piece>() == null) return;
            if (__instance.m_wagon != null || __instance.m_rootObjectOverride != null || __instance.m_autoDestroyEmpty) return;
            if (__instance.GetComponentInParent<Ship>() != null || __instance.GetComponentInParent<Incinerator>() != null) return;

            var chest = __instance;
            view.Register<long>(RequestOwnRpc, (sender, playerID) => RPC_RequestOwn(chest, sender, playerID));
            Chests.Add(chest);
        }
    }

    // Runs on the chest's owner. Not gated on the config: handing over a closed chest is what vanilla does when
    // another player opens it.
    private static void RPC_RequestOwn(Container chest, long sender, long playerID)
    {
        var view = ContainerView(chest);
        if (!view.IsValid() || !view.IsOwner() || chest.IsInUse() || !HasAccess(chest, playerID)) return;
        ZDOMan.instance.ForceSendZDO(sender, view.GetZDO().m_uid);
        view.GetZDO().SetOwner(sender);
    }

    // Vanilla Container.CheckAccess: public chests for everyone, private ones for their builder.
    private static bool HasAccess(Container chest, long playerID) => chest.m_privacy switch
    {
        Container.PrivacySetting.Public => true,
        Container.PrivacySetting.Private => chest.GetComponent<Piece>().GetCreator() == playerID,
        _ => false,
    };

    // Per chest: earliest time of the next request, and the current back-off (doubles up to MaxRetry so an owner
    // without the mod is not spammed).
    private static readonly Dictionary<Container, float> NextRequest = new();
    private static readonly Dictionary<Container, float> RequestDelay = new();

    private static void RequestOwnership(Container chest, ZNetView view)
    {
        var owner = view.GetZDO().GetOwner();
        // No owner: the server assigns one within a few seconds. A chest open elsewhere would refuse anyway.
        if (owner == 0 || view.GetZDO().GetInt(ZDOVars.s_inUse) == 1) return;
        if (NextRequest.TryGetValue(chest, out var next) && Time.time < next) return;
        RequestDelay.TryGetValue(chest, out var delay);
        delay = Mathf.Clamp(delay * 2f, MinRetry, MaxRetry);
        RequestDelay[chest] = delay;
        NextRequest[chest] = Time.time + delay;
        view.InvokeRPC(owner, RequestOwnRpc, Player.m_localPlayer.GetPlayerID());
    }

    // ---------------------------------------------------------------- usable chests

    private static readonly List<Container> Usable = new();
    private static int _usableFrame = -1;

    // Chests in range this peer may edit right now. Recomputed once per frame; ownership is re-checked on every
    // use because network messages can be processed later in the same frame.
    private static List<Container> UsableChests()
    {
        if (_usableFrame == Time.frameCount) return Usable;
        _usableFrame = Time.frameCount;
        Usable.Clear();
        var player = Player.m_localPlayer;
        if (player == null) return Usable;
        var playerID = player.GetPlayerID();
        var position = player.transform.position;
        for (var i = Chests.Count - 1; i >= 0; i--)
        {
            var chest = Chests[i];
            if (chest == null) { Chests.RemoveAt(i); NextRequest.Remove(chest!); RequestDelay.Remove(chest!); continue; }
            var view = ContainerView(chest);
            if (!view.IsValid() || Vector3.Distance(chest.transform.position, position) > _radius.Value) continue;
            if (!chest.GetComponent<Piece>().IsPlacedByPlayer() || !HasAccess(chest, playerID)) continue;
            if (chest.m_checkGuardStone && !PrivateArea.CheckAccess(chest.transform.position, 0f, flash: false)) continue;
            if (!view.IsOwner()) { RequestOwnership(chest, view); continue; }
            NextRequest.Remove(chest);
            RequestDelay.Remove(chest);
            Usable.Add(chest);
        }
        return Usable;
    }

    // Our own edits are always current; this picks up the data that arrived with a fresh hand-over.
    private static Inventory? Writable(Container chest)
    {
        if (chest == null || !ContainerView(chest).IsOwner()) return null;
        ContainerLoad(chest);
        return chest.GetInventory();
    }

    // ---------------------------------------------------------------- counting

    private static int _countScope;

    private static bool Active(Player player) =>
        _enabled.Value && player != null && player == Player.m_localPlayer && !player.NoCostCheat();

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
    private static class AddChestCounts
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            if (_countScope == 0 || name == null) return;
            var player = Player.m_localPlayer;
            if (!Active(player) || __instance != player.GetInventory()) return;
            foreach (var chest in UsableChests())
            {
                var inventory = Writable(chest);
                if (inventory != null) __result += inventory.CountItems(name, quality, matchWorldLevel);
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

    // Requirement icons in the crafting panel and the build HUD: the amount stops flashing red.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    private static class CountForDisplay
    {
        private static void Prefix() => _countScope++;
        private static void Finalizer() => _countScope--;
    }

    // ---------------------------------------------------------------- consuming

    // The prefix measures what the inventory lacks with the same filters RemoveItem uses; vanilla then takes what
    // the inventory has and the postfix takes the rest from chests. Same requirement filter as vanilla.
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
                foreach (var chest in UsableChests())
                {
                    if (missing <= 0) break;
                    var inventory = Writable(chest);
                    if (inventory == null) continue;
                    var take = Mathf.Min(missing, inventory.CountItems(name, itemQuality));
                    if (take <= 0) continue;
                    inventory.RemoveItem(name, take, itemQuality);
                    missing -= take;
                }
            }
        }
    }
}
