using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// Windmill.UpdateAudio, on every client each frame, moves the volume of the grinding and propeller loops a little
// towards m_maxVol times the wind. m_maxVol is lowered for that call only, so the loops fade to the new volume, and
// back when the toggle goes off, the way vanilla fades them with the wind.
internal static class QuietWindmills
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _volume = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "QuietWindmills",
            true,
            Plugin.Synced(
                "The grinding and turning blades of windmills play at QuietWindmillsVolume. Adding barley and " +
                "dropping flour sound as in vanilla."));
        _volume = config.Bind(
            "Crafting",
            "QuietWindmillsVolume",
            0,
            Plugin.Synced(
                "Volume of windmills grinding and turning, in percent of vanilla: 0 mutes them, 100 is vanilla.",
                new AcceptableValueRange<int>(0, 100)));
    }

    [HarmonyPatch(typeof(Windmill), "UpdateAudio")]
    private static class Sound
    {
        private static void Prefix(Windmill __instance, out float __state)
        {
            __state = __instance.m_maxVol;
            if (_enabled.Value) __instance.m_maxVol *= _volume.Value / 100f;
        }

        private static void Finalizer(Windmill __instance, float __state)
        {
            __instance.m_maxVol = __state;
        }
    }
}
