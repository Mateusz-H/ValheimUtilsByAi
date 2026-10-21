using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// A tamed animal's calls are effect prefabs spawned on every client that has the animal loaded: the lox moo
// (sfx_lox_shout) and wolf howl (sfx_wolf_haul) by AnimationEffect.Effect from animation events, the grunts and clucks
// of boars, hens and asksvin by BaseAI.DoIdleSound. Most of them carry a ZNetView, so each client's copy is also sent to
// the other players and built there by ZNetScene.CreateObject. A ZSFX starts playing in its first update, after
// Instantiate returns, so its volume modifier is set in ZSFX.Awake while such a call is being spawned; the networked
// copy gets a ZDO flag, whatever the sender's setting, so each receiving player's own setting decides.
internal static class QuietTamedAnimals
{
    private static readonly int CallKey = "mh_QuietTamedAnimalsCall".GetStableHashCode();

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _volume = null!;
    private static bool _inCall;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "QuietTamedAnimals",
            true,
            Plugin.Synced(
                "Tamed animals' calls (lox moo, wolf howl, grunts and clucks of boars, hens and asksvin) play at " +
                "QuietTamedAnimalsVolume. Attacks, getting hurt, eating, breeding and wild animals sound as in vanilla."));
        _volume = config.Bind(
            "World",
            "QuietTamedAnimalsVolume",
            0,
            Plugin.Synced(
                "Volume of tamed animals' calls, in percent of vanilla: 0 mutes them, 100 is vanilla.",
                new AcceptableValueRange<int>(0, 100)));
    }

    // Summons (Staff of the Wild troll, Dead Raiser skeletons) start tamed and keep their sounds. Attack sounds of
    // asksvin and moose also come from animation events; they stay vanilla.
    private static bool IsTamedCall(Character? character) =>
        character != null && !character.IsPlayer() && character.IsTamed() &&
        character.GetComponent<Tameable>() is { m_startsTamed: false } && !character.InAttack();

    [HarmonyPatch(typeof(BaseAI), "DoIdleSound")]
    private static class IdleSound
    {
        private static void Prefix(Character ___m_character) => _inCall = IsTamedCall(___m_character);

        private static void Finalizer() => _inCall = false;
    }

    [HarmonyPatch(typeof(AnimationEffect), nameof(AnimationEffect.Effect))]
    private static class AnimationSound
    {
        private static void Prefix(AnimationEffect __instance) =>
            _inCall = IsTamedCall(__instance.GetComponentInParent<Character>());

        private static void Finalizer() => _inCall = false;
    }

    // Another player's copy of a call.
    [HarmonyPatch(typeof(ZNetScene), "CreateObject")]
    private static class RemoteCopy
    {
        private static void Prefix(ZDO zdo) => _inCall = zdo.GetBool(CallKey);

        private static void Finalizer() => _inCall = false;
    }

    [HarmonyPatch(typeof(ZNetView), "Awake")]
    private static class FlagCopy
    {
        private static void Postfix(ZNetView __instance)
        {
            if (_inCall && __instance.IsValid() && __instance.IsOwner()) __instance.GetZDO().Set(CallKey, true);
        }
    }

    [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Awake))]
    private static class Volume
    {
        private static void Postfix(ZSFX __instance)
        {
            if (_inCall && _enabled.Value) __instance.SetVolumeModifier(_volume.Value / 100f);
        }
    }
}
