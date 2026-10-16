using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// Equipping a one-handed weapon with an empty left hand also equips a shield from the hotbar; unequipping that weapon
// also unequips the shield in the left hand. Only the player's own choices count: Player.ToggleEquipped, which every
// hotbar key, inventory click and radial menu pick goes through (via Humanoid.UseItem), and the equip queue it fills
// for items with an equip time. Equips the game makes on its own (loading the character, ShowHandItems after swimming
// or R, auto-equip on pickup) are left alone. The shield is toggled the same way, so it keeps the vanilla equip queue.
internal static class AutoShield
{
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem =
        AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
    private static readonly AccessTools.FieldRef<Player, List<Player.MinorActionData>> ActionQueue =
        AccessTools.FieldRefAccess<Player, List<Player.MinorActionData>>("m_actionQueue");
    private static readonly Func<Player, ItemDrop.ItemData, bool> ToggleEquipped =
        AccessTools.MethodDelegate<Func<Player, ItemDrop.ItemData, bool>>(
            AccessTools.Method(typeof(Player), "ToggleEquipped"));

    private static ConfigEntry<bool> _enabled = null!;

    // The shield the player last had in hand; only used while it is still on their hotbar. Items are new objects after
    // a relog or a tombstone, so it is also refreshed whenever a weapon change finds a shield in the left hand.
    private static ItemDrop.ItemData? _lastShield;

    // A shield action this feature left in the equip queue. It belongs to the weapon change that queued it, so the
    // player's next hand choice cancels it before it can undo that choice.
    private static ItemDrop.ItemData? _queuedShield;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Combat",
            "AutoShield",
            true,
            Plugin.Synced(
                "Equipping a one-handed weapon with nothing in your left hand also equips a shield from your hotbar " +
                "(the one you used last, else the leftmost), and unequipping that weapon unequips the shield too."));
    }

    // __state: the item was equipped before the toggle. A toggle that only queued the item, or was refused (in an
    // attack, dodging, swimming, broken), leaves it as it was; a queued one is handled when the queue runs it.
    [HarmonyPatch(typeof(Player), "ToggleEquipped")]
    private static class TogglePatch
    {
        private static void Prefix(Player __instance, ItemDrop.ItemData item, out bool __state)
        {
            __state = __instance.IsItemEquiped(item);
            if (!_enabled.Value || __instance != Player.m_localPlayer || _queuedShield == null) return;
            if (!item.IsWeapon() && item.m_shared.m_itemType is not
                    (ItemDrop.ItemData.ItemType.Shield or ItemDrop.ItemData.ItemType.Tool)) return;
            if (item != _queuedShield) __instance.RemoveEquipAction(_queuedShield);
            _queuedShield = null;
        }

        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __state)
        {
            if (!_enabled.Value || __instance != Player.m_localPlayer) return;
            var equipped = __instance.IsItemEquiped(item);
            if (equipped != __state) OnHandChanged(__instance, item, equipped);
        }
    }

    // UpdateActionQueue only removes its first action when that action is done, right before running it.
    [HarmonyPatch(typeof(Player), "UpdateActionQueue")]
    private static class QueuePatch
    {
        private static void Prefix(Player __instance, out Player.MinorActionData? __state)
        {
            var queue = ActionQueue(__instance);
            __state = _enabled.Value && queue.Count > 0 ? queue[0] : null;
        }

        private static void Postfix(Player __instance, Player.MinorActionData? __state)
        {
            if (__state == null || __instance != Player.m_localPlayer) return;
            var queue = ActionQueue(__instance);
            if (queue.Count > 0 && queue[0] == __state) return;
            var equipped = __instance.IsItemEquiped(__state.m_item);
            if (__state.m_type == Player.MinorActionData.ActionType.Equip && equipped ||
                __state.m_type == Player.MinorActionData.ActionType.Unequip && !equipped)
                OnHandChanged(__instance, __state.m_item, equipped);
        }
    }

    // Runs once the item really is in hand or put away. Vanilla's one-handed equip keeps a shield or torch in the left
    // hand (moving a torch over from the right) and drops anything else there, so an empty left hand is free.
    private static void OnHandChanged(Player player, ItemDrop.ItemData item, bool equipped)
    {
        var type = item.m_shared.m_itemType;
        if (type == ItemDrop.ItemData.ItemType.Shield)
        {
            if (equipped) _lastShield = item;
            return;
        }
        if (type != ItemDrop.ItemData.ItemType.OneHandedWeapon) return;
        var left = LeftItem(player);
        var leftShield = left != null && left.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        if (leftShield) _lastShield = left;
        if (equipped && left == null) Toggle(player, FindShield(player));
        else if (!equipped && leftShield) Toggle(player, left);
    }

    private static void Toggle(Player player, ItemDrop.ItemData? shield)
    {
        // Toggling a shield whose action is already queued would cancel the player's own action.
        if (shield == null || player.IsEquipActionQueued(shield)) return;
        ToggleEquipped(player, shield);
        if (player.IsEquipActionQueued(shield)) _queuedShield = shield;
    }

    private static ItemDrop.ItemData? FindShield(Player player)
    {
        var inventory = player.GetInventory();
        if (IsUsableShield(_lastShield, inventory)) return _lastShield;
        for (var x = 0; x < inventory.GetWidth(); x++)
        {
            var item = inventory.GetItemAt(x, 0);
            if (IsUsableShield(item, inventory)) return item;
        }
        return null;
    }

    // On the hotbar (the inventory's top row) and not broken; EquipItem refuses a shield at 0 durability.
    private static bool IsUsableShield(ItemDrop.ItemData? item, Inventory inventory) =>
        item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield && item.m_gridPos.y == 0 &&
        inventory.ContainsItem(item) && (!item.m_shared.m_useDurability || item.m_durability > 0f);
}
