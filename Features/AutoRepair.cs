using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// UpdateRepair runs every frame while the crafting panel is open and enables the repair button
// only when the current station can repair something; then repair everything it can at once.
[HarmonyPatch(typeof(InventoryGui), "UpdateRepair")]
internal static class AutoRepair
{
    private static ConfigEntry<bool> _enabled = null!;

    private static readonly Func<InventoryGui, ItemDrop.ItemData, bool> CanRepair =
        AccessTools.MethodDelegate<Func<InventoryGui, ItemDrop.ItemData, bool>>(AccessTools.Method(typeof(InventoryGui), "CanRepair"));
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

        Player player = Player.m_localPlayer;
        // Mirrors InventoryGui.RepairOneItem for all items, one effect and message; re-check after a game patch.
        var repairable = new List<ItemDrop.ItemData>();
        player.GetInventory().GetWornItems(repairable);
        repairable.RemoveAll(item => !CanRepair(__instance, item));
        foreach (ItemDrop.ItemData item in repairable)
        {
            player.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
            item.m_durability = item.GetMaxDurability();
        }

        CraftingStation station = player.GetCurrentCraftingStation();
        if (station != null) station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
        player.Message(MessageHud.MessageType.Center, repairable.Count == 1
            ? Localization.instance.Localize("$msg_repaired", repairable[0].m_shared.m_name)
            : $"{repairable.Count} items repaired");
        UpdateCraftingPanel(__instance, false);
    }
}
