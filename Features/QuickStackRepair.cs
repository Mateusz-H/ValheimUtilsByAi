using BepInEx.Configuration;

namespace ValheimUtilsByAi.Features;

// QuickStack's key and buttons also repair every worn item the crafting stations within AutoRepairNearbyMeters can
// repair, with AutoRepair's rules, without opening a station. The message goes top left: quick stack's own center
// message would replace it.
internal static class QuickStackRepair
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "QuickStackRepair",
            true,
            Plugin.Synced(
                "Quick stacking (QuickStackKey or its buttons) also repairs your worn items at crafting stations within " +
                "AutoRepairNearbyMeters, each with its vanilla repair rules, without opening one."));
    }

    internal static void Run()
    {
        var gui = InventoryGui.instance;
        if (_enabled.Value && gui != null) AutoRepair.RepairAll(gui, atOpenStation: false, MessageHud.MessageType.TopLeft);
    }
}
