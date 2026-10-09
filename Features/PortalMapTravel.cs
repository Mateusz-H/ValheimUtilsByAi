using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using Splatform;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Every portal use goes through TeleportWorld.Teleport (called by TeleportWorldTrigger for the local player only).
// Instead of jumping to the tag-linked portal, the large map opens with a temporary pin per known portal; a left click
// on a pin teleports exactly as vanilla does, closing the map cancels. Clients only know portal ZDOs the server sent
// them, so on the server every portal is force-sent each time vanilla re-links portals (every 5 s, revision-gated).
// With PortalsOnMap the same pins also appear whenever the large map is opened normally, for viewing only.
// Since any portal now reaches every other, building one costs more: the hammer's portal pieces get their vanilla
// requirement amounts times PortalCostMultiplier, recomputed from the stored originals whenever either setting changes.
internal static class PortalMapTravel
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _costMultiplier = null!;
    private static ConfigEntry<bool> _showOnMap = null!;

    private static readonly Func<Minimap, Vector3, Vector3> ScreenToWorldPoint =
        AccessTools.MethodDelegate<Func<Minimap, Vector3, Vector3>>(
            AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint"));

    // UnityEngine.ImageConversionModule is built against netstandard 2.1, which a net462 project cannot reference.
    private static readonly Func<Texture2D, byte[], bool> LoadImage =
        AccessTools.MethodDelegate<Func<Texture2D, byte[], bool>>(
            AccessTools.Method(AccessTools.TypeByName("UnityEngine.ImageConversion"), "LoadImage",
                new[] { typeof(Texture2D), typeof(byte[]) }));

    private static readonly Dictionary<Minimap.PinData, Quaternion> Pins = new();
    private static readonly Dictionary<Piece.Requirement, int> VanillaAmounts = new();
    private static Sprite? _pinIcon;
    private static float _exitDistance;
    private static bool _allowAllItems;
    // True while the map was opened by a portal: only then a pin click travels.
    private static bool _choosing;

    // Vanilla m_animate pulses at sin(t * 5) +-20 %; this one is slower and softer.
    private const float PulseSpeed = 2.5f;
    private const float PulseAmount = 0.15f;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "PortalMapTravel",
            true,
            "Stepping into a portal opens the map with a pin for every portal in the world. Click a pin to travel " +
            "there; close the map to stay. Portal tags no longer matter.");
        _costMultiplier = config.Bind(
            "World",
            "PortalCostMultiplier",
            1.5f,
            new ConfigDescription(
                "While PortalMapTravel is on, every material of the portal and stone portal costs this many times " +
                "the vanilla amount, rounded up. 1 keeps vanilla prices.",
                new AcceptableValueRange<float>(0.1f, 10f)));
        _showOnMap = config.Bind(
            "World",
            "PortalsOnMap",
            true,
            "Every portal in the world is shown on the large map (M) all the time, not only when stepping into one. " +
            "These pins are for viewing; travel still starts at a portal.");
        _enabled.SettingChanged += (_, _) => ApplyCost();
        _costMultiplier.SettingChanged += (_, _) => ApplyCost();
        PrefabManager.OnVanillaPrefabsAvailable += FindPortalPieces;
    }

    private static void FindPortalPieces()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= FindPortalPieces;
        var pieces = PrefabManager.Instance.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared
            .m_buildPieces?.m_pieces;
        if (pieces == null)
        {
            Plugin.Log.LogWarning("PortalMapTravel: hammer piece table not found, portal cost unchanged");
            return;
        }
        foreach (GameObject prefab in pieces)
        {
            if (prefab == null || prefab.GetComponent<TeleportWorld>() == null) continue;
            foreach (var requirement in prefab.GetComponent<Piece>().m_resources)
                VanillaAmounts[requirement] = requirement.m_amount;
            Plugin.Log.LogInfo($"PortalMapTravel: cost multiplier applies to {prefab.name}");
        }
        ApplyCost();
    }

    // Prefab-only: placed portals copied their requirements when instantiated and refund those on destruction.
    private static void ApplyCost()
    {
        float multiplier = _enabled.Value ? _costMultiplier.Value : 1f;
        foreach (var entry in VanillaAmounts)
            entry.Key.m_amount = Mathf.Max(1, Mathf.CeilToInt(entry.Value * multiplier));
    }

    private static Sprite? PinIcon()
    {
        if (_pinIcon != null) return _pinIcon;
        using var stream = typeof(PortalMapTravel).Assembly.GetManifestResourceStream("portal-pin.png");
        if (stream == null) return null;
        var bytes = new byte[stream.Length];
        stream.Read(bytes, 0, bytes.Length);
        var texture = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Clamp };
        if (!LoadImage(texture, bytes)) return null;
        _pinIcon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        return _pinIcon;
    }

    private static void ClearPins()
    {
        foreach (var pin in Pins.Keys) Minimap.instance.RemovePin(pin);
        Pins.Clear();
    }

    // PinType.None, like vanilla location pins: the icon filter never hides it, and AddPin does not switch a hidden
    // filter back on as it would for Icon4. It has no sprite of its own, so the portal icon is set here. save: false
    // also keeps vanilla right click from deleting them, since GetClosestPin only considers saved pins.
    private static void AddPortalPins(ZDO? skip)
    {
        Sprite? icon = PinIcon();
        foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
        {
            if (zdo == skip) continue;
            string tag = zdo.GetString(ZDOVars.s_tag).RemoveRichTextTags();
            string author = zdo.GetString(ZDOVars.s_tagauthor);
            var pin = Minimap.instance.AddPin(
                zdo.GetPosition(), icon != null ? Minimap.PinType.None : Minimap.PinType.Icon4,
                string.IsNullOrWhiteSpace(tag) ? "$piece_portal" : tag,
                save: false, isChecked: false,
                author: string.IsNullOrEmpty(author) ? PlatformUserID.None : new PlatformUserID(author));
            if (icon != null) pin.m_icon = icon;
            Pins[pin] = zdo.GetRotation();
        }
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

            _exitDistance = __instance.m_exitDistance;
            _allowAllItems = __instance.m_allowAllItems;
            // Pins first: the map may already be open with viewing pins, and if it is not, the SetMapMode postfix
            // only adds viewing pins while not choosing.
            ClearPins();
            AddPortalPins(__instance.GetComponent<ZNetView>()?.GetZDO());
            _choosing = true;
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            player.Message(MessageHud.MessageType.Center, "Click a portal on the map");
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    private static class PickPortalOnClick
    {
        private static bool Prefix(Minimap __instance)
        {
            if (!_enabled.Value || !_choosing || Pins.Count == 0) return true;
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
            !_enabled.Value || (__instance.m_mode == Minimap.MapMode.Large && !_choosing);
    }

    // UpdatePins creates a pin's marker (and its name) as the last child only when it scrolls into view, so other pins
    // can end up drawn over ours. Move ours back to the end, which UI draws last, after every rebuild.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    private static class DrawPortalPinsOnTop
    {
        private static void Postfix()
        {
            foreach (var pin in Pins.Keys)
            {
                if (pin.m_uiElement != null) pin.m_uiElement.SetAsLastSibling();
                pin.m_NamePinData?.PinNameRectTransform?.SetAsLastSibling();
            }
        }
    }

    // UpdatePins only runs when something changed, so the pulse is driven from Update every frame.
    [HarmonyPatch(typeof(Minimap), "Update")]
    private static class PulsePortalPins
    {
        private static void Postfix(Minimap __instance)
        {
            if (!_choosing || Pins.Count == 0 || __instance.m_mode != Minimap.MapMode.Large) return;
            float size = __instance.m_pinSizeLarge * (1f + Mathf.Sin(Time.time * PulseSpeed) * PulseAmount);
            foreach (var pin in Pins.Keys)
            {
                if (pin.m_uiElement == null) continue;
                pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
                pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
            }
        }
    }

    // Viewing pins are rebuilt on every open, so new portals and a changed PortalsOnMap show up the next time.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    private static class SyncPinsWithMapMode
    {
        private static void Postfix(Minimap __instance)
        {
            if (__instance.m_mode != Minimap.MapMode.Large)
            {
                if (Pins.Count > 0) ClearPins();
                _choosing = false;
            }
            else if (Pins.Count == 0 && !_choosing && _showOnMap.Value)
            {
                AddPortalPins(null);
            }
        }
    }

    // Server only (vanilla runs ConnectPortals only where IsServer). ZDOPeer.ShouldSend drops portals a client already
    // has at the current revision, so this costs a hash lookup per portal per peer, not a resend.
    [HarmonyPatch(typeof(Game), nameof(Game.ConnectPortals))]
    private static class SendAllPortalsToClients
    {
        private static void Postfix()
        {
            if (!(_enabled.Value || _showOnMap.Value) || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList()) ZDOMan.instance.ForceSendZDO(zdo.m_uid);
        }
    }
}
