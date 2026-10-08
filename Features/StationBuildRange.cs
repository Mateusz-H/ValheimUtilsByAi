using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// GetExtensions recomputes m_buildRange (used by HaveBuildStationInRange and the area marker circle) every 2 s
// from m_rangeBuild and m_extraRangePerLevel. Both are scaled for that call only and restored afterwards; the
// station's effect area collider, resized in the same pass, is put back to its vanilla radius so only building
// changes.
[HarmonyPatch(typeof(CraftingStation), "GetExtensions")]
internal static class StationBuildRange
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _multiplier = null!;

    private static readonly AccessTools.FieldRef<CraftingStation, float> UpdateExtensionTimer =
        AccessTools.FieldRefAccess<CraftingStation, float>("m_updateExtensionTimer");

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "StationBuildRange",
            true,
            "Multiplies how far from a workbench, forge or other crafting station you can build, " +
            "by StationBuildRangeMultiplier.");
        _multiplier = config.Bind(
            "Building",
            "StationBuildRangeMultiplier",
            2f,
            new ConfigDescription(
                "Build range multiplier, including the bonus from station upgrades. 1 is vanilla (workbench: 20 m).",
                new AcceptableValueRange<float>(1f, 10f)));
    }

    private struct State
    {
        public float RangeBuild;
        public float ExtraRangePerLevel;
    }

    private static void Prefix(CraftingStation __instance, out State __state)
    {
        __state = new State { RangeBuild = __instance.m_rangeBuild, ExtraRangePerLevel = __instance.m_extraRangePerLevel };
        if (!_enabled.Value) return;
        __instance.m_rangeBuild *= _multiplier.Value;
        __instance.m_extraRangePerLevel *= _multiplier.Value;
    }

    private static void Postfix(CraftingStation __instance, State __state)
    {
        __instance.m_rangeBuild = __state.RangeBuild;
        __instance.m_extraRangePerLevel = __state.ExtraRangePerLevel;
        // The timer is reset to 0 only on the call that recomputed the range.
        if (UpdateExtensionTimer(__instance) != 0f) return;
        var vanillaRange = __state.RangeBuild + __instance.GetExtentionCount(checkExtensions: false) * __state.ExtraRangePerLevel;
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
