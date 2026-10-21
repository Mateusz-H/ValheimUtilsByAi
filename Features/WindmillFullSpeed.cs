using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

internal static class WindmillFullSpeed
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "WindmillFullSpeed",
            true,
            Plugin.Synced(
                "Windmills always grind barley at full speed, as in the strongest wind, whatever the weather and even " +
                "when walls or a roof cover the blades."));
    }

    // Vanilla scales the output from 0 (wind below 10 %) to 1 (full wind), times the uncovered share of the propeller.
    // The grinding reads it on the windmill's owner (Smelter.UpdateSmelter, also for the catch-up after the area
    // reloads); the propeller, grindstone, sound and the blades' damage area read it on every client.
    [HarmonyPatch(typeof(Windmill), nameof(Windmill.GetPowerOutput))]
    private static class FullPower
    {
        private static void Postfix(ref float __result)
        {
            if (_enabled.Value) __result = 1f;
        }
    }
}
