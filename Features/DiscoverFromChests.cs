using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// Picking an item up learns it through Player.AddKnownItem (Player.OnInventoryChanged), which also unlocks every recipe
// and build piece whose materials are now all known. Every container (chest, cart, ship, tombstone) opens through
// InventoryGui.Show once the owner grants it (Container.RPC_OpenResponse); the plain inventory and crafting stations
// pass no container. A prefix, because Show builds the crafting list (SetupCrafting) from the recipes known by then.
// AddKnownItem only touches the local player, so nothing is sent to the server or other players.
internal static class DiscoverFromChests
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "DiscoverFromChests",
            true,
            Plugin.Synced(
                "Opening a chest, cart, ship or tombstone teaches you every item inside as if you had picked it up, " +
                "so recipes and build pieces that need them unlock without taking anything out."));
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    private static class Patch
    {
        private static void Prefix(Container? container)
        {
            if (!_enabled.Value || container == null || Player.m_localPlayer == null) return;
            foreach (var item in container.GetInventory().GetAllItems()) Player.m_localPlayer.AddKnownItem(item);
        }
    }
}
