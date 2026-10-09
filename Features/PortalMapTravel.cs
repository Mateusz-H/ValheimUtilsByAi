using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Splatform;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Every portal use goes through TeleportWorld.Teleport (called by TeleportWorldTrigger for the local player only).
// Instead of jumping to the tag-linked portal, the large map opens with a temporary pin per known portal; a left click
// on a pin teleports exactly as vanilla does, closing the map cancels. Clients only know portal ZDOs the server sent
// them, so on the server every portal is force-sent each time vanilla re-links portals (every 5 s, revision-gated).
internal static class PortalMapTravel
{
    private static ConfigEntry<bool> _enabled = null!;

    private static readonly Func<Minimap, Vector3, Vector3> ScreenToWorldPoint =
        AccessTools.MethodDelegate<Func<Minimap, Vector3, Vector3>>(
            AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint"));

    private static readonly Dictionary<Minimap.PinData, Quaternion> Pins = new();
    private static float _exitDistance;
    private static bool _allowAllItems;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "PortalMapTravel",
            true,
            "Stepping into a portal opens the map with a pin for every portal in the world. Click a pin to travel " +
            "there; close the map to stay. Portal tags no longer matter.");
    }

    private static void ClearPins()
    {
        foreach (var pin in Pins.Keys) Minimap.instance.RemovePin(pin);
        Pins.Clear();
    }

    // Same checks and messages as the start of vanilla TeleportWorld.Teleport, minus the tag link requirement.
    private static bool PassesVanillaChecks(Player player, bool allowAllItems)
    {
        if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoPortals))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_blocked");
            return false;
        }
        if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals)
            && (RandEventSystem.instance.GetBossEvent() != null
                || (ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out float bosses) && bosses > 0f)))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_blockedbyboss");
            return false;
        }
        if (!player.IsTeleportable(allowAllItems))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_noteleport");
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    private static class OpenMapInsteadOfTeleport
    {
        private static bool Prefix(TeleportWorld __instance, Player player)
        {
            if (!_enabled.Value || Game.m_noMap || Minimap.instance == null) return true;
            if (player.IsTeleporting() || !PassesVanillaChecks(player, __instance.m_allowAllItems)) return false;

            ClearPins();
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            _exitDistance = __instance.m_exitDistance;
            _allowAllItems = __instance.m_allowAllItems;
            ZDO? source = __instance.GetComponent<ZNetView>()?.GetZDO();
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
            {
                if (zdo == source) continue;
                string tag = zdo.GetString(ZDOVars.s_tag).RemoveRichTextTags();
                string author = zdo.GetString(ZDOVars.s_tagauthor);
                var pin = Minimap.instance.AddPin(
                    zdo.GetPosition(), Minimap.PinType.Icon4, string.IsNullOrWhiteSpace(tag) ? "$piece_portal" : tag,
                    save: false, isChecked: false,
                    author: string.IsNullOrEmpty(author) ? PlatformUserID.None : new PlatformUserID(author));
                Pins[pin] = zdo.GetRotation();
            }
            player.Message(MessageHud.MessageType.Center, "Click a portal on the map");
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    private static class PickPortalOnClick
    {
        private static bool Prefix(Minimap __instance)
        {
            if (!_enabled.Value || Pins.Count == 0) return true;
            Player player = Player.m_localPlayer;
            if (player == null) return true;

            Vector3 clicked = ScreenToWorldPoint(__instance, ZInput.pointerPosition);
            float radius = __instance.m_removeRadius * __instance.LargeZoom * 2f;
            Minimap.PinData? closest = null;
            float closestDistance = radius;
            foreach (var pin in Pins.Keys)
            {
                float distance = Utils.DistanceXZ(clicked, pin.m_pos);
                if (distance >= closestDistance) continue;
                closest = pin;
                closestDistance = distance;
            }
            if (closest == null) return false;

            Quaternion rotation = Pins[closest];
            Vector3 target = closest.m_pos + rotation * Vector3.forward * _exitDistance + Vector3.up;
            // Checked again: autopickup and walking keep working while the map is open.
            if (PassesVanillaChecks(player, _allowAllItems) && player.TeleportTo(target, rotation, distantTeleport: true))
                Game.instance.IncrementPlayerStat(PlayerStatType.PortalsUsed);
            __instance.SetMapMode(Minimap.MapMode.Small);
            return false;
        }
    }

    // Double click and gamepad A would otherwise drop a saved pin while choosing a portal. Vanilla only calls this on
    // the large map; a travel click closes it before the double-click check in the same OnMapLeftUp, so block that too.
    [HarmonyPatch(typeof(Minimap), "ShowPinNameInput")]
    private static class BlockNewPinsWhileChoosing
    {
        private static bool Prefix(Minimap __instance) =>
            !_enabled.Value || (__instance.m_mode == Minimap.MapMode.Large && Pins.Count == 0);
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    private static class ClearPinsWhenMapCloses
    {
        private static void Postfix(Minimap __instance)
        {
            if (Pins.Count > 0 && __instance.m_mode != Minimap.MapMode.Large) ClearPins();
        }
    }

    // Server only (vanilla runs ConnectPortals only where IsServer). ZDOPeer.ShouldSend drops portals a client already
    // has at the current revision, so this costs a hash lookup per portal per peer, not a resend.
    [HarmonyPatch(typeof(Game), nameof(Game.ConnectPortals))]
    private static class SendAllPortalsToClients
    {
        private static void Postfix()
        {
            if (!_enabled.Value || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList()) ZDOMan.instance.ForceSendZDO(zdo.m_uid);
        }
    }
}
