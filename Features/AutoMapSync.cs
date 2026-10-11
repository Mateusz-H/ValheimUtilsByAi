using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Shares the explored map and pins between all players without a cartography table. Each client uploads its map (the
// vanilla cartography table format) when it joins and then at most every 5 minutes, only after the player explored or
// changed their own pins. The server merges every upload into one shared map, like a table everyone writes to, and
// sends it to whoever is missing something. Nothing is saved on the server: after a restart the uploads rebuild it,
// because every player's own map still holds everything that was shared before.
internal static class AutoMapSync
{
    private const float UploadInterval = 300f;

    private static ConfigEntry<bool> _enabled = null!;
    private static CustomRPC _rpc = null!;

    private static SharedMap _sharedMap = new(); // server side

    private static float _nextUpload;
    private static bool _exploredSinceUpload;
    private static List<SharedPin>? _ownPinsAtLastUpload; // null until this session's join upload
    private static byte[]? _pendingSharedMap; // latest map from the server, applied by the minimap when it can
    private static bool _applyingSharedMap;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "AutoMapSync",
            true,
            Plugin.Synced(
                "All players share one map: explored areas and pins are merged on the server and passed on to " +
                "everyone, no cartography table needed. Your map is sent when you join and then at most every 5 " +
                "minutes, only after you explored something or changed your own pins."));
        _enabled.SettingChanged += (_, _) => ResetClient(); // switched back on: upload and fetch the shared map again
        _rpc = NetworkManager.Instance.AddRPC("ValheimUtilsByAi.AutoMapSync", OnServerReceive, OnClientReceive);
    }

    private static void ResetClient()
    {
        _nextUpload = 0f;
        _exploredSinceUpload = false;
        _ownPinsAtLastUpload = null;
        _pendingSharedMap = null;
    }

    // The wire format is the one of 0.0.4 (Utils.Compress of the vanilla shared map data, an empty array asks for the
    // shared map), so clients of older versions keep working with this server.
    private static IEnumerator OnServerReceive(long sender, ZPackage package)
    {
        byte[] upload = package.ReadByteArray();
        if (!_enabled.Value) yield break;
        if (upload.Length == 0) SendSharedMap(sender); // join request of 0.0.4-0.0.10 clients
        else MergeUpload(sender, upload);
    }

    private static void MergeUpload(long sender, byte[] upload)
    {
        if (!SharedMap.TryReadUpload(upload, out byte[] explored, out List<SharedPin> pins)) return;
        bool changed = _sharedMap.Merge(PlayerIdOf(sender), explored, pins, out bool uploaderIsBehind);
        if (changed)
        {
            List<ZNetPeer> others = ZNet.instance.GetPeers().Where(peer => peer.m_uid != sender).ToList();
            if (others.Count > 0) _rpc.SendPackage(others, Pack(_sharedMap.Payload()!));
            bool hostPlaysToo = !ZNet.instance.IsDedicated();
            if (hostPlaysToo && sender != ZNet.GetUID()) _pendingSharedMap = _sharedMap.Payload();
        }
        if (uploaderIsBehind) SendSharedMap(sender);
    }

    private static void SendSharedMap(long target)
    {
        byte[]? payload = _sharedMap.Payload();
        if (payload == null) return;
        // CustomRPC hands a package sent to ourselves to OnServerReceive, so the listen-server host applies it directly.
        if (target == ZNet.GetUID()) _pendingSharedMap = payload;
        else _rpc.SendPackage(target, Pack(payload));
    }

    // Uploads list the uploader's own pins under its player ID (vanilla GetSharedMapData). The player's ZDO can reach
    // the server after the join upload; while it is unknown (0) the upload only fills gaps and never replaces pins.
    private static long PlayerIdOf(long sender)
    {
        if (sender == ZNet.GetUID()) return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
        ZNetPeer? peer = ZNet.instance.GetPeer(sender);
        ZDO? character = peer == null ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
        return character?.GetLong(ZDOVars.s_playerID) ?? 0L;
    }

    private static IEnumerator OnClientReceive(long sender, ZPackage package)
    {
        if (_enabled.Value) _pendingSharedMap = package.ReadByteArray();
        yield break;
    }

    private static ZPackage Pack(byte[] data)
    {
        var package = new ZPackage();
        package.Write(data);
        return package;
    }

    // Runs every frame but is only a few compares between uploads: the ~4 MB map is built only when it gets sent.
    [HarmonyPatch(typeof(Minimap), "Update")]
    private static class SyncWithServer
    {
        private static void Postfix(
            Minimap __instance, bool ___m_hasGenerated, List<Minimap.PinData> ___m_pins, Minimap.PinData? ___m_namePin)
        {
            if (!_enabled.Value || Game.m_noMap || Player.m_localPlayer == null || !___m_hasGenerated) return;
            // Applying before our own join upload would drop pins only we hold (vanilla deletes other players' pins
            // missing from the shared map); the server answers that upload with the merged map anyway.
            if (_pendingSharedMap != null && _ownPinsAtLastUpload != null) ApplySharedMap(__instance);
            // A pin being named is still nameless; uploaded now, it would stay nameless for everyone else.
            if (Time.time < _nextUpload || ___m_namePin != null || !TryGetServer(out long server)) return;

            List<SharedPin> ownPins = OwnPins(___m_pins);
            _nextUpload = Time.time + UploadInterval;
            if (_ownPinsAtLastUpload != null && !_exploredSinceUpload && SamePins(ownPins, _ownPinsAtLastUpload)) return;
            _exploredSinceUpload = false;
            _ownPinsAtLastUpload = ownPins;
            _rpc.SendPackage(server, Pack(Utils.Compress(__instance.GetSharedMapData(null))));
        }
    }

    private static bool TryGetServer(out long server)
    {
        server = 0L;
        if (ZNet.instance.IsServer())
        {
            if (ZNet.instance.GetPeers().Count == 0) return false; // playing alone: the first player to join triggers it
            server = ZNet.GetUID();
            return true;
        }
        ZNetPeer? serverPeer = ZNet.instance.GetServerPeer();
        if (serverPeer == null || !ModCompatibility.IsModuleOnServer(MyPluginInfo.PLUGIN_GUID)) return false;
        server = serverPeer.m_uid;
        return true;
    }

    private static void ApplySharedMap(Minimap minimap)
    {
        byte[] compressed = _pendingSharedMap!;
        _pendingSharedMap = null;
        _applyingSharedMap = true;
        try
        {
            minimap.AddSharedMapData(Utils.Decompress(compressed));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"AutoMapSync: could not apply the shared map: {e.Message}");
        }
        finally
        {
            _applyingSharedMap = false;
        }
    }

    [HarmonyPatch(typeof(Minimap), "Explore", typeof(int), typeof(int))]
    private static class NoteLocalExploration
    {
        private static void Postfix(bool __result)
        {
            if (__result) _exploredSinceUpload = true;
        }
    }

    // Vanilla AddPin shows a hidden pin type again (with a gamepad rumble) for every new pin of that type. Fine when
    // reading a table by hand, but automatic syncs would flip the player's icon filters at random moments.
    [HarmonyPatch(typeof(Minimap), "ToggleIconFilter")]
    private static class KeepIconFiltersDuringSync
    {
        private static bool Prefix() => !_applyingSharedMap;
    }

    // A listen-server host can play several worlds in one game session; shared map data must never cross worlds.
    [HarmonyPatch(typeof(ZNet), "Awake")]
    private static class ResetOnWorldStart
    {
        private static void Postfix()
        {
            _sharedMap = new SharedMap();
            ResetClient();
        }
    }

    private static List<SharedPin> OwnPins(List<Minimap.PinData> pins) =>
        pins.Where(pin => pin.m_save && pin.m_ownerID == 0L && pin.m_type != Minimap.PinType.Death)
            .Select(pin => new SharedPin { Name = pin.m_name, Pos = pin.m_pos, Type = (int)pin.m_type, Checked = pin.m_checked })
            .ToList();

    private static bool SamePins(List<SharedPin> a, List<SharedPin> b) =>
        a.Count == b.Count && a.Zip(b, (x, y) => x.SameAs(y)).All(same => same);

    private sealed class SharedPin
    {
        public long Owner;
        public string Name = "";
        public Vector3 Pos;
        public int Type;
        public bool Checked;
        public string Author = "";

        public bool SameAs(SharedPin other) =>
            Owner == other.Owner && Name == other.Name && Pos == other.Pos && Type == other.Type && Checked == other.Checked;
    }

    // The server's merged map: one cartography table that every upload is written to.
    private sealed class SharedMap
    {
        private const int VanillaVersion = 3; // Version.SharedMap.PinsAuthor, what Minimap.GetSharedMapData writes

        private byte[]? _explored; // one byte per map pixel as in the vanilla data, 1 = explored by someone
        private readonly List<SharedPin> _pins = new();
        // Owners who uploaded since the server started: from then on only their own uploads change their pins.
        private readonly HashSet<long> _ownersWhoUploaded = new();
        private byte[]? _payload; // compressed, rebuilt only after a change

        // Vanilla layout: version, pixel count, one bool per pixel, pin count, pins (author since version 3).
        public static bool TryReadUpload(byte[] compressed, out byte[] explored, out List<SharedPin> pins)
        {
            explored = Array.Empty<byte>();
            pins = new List<SharedPin>();
            try
            {
                byte[] data = Utils.Decompress(compressed);
                var package = new ZPackage(data);
                int version = package.ReadInt();
                if (version < 2 || version > VanillaVersion) throw new InvalidOperationException($"unknown version {version}");
                int pixels = package.ReadInt();
                if (pixels <= 0 || pixels > data.Length) throw new InvalidOperationException($"bad pixel count {pixels}");
                explored = package.ReadByteArray(pixels);
                if (explored.Length != pixels) throw new InvalidOperationException("explored area is cut off");
                int count = package.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    pins.Add(new SharedPin
                    {
                        Owner = package.ReadLong(),
                        Name = package.ReadString(),
                        Pos = package.ReadVector3(),
                        Type = package.ReadInt(),
                        Checked = package.ReadBool(),
                        Author = version >= VanillaVersion ? package.ReadString() : "",
                    });
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"AutoMapSync: dropped an unreadable map upload: {e.Message}");
                return false;
            }
        }

        // Returns whether the shared map changed; uploaderIsBehind tells whether it holds something the upload lacks.
        public bool Merge(long uploader, byte[] explored, List<SharedPin> uploadedPins, out bool uploaderIsBehind)
        {
            uploaderIsBehind = false;
            _explored ??= new byte[explored.Length];
            if (_explored.Length != explored.Length)
            {
                Plugin.Log.LogWarning(
                    $"AutoMapSync: dropped a map upload of {explored.Length} pixels, the shared map has {_explored.Length}");
                return false;
            }

            bool changed = false;
            for (int i = 0; i < explored.Length; i++)
            {
                if (explored[i] != 0)
                {
                    if (_explored[i] != 0) continue;
                    _explored[i] = 1;
                    changed = true;
                }
                else if (_explored[i] != 0)
                {
                    uploaderIsBehind = true;
                }
            }

            if (uploader != 0L)
            {
                List<SharedPin> ownPins = uploadedPins.Where(pin => pin.Owner == uploader).ToList();
                if (!SamePins(ownPins, _pins.Where(pin => pin.Owner == uploader).ToList()))
                {
                    _pins.RemoveAll(pin => pin.Owner == uploader);
                    _pins.AddRange(ownPins);
                    changed = true;
                }
                _ownersWhoUploaded.Add(uploader);
            }
            foreach (SharedPin pin in uploadedPins)
            {
                // Second-hand pins (e.g. of offline players) only fill in for owners who have not uploaded themselves.
                if (pin.Owner == 0L || pin.Owner == uploader || _ownersWhoUploaded.Contains(pin.Owner)) continue;
                if (_pins.Any(known => known.Owner == pin.Owner && Utils.DistanceXZ(known.Pos, pin.Pos) < 1f)) continue;
                _pins.Add(pin);
                changed = true;
            }
            // Vanilla treats pins less than 1 m apart as the same pin.
            uploaderIsBehind |= _pins.Any(known => !uploadedPins.Any(pin => Utils.DistanceXZ(pin.Pos, known.Pos) < 1f));

            if (changed) _payload = null;
            return changed;
        }

        // Null until the first upload arrived.
        public byte[]? Payload() => _explored == null ? null : _payload ??= Utils.Compress(Serialize(_explored));

        private byte[] Serialize(byte[] explored)
        {
            var package = new ZPackage();
            package.Write(VanillaVersion);
            package.Write(explored); // array length, then one byte per pixel: the same bytes vanilla writes as bools
            package.Write(_pins.Count);
            foreach (SharedPin pin in _pins)
            {
                package.Write(pin.Owner);
                package.Write(pin.Name);
                package.Write(pin.Pos);
                package.Write(pin.Type);
                package.Write(pin.Checked);
                package.Write(pin.Author);
            }
            return package.GetArray();
        }
    }
}
