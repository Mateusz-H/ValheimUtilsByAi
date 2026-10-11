using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// Hovering a planted thing shows how long until it grows. Vanilla saplings (Plant) grow once after a per-object random
// time; planted pickables (a Pickable with a Piece, e.g. FarmEverything bushes) show nothing while bare, so the time
// until the next harvest is added there.
internal static class PlantGrowthTimer
{
    private static ConfigEntry<bool> _enabled = null!;

    private static readonly Func<Plant, float> GrowTime =
        AccessTools.MethodDelegate<Func<Plant, float>>(AccessTools.Method(typeof(Plant), "GetGrowTime"));
    private static readonly Func<Plant, double> TimeSincePlanted =
        AccessTools.MethodDelegate<Func<Plant, double>>(AccessTools.Method(typeof(Plant), "TimeSincePlanted"));

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "PlantGrowthTimer",
            true,
            Plugin.Synced(
                "Hovering a sapling or a planted bush shows how long until it grows (or regrows its harvest)."));
    }

    private static string Remaining(double seconds)
    {
        if (seconds < 60) return "grows in less than a minute";
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"grows in {(int)time.TotalHours}h {time.Minutes:00}m"
            : $"grows in {time.Minutes}m";
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
    private static class SaplingTimer
    {
        private static void Postfix(Plant __instance, ZNetView ___m_nview, ref string __result)
        {
            if (!_enabled.Value || ___m_nview == null || !___m_nview.IsValid()) return;
            __result += "\n" + Remaining(GrowTime(__instance) - TimeSincePlanted(__instance));
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
    private static class PlantedPickableTimer
    {
        private static void Postfix(Pickable __instance, bool ___m_picked, ZNetView ___m_nview, ref string __result)
        {
            if (!_enabled.Value || !___m_picked || __instance.m_respawnTimeMinutes <= 0f) return;
            if (___m_nview == null || !___m_nview.IsValid() || __instance.GetComponent<Piece>() == null) return;
            var pickedTicks = ___m_nview.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L);
            var elapsed = pickedTicks > 1 ? (ZNet.instance.GetTime() - new DateTime(pickedTicks)).TotalSeconds : 0.0;
            __result = Localization.instance.Localize(__instance.GetHoverName()) + "\n"
                       + Remaining(__instance.m_respawnTimeMinutes * 60.0 - elapsed);
        }
    }
}
