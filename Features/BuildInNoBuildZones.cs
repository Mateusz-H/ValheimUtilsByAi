using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

internal static class BuildInNoBuildZones
{
    private static readonly HashSet<string> BuildableLocations = new HashSet<string>
    {
        "StartTemple", "Vendor_BlackForest", "Hildir_camp", "BogWitch_Camp",
    };

    private static ConfigEntry<bool> _enabled = null!;
    private static bool _removingWithHammer;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "BuildInNoBuildZones",
            true,
            Plugin.Synced(
                "Build, remove your own pieces and use the hoe or pickaxe at the traders (Haldor, Hildir, Bog Witch) " +
                "and at the spawn stones. Monsters still stay out of the traders' safe zone."));
    }

    // Placing, hammer removal and terrain tools all ask this one method. Other no-build zones (the Fader arena,
    // Hildir's towers, Deep North) stay vanilla.
    [HarmonyPatch(typeof(Location), nameof(Location.IsInsideNoBuildLocation))]
    private static class AllowBuilding
    {
        private static void Postfix(Vector3 point, List<Location> ___s_allLocations, ref bool __result)
        {
            if (!__result || !_enabled.Value) return;
            __result = ___s_allLocations.Any(location => StaysNoBuild(location, point));
        }

        // Players without a bed respawn 2 m above the StartTemple centre with no clearance check (Game.FindSpawnPoint),
        // so a building there could trap them. The check uses the piece's centre, hence the margin.
        private static bool StaysNoBuild(Location location, Vector3 point)
        {
            if (!location.m_noBuild || !location.IsInside(point, 0f, buildCheck: true)) return false;
            var prefabName = Utils.GetPrefabName(location.gameObject.name);
            if (prefabName == "StartTemple") return Utils.DistanceXZ(location.transform.position, point) < 5f;
            return !BuildableLocations.Contains(prefabName);
        }
    }

    [HarmonyPatch]
    private static class VanillaNoBuildZone
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(Location), nameof(Location.IsInsideNoBuildLocation))]
        public static bool Contains(Vector3 point) => throw new NotImplementedException("reverse patch not applied");
    }

    [HarmonyPatch(typeof(Player), "RemovePiece")]
    private static class TrackHammerRemoval
    {
        private static void Prefix() => _removingWithHammer = true;

        private static void Finalizer() => _removingWithHammer = false;
    }

    // Traders' fire pits and the bog witch's candles are removable pieces that vanilla protects only with the
    // no-build zone. Piece.CanBeRemoved also gates weather damage, hence the hammer-only flag.
    [HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
    private static class KeepLocationPieces
    {
        private static void Postfix(Piece __instance, ref bool __result)
        {
            if (__result && _removingWithHammer && _enabled.Value && !__instance.IsPlacedByPlayer() &&
                VanillaNoBuildZone.Contains(__instance.transform.position))
            {
                __result = false;
            }
        }
    }
}
