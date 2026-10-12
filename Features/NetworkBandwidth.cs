using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;

namespace ValheimUtilsByAi.Features;

// Two limits cap how fast ZDO changes (creature positions, doors, chest contents) travel between a game and the server.
// Steam paces every connection at 153600 B/s: ZSteamSocket.RegisterGlobalCallbacks writes it as the global
// SendRateMin and SendRateMax whenever a socket opens while none is open (on a client: every connect, before the
// server's synced config arrives). ZDOMan.SendZDOs, the only sender of ZDO data on server and client, tops each peer up
// to 10240 bytes counted by the socket as queued, pending or sent but not yet acknowledged, so the literal is an
// in-flight window rather than a packet size. The window grows with the rate at vanilla's 1/15 s ratio: a larger window
// on the same rate only queues latency in front of every RPC, and the 100 ms a client gets to send its last changes on
// logout stays enough to drain it. Crossplay (PlayFab) sockets have no rate to raise and keep the vanilla window.
internal static class NetworkBandwidth
{
    private const int VanillaRate = 153600;
    private const int VanillaWindow = 10240;

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _rate = null!;

    private static readonly AccessTools.FieldRef<List<ZSteamSocket>> Sockets =
        AccessTools.StaticFieldRefAccess<List<ZSteamSocket>>(AccessTools.Field(typeof(ZSteamSocket), "m_sockets"));

    private static readonly AccessTools.FieldRef<ZSteamSocket, HSteamNetConnection> Connection =
        AccessTools.FieldRefAccess<ZSteamSocket, HSteamNetConnection>("m_con");

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

    private static int SendWindow() =>
        _enabled.Value && ZNet.m_onlineBackend == OnlineBackendType.Steamworks
            ? Math.Max(VanillaWindow, _rate.Value * 1024 / 15)
            : VanillaWindow;

    // The global value covers connections opened later; open ones are set one by one as well, because the server's
    // value arrives after the client's connection exists.
    private static void ApplySendRate()
    {
        var rate = GCHandle.Alloc(SendRate(), GCHandleType.Pinned);
        try
        {
            SetSendRate(ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, rate.AddrOfPinnedObject());
            foreach (var socket in Sockets())
            {
                var connection = Connection(socket);
                if (connection == HSteamNetConnection.Invalid) continue;
                SetSendRate(ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Connection,
                    new IntPtr(connection.m_HSteamNetConnection), rate.AddrOfPinnedObject());
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

    // A dedicated server runs Steam as a game server, where the client API throws, and the other way round.
    private static void SetSendRate(ESteamNetworkingConfigScope scope, IntPtr target, IntPtr rate)
    {
        var dedicated = ZNet.instance != null && ZNet.instance.IsDedicated();
        foreach (var key in new[]
                 {
                     ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin,
                     ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax,
                 })
        {
            if (dedicated)
                SteamGameServerNetworkingUtils.SetConfigValue(
                    key, scope, target, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, rate);
            else
                SteamNetworkingUtils.SetConfigValue(
                    key, scope, target, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, rate);
        }
    }

    [HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
    private static class RatePatch
    {
        private static void Postfix() => ApplySendRate();
    }

    [HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
    private static class WindowPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var window = AccessTools.Method(typeof(NetworkBandwidth), nameof(SendWindow));
            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.LoadsConstant(VanillaWindow))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = window;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 2)
                Plugin.Log.LogWarning($"NetworkBandwidth: expected 2 send limits in ZDOMan.SendZDOs, found {replaced}");
        }
    }
}
