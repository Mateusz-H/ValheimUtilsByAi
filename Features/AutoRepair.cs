using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// UpdateRepair runs every frame while the crafting panel is open and shows the repair button only at a station that
// can repair; then repair everything that station and the stations within AutoRepairNearbyMeters can repair at once.
[HarmonyPatch(typeof(InventoryGui), "UpdateRepair")]
internal static class AutoRepair
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _nearbyMeters = null!;

    private static readonly Func<InventoryGui, ItemDrop.ItemData, bool> CanRepair =
        AccessTools.MethodDelegate<Func<InventoryGui, ItemDrop.ItemData, bool>>(AccessTools.Method(typeof(InventoryGui), "CanRepair"));
    private static readonly Action<InventoryGui, bool> UpdateCraftingPanel =
        AccessTools.MethodDelegate<Action<InventoryGui, bool>>(AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel"));
    private static readonly AccessTools.FieldRef<Player, CraftingStation> CurrentStation =
        AccessTools.FieldRefAccess<Player, CraftingStation>("m_currentStation");
    // Every loaded station built in the world; the game only adds and removes, never replaces the list.
    private static readonly List<CraftingStation> AllStations =
        AccessTools.StaticFieldRefAccess<List<CraftingStation>>(typeof(CraftingStation), "m_allStations");
    private static readonly AccessTools.FieldRef<CraftingStation, ZNetView> StationView =
        AccessTools.FieldRefAccess<CraftingStation, ZNetView>("m_nview");

    private static readonly List<ItemDrop.ItemData> Worn = new List<ItemDrop.ItemData>();
    private static readonly List<ItemDrop.ItemData> Repairable = new List<ItemDrop.ItemData>();
    private static readonly List<CraftingStation> Stations = new List<CraftingStation>();
    // CanRepair scans every recipe per item and station, so items nothing can repair are rechecked only twice a second.
    private static float _idleUntil;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "AutoRepair",
            true,
            "Opening a crafting station repairs every item it can repair, without clicking the repair button.");
        _nearbyMeters = config.Bind(
            "Crafting",
            "AutoRepairNearbyMeters",
            15f,
            new ConfigDescription(
                "Crafting stations within this many meters of you also repair what they can when you open one, each " +
                "with its vanilla rules (item, station level, roof, fire). 0 repairs only at the station you open.",
                new AcceptableValueRange<float>(0f, 50f)));
    }

    private static void Postfix(InventoryGui __instance)
    {
        if (!_enabled.Value || !__instance.m_repairButton.gameObject.activeSelf) return;

        Player player = Player.m_localPlayer;
        Worn.Clear();
        player.GetInventory().GetWornItems(Worn);
        Worn.RemoveAll(item => !item.m_shared.m_canBeReparied);
        if (Worn.Count == 0 || Time.time < _idleUntil) return;

        // The open station first, as the repair button; it is null only with the devcommands no-cost cheat.
        CraftingStation current = player.GetCurrentCraftingStation();
        Stations.Clear();
        Stations.Add(current);
        float range = _nearbyMeters.Value;
        foreach (CraftingStation station in AllStations)
        {
            // A valid ZNetView rules out the hammer's placement ghost and stations whose zone is unloading.
            ZNetView view = StationView(station);
            if (station != current && station.m_canRepair && view != null && view.IsValid() &&
                Vector3.Distance(station.transform.position, player.transform.position) < range)
                Stations.Add(station);
        }

        int repaired = 0;
        string firstName = "";
        foreach (CraftingStation station in Stations)
        {
            // CanRepair judges the player's current station; point it at this one for the check only.
            Repairable.Clear();
            CurrentStation(player) = station;
            try
            {
                foreach (ItemDrop.ItemData item in Worn)
                    if (CanRepair(__instance, item)) Repairable.Add(item);
            }
            finally
            {
                CurrentStation(player) = current;
            }
            if (Repairable.Count == 0 || (station != null && !station.CheckUsable(player, showMessage: false))) continue;

            // Mirrors InventoryGui.RepairOneItem for all items, one effect per station and one message; re-check after a game patch.
            foreach (ItemDrop.ItemData item in Repairable)
            {
                player.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                Worn.Remove(item);
                if (repaired++ == 0) firstName = item.m_shared.m_name;
            }
            if (station != null) station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
        }
        if (repaired == 0)
        {
            _idleUntil = Time.time + 0.5f;
            return;
        }

        player.Message(MessageHud.MessageType.Center, repaired == 1
            ? Localization.instance.Localize("$msg_repaired", firstName)
            : $"{repaired} items repaired");
        UpdateCraftingPanel(__instance, false);
    }
}
