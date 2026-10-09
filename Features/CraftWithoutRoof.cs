using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

internal static class CraftWithoutRoof
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "CraftWithoutRoof",
            true,
            "Crafting stations (workbench, forge, ...), fermenters, spinning wheels and beds work without a roof " +
            "and without cover.");
    }

    // The flag is restored in the postfix so toggling the setting at runtime takes effect immediately.
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.CheckUsable))]
    private static class StationsWithoutRoof
    {
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

    // Skipping vanilla also skips the owner restarting the fermentation timer while the barrel is exposed.
    [HarmonyPatch(typeof(Fermenter), "UpdateCover")]
    private static class FermenterWithoutCover
    {
        private static bool Prefix(ref bool ___m_exposed, ref bool ___m_hasRoof)
        {
            if (!_enabled.Value) return true;
            ___m_exposed = false;
            ___m_hasRoof = true;
            return false;
        }
    }

    // Only the spinning wheel requires a roof. Vanilla recomputes m_haveRoof every second, so turning the toggle off
    // takes effect on the next tick.
    [HarmonyPatch(typeof(Smelter), "UpdateRoof")]
    private static class SpinningWheelWithoutRoof
    {
        private static void Postfix(Smelter __instance, ref bool ___m_haveRoof)
        {
            if (_enabled.Value && __instance.m_requiresRoof) ___m_haveRoof = true;
        }
    }

    [HarmonyPatch(typeof(Bed), "CheckExposure")]
    private static class BedWithoutCover
    {
        private static bool Prefix(ref bool __result)
        {
            if (!_enabled.Value) return true;
            __result = true;
            return false;
        }
    }
}
