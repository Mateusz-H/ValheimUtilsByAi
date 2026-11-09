using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

internal static class NoLavaRocks
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "NoLavaRocks",
            true,
            Plugin.Synced("Lava in the Ashlands no longer bubbles up and throws burning rocks. Cinder rain, lava heat and lava damage stay vanilla."));
    }

    // The Ashlands world spawner entry "Lava Rock" spawns a LavaRock object on lava, which throws projectile_lavaRock.
    // Every world, event and alt-biome spawn ends in SpawnSystem.Spawn on the zone owner, so skip that one prefab there.
    [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
    private static class Patch
    {
        private static bool Prefix(SpawnSystem.SpawnData critter)
        {
            if (!_enabled.Value) return true;
            return critter?.m_prefab == null || critter.m_prefab.name != "LavaRock";
        }
    }
}
