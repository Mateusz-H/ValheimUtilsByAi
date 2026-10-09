using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ValheimUtilsByAi.Features;

namespace ValheimUtilsByAi;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(Jotunn.Main.ModGuid)]
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
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }
}
