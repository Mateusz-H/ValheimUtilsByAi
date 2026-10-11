using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimUtilsByAi.Features;

// A trash can under the armor value in the inventory panel. Clicking it while dragging an item from the inventory, or
// pressing Delete while dragging, asks for confirmation and destroys the dragged amount. Liked, quest and equipped
// unstackable items are refused. The drag ends when the question opens, so nothing can be dropped behind it; Yes
// destroys what was dragged if it is still there, as it was.
//
// The can is a copy of the panel's "Armor" display, 78 px lower, the way QuickStackStore builds its own.
internal static class TrashItems
{
    private static readonly AccessTools.FieldRef<InventoryGui, GameObject?> DragGo =
        AccessTools.FieldRefAccess<InventoryGui, GameObject?>("m_dragGo");
    private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData?> DragItem =
        AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData?>("m_dragItem");
    private static readonly AccessTools.FieldRef<InventoryGui, Inventory?> DragInventory =
        AccessTools.FieldRefAccess<InventoryGui, Inventory?>("m_dragInventory");
    private static readonly AccessTools.FieldRef<InventoryGui, int> DragAmount =
        AccessTools.FieldRefAccess<InventoryGui, int>("m_dragAmount");

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<KeyboardShortcut> _key = null!;
    private static GameObject? _can;
    private static bool _noArmor;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "TrashItems",
            true,
            "Adds a trash can to the inventory panel. Click it while dragging an item from your inventory, or press " +
            "TrashKey while dragging, to destroy that item after a confirmation. Liked items and equipped gear can't be " +
            "trashed.");
        _key = config.Bind("Inventory", "TrashKey", new KeyboardShortcut(KeyCode.Delete),
            "Key that trashes the item you are dragging.");
    }

    // A class, not a tuple: the game's runtime has no System.ValueTuple.
    private sealed class Pending
    {
        public readonly ItemDrop.ItemData Item;
        public readonly int Stack;
        public readonly int Amount;
        public Pending(ItemDrop.ItemData item, int amount) { Item = item; Stack = item.m_stack; Amount = amount; }
    }

    private static Pending? _pending;
    private static PopupBase? _popup;

    private static readonly System.Reflection.FieldInfo PopupInstance = AccessTools.Field(typeof(UnifiedPopup), "instance");
    private static readonly System.Reflection.FieldInfo PopupStack = AccessTools.Field(typeof(UnifiedPopup), "popupStack");

    private static System.Collections.Generic.Stack<PopupBase>? Popups()
    {
        var instance = PopupInstance.GetValue(null) as UnifiedPopup;
        return instance != null ? PopupStack.GetValue(instance) as System.Collections.Generic.Stack<PopupBase> : null;
    }

    // Our question is still up, or waiting under another popup. A new session has a new, empty popup stack.
    private static bool Asking()
    {
        var popups = Popups();
        if (_popup != null && (popups == null || !popups.Contains(_popup))) _popup = null;
        return _popup != null;
    }

    // UnifiedPopup.Pop pops whatever is on top, which may be a popup opened over ours (an accepted invite asks to log
    // out). Ours then stays until its own Yes or No.
    private static void PopOurs()
    {
        if (!Asking() || Popups()!.Peek() != _popup) return;
        UnifiedPopup.Pop();
        _popup = null;
    }

    private static void TrashDragged()
    {
        var gui = InventoryGui.instance;
        var player = Player.m_localPlayer;
        if (!_enabled.Value || gui == null || player == null || _pending != null || Asking() || DragGo(gui) == null) return;
        var item = DragItem(gui);
        var inventory = DragInventory(gui);
        if (item == null || inventory != player.GetInventory() || !inventory.ContainsItem(item)) return;
        if (item.m_shared.m_questItem)
        {
            player.Message(MessageHud.MessageType.Center, "$msg_cantdrop");
            return;
        }
        if (LikedItems.IsLiked(inventory, item))
        {
            player.Message(MessageHud.MessageType.Center, "Liked items can't be trashed");
            return;
        }
        // Unequipping here would skip the checks of the game's own unequip, such as Better Archery refusing to take off
        // a quiver that still holds arrows (they would be hidden until another quiver is equipped). Weapons put away
        // with R or at a station are already unequipped (Humanoid.HideHandItems), so they can be trashed.
        if (!QuickStack.Stackable(item) && (item.m_equipped || player.IsItemEquiped(item)))
        {
            player.Message(MessageHud.MessageType.Center, "Unequip it first");
            return;
        }
        var amount = Mathf.Clamp(DragAmount(gui), 1, item.m_stack);
        QuickStack.CancelDrag();
        _pending = new Pending(item, amount);
        var name = Localization.instance.Localize(item.m_shared.m_name);
        _popup = new YesNoPopup("Trash", $"Destroy {amount} x {name}? This can't be undone.", Confirm, Cancel,
            localizeText: false);
        UnifiedPopup.Push(_popup);
    }

    private static void Confirm()
    {
        PopOurs();
        var pending = _pending;
        _pending = null;
        var player = Player.m_localPlayer;
        if (pending == null || player == null) return;
        var inventory = player.GetInventory();
        var item = pending.Item;
        // Gone, merged or split since the question opened: destroy nothing rather than the wrong amount.
        if (!inventory.ContainsItem(item) || item.m_stack != pending.Stack || LikedItems.IsLiked(inventory, item)) return;
        player.RemoveEquipAction(item);
        if (pending.Amount >= item.m_stack)
        {
            player.UnequipItem(item, triggerEquipEffects: false);
            inventory.RemoveItem(item);
        }
        else
        {
            inventory.RemoveItem(item, pending.Amount);
        }
        QuickStack.RefreshCrafting();
    }

    private static void Cancel()
    {
        PopOurs();
        _pending = null;
    }

    // The question is not part of the inventory screen: closing that (or dying, which closes it) closes the question.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    private static class CancelOnHide
    {
        private static void Postfix()
        {
            if (_pending == null) return;
            _pending = null;
            PopOurs();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    private static class ShowCan
    {
        private static void Postfix(InventoryGui __instance)
        {
            if (!InventoryGui.IsVisible()) return;
            if (_can == null)
            {
                if (!_enabled.Value || _noArmor) return;
                try
                {
                    _can = CreateCan(__instance);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"TrashItems: {e}");
                }
                if (_can == null) return;
            }
            _can.SetActive(_enabled.Value);
        }
    }

    private static GameObject? CreateCan(InventoryGui gui)
    {
        var armor = gui.m_player.Find("Armor");
        if (armor == null)
        {
            Plugin.Log.LogWarning("TrashItems: no Armor display in the inventory panel, the trash can was not added");
            _noArmor = true;
            return null;
        }
        var can = Object.Instantiate(armor, gui.m_player);
        can.name = "VU_Trash";
        can.SetSiblingIndex(armor.GetSiblingIndex() + 1);
        ((RectTransform)can).anchoredPosition -= new Vector2(0f, 78f);
        var text = can.Find("ac_text")?.GetComponent<TMP_Text>();
        if (text != null) text.text = "Trash";
        var icon = can.Find("armor_icon")?.GetComponent<Image>();
        if (icon != null) icon.sprite = CanSprite();

        var target = new GameObject("VU_TrashButton", typeof(RectTransform));
        var rect = (RectTransform)target.transform;
        rect.SetParent(can, worldPositionStays: false);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(70f, 74f);
        // Invisible, but it catches the click.
        target.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        target.AddComponent<Button>().onClick.AddListener(TrashDragged);
        return can.gameObject;
    }

    // A white bin, tinted by the armor icon's color: lid, handle and a tapered body with three slots.
    private static Sprite CanSprite()
    {
        const int size = 64;
        var pixels = new Color32[size * size];
        var white = new Color32(255, 255, 255, 255);
        void Fill(int x0, int y0, int x1, int y1)
        {
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
                pixels[y * size + x] = white;
        }
        Fill(10, 46, 53, 51);
        Fill(25, 52, 38, 57);
        for (var y = 6; y <= 43; y++)
        {
            var half = 15 + (y - 6) * 3 / 37;
            for (var x = 32 - half; x < 32 + half; x++)
            {
                var slot = y >= 12 && y <= 37 && (x is >= 24 and <= 26 || x is >= 31 and <= 32 || x is >= 37 and <= 39);
                if (!slot) pixels[y * size + x] = white;
            }
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }

    [HarmonyPatch(typeof(Player), "Update")]
    private static class Keys
    {
        private static void Postfix(Player __instance)
        {
            if (!_enabled.Value || __instance != Player.m_localPlayer || !InventoryGui.IsVisible()) return;
            if (QuickStack.Pressed(_key.Value) && QuickStack.KeysAllowed(__instance)) TrashDragged();
        }
    }
}
