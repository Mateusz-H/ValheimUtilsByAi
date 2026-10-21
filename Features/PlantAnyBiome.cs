using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Saplings grow in any biome. Vanilla places them anywhere (no sapling sets Piece.m_onlyInBiome), but one outside its
// Plant.m_biome withers ("Wrong biome"), as does one in the Ashlands ("Too hot") or the Mountains and Deep North ("Too
// cold") unless it tolerates them or sits under a shield dome; when its time is up it is destroyed instead of growing.
// PlantEasily refuses to plant those in the first place, in its own GhostStatus.EvaluateStatus. Both checks run with the
// sapling's biome fields lifted and put back right after, so every other check stays vanilla: cultivated ground, sun,
// space and, for vines, a wall to climb.
internal static class PlantAnyBiome
{
    // Every biome flag, so (biome & m_biome) is never 0.
    private const Heightmap.Biome AnyBiome = (Heightmap.Biome)~0;

    // A sapling's biome fields as they were before a check; Plant is null when nothing was lifted. A struct, as
    // PlantEasily checks every ghost of its grid each frame.
    private struct Saved
    {
        public Plant? Plant;
        public Heightmap.Biome Biome;
        public bool TolerateHeat;
        public bool TolerateCold;
    }

    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "PlantAnyBiome",
            true,
            Plugin.Synced(
                "Saplings and seeds grow in any biome: barley and flax outside the Plains, Jotun puffs and magecap " +
                "outside the Mistlands, crops in the cold Mountains and Deep North and in the hot Ashlands without a " +
                "shield. Cultivated ground, sunlight and free space are still needed."));
    }

    private static Saved Lift(Plant? plant)
    {
        if (!_enabled.Value || plant == null) return default;
        var saved = new Saved
        {
            Plant = plant, Biome = plant.m_biome, TolerateHeat = plant.m_tolerateHeat, TolerateCold = plant.m_tolerateCold,
        };
        plant.m_biome = AnyBiome;
        plant.m_tolerateHeat = plant.m_tolerateCold = true;
        return saved;
    }

    private static void Restore(Saved saved)
    {
        if (saved.Plant == null) return;
        saved.Plant.m_biome = saved.Biome;
        saved.Plant.m_tolerateHeat = saved.TolerateHeat;
        saved.Plant.m_tolerateCold = saved.TolerateCold;
    }

    // Each slow update on every client, for the hover text, the healthy or withered model and the scythe, which clears
    // saplings that aren't healthy (Attack). On the owner the status also decides whether the sapling grows once its
    // time is up or is destroyed.
    [HarmonyPatch(typeof(Plant), nameof(Plant.UpdateHealth))]
    private static class GrowAnywhere
    {
        private static void Prefix(Plant __instance, out Saved __state) => __state = Lift(__instance);

        private static void Finalizer(Saved __state) => Restore(__state);
    }

    // PlantEasily's status of every grid ghost, the cursor one included, read from the ghost's own Plant. With
    // PreventInvalidPlanting (its default) a wrong biome, too hot or too cold ghost turns red and refuses the click.
    [HarmonyPatch]
    private static class PlantEasilyGhost
    {
        private const string Guid = "advize.PlantEasily";
        private static MethodBase? _target;
        private static bool _searched;

        // PlantEasily loads first when installed: it is a soft dependency of Plugin.
        private static bool Prepare()
        {
            if (_searched) return _target != null;
            _searched = true;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return false;
            _target = AccessTools.Method(
                info.Instance.GetType().Assembly.GetType("Advize_PlantEasily.GhostStatus"), "EvaluateStatus");
            if (_target == null)
                Plugin.Log.LogWarning($"PlantAnyBiome: unknown PlantEasily {info.Metadata.Version}, its grid still checks the biome");
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target!;

        private static void Prefix(GameObject ghost, out Saved __state) => __state = Lift(ghost.GetComponent<Plant>());

        private static void Finalizer(Saved __state) => Restore(__state);
    }
}
