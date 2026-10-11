using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// L (or the R button under the weight) fills partial stacks of ammo, food and potions in the inventory, hotbar and
// equipped ammo included, from nearby chests; with a chest open, L and the Restock button in the container panel take
// from that chest only. Only stacks that can merge are used (same item, quality and world level), and the last unit of
// a liked chest stack stays. Chests are found and handed over as for QuickStack.
internal static class Restock
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<KeyboardShortcut> _key = null!;
    private static ConfigEntry<float> _radius = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "Restock",
            true,
            Plugin.Synced(
                "RestockKey (or the R button under your weight) fills partial stacks of ammo, food and potions in " +
                "your inventory from nearby chests; with a chest open, from that chest only."));
        _key = config.Bind("Inventory", "RestockKey", new KeyboardShortcut(KeyCode.L),
            "Key for restocking. Works with the inventory closed too.");
        _radius = config.Bind(
            "Inventory",
            "RestockRadius",
            10f,
            Plugin.Synced("How far from you, in meters, a chest can be for restocking.",
                new AcceptableValueRange<float>(1f, 50f)));

        QuickStack.AddMiniButton("VU_RestockArea", "R", () => _enabled.Value && QuickStack.OpenChest() == null, RestockNearby);
        QuickStack.AddContainerButton("VU_RestockContainer", "Restock", () => _enabled.Value && QuickStack.OpenChest() != null, RestockFromOpenChest);
    }

    private static bool Restockable(ItemDrop.ItemData item) =>
        item.m_shared.m_itemType is ItemDrop.ItemData.ItemType.Ammo or ItemDrop.ItemData.ItemType.AmmoNonEquipable
            or ItemDrop.ItemData.ItemType.Consumable &&
        QuickStack.Stackable(item) && item.m_stack < item.m_shared.m_maxStackSize;

    private static List<ItemDrop.ItemData> Wanted(Player player) =>
        player.GetInventory().GetAllItems().Where(Restockable).ToList();

    private static void Run()
    {
        var gui = InventoryGui.instance;
        if (gui != null && gui.IsContainerOpen()) RestockFromOpenChest();
        else RestockNearby();
    }

    private static void RestockFromOpenChest()
    {
        var player = Player.m_localPlayer;
        var chest = QuickStack.OpenChest();
        if (player != null && chest != null) Fill(player, new List<Inventory> { chest.GetInventory() }, 0);
    }

    private static void RestockNearby()
    {
        var player = Player.m_localPlayer;
        if (player == null) return;
        var wanted = Wanted(player);
        if (wanted.Count == 0)
        {
            player.Message(MessageHud.MessageType.Center, "Nothing to restock");
            return;
        }
        // Fill's own rule, so no chest is asked for that it wouldn't take from.
        var chests = QuickStack.NearbyChests(_radius.Value, inventory => inventory.GetAllItems().Any(source =>
            source.m_stack > LikedItems.Reserved(source) && wanted.Exists(w => QuickStack.SameStack(source, w))));
        QuickStack.WithChests(chests, (owned, busy) =>
        {
            var p = Player.m_localPlayer;
            if (p != null) Fill(p, owned.ConvertAll(CraftFromContainers.LoadedInventory), busy);
        });
    }

    private static void Fill(Player player, List<Inventory> chests, int busy)
    {
        var wanted = Wanted(player);
        if (wanted.Count == 0)
        {
            player.Message(MessageHud.MessageType.Center, "Nothing to restock");
            return;
        }
        // A drag or split of a chest stack must not outlive the units taken from it.
        QuickStack.CancelDrag();
        var restocked = 0;
        foreach (var chest in chests)
        {
            var taken = false;
            foreach (var stack in wanted)
            {
                var max = stack.m_shared.m_maxStackSize;
                foreach (var source in chest.GetAllItems())
                {
                    if (stack.m_stack >= max) break;
                    if (!QuickStack.SameStack(source, stack)) continue;
                    var count = Mathf.Min(max - stack.m_stack, source.m_stack - LikedItems.Reserved(source));
                    if (count <= 0) continue;
                    source.m_stack -= count;
                    stack.m_stack += count;
                    if (source.m_cheated && !PlayerProfile.s_bypassCheatChecks) stack.m_cheated = true;
                    restocked += count;
                    taken = true;
                }
            }
            if (!taken) continue;
            chest.GetAllItems().RemoveAll(i => i.m_stack <= 0);
            QuickStack.Changed(chest);
        }
        if (restocked > 0) QuickStack.Changed(player.GetInventory());
        var text = restocked > 0 ? $"Restocked {QuickStack.Items(restocked)}" : "Nothing nearby to restock with";
        player.Message(MessageHud.MessageType.Center, text + QuickStack.Busy(busy));
        QuickStack.RefreshCrafting();
    }

    [HarmonyPatch(typeof(Player), "Update")]
    private static class Keys
    {
        private static void Postfix(Player __instance)
        {
            if (!_enabled.Value || __instance != Player.m_localPlayer) return;
            if (QuickStack.Pressed(_key.Value) && QuickStack.KeysAllowed(__instance)) Run();
        }
    }
}
