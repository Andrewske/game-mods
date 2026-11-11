using System;
using HarmonyLib;
using ConnState = GameCode.XNetworkSYS.XNetPlayerData.eConnectionState;

namespace EmberKnightsTweaks.Patches;

// Mid-run disconnect/rejoin during a resumed online run. Once the game has started,
// HandlePacket_C2S_JoinRequest only lets a player back in if the request's GameId equals
// XNetworkSYS.GameId (ISIL 617-618), then matches their PlatformId in allPlayerStates. A normal
// start sends the GameId in S2C_GameStarted and the client stores it (LastConnectedHostGameId);
// a resumed run never sends one, so the client asks with an old id and is refused. Bre's game is
// unmodded, so the host adopts the id she sends instead, but only for a player already in this
// run who isn't currently connected.
internal static class ResumedRunRejoinPatch
{
    internal static bool ResumedSession;

    [HarmonyPatch(typeof(GameCode.XNetworkSYS), "HandlePacket_C2S_JoinRequest")]
    internal static class AdoptClientGameId
    {
        private static void Prefix(GameCode.XNetworkSYS __instance, XByteReader reader)
        {
            if (!ResumedSession || !__instance.SERVER_GameStarted) return;
            try
            {
                var request = PeekRequest(reader);
                if (request == null || request.GameId == __instance.GameId) return;
                var player = FindReturningPlayer(__instance, request.PlatformId);
                if (player == null) return;
                Plugin.Logger.LogInfo($"Online resume: player {player.playerIndex} is rejoining with run id {request.GameId}; adopting it (was {__instance.GameId})");
                __instance.GameId = request.GameId;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"Online resume: could not inspect a join request: {e.Message}");
            }
        }
    }

    // The packet is the client frame (UInt64) then JoinGameRequestData; read it and rewind so the
    // game's handler sees the same bytes.
    private static GameCode.XNetworkSYS.JoinGameRequestData? PeekRequest(XByteReader reader)
    {
        var at = reader.Position;
        try
        {
            reader.ReadUInt64();
            var request = new GameCode.XNetworkSYS.JoinGameRequestData();
            request.NetDeserialize(reader);
            return request;
        }
        finally
        {
            reader.Position = at;
        }
    }

    private static GameCode.XNetworkSYS.XNetPlayerData? FindReturningPlayer(GameCode.XNetworkSYS net, string platformId)
    {
        if (string.IsNullOrEmpty(platformId)) return null;
        foreach (var p in net.allPlayerStates)
        {
            if (p == null || p.PlatformId != platformId) continue;
            return p.connectionState == ConnState.Connected ? null : p;
        }
        return null;
    }
}
