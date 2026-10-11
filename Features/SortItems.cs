using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// O (or the S button under the weight) sorts the inventory; with a chest open, O and the Sort button in the container
// panel sort the chest. Partial stacks of the same item are merged first, then items are laid out from the top left,
// grouped by kind (weapons, shields, tools, armor, utility, ammo, food, fish, materials, trophies, the rest) and by name.
//
// Never moved: the hotbar row, rows other mods added below the inventory (Better Archery's quiver) and liked stacks in
// the inventory, liked stacks in a chest. Equipped items move, they stay equipped. A chest is only sorted while this
// game owns it, which it does while the chest is open.
internal static class SortItems
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<KeyboardShortcut> _key = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "SortItems",
            true,
            Plugin.Synced(
                "SortKey (or the S button under your weight) sorts your inventory by kind and name and merges " +
                "partial stacks; with a chest open it sorts the chest. The hotbar and liked items keep their slots."));
        _key = config.Bind("Inventory", "SortKey", new KeyboardShortcut(KeyCode.O),
            "Key for sorting. Works while the inventory is open.");

        QuickStack.AddMiniButton("VU_SortInventory", "S", () => _enabled.Value, SortInventory);
        QuickStack.AddContainerButton("VU_SortContainer", "Sort", () => _enabled.Value && QuickStack.OpenChest() != null, SortOpenChest);
    }

    private static void SortInventory()
    {
        var player = Player.m_localPlayer;
        if (player == null) return;
        var inventory = player.GetInventory();
        var rows = QuickStack.PlayerRows(player);
        // Equipped stacks never give units away: emptied, they would stay equipped while gone from the inventory.
        Sort(inventory, firstRow: 1, rows,
            stays: item => item.m_gridPos.y == 0 || item.m_gridPos.y >= rows || LikedItems.IsLiked(inventory, item),
            canGive: item => !QuickStack.IsEquipped(player, item));
    }

    private static void SortOpenChest()
    {
        var chest = QuickStack.OpenChest();
        if (chest == null) return;
        var inventory = chest.GetInventory();
        Sort(inventory, firstRow: 0, inventory.GetHeight(), stays: item => LikedItems.IsLiked(inventory, item), canGive: _ => true);
    }

    // Stacks that stay keep their slot and are never merged in the inventory; in a chest they still take units, so a
    // liked chest stack fills up first. Items are laid out in rows firstRow to rows - 1.
    private static void Sort(Inventory inventory, int firstRow, int rows, Func<ItemDrop.ItemData, bool> stays, Func<ItemDrop.ItemData, bool> canGive)
    {
        QuickStack.CancelDrag();
        var inChest = firstRow == 0;
        QuickStack.MergeStacks(inventory, item => !stays(item) && canGive(item), item => inChest || !stays(item));

        var width = inventory.GetWidth();
        var height = rows;
        var taken = new bool[width, height];
        foreach (var item in inventory.GetAllItems())
        {
            var position = item.m_gridPos;
            if (stays(item) && position.x >= 0 && position.x < width && position.y >= 0 && position.y < height)
                taken[position.x, position.y] = true;
        }
        var moving = inventory.GetAllItems().Where(item => !stays(item)).ToList();
        moving.Sort(Compare);
        var x = 0;
        var y = firstRow;
        foreach (var item in moving)
        {
            while (y < height && taken[x, y])
            {
                if (++x < width) continue;
                x = 0;
                y++;
            }
            // Only when items overlapped before sorting: those keep their slot.
            if (y >= height) break;
            item.m_gridPos = new Vector2i(x, y);
            taken[x, y] = true;
        }
        QuickStack.Changed(inventory);
        QuickStack.RefreshCrafting();
    }

    private static int Kind(ItemDrop.ItemData item) => item.m_shared.m_itemType switch
    {
        ItemDrop.ItemData.ItemType.OneHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeapon or
            ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft or ItemDrop.ItemData.ItemType.Bow or
            ItemDrop.ItemData.ItemType.Attach_Atgeir or ItemDrop.ItemData.ItemType.Torch => 0,
        ItemDrop.ItemData.ItemType.Shield => 1,
        ItemDrop.ItemData.ItemType.Tool => 2,
        ItemDrop.ItemData.ItemType.Helmet or ItemDrop.ItemData.ItemType.Chest or ItemDrop.ItemData.ItemType.Legs or
            ItemDrop.ItemData.ItemType.Shoulder or ItemDrop.ItemData.ItemType.Hands => 3,
        ItemDrop.ItemData.ItemType.Utility or ItemDrop.ItemData.ItemType.Trinket => 4,
        ItemDrop.ItemData.ItemType.Ammo or ItemDrop.ItemData.ItemType.AmmoNonEquipable => 5,
        ItemDrop.ItemData.ItemType.Consumable => 6,
        ItemDrop.ItemData.ItemType.Fish => 7,
        ItemDrop.ItemData.ItemType.Material => 8,
        ItemDrop.ItemData.ItemType.Trophy => 9,
        _ => 10,
    };

    private static int Compare(ItemDrop.ItemData a, ItemDrop.ItemData b)
    {
        var result = Kind(a).CompareTo(Kind(b));
        if (result != 0) return result;
        result = string.Compare(Localization.instance.Localize(a.m_shared.m_name),
            Localization.instance.Localize(b.m_shared.m_name), StringComparison.CurrentCultureIgnoreCase);
        if (result != 0) return result;
        result = b.m_quality.CompareTo(a.m_quality);
        if (result != 0) return result;
        result = b.m_stack.CompareTo(a.m_stack);
        if (result != 0) return result;
        // List.Sort is unstable: the old slot decides ties, so sorting twice changes nothing.
        result = a.m_gridPos.y.CompareTo(b.m_gridPos.y);
        return result != 0 ? result : a.m_gridPos.x.CompareTo(b.m_gridPos.x);
    }

    [HarmonyPatch(typeof(Player), "Update")]
    private static class Keys
    {
        private static void Postfix(Player __instance)
        {
            if (!_enabled.Value || __instance != Player.m_localPlayer || !InventoryGui.IsVisible()) return;
            if (!QuickStack.Pressed(_key.Value) || !QuickStack.KeysAllowed(__instance)) return;
            var gui = InventoryGui.instance;
            if (gui != null && gui.IsContainerOpen()) SortOpenChest();
            else SortInventory();
        }
    }
}
