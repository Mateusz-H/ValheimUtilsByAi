using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

internal static class KilnWoodFilter
{
    private const string KilnPieceName = "$piece_charcoalkiln";

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<string> _allowedWood = null!;
    private static HashSet<string> _allowedNames = null!;
    private static bool _allowedNamesChecked;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "KilnWoodFilter",
            true,
            "The charcoal kiln only accepts the wood types listed in KilnAllowedWood. " +
            "Other wood (fine wood, core wood, ...) is refused with the vanilla \"won't work\" message.");
        _allowedWood = config.Bind(
            "Crafting",
            "KilnAllowedWood",
            "Wood",
            "Comma-separated item prefab names the kiln accepts when KilnWoodFilter is on. " +
            "Vanilla kiln wood: Wood, FineWood, RoundLog, Blackwood.");
        ParseAllowedWood();
        _allowedWood.SettingChanged += (_, _) => ParseAllowedWood();
    }

    private static void ParseAllowedWood()
    {
        _allowedNames = new HashSet<string>(
            _allowedWood.Value.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _allowedNamesChecked = false;
    }

    // Kiln wood is only known from a loaded kiln, so each config value is checked on its first use.
    private static void WarnAboutNonKilnWood(List<Smelter.ItemConversion> kilnConversions)
    {
        _allowedNamesChecked = true;
        var kilnWood = kilnConversions
            .Where(conversion => conversion.m_from != null)
            .Select(conversion => conversion.m_from.gameObject.name)
            .ToList();
        var notKilnWood = _allowedNames.Where(name => !kilnWood.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (notKilnWood.Count > 0)
            Plugin.Log.LogWarning($"KilnAllowedWood: ignoring {string.Join(", ", notKilnWood)}; " +
                                  $"the charcoal kiln only takes {string.Join(", ", kilnWood)}");
    }

    // Only this player's own adds are narrowed: OnAddOre (E key, hotbar, radial pick) and FindCookableItem (also
    // AutoStorage's chest feed). RPC_AddOre on the kiln owner stays vanilla: the sender has already removed the item
    // from their inventory, so refusing it there would delete it.
    [HarmonyPatch]
    private static class Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Smelter), "OnAddOre");
            yield return AccessTools.Method(typeof(Smelter), "FindCookableItem");
        }

        private static void Prefix(Smelter __instance, out List<Smelter.ItemConversion> __state)
        {
            __state = __instance.m_conversion;
            if (!_enabled.Value || __instance.m_name != KilnPieceName) return;

            if (!_allowedNamesChecked) WarnAboutNonKilnWood(__state);
            __instance.m_conversion = __state
                .Where(conversion => conversion.m_from != null && _allowedNames.Contains(conversion.m_from.gameObject.name))
                .ToList();
        }

        // __state is null when an earlier prefix threw and this one never ran.
        private static void Finalizer(Smelter __instance, List<Smelter.ItemConversion>? __state)
        {
            if (__state != null) __instance.m_conversion = __state;
        }
    }
}
