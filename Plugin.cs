using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using ValheimUtilsByAi.Features;

namespace ValheimUtilsByAi;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(Jotunn.Main.ModGuid)]
// Loaded first when installed, so PlantSpacing can patch its grid ghosts.
[BepInDependency("advize.PlantEasily", BepInDependency.DependencyFlags.SoftDependency)]
// Jötunn refuses the connection, with its version window, when client and server both run this mod in different versions.
[NetworkCompatibility(CompatibilityLevel.VersionCheckOnly, VersionStrictness.Patch)]
// Synced settings follow the server only when it runs this mod; elsewhere every player keeps their own.
[SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    // Description of a setting the server decides for everyone: on a server running this mod Jötunn sends the
    // server's value to each player on connect and on every change (SettingChanged fires as for a local edit), locks
    // it for non-admins, and puts the player's own value back on disconnect. The player's config file is never
    // overwritten. Keys and the main-menu mod list stay each player's own.
    internal static ConfigDescription Synced(string description, AcceptableValueBase? acceptableValues = null) =>
        new(description, acceptableValues, new ConfigurationManagerAttributes { IsAdminOnly = true });

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
        BiggerChests.Bind(Config);
        PlantSpacing.Bind(Config);
        NetworkBandwidth.Bind(Config);
        QuickStackRepair.Bind(Config);
        BuildInNoBuildZones.Bind(Config);
        AutoShield.Bind(Config);
        PlantAnyBiome.Bind(Config);
        WindmillFullSpeed.Bind(Config);
        QuietTamedAnimals.Bind(Config);
        QuietWindmills.Bind(Config);
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }
}
