using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimUtilsByAi.Features;

// Alt + right click on an item in the inventory or in an open chest likes (or unlikes) that stack. The like lives in
// the item's m_customData: the game saves, clones and syncs it with the item, death moves the same objects into the
// tombstone, and players without the mod keep it untouched.
//
// The stored value says where the item was liked, and a like only counts there:
// - in the player's inventory, quick stack, store all, sort and trash leave the stack alone;
// - in a chest, crafting from chests, AutoStorage and restock never take its last unit, so the chest keeps holding that
//   item type for quick stacking, and sort leaves it in place.
// A like that doesn't count where the item is now is dropped once its grid is drawn, so moving a liked item between the
// inventory and a chest by hand unlikes it. Tombstones keep the likes of what they hold, so items picked up after death
// are still liked.
internal static class LikedItems
{
    private const string Key = "mh.ValheimUtilsByAi.Liked";
    private const string InInventory = "inventory";
    private const string InChest = "chest";
    private const string BorderName = "VU_LikedBorder";
    private static readonly Color BorderColor = new(1f, 0.85f, 0f);

    private static readonly AccessTools.FieldRef<InventoryGui, GameObject?> DragGo =
        AccessTools.FieldRefAccess<InventoryGui, GameObject?>("m_dragGo");
    private static readonly AccessTools.FieldRef<InventoryGui, Container?> CurrentContainer =
        AccessTools.FieldRefAccess<InventoryGui, Container?>("m_currentContainer");
    private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData?> CraftUpgradeItem =
        AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData?>("m_craftUpgradeItem");

    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "LikedItems",
            true,
            Plugin.Synced(
                "Alt + right click on an item likes it (gold border); click again to unlike. In your inventory quick " +
                "stack, store all, sort and trash leave a liked stack alone. In a chest a liked stack always keeps " +
                "at least one item when crafting from chests, AutoStorage or restock take from it, so quick stack " +
                "keeps finding that chest. When this is off no item counts as liked; the likes are kept."));
    }

    private static string? Flag(ItemDrop.ItemData item) =>
        item.m_customData != null && item.m_customData.TryGetValue(Key, out var value) ? value : null;

    private static bool IsPlayerInventory(Inventory inventory) =>
        Player.m_localPlayer != null && inventory == Player.m_localPlayer.GetInventory();

    // Liked where it is now: inventory is the inventory holding the item.
    internal static bool IsLiked(Inventory inventory, ItemDrop.ItemData item) =>
        _enabled.Value && Flag(item) == (IsPlayerInventory(inventory) ? InInventory : InChest);

    // Units of a chest stack nothing may take: the last one of a liked stack.
    internal static int Reserved(ItemDrop.ItemData item) => _enabled.Value && Flag(item) == InChest ? 1 : 0;

    internal static void Unlike(ItemDrop.ItemData item) => item.m_customData?.Remove(Key);

    // Inventory.CountItems without the reserved units.
    internal static int CountAvailable(Inventory inventory, string name, int quality = -1, bool matchWorldLevel = true)
    {
        var count = 0;
        foreach (var item in inventory.GetAllItems())
        {
            if (item.m_shared.m_name != name || (quality >= 0 && item.m_quality != quality)) continue;
            if (matchWorldLevel && item.m_worldLevel < Game.m_worldLevel) continue;
            count += Mathf.Max(0, item.m_stack - Reserved(item));
        }
        return count;
    }

    // Inventory.RemoveItem(name, amount, quality), in the same list order, leaving the reserved units. Returns how many
    // it removed.
    internal static int RemoveAvailable(Inventory inventory, string name, int amount, int quality = -1)
    {
        var removed = 0;
        var items = inventory.GetAllItems();
        for (var i = 0; i < items.Count && removed < amount; i++)
        {
            var item = items[i];
            if (item.m_shared.m_name != name || (quality >= 0 && item.m_quality != quality)) continue;
            if (item.m_worldLevel < Game.m_worldLevel) continue;
            var take = Mathf.Min(amount - removed, item.m_stack - Reserved(item));
            if (take <= 0) continue;
            inventory.RemoveItem(item, take);
            removed += take;
            if (i >= items.Count || items[i] != item) i--;
        }
        return removed;
    }

    private static int _skipReserved;

    // Runs find with Inventory.GetItem passing over stacks that hold nothing but their reserved unit.
    internal static ItemDrop.ItemData? SkippingReserved(Func<ItemDrop.ItemData?> find)
    {
        _skipReserved++;
        try { return find(); }
        finally { _skipReserved--; }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetItem), typeof(string), typeof(int), typeof(bool))]
    private static class SkipReservedStacks
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool isPrefabName, ref ItemDrop.ItemData? __result)
        {
            if (_skipReserved == 0 || __result == null || __result.m_stack > Reserved(__result)) return;
            __result = null;
            foreach (var item in __instance.GetAllItems())
            {
                var named = isPrefabName ? item.m_dropPrefab != null && item.m_dropPrefab.name == name : item.m_shared.m_name == name;
                if (!named || (quality >= 0 && item.m_quality != quality) || item.m_worldLevel < Game.m_worldLevel) continue;
                if (item.m_stack <= Reserved(item)) continue;
                __result = item;
                return;
            }
        }
    }

    private static bool InTombstone(InventoryGui gui, Inventory inventory)
    {
        var container = CurrentContainer(gui);
        return container != null && container.GetInventory() == inventory && container.GetComponent<TombStone>() != null;
    }

    // ---------------------------------------------------------------- Alt + right click

    // A right click otherwise uses the item (eat, equip). Alt + left click is left to other mods, such as MyLittleUI's
    // chat item links. A running drag is left to vanilla.
    [HarmonyPatch(typeof(InventoryGrid), "OnRightDown")]
    private static class ToggleOnAltClick
    {
        private static bool Prefix(InventoryGrid __instance, UIInputHandler element)
        {
            if (!_enabled.Value || !(ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt))) return true;
            var gui = InventoryGui.instance;
            var player = Player.m_localPlayer;
            if (gui == null || player == null || player.IsTeleporting() || DragGo(gui) != null) return true;
            var inventory = __instance.GetInventory();
            var slot = element.GetComponent<InventoryElement>();
            if (inventory == null || slot == null || InTombstone(gui, inventory)) return true;
            var item = inventory.GetItemAt(slot.Position.x, slot.Position.y);
            if (item == null) return true;
            if (IsLiked(inventory, item)) Unlike(item);
            else item.m_customData[Key] = IsPlayerInventory(inventory) ? InInventory : InChest;
            // Saves a chest: its grid is only shown while this game owns it.
            inventory.m_onChanged?.Invoke();
            return false;
        }
    }

    // ---------------------------------------------------------------- border, tooltip, stale likes

    // A gold frame over the slot, as a child of the slot's "queued" image the way QuickStackStore draws it: vanilla only
    // toggles that Image component, never its GameObject. Elements are rebuilt when the grid size changes, so the frame
    // is looked up every frame.
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    private static class DrawBorders
    {
        private static void Postfix(Inventory ___m_inventory, List<InventoryElement> ___m_elements)
        {
            var inventory = ___m_inventory;
            var gui = InventoryGui.instance;
            var bordered = new bool[___m_elements.Count];
            if (_enabled.Value && inventory != null && gui != null)
            {
                var liked = IsPlayerInventory(inventory) ? InInventory : InChest;
                var keepOthers = liked == InChest && InTombstone(gui, inventory);
                var width = inventory.GetWidth();
                var unliked = false;
                foreach (var item in inventory.GetAllItems())
                {
                    var flag = Flag(item);
                    if (flag == null) continue;
                    if (flag != liked)
                    {
                        if (keepOthers) continue;
                        Unlike(item);
                        unliked = true;
                        continue;
                    }
                    var index = item.m_gridPos.y * width + item.m_gridPos.x;
                    if (index >= 0 && index < bordered.Length) bordered[index] = true;
                }
                // The container grid is only drawn while this game owns the chest, so this saves it.
                if (unliked) inventory.m_onChanged?.Invoke();
            }
            for (var i = 0; i < bordered.Length; i++) SetBorder(___m_elements[i], bordered[i]);
        }
    }

    // Writes only changes: toggling an Image off and on every frame would rebuild the canvas every frame.
    private static void SetBorder(InventoryElement element, bool on)
    {
        var queued = element.m_queued;
        if (queued == null) return;
        var found = queued.transform.Find(BorderName);
        if (found == null && !on) return;
        var border = found != null ? found.GetComponent<Image>() : CreateBorder(queued);
        if (border.enabled != on) border.enabled = on;
    }

    private static Image CreateBorder(Image queued)
    {
        var border = UnityEngine.Object.Instantiate(queued, queued.transform.parent);
        foreach (Transform child in border.transform) UnityEngine.Object.Destroy(child.gameObject);
        border.name = BorderName;
        border.transform.SetParent(queued.transform, worldPositionStays: true);
        border.sprite = BorderSprite();
        border.type = Image.Type.Simple;
        border.color = BorderColor;
        border.raycastTarget = false;
        return border;
    }

    private static Sprite? _borderSprite;

    private static Sprite BorderSprite()
    {
        if (_borderSprite != null) return _borderSprite;
        const int size = 64, thickness = 4;
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var edge = x < thickness || y < thickness || x >= size - thickness || y >= size - thickness;
            pixels[y * size + x] = edge ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        _borderSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        return _borderSprite;
    }

    // Replaces vanilla's tooltip for a liked item instead of appending in a postfix: UITooltip.Set redraws only when the
    // text changes, and two Sets per frame would redraw it every frame.
    [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
    private static class TooltipLine
    {
        private static bool Prefix(InventoryGrid __instance, ItemDrop.ItemData item, UITooltip tooltip)
        {
            var inventory = __instance.GetInventory();
            if (inventory == null || !IsLiked(inventory, item)) return true;
            var line = IsPlayerInventory(inventory)
                ? "Liked: quick stack, store all, sort and trash skip it"
                : "Liked: crafting and AutoStorage leave the last one here";
            tooltip.Set(item.m_shared.m_name,
                item.GetTooltip() + $"\n<color=#{ColorUtility.ToHtmlStringRGB(BorderColor)}>{line}</color>",
                __instance.m_tooltipAnchor);
            return false;
        }
    }

    // ---------------------------------------------------------------- upgrades

    // An upgrade replaces the item with a new one, made by this AddItem overload, so the like is copied over. The
    // materials an upgrader station refunds when it breaks the item come through the same overload; only the item of
    // the upgraded kind is liked. Whatever the toggle, so upgrading while it is off loses no like.
    private static string? _upgradeLiked;

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    private static class KeepLikeOnUpgrade
    {
        private static void Prefix(InventoryGui __instance)
        {
            var item = CraftUpgradeItem(__instance);
            _upgradeLiked = item != null && Flag(item) == InInventory ? item.m_shared.m_name : null;
        }

        private static void Finalizer() => _upgradeLiked = null;
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(string), typeof(int), typeof(int), typeof(int),
        typeof(long), typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool))]
    private static class LikeUpgradedItem
    {
        private static void Postfix(ItemDrop.ItemData? __result)
        {
            if (_upgradeLiked != null && __result != null && __result.m_shared.m_name == _upgradeLiked)
                __result.m_customData[Key] = InInventory;
        }
    }

    // ---------------------------------------------------------------- merges into the inventory

    // Vanilla merges keep the receiving stack's data, so liked units merged into another stack would lose the like:
    // Take all on a tombstone (Inventory.MoveAll) merges into stacks picked up since respawning. Moving into the
    // player's inventory, the stack that received units of a liked stack is liked. Whatever the toggle, so nothing is
    // lost while it is off.
    // A class, not a tuple: the game's runtime has no System.ValueTuple.
    private sealed class LikedSource
    {
        public readonly ItemDrop.ItemData Item;
        public readonly int Stack;
        public readonly Vector2i Slot;
        public LikedSource(ItemDrop.ItemData item) { Item = item; Stack = item.m_stack; Slot = item.m_gridPos; }
    }

    private static List<LikedSource>? LikedSources(Inventory to, IEnumerable<ItemDrop.ItemData> items)
    {
        if (!IsPlayerInventory(to)) return null;
        List<LikedSource>? sources = null;
        foreach (var item in items)
        {
            if (Flag(item) == InInventory) (sources ??= new List<LikedSource>()).Add(new LikedSource(item));
        }
        return sources;
    }

    // slot: where a positional move put the units; null for MoveAll, which tries the item's own slot, then any stack.
    private static void Relike(Inventory to, List<LikedSource>? sources, Vector2i? slot)
    {
        if (sources == null) return;
        foreach (var source in sources)
        {
            var item = source.Item;
            // Nothing moved, or the object itself moved and is still liked.
            if (item.m_stack >= source.Stack && item.m_stack > 0 || to.ContainsItem(item)) continue;
            var at = slot ?? source.Slot;
            var target = to.GetItemAt(at.x, at.y);
            if (target == null || !QuickStack.SameStack(target, item))
                target = slot == null ? to.GetAllItems().Find(i => QuickStack.SameStack(i, item)) : null;
            if (target != null) target.m_customData[Key] = InInventory;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
    private static class KeepLikeOnMoveAll
    {
        private static void Prefix(Inventory __instance, Inventory fromInventory, out List<LikedSource>? __state) =>
            __state = LikedSources(__instance, fromInventory.GetAllItems());

        private static void Postfix(Inventory __instance, List<LikedSource>? __state) => Relike(__instance, __state, null);
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), typeof(Inventory), typeof(ItemDrop.ItemData))]
    private static class KeepLikeOnMove
    {
        private static void Prefix(Inventory __instance, ItemDrop.ItemData item, out List<LikedSource>? __state) =>
            __state = LikedSources(__instance, new[] { item });

        private static void Postfix(Inventory __instance, List<LikedSource>? __state) => Relike(__instance, __state, null);
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), typeof(Inventory), typeof(ItemDrop.ItemData),
        typeof(int), typeof(int), typeof(int))]
    private static class KeepLikeOnPositionalMove
    {
        private static void Prefix(Inventory __instance, ItemDrop.ItemData item, out List<LikedSource>? __state) =>
            __state = LikedSources(__instance, new[] { item });

        private static void Postfix(Inventory __instance, int x, int y, List<LikedSource>? __state) =>
            Relike(__instance, __state, new Vector2i(x, y));
    }
}
