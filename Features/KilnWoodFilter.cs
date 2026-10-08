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
    }

    // Both the client-side pick (FindCookableItem/OnAddOre) and the owner-side check (RPC_AddOre) read
    // m_conversion, so narrowing that list for the duration of the call covers every path.
    [HarmonyPatch]
    private static class Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Smelter), "IsItemAllowed", new[] { typeof(string) });
            yield return AccessTools.Method(typeof(Smelter), "FindCookableItem");
        }

        private static void Prefix(Smelter __instance, out List<Smelter.ItemConversion> __state)
        {
            __state = __instance.m_conversion;
            if (!_enabled.Value || __instance.m_name != KilnPieceName) return;

            var allowed = _allowedWood.Value
                .Split(',')
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .ToArray();
            __instance.m_conversion = __state
                .Where(conversion => conversion.m_from != null &&
                                     allowed.Contains(conversion.m_from.gameObject.name, StringComparer.Ordinal))
                .ToList();
        }

        private static void Postfix(Smelter __instance, List<Smelter.ItemConversion> __state) =>
            __instance.m_conversion = __state;
    }
}
