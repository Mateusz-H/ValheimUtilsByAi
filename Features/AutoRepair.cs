using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// UpdateRepair runs every frame while the crafting panel is open and enables the repair button
// only when the current station can repair something; repair one item per frame while it is.
[HarmonyPatch(typeof(InventoryGui), "UpdateRepair")]
internal static class AutoRepair
{
    private static ConfigEntry<bool> _enabled = null!;

    private static readonly Action<InventoryGui> RepairOneItem =
        AccessTools.MethodDelegate<Action<InventoryGui>>(AccessTools.Method(typeof(InventoryGui), "RepairOneItem"));
    private static readonly Action<InventoryGui, bool> UpdateCraftingPanel =
        AccessTools.MethodDelegate<Action<InventoryGui, bool>>(AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel"));

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "AutoRepair",
            true,
            "Opening a crafting station repairs every item it can repair, without clicking the repair button.");
    }

    private static void Postfix(InventoryGui __instance)
    {
        if (!_enabled.Value || !__instance.m_repairButton.gameObject.activeSelf || !__instance.m_repairButton.interactable) return;
        RepairOneItem(__instance);
        UpdateCraftingPanel(__instance, false);
    }
}
