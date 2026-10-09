using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Vehicles are prefabs with a Vagon (cart) or Ship component; each gets a map pin with its own build-menu icon.
// ZDOMan keeps every ZDO in a per-sector array; a sweep walks it a slice per frame on server and client alike.
// The server force-sends each vehicle it finds to every peer (ZDOPeer drops ones a peer already has at the current
// revision), and clients never forget ZDOs they were sent, so every client ends up knowing all carts and ships.
// Pins follow the ZDO position from UpdateDynamicPins, the same place vanilla moves the other players' pins.
internal static class VehiclesOnMap
{
    private static ConfigEntry<bool> _enabled = null!;

    private static readonly AccessTools.FieldRef<ZDOMan, List<ZDO>[]> ObjectsBySector =
        AccessTools.FieldRefAccess<ZDOMan, List<ZDO>[]>("m_objectsBySector");
    private static readonly AccessTools.FieldRef<Minimap, bool> PinUpdateRequired =
        AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");

    // Prefab hash -> pin icon (null when the prefab has no Piece icon).
    private static readonly Dictionary<int, Sprite?> VehicleIcons = new();
    private static readonly Dictionary<ZDOID, Minimap.PinData> Pins = new();
    private static HashSet<ZDOID> _found = new();
    private static HashSet<ZDOID> _sweeping = new();
    private static int _sector;

    // Vanilla GetAllZDOsWithPrefabIterative also stops after 400 non-empty sectors per call.
    private const int SectorsPerFrame = 400;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "VehiclesOnMap",
            true,
            "Every cart and ship in the world is shown on the map and minimap with a small icon, wherever it is.");
        _enabled.SettingChanged += (_, _) =>
        {
            if (!_enabled.Value) ClearPins();
        };
    }

    private static void ClearPins()
    {
        if (Minimap.instance != null)
            foreach (var pin in Pins.Values) Minimap.instance.RemovePin(pin);
        Pins.Clear();
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    private static class FindVehiclePrefabs
    {
        private static void Postfix(ZNetScene __instance)
        {
            VehicleIcons.Clear();
            _found.Clear();
            _sweeping.Clear();
            _sector = 0;
            Pins.Clear();
            foreach (GameObject prefab in __instance.m_prefabs)
            {
                if (prefab == null || (prefab.GetComponent<Vagon>() == null && prefab.GetComponent<Ship>() == null))
                    continue;
                VehicleIcons[prefab.name.GetStableHashCode()] = prefab.GetComponent<Piece>()?.m_icon;
            }
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
    private static class SweepForVehicles
    {
        private static void Postfix(ZDOMan __instance)
        {
            if (!_enabled.Value || VehicleIcons.Count == 0 || ZNet.instance == null) return;
            List<ZDO>[] sectors = ObjectsBySector(__instance);
            if (sectors == null) return;
            bool isServer = ZNet.instance.IsServer();
            int scanned = 0;
            while (scanned < SectorsPerFrame)
            {
                if (_sector >= sectors.Length)
                {
                    (_found, _sweeping) = (_sweeping, _found);
                    _sweeping.Clear();
                    _sector = 0;
                    return;
                }
                List<ZDO>? list = sectors[_sector++];
                if (list == null) continue;
                scanned++;
                foreach (ZDO zdo in list)
                {
                    if (!VehicleIcons.ContainsKey(zdo.GetPrefab())) continue;
                    _sweeping.Add(zdo.m_uid);
                    _found.Add(zdo.m_uid);
                    if (isServer) __instance.ForceSendZDO(zdo.m_uid);
                }
            }
        }
    }

    // PinType.None, like vanilla location pins and the portal pins: the icon filter never hides it. save: false keeps
    // them out of the profile and out of reach of right-click delete.
    [HarmonyPatch(typeof(Minimap), "UpdateDynamicPins")]
    private static class UpdateVehiclePins
    {
        private static readonly List<ZDOID> Gone = new();

        private static void Postfix(Minimap __instance)
        {
            if (!_enabled.Value || ZDOMan.instance == null) return;
            bool changed = false;

            Gone.Clear();
            foreach (var entry in Pins)
            {
                ZDO? zdo = _found.Contains(entry.Key) ? ZDOMan.instance.GetZDO(entry.Key) : null;
                if (zdo == null)
                {
                    Gone.Add(entry.Key);
                    continue;
                }
                Vector3 pos = zdo.GetPosition();
                if (pos == entry.Value.m_pos) continue;
                entry.Value.m_pos = pos;
                changed = true;
            }
            foreach (ZDOID id in Gone)
            {
                __instance.RemovePin(Pins[id]);
                Pins.Remove(id);
            }

            foreach (ZDOID id in _found)
            {
                if (Pins.ContainsKey(id)) continue;
                ZDO? zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null || !VehicleIcons.TryGetValue(zdo.GetPrefab(), out Sprite? icon)) continue;
                var pin = __instance.AddPin(zdo.GetPosition(), icon != null ? Minimap.PinType.None : Minimap.PinType.Icon3,
                    "", save: false, isChecked: false);
                if (icon != null) pin.m_icon = icon;
                Pins[id] = pin;
            }

            if (changed) PinUpdateRequired(__instance) = true;
        }
    }
}
