using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using ValheimUtilsByAi.Features;

namespace ValheimUtilsByAi;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(Jotunn.Main.ModGuid)]
// Jötunn refuses the connection, with its version window, when client and server both run this mod in different versions.
[NetworkCompatibility(CompatibilityLevel.VersionCheckOnly, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        CraftWithoutRoof.Bind(Config);
        KilnWoodFilter.Bind(Config);
        AutoStorage.Bind(Config);
        AutoMapSync.Bind(Config);
        FeedingTrough.Bind(Config);
        ForsakenPowerAnywhere.Bind(Config);
        AutoRepair.Bind(Config);
        StationBuildRange.Bind(Config);
        FastCorpseDrop.Bind(Config);
        CraftFromContainers.Bind(Config);
        ComfortRange.Bind(Config);
        ExtensionRange.Bind(Config);
        PortalMapTravel.Bind(Config);
        FarmEverything.Bind(Config);
        PlantGrowthTimer.Bind(Config);
        VehiclesOnMap.Bind(Config);
        BreedingLimit.Bind(Config);
        AutoDoors.Bind(Config);
        ModVersions.Bind(Config);
        LikedItems.Bind(Config);
        // Registration order is the order of their buttons.
        QuickStack.Bind(Config);
        StoreAll.Bind(Config);
        Restock.Bind(Config);
        SortItems.Bind(Config);
        TrashItems.Bind(Config);
        ChestReach.Bind(Config);
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }
}
