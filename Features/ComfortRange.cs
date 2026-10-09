using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// SE_Rested counts comfort pieces within a hard-coded 10 m of the player through this single method.
[HarmonyPatch(typeof(Piece), nameof(Piece.GetAllComfortPiecesInRadius))]
internal static class ComfortRange
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _multiplier = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "ComfortRange",
            true,
            "Multiplies how far from you beds, chairs, rugs, fires and other comfort pieces still count " +
            "towards your comfort level, by ComfortRangeMultiplier.");
        _multiplier = config.Bind(
            "Building",
            "ComfortRangeMultiplier",
            2f,
            new ConfigDescription(
                "Comfort range multiplier. 1 is vanilla (10 m).",
                new AcceptableValueRange<float>(1f, 10f)));
    }

    private static void Prefix(ref float radius)
    {
        if (!_enabled.Value) return;
        radius *= _multiplier.Value;
    }
}
