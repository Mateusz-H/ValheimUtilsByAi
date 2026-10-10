using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// GetExtensions recomputes m_buildRange (used by HaveBuildStationInRange and the area marker circle) every 2 s
// from m_rangeBuild and m_extraRangePerLevel. m_rangeBuild is raised to at least StationBuildRangeMeters for that
// call only and restored afterwards; the station's effect area collider, resized in the same pass, is put back to
// its vanilla radius so only building changes.
[HarmonyPatch(typeof(CraftingStation), "GetExtensions")]
internal static class StationBuildRange
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _rangeMeters = null!;

    private static readonly AccessTools.FieldRef<CraftingStation, float> UpdateExtensionTimer =
        AccessTools.FieldRefAccess<CraftingStation, float>("m_updateExtensionTimer");

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "StationBuildRange",
            true,
            "You can build up to StationBuildRangeMeters from a workbench, forge or other crafting station " +
            "instead of their vanilla 20 m (artisan table 40 m).");
        _rangeMeters = config.Bind(
            "Building",
            "StationBuildRangeMeters",
            40f,
            new ConfigDescription(
                "Build range of every crafting station, in meters, before any bonus from station upgrades. Vanilla " +
                "is 20 (artisan table 40); this setting never shrinks a range.",
                // Only loaded stations count, and the lowest Simulation Distance loads only 64 m around the player.
                new AcceptableValueRange<float>(1f, 64f)));
    }

    private static void Prefix(CraftingStation __instance, out float __state)
    {
        __state = __instance.m_rangeBuild;
        if (!_enabled.Value) return;
        __instance.m_rangeBuild = Mathf.Max(__state, _rangeMeters.Value);
    }

    // A finalizer, not the postfix, so the vanilla range comes back even when the original throws.
    private static void Finalizer(CraftingStation __instance, float __state) => __instance.m_rangeBuild = __state;

    private static void Postfix(CraftingStation __instance, float __state)
    {
        // The timer is reset to 0 only on the call that recomputed the range.
        if (UpdateExtensionTimer(__instance) != 0f) return;
        var vanillaRange = __state + __instance.GetExtentionCount(checkExtensions: false) * __instance.m_extraRangePerLevel;
        switch (__instance.m_effectAreaCollider)
        {
            case SphereCollider sphere:
                sphere.radius = vanillaRange;
                break;
            case CapsuleCollider capsule:
                capsule.radius = vanillaRange;
                break;
        }
    }
}
