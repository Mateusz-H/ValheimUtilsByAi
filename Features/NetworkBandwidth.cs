using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;

namespace ValheimUtilsByAi.Features;

// Two limits cap how fast ZDO changes (creature positions, doors, chest contents) travel between a game and the server.
// Steam paces every connection at 153600 B/s: ZSteamSocket.RegisterGlobalCallbacks writes it as the global
// SendRateMin and SendRateMax whenever a socket opens while none is open (on a client: every connect, before the
// server's synced config arrives). Steam reads the global value live for every connection not set on its own.
// ZDOMan.SendZDOs, the only vanilla reader of GetSendQueueSize, tops each peer up to 10240 bytes counted by the Steam
// socket as pending or sent but not yet acknowledged: an in-flight window, and with a ping of 50 ms it caps a peer at
// about 70 KB/s, well below the rate. The window grows with the rate at vanilla's 1/15 s ratio: a larger window on the
// same rate only queues latency in front of every RPC, and the 100 ms a client gets to send its last changes on logout
// stays enough to drain it. The Steam socket reports its queue that much smaller, so SendZDOs fills the larger window
// and Jötunn's and ServerSync's RPCs, which wait while the reported queue is above 20000 bytes and disconnect the peer
// after 30 s, still see vanilla's room. Crossplay (PlayFab) sockets have no rate to raise and keep the vanilla window.
internal static class NetworkBandwidth
{
    private const int VanillaRate = 153600;
    private const int VanillaWindow = 10240;

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _rate = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Network",
            "NetworkBandwidth",
            true,
            Plugin.Synced(
                "Lets the server and players send world updates (moving animals, doors, chest contents) at up to " +
                "NetworkSendRate instead of vanilla's 150 KB/s, so busy bases with many animals don't lag or jump."));
        _rate = config.Bind(
            "Network",
            "NetworkSendRate",
            400,
            Plugin.Synced(
                "Most KB per second the server sends to each player, and each player to the server. Vanilla is 150. " +
                "The server's upload must cope with this times the number of players online.",
                new AcceptableValueRange<int>(150, 1024)));
        _enabled.SettingChanged += (_, _) => ApplySendRate();
        _rate.SettingChanged += (_, _) => ApplySendRate();
    }

    private static int SendRate() => _enabled.Value ? _rate.Value * 1024 : VanillaRate;

    private static int ExtraWindow() => _enabled.Value ? Math.Max(0, _rate.Value * 1024 / 15 - VanillaWindow) : 0;

    // A dedicated server runs Steam as a game server, where the client API throws, and the other way round.
    private static void ApplySendRate()
    {
        var rate = GCHandle.Alloc(SendRate(), GCHandleType.Pinned);
        try
        {
            var dedicated = ZNet.instance != null && ZNet.instance.IsDedicated();
            foreach (var key in new[]
                     {
                         ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin,
                         ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax,
                     })
            {
                if (dedicated)
                    SteamGameServerNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, rate.AddrOfPinnedObject());
                else
                    SteamNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, rate.AddrOfPinnedObject());
            }
        }
        catch (InvalidOperationException)
        {
            // Steam is not initialized yet; RegisterGlobalCallbacks applies the rate when the first socket opens.
        }
        finally
        {
            rate.Free();
        }
    }

    [HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
    private static class RatePatch
    {
        private static void Postfix() => ApplySendRate();
    }

    // Not clamped at 0: SendZDOs computes 10240 - queue, which must come out as the whole free window.
    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.GetSendQueueSize))]
    private static class WindowPatch
    {
        private static void Postfix(ref int __result) => __result -= ExtraWindow();
    }
}
