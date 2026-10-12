using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// A client only drops an object it was sent when the server lists it as left its area (ZDOPeer.m_invalidSector);
// it never times one out. The server queues that in ZDOPeer.ZDOSectorInvalidated, called from ZDO.SetSector each
// time an object changes zone, and checks the object's position against each peer's area. But
// ZDO.InternalSetPosition calls SetSector before it stores the new position, so the check sees where the object was:
// players near a portal are never told that someone who went through it left, and keep a frozen copy at the portal
// until the traveller crosses another zone border. Running the check again once the new position is in place tells
// them at once. Only for jumps of more than one zone: a step to the next zone stays in the sync range (FindSectorObjects
// reaches a ring past the active area), and the vanilla check catches the step after it.
internal static class TeleportGhostFix
{
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "TeleportGhostFix",
            true,
            Plugin.Synced(
                "Players who go through a portal disappear at once for the players left behind, instead of " +
                "standing frozen at the portal until they walk on."));
    }

    // Every position change on the server goes through here: ZDOData from a client and ZDO.SetPosition alike.
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.InternalSetPosition))]
    private static class Patch
    {
        private static void Prefix(ZDO __instance, out Vector3 __state) => __state = __instance.GetPosition();

        // Portals: SetSector never moves them between sectors, so vanilla never invalidates them either.
        private static void Postfix(ZDO __instance, Vector3 __state)
        {
            if (!_enabled.Value || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            Vector2s from = ZoneSystem.GetZone(__state);
            Vector2s to = ZoneSystem.GetZone(__instance.GetPosition());
            if (Mathf.Abs(from.x - to.x) <= 1 && Mathf.Abs(from.y - to.y) <= 1) return;
            if (Game.instance != null && Game.instance.PortalPrefabHash.Contains(__instance.GetPrefab())) return;
            ZDOMan.instance.ZDOSectorInvalidated(__instance);
        }
    }
}
