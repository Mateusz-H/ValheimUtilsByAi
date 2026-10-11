using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// Procreation.Procreate is the only reader of m_maxCreatures: every 10 s the animal's ZDO owner counts adults of its
// own prefab plus their offspring within m_totalCheckRange (10 m) and skips the love-point step once the count reaches
// the per-prefab cap (a handful in vanilla). The cap is replaced for the duration of each call and restored
// afterwards.
internal static class BreedingLimit
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _maxCreatures = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "BreedingLimit",
            true,
            Plugin.Synced(
                "Tamed animals keep breeding until BreedingLimitCount animals of their kind (adults and young) stand " +
                "within 10 m, instead of the handful vanilla allows."));
        _maxCreatures = config.Bind(
            "World",
            "BreedingLimitCount",
            20,
            Plugin.Synced(
                "How many animals of one kind, adults and young together, may stand within 10 m before tamed " +
                "animals of that kind stop breeding. Vanilla allows only a handful, set per animal.",
                new AcceptableValueRange<int>(1, 100)));
    }

    [HarmonyPatch(typeof(Procreation), "Procreate")]
    private static class Patch
    {
        private static void Prefix(Procreation __instance, out int __state)
        {
            __state = __instance.m_maxCreatures;
            if (!_enabled.Value) return;
            __instance.m_maxCreatures = _maxCreatures.Value;
        }

        private static void Finalizer(Procreation __instance, int __state)
        {
            __instance.m_maxCreatures = __state;
        }
    }
}
