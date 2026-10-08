using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Ragdoll.Awake schedules DestroyNow (poof effect + loot) after m_ttl seconds; only the ZDO owner, the game
// that killed the creature, acts on it. The ttl is capped for that call and restored afterwards.
[HarmonyPatch(typeof(Ragdoll), "Awake")]
internal static class FastCorpseDrop
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _seconds = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Combat",
            "FastCorpseDrop",
            true,
            "Dead creatures vanish and drop their loot after FastCorpseDropSeconds instead of lying around for " +
            "several seconds.");
        _seconds = config.Bind(
            "Combat",
            "FastCorpseDropSeconds",
            0.5f,
            new ConfigDescription(
                "Seconds a dead creature's body stays before it vanishes and drops loot. Never makes it slower than vanilla.",
                new AcceptableValueRange<float>(0.1f, 10f)));
    }

    private static void Prefix(Ragdoll __instance, out float __state)
    {
        __state = __instance.m_ttl;
        if (_enabled.Value) __instance.m_ttl = Mathf.Min(__instance.m_ttl, _seconds.Value);
    }

    private static void Postfix(Ragdoll __instance, float __state) => __instance.m_ttl = __state;
}
