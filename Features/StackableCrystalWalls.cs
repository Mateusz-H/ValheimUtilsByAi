using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimUtilsByAi.Features;

// Crystal walls have WearNTear.m_supports = false: Player.UpdatePlacementGhost marks a piece aimed at one Invalid,
// and WearNTear.UpdateSupport (run by each piece's ZDO owner) takes no support from one. Each crystal wall's flag
// follows the toggle: set in Awake, and on every live wall when the setting changes (also on connect/disconnect).
internal static class StackableCrystalWalls
{
    private static ConfigEntry<bool> _enabled = null!;
    private static readonly HashSet<WearNTear> Walls = new HashSet<WearNTear>();

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "StackableCrystalWalls",
            true,
            Plugin.Synced(
                "Crystal walls can be built on top of and against other crystal walls, and hold up what is built on " +
                "them like stone walls. Turning it off makes pieces resting only on crystal walls collapse."));
        _enabled.SettingChanged += (_, _) =>
        {
            Walls.RemoveWhere(w => w == null);
            foreach (WearNTear wall in Walls) wall.m_supports = _enabled.Value;
        };
    }

    [HarmonyPatch(typeof(WearNTear), "Awake")]
    private static class Register
    {
        private static void Postfix(WearNTear __instance)
        {
            if (__instance.m_supports || !Utils.GetPrefabName(__instance.gameObject).StartsWith("crystal_wall")) return;
            Walls.Add(__instance);
            __instance.m_supports = _enabled.Value;
        }
    }

    [HarmonyPatch(typeof(WearNTear), "OnDestroy")]
    private static class Unregister
    {
        private static void Postfix(WearNTear __instance) => Walls.Remove(__instance);
    }
}
