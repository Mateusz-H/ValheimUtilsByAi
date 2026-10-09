using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// SE_Rested counts comfort pieces within a hard-coded 10 m of the player through this single method.
[HarmonyPatch(typeof(Piece), nameof(Piece.GetAllComfortPiecesInRadius))]
internal static class ComfortRange
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _rangeMeters = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "ComfortRange",
            true,
            "Beds, chairs, rugs, fires and other comfort pieces count towards your comfort level from up to " +
            "ComfortRangeMeters away instead of the vanilla 10 m. " +
            "Resting still starts only near a burning fire, sitting or under a roof; the longer range only " +
            "raises the comfort level, which sets how long Rested lasts.");
        _rangeMeters = config.Bind(
            "Building",
            "ComfortRangeMeters",
            20f,
            new ConfigDescription(
                "How far from you, in meters, a comfort piece still counts. Vanilla is 10.",
                // Only loaded pieces count, and the lowest Simulation Distance loads only 64 m around the player.
                new AcceptableValueRange<float>(1f, 64f)));
    }

    private static void Prefix(ref float radius)
    {
        if (!_enabled.Value) return;
        radius = _rangeMeters.Value;
    }
}
