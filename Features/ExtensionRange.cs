using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Vanilla reads StationExtension.m_maxStationDistance through two methods: the static FindExtensions, which each
// station calls every 2 s to collect its extensions, and the instance FindClosestStationInRange, which the placement
// ghost and the hover connection line use. The field is raised to at least ExtensionRangeMeters for the duration of
// each call and restored afterwards. The third reader, FindStationsInRange(Vector3), has no vanilla caller and is
// deliberately not patched.
internal static class ExtensionRange
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _rangeMeters = null!;

    // The game never reassigns this list, so one lookup is enough.
    private static readonly List<StationExtension> AllExtensions =
        AccessTools.StaticFieldRefAccess<List<StationExtension>>(typeof(StationExtension), "m_allExtensions");

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "ExtensionRange",
            true,
            "Extensions of the workbench, forge, black forge, artisan table, galdr table and cauldron (chopping " +
            "block, tanning rack, bellows, ...) work from up to ExtensionRangeMeters away from the station instead " +
            "of their vanilla 2-5 m, and can be placed that far away.");
        _rangeMeters = config.Bind(
            "Building",
            "ExtensionRangeMeters",
            10f,
            new ConfigDescription(
                "How far from a crafting station, in meters, an extension still upgrades it. Vanilla is 2-5 " +
                "depending on the extension; this setting never shrinks a range.",
                new AcceptableValueRange<float>(1f, 50f)));
    }

    [HarmonyPatch(typeof(StationExtension), nameof(StationExtension.FindExtensions))]
    private static class FindExtensionsPatch
    {
        private static void Prefix(out List<float>? __state)
        {
            __state = null;
            if (!_enabled.Value) return;
            var extensions = AllExtensions;
            __state = new List<float>(extensions.Count);
            foreach (var extension in extensions)
            {
                __state.Add(extension.m_maxStationDistance);
                extension.m_maxStationDistance = Mathf.Max(extension.m_maxStationDistance, _rangeMeters.Value);
            }
        }

        private static void Finalizer(List<float>? __state)
        {
            if (__state == null) return;
            var extensions = AllExtensions;
            for (var i = 0; i < __state.Count && i < extensions.Count; i++)
                extensions[i].m_maxStationDistance = __state[i];
        }
    }

    [HarmonyPatch(typeof(StationExtension), nameof(StationExtension.FindClosestStationInRange))]
    private static class FindClosestStationPatch
    {
        private static void Prefix(StationExtension __instance, out float __state)
        {
            __state = __instance.m_maxStationDistance;
            if (!_enabled.Value) return;
            __instance.m_maxStationDistance = Mathf.Max(__state, _rangeMeters.Value);
        }

        private static void Finalizer(StationExtension __instance, float __state)
        {
            __instance.m_maxStationDistance = __state;
        }
    }
}
