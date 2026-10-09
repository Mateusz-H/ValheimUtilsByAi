using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Door.UpdateState runs every 0.2 s on every loaded door. When the local player walks up to a player-built door
// or gate, send the same "UseDoor" RPC as pressing E, and send it again once nobody is near the door any more.
[HarmonyPatch(typeof(Door), "UpdateState")]
internal static class AutoDoors
{
    private const float OpenRange = 2.5f;
    private const float CloseRange = 4f;

    private static ConfigEntry<bool> _enabled = null!;

    // Doors this client opened and still has to close; a door closed by hand stays here, so it isn't reopened
    // until the player has walked away.
    private static readonly HashSet<Door> Opened = new HashSet<Door>();

    private static readonly AccessTools.FieldRef<Door, ZNetView> NView = AccessTools.FieldRefAccess<Door, ZNetView>("m_nview");
    private static readonly Func<Door, bool> CanInteract =
        AccessTools.MethodDelegate<Func<Door, bool>>(AccessTools.Method(typeof(Door), "CanInteract"));
    private static readonly Action<Door, Vector3> Open =
        AccessTools.MethodDelegate<Action<Door, Vector3>>(AccessTools.Method(typeof(Door), "Open"));

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Building",
            "AutoDoors",
            true,
            "Doors and gates you build open by themselves when you walk up to them and close once you've walked through. " +
            "Doors in dungeons, crypts and other places the game generated are left alone.");
    }

    private static void Postfix(Door __instance)
    {
        Player player = Player.m_localPlayer;
        ZNetView nview = NView(__instance);
        if (!_enabled.Value || player == null || !nview.IsValid())
        {
            Opened.Remove(__instance);
            return;
        }

        Vector3 doorPos = __instance.transform.position;
        float distance = Vector3.Distance(player.transform.position, doorPos);
        bool isOpen = nview.GetZDO().GetInt(ZDOVars.s_state) != 0;

        if (Opened.Contains(__instance))
        {
            if (distance < CloseRange) return;
            // Someone else still standing in the doorway keeps it open; this client closes it once they leave.
            if (Player.IsPlayerInRange(doorPos, CloseRange)) return;
            if (isOpen && CanInteract(__instance)) Open(__instance, player.transform.position - doorPos);
            if (!isOpen || CanInteract(__instance)) Opened.Remove(__instance);
            return;
        }

        if (isOpen || distance > OpenRange || player.IsDead()) return;
        Piece piece = __instance.GetComponent<Piece>();
        // Generated doors (dungeons, crypts, villages) have no creator; keyed and one-way doors stay manual.
        if (piece == null || piece.GetCreator() == 0L || __instance.m_keyItem != null || __instance.m_canNotBeClosed) return;
        if (!CanInteract(__instance)) return;
        if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(doorPos, 0f, flash: false)) return;

        Open(__instance, player.transform.position - doorPos);
        Opened.Add(__instance);
    }
}
