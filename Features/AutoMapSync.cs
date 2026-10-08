using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

internal static class AutoMapSync
{
    private const float CheckInterval = 10f;
    private const float WriteInterval = 300f;
    private const float Range = 30f;

    private static ConfigEntry<bool> _enabled = null!;
    private static readonly List<MapTable> Tables = new();
    private static float _nextCheck;
    private static float _nextWrite;
    private static byte[]? _lastRead;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "AutoMapSync",
            true,
            "While you are within 30 m of a cartography table, your map and pins are synced with it automatically: " +
            "you read new data as soon as someone else writes it, and your own map is written when you arrive and every 5 minutes.");
    }

    [HarmonyPatch(typeof(MapTable), "Start")]
    private static class TrackTables
    {
        private static void Postfix(MapTable __instance) => Tables.Add(__instance);
    }

    // Runs every frame, but only a float compare until the 10 s check. The heavy part (the explored array is
    // ~4 MB uncompressed) only runs when the table's data changed or on arrival / every 5 minutes.
    [HarmonyPatch(typeof(Minimap), "Update")]
    private static class Tick
    {
        private static void Postfix()
        {
            Player player = Player.m_localPlayer;
            if (!_enabled.Value || player == null || Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            Tables.RemoveAll(t => t == null);
            Vector3 pos = player.transform.position;
            MapTable? table = Tables
                .Where(t => Vector3.Distance(t.transform.position, pos) < Range)
                .OrderBy(t => Vector3.Distance(t.transform.position, pos))
                .FirstOrDefault();
            ZNetView? nview = table != null ? table.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid())
            {
                _nextWrite = 0f; // write on the next arrival
                return;
            }

            byte[] stored = nview.GetZDO().GetByteArray(ZDOVars.s_data);
            byte[]? data = null;
            // The ZDO hands out the same array until someone writes, so a reference compare is enough.
            if (stored != null && stored != _lastRead)
            {
                data = Utils.Decompress(stored);
                Minimap.instance.AddSharedMapData(data);
                _lastRead = stored;
            }

            if (Time.time < _nextWrite || !PrivateArea.CheckAccess(table!.transform.position, 0f, flash: false)) return;
            _nextWrite = Time.time + WriteInterval;
            if (data == null && stored != null) data = Utils.Decompress(stored);
            byte[] merged = Minimap.instance.GetSharedMapData(data);
            if (data != null && merged.SequenceEqual(data)) return;
            nview.InvokeRPC("MapData", new ZPackage(Utils.Compress(merged)));
        }
    }
}
