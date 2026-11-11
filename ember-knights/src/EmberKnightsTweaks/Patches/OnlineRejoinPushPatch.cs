using System;
using HarmonyLib;
using ConnState = GameCode.XNetworkSYS.XNetPlayerData.eConnectionState;

namespace EmberKnightsTweaks.Patches;

// A resumed online run skips StartGame_Internal, so lobby peers never get the "game started"
// packet, and the host's area sync (NET_SyncStartAreaDataCR) waits forever for their ack.
// This brings them in the way the game handles a mid-run reconnect, so their (unmodded) game
// needs nothing new:
//   1. Before the resume starts, every connected peer is marked ReconnectWaitingForNextArea.
//      The host's sync waits only count Connected players, so loading no longer blocks.
//   2. When the resume coroutine has finished, each peer gets what HandlePacket_C2S_JoinRequest
//      sends a reconnecting player: state Reconnecting, then SendInitialRejoinData. The client
//      runs RejoinGameCR and reports back through the normal rejoin packets.
// SendInitialRejoinData wants the client's current frame (the client uses it to estimate latency).
// Clients send their frame first in the join request, so it is captured there and aged by the
// frames the host has run since.
internal static class OnlineRejoinPushPatch
{
    private static readonly System.Collections.Generic.Dictionary<int, (long client, long host)> joinFrames = new();
    private static readonly System.Collections.Generic.List<GameCode.XNetworkSYS.XNetPlayerData> pending = new();
    private static Il2CppSystem.Collections.IEnumerator? resumeCR;

    private static long HostFrame => GameEngine.Instance == null ? 0 : GameEngine.Instance.CurrentFrame;

    [HarmonyPatch(typeof(GameCode.XNetworkSYS), "HandlePacket_C2S_JoinRequest")]
    internal static class CaptureClientFrame
    {
        private static void Prefix(XNetworkPeer peer, XByteReader reader)
        {
            try
            {
                var at = reader.Position;
                var clientFrame = (long)reader.ReadUInt64();
                reader.Position = at;
                joinFrames[peer.Id] = (clientFrame, HostFrame);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"Online resume: could not read the joining client's frame: {e.Message}");
            }
        }
    }

    // Called by OnlineResumePatch right before ResumeSavedStateCR starts.
    internal static void HoldLobbyPeers(Il2CppSystem.Collections.IEnumerator cr)
    {
        pending.Clear();
        resumeCR = cr;
        var net = GameCode.Game?.NetworkSYS;
        if (net == null) return;
        net.SERVER_GameStarted = true;
        net.GameId = (ulong)DateTime.UtcNow.Ticks; // StartGame_Internal rolls a fresh id per run
        foreach (var p in net.allPlayerStates)
        {
            if (p == null || p.netPeer == null || p.playerIndex == 0) continue;
            p.connectionState = ConnState.ReconnectWaitingForNextArea;
            pending.Add(p);
            Plugin.Logger.LogInfo($"Online resume: holding peer {p.netPeer.Id} (player {p.playerIndex}) until the run is loaded");
        }
    }

    // The compiler-generated iterator keeps its state at 0x10: 0 before the first step, >= 0 while
    // suspended, -1 once MoveNext has returned false. Outside MoveNext, < 0 means finished.
    // (GameUI.ShowHud was tried as the trigger, but it fires before the players spawn.)
    private static bool ResumeFinished =>
        resumeCR != null && System.Runtime.InteropServices.Marshal.ReadInt32(resumeCR.Pointer + 0x10) < 0;

    [HarmonyPatch(typeof(GameCode.GameInst), nameof(GameCode.GameInst.GameFixedUpdate))]
    internal static class PushWhenLoaded
    {
        private static void Postfix()
        {
            if (!ResumeFinished) return;
            resumeCR = null;
            var players = pending.ToArray();
            pending.Clear();
            foreach (var p in players)
            {
                try
                {
                    Push(p);
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError($"Online resume: rejoin push to player {p.playerIndex} failed: {e}");
                }
            }
        }
    }

    private static void Push(GameCode.XNetworkSYS.XNetPlayerData p)
    {
        var net = GameCode.Game.NetworkSYS;
        if (p.netPeer == null)
        {
            Plugin.Logger.LogWarning($"Online resume: player {p.playerIndex} left during loading");
            return;
        }
        if (!GameCode.Game.World.GetPlayerByPlayerIndex(p.playerIndex).IsValidEX())
        {
            Plugin.Logger.LogWarning($"Online resume: the saved run has no player {p.playerIndex} ({GameCode.Game.World.AllPlayers.Count} players in the world); that peer stays in limbo");
            return;
        }
        var frame = EstimateClientFrame(p.netPeer.Id);
        p.connectionState = ConnState.Reconnecting;
        net.NET_SendPlayerConnectionStateUpdate(p.playerIndex, ConnState.Reconnecting, false);
        p.lastAckedSnapshotId = -1;
        net.SendInitialRejoinData(p.netPeer, frame, p.playerIndex, false, false);
        Plugin.Logger.LogInfo($"Online resume: sent rejoin data to peer {p.netPeer.Id} (player {p.playerIndex}, client frame ~{frame})");
    }

    private static long EstimateClientFrame(int peerId)
    {
        if (joinFrames.TryGetValue(peerId, out var f)) return f.client + (HostFrame - f.host);
        Plugin.Logger.LogWarning($"Online resume: no join frame for peer {peerId}; using the host frame");
        return HostFrame;
    }
}
