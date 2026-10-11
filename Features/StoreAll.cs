using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// A Store all button in the container panel (and StoreAllKey, unbound by default) moves everything from the
// inventory into the open chest, stackable or not, filling stacks first and merging partial stacks afterwards. The
// hotbar row, equipped and quest items and liked stacks stay, as for QuickStack.
internal static class StoreAll
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<KeyboardShortcut> _key = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "StoreAll",
            true,
            "Adds a Store all button to chests: everything in your inventory goes into the open chest, except the " +
            "hotbar, equipped and liked items.");
        _key = config.Bind("Inventory", "StoreAllKey", KeyboardShortcut.Empty,
            "Optional key for Store all while a chest is open. Unbound by default.");

        QuickStack.AddContainerButton("VU_StoreAll", "Store all", () => _enabled.Value && QuickStack.OpenChest() != null, Run);
    }

    private static void Run()
    {
        var player = Player.m_localPlayer;
        var chest = QuickStack.OpenChest();
        if (player != null && chest != null) QuickStack.Store(player, new List<Inventory> { chest.GetInventory() }, 0, everything: true);
    }

    [HarmonyPatch(typeof(Player), "Update")]
    private static class Keys
    {
        private static void Postfix(Player __instance)
        {
            if (!_enabled.Value || __instance != Player.m_localPlayer || !InventoryGui.IsVisible()) return;
            if (QuickStack.Pressed(_key.Value) && QuickStack.KeysAllowed(__instance)) Run();
        }
    }
}
