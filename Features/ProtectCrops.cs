using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

internal static class ProtectCrops
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "ProtectCrops",
            true,
            Plugin.Synced("Monsters and animals can't damage planted crops, saplings or pickable plants. Players can still remove them."));
    }

    private static bool _withering;

    // A sapling that fails to grow destroys itself with an attacker-less 9999 hit; let only that one through.
    [HarmonyPatch(typeof(Plant), "Destroy")]
    private static class WitherPatch
    {
        private static void Prefix() => _withering = true;
        private static void Finalizer() => _withering = false;
    }

    // Every hit on a plant (melee, projectile, AoE) ends in Destructible.RPC_Damage on the ZDO owner.
    // Only player hits and Plant.Destroy go through; fire and hits whose attacker died or is not loaded are blocked.
    [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
    private static class Patch
    {
        private static bool Prefix(Destructible __instance, HitData hit)
        {
            if (!_enabled.Value || hit == null || _withering) return true;
            if (!__instance.GetComponent<Plant>() && !__instance.GetComponent<Pickable>()) return true;
            var attacker = hit.GetAttacker();
            return attacker is Player;
        }
    }
}
