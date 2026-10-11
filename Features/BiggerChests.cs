using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// Container.Awake creates the chest's Inventory with the prefab's m_width x m_height, and after every Load
// UpdateRows sets the height to max(m_height, deepest used row + 1). The size itself is never saved, and
// Inventory.Load keeps items in rows beyond the height (skipValidPositionCheck) but drops items in columns beyond
// the width. So only rows are added, to this game's copy of the chest: a game without the mod, or with it off,
// still gets every row that holds an item, and only empty extra rows disappear.
internal static class BiggerChests
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _rowMultiplier = null!;
    private static ConfigEntry<string> _prefabs = null!;
    private static HashSet<string> _prefabNames = null!;

    private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
        AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
    private static readonly Action<Container> UpdateRows =
        AccessTools.MethodDelegate<Action<Container>>(AccessTools.Method(typeof(Container), "UpdateRows"));

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "BiggerChests",
            true,
            "Chests listed in BiggerChestsPrefabs get more rows: their vanilla row count times " +
            "BiggerChestsRowMultiplier. Turning it off keeps every row that still holds items.");
        _rowMultiplier = config.Bind(
            "Inventory",
            "BiggerChestsRowMultiplier",
            2,
            new ConfigDescription(
                "Rows of a listed chest = vanilla rows x this. 2 makes a reinforced chest 6x8 instead of 6x4.",
                new AcceptableValueRange<int>(1, 5)));
        _prefabs = config.Bind(
            "Inventory",
            "BiggerChestsPrefabs",
            "piece_chest_wood,piece_chest,piece_chest_blackmetal,piece_chest_grausten,piece_chest_barrel,piece_chest_private",
            "Comma-separated prefab names of the containers that get more rows. Default: chest, reinforced chest, " +
            "black metal chest, grausten chest, barrel, personal chest. Other containers work too, for example " +
            "piece_chest_warderobe, Cart or VikingShip.");
        ParsePrefabs();
        _enabled.SettingChanged += (_, _) => Refresh();
        _rowMultiplier.SettingChanged += (_, _) => Refresh();
        _prefabs.SettingChanged += (_, _) =>
        {
            ParsePrefabs();
            Refresh();
        };
    }

    private static void ParsePrefabs() =>
        _prefabNames = new HashSet<string>(
            _prefabs.Value.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);

    // Vanilla UpdateRows shrinks every loaded chest back to max(vanilla rows, deepest used row + 1), then the postfix
    // adds the rows the current settings give. An open panel only picks its scroll pivot on the first update after
    // opening, so it is reset here for the new height.
    private static void Refresh()
    {
        foreach (var container in UnityEngine.Object.FindObjectsByType<Container>(UnityEngine.FindObjectsSortMode.None))
            if (container.GetInventory() != null) UpdateRows(container);
        if (InventoryGui.instance != null && InventoryGui.instance.IsContainerOpen())
            InventoryGui.instance.ContainerGrid.ResetView();
    }

    private static void AddRows(Container container)
    {
        var inventory = container.GetInventory();
        if (!_enabled.Value || inventory == null) return;
        var view = ContainerView(container);
        if (view == null || !_prefabNames.Contains(Utils.GetPrefabName(view.gameObject.name))) return;
        var rows = container.m_height * _rowMultiplier.Value;
        if (inventory.GetHeight() < rows) inventory.SetHeight(rows);
    }

    // A new or empty chest is never loaded, so the rows are added when it is created too.
    [HarmonyPatch(typeof(Container), "Awake")]
    private static class AddRowsOnCreate
    {
        private static void Postfix(Container __instance) => AddRows(__instance);
    }

    [HarmonyPatch(typeof(Container), "UpdateRows")]
    private static class AddRowsOnLoad
    {
        private static void Postfix(Container __instance) => AddRows(__instance);
    }
}
