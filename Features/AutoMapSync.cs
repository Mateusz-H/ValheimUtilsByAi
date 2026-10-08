using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Shares the explored map and pins between all players without a cartography table. Each client uploads its map
// (the vanilla cartography table format) every 5 minutes; the server keeps the latest upload in memory and relays it
// to everyone else. Nothing is saved on the server: after a restart the first uploads rebuild it.
// ponytail: last upload wins on the server, so two near-simultaneous uploads can hide a fresh pin from third players
// until the next upload (max 5 min). Merge on the server if that ever matters.
internal static class AutoMapSync
{
    private const float FirstUploadDelay = 30f;
    private const float UploadInterval = 300f;

    private static ConfigEntry<bool> _enabled = null!;
    private static CustomRPC _rpc = null!;
    private static byte[]? _latest;         // server: last upload, compressed
    private static byte[]? _lastUploaded;   // client: uncompressed data of the last upload
    private static float _nextUpload = -1f; // client: -1 until the local player spawns

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "AutoMapSync",
            true,
            "All players share one map: explored areas and pins are synced through the server automatically, " +
            "no cartography table needed. Your map is sent 30 seconds after joining and then every 5 minutes.");
        _rpc = NetworkManager.Instance.AddRPC("ValheimUtilsByAi.AutoMapSync", OnServerReceive, OnClientReceive);
    }

    private static IEnumerator OnServerReceive(long sender, ZPackage package)
    {
        byte[] data = package.ReadByteArray();
        if (!_enabled.Value) yield break;
        if (data.Length == 0) // join request
        {
            if (_latest != null) _rpc.SendPackage(sender, Pack(_latest));
            yield break;
        }
        _latest = data;
        _rpc.SendPackage(ZNet.instance.GetPeers().Where(p => p.m_uid != sender).ToList(), Pack(data));
        if (sender != ZNet.GetUID()) Merge(data); // a listen-server host plays too
    }

    private static IEnumerator OnClientReceive(long sender, ZPackage package)
    {
        Merge(package.ReadByteArray());
        yield break;
    }

    private static void Merge(byte[] compressed)
    {
        if (!_enabled.Value || Player.m_localPlayer == null || Minimap.instance == null) return;
        Minimap.instance.AddSharedMapData(Utils.Decompress(compressed));
    }

    private static ZPackage Pack(byte[] data)
    {
        var package = new ZPackage();
        package.Write(data);
        return package;
    }

    // Runs every frame but is only a float compare between uploads. Building the map data walks the ~4 MB explored
    // array, so that happens at most every 5 minutes and is only compressed and sent when something changed.
    [HarmonyPatch(typeof(Minimap), "Update")]
    private static class Tick
    {
        private static void Postfix()
        {
            if (Player.m_localPlayer == null)
            {
                _nextUpload = -1f;
                _lastUploaded = null;
                return;
            }
            if (!_enabled.Value || Time.time < _nextUpload) return;

            bool isServer = ZNet.instance.IsServer();
            ZNetPeer? serverPeer = isServer ? null : ZNet.instance.GetServerPeer();
            if (!isServer && serverPeer == null) return;
            long server = isServer ? ZNet.GetUID() : serverPeer!.m_uid;

            if (_nextUpload < 0f)
            {
                if (!isServer) _rpc.SendPackage(server, Pack(new byte[0]));
                _nextUpload = Time.time + FirstUploadDelay;
                return;
            }
            _nextUpload = Time.time + UploadInterval;
            byte[] data = Minimap.instance.GetSharedMapData(null);
            if (_lastUploaded != null && data.SequenceEqual(_lastUploaded)) return;
            _lastUploaded = data;
            _rpc.SendPackage(server, Pack(Utils.Compress(data)));
        }
    }
}
