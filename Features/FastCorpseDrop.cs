using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Ragdoll.Awake schedules DestroyNow (puff + loot) after m_ttl seconds; only the ZDO owner, the game that controlled
// the creature when it died, acts on it. Setup runs right after Awake on that game and is the first point that knows
// the creature: players pass no CharacterDrop, and boss deaths are timed to the body (Fader's explosion sound).
[HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
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
            Plugin.Synced(
                "Dead creatures vanish and drop their loot after FastCorpseDropSeconds instead of lying around for " +
                "several seconds. Player bodies and bosses keep the vanilla timing."));
        _seconds = config.Bind(
            "Combat",
            "FastCorpseDropSeconds",
            0.5f,
            Plugin.Synced(
                "Seconds a dead creature's body stays before it vanishes and drops loot. Never makes it slower than vanilla.",
                new AcceptableValueRange<float>(0.1f, 10f)));
    }

    private static void Postfix(Ragdoll __instance, CharacterDrop? characterDrop)
    {
        if (!_enabled.Value || characterDrop == null) return;
        if (characterDrop.TryGetComponent(out Character character) && character.IsBoss()) return;
        __instance.CancelInvoke("DestroyNow");
        __instance.InvokeRepeating("DestroyNow", Mathf.Min(__instance.m_ttl, _seconds.Value), 1f);
    }
}
