using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

[HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.CheckUsable))]
internal static class CraftWithoutRoof
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "CraftWithoutRoof",
            true,
            "Crafting stations (workbench, forge, ...) work without a roof and without cover. " +
            "The fire requirement of forges and similar stations is unaffected.");
    }

    // The flag is restored in the postfix so toggling the setting at runtime takes effect immediately.
    private static void Prefix(CraftingStation __instance, out bool __state)
    {
        __state = __instance.m_craftRequireRoof;
        if (_enabled.Value)
        {
            __instance.m_craftRequireRoof = false;
        }
    }

    private static void Postfix(CraftingStation __instance, bool __state) => __instance.m_craftRequireRoof = __state;
}
