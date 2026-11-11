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
//      runs RejoinGameCR and answers C2S_RejoinPreloadDone (host sets ReconnectLoadingDone).
//   3. Natively the host only sends the final Send_RejoinData during the next transition (door,
//      area loading, teleport). A resumed peer's character is standing in the room, so the host
//      can't get through a door and that never happens. The client's S2C_RejoinData handler
//      doesn't need a transition, so it is sent right after the preload ack instead.
// ClientFrame (used by the client to estimate latency): once the client has had AllPlayersReady
// its frame counter follows the server's, so the host frame is the right value. Aging the frame
// from the join request was wrong (test 5: calculatedServerFrame -8677).
internal static class OnlineRejoinPushPatch
{
    private static readonly System.Collections.Generic.HashSet<int> awaitingPreload = new();
    private static readonly System.Collections.Generic.List<GameCode.XNetworkSYS.XNetPlayerData> pending = new();
    private static Il2CppSystem.Collections.IEnumerator? resumeCR;

    private static long HostFrame => GameEngine.Instance == null ? 0 : GameEngine.Instance.CurrentFrame;

    internal static System.Collections.Generic.List<int> LobbyPeerIndexes()
    {
        var indexes = new System.Collections.Generic.List<int>();
        var net = GameCode.Game?.NetworkSYS;
        if (net == null) return indexes;
        foreach (var p in net.allPlayerStates)
            if (p != null && p.netPeer != null && p.playerIndex != 0) indexes.Add(p.playerIndex);
        return indexes;
    }

    // Called by OnlineResumePatch right before ResumeSavedStateCR starts.
    internal static void HoldLobbyPeers(Il2CppSystem.Collections.IEnumerator cr)
    {
        pending.Clear();
        awaitingPreload.Clear();
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
        var held = new System.Collections.Generic.List<int>();
        foreach (var p in pending) held.Add(p.playerIndex);
        OnlineResumeGuardPatch.Begin(held);
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
            OnlineResumeGuardPatch.StopBlocking();
            var players = pending.ToArray();
            pending.Clear();
            SyncNetIds();
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

    // The rejoining client spawns every player with XNetPlayerData.NetId (RejoinGameCR), and an
    // entity spawned with NetId 0 gets no XNetCMP, so XPlayerSYS.Net_SendPlayerStateChange throws
    // every frame (test 6: her movement never reached the host, she never saw his). StartGameCR
    // records the NetIds; ResumeSavedStateCR spawns players with NetId = index + 1 but leaves the
    // net player list as the lobby set it, so copy the real ids over.
    private static void SyncNetIds()
    {
        var world = GameCode.Game.World;
        foreach (var p in GameCode.Game.NetworkSYS.allPlayerStates)
        {
            if (p == null) continue;
            var entity = world.GetPlayerByPlayerIndex(p.playerIndex);
            if (!entity.IsValidEX()) continue;
            var netId = world.GetNetIdForEntityId(entity.Id);
            if (netId == p.NetId) continue;
            Plugin.Logger.LogInfo($"Online resume: player {p.playerIndex} NetId {p.NetId} -> {netId}");
            p.NetId = netId;
        }
    }

    private static void Push(GameCode.XNetworkSYS.XNetPlayerData p)
    {
        var net = GameCode.Game.NetworkSYS;
        // OnPeerDisconnected marks the record Disconnected but leaves netPeer set, so check both.
        if (p.netPeer == null || p.connectionState == ConnState.Disconnected)
        {
            Plugin.Logger.LogWarning($"Online resume: player {p.playerIndex} left during loading");
            return;
        }
        // Natively nothing rebuilds a missing host-side entity (CommonPlayerConnected just exits),
        // so a missing one here means the peer would rejoin as a spectator.
        var hasEntity = GameCode.Game.World.GetPlayerByPlayerIndex(p.playerIndex).IsValidEX();
        var frame = HostFrame;
        p.connectionState = ConnState.Reconnecting;
        net.NET_SendPlayerConnectionStateUpdate(p.playerIndex, ConnState.Reconnecting, false);
        p.lastAckedSnapshotId = -1;
        net.SendInitialRejoinData(p.netPeer, frame, p.playerIndex, false, false);
        awaitingPreload.Add(p.playerIndex);
        Plugin.Logger.LogInfo($"Online resume: sent rejoin data to peer {p.netPeer.Id} (player {p.playerIndex}, client frame ~{frame}, entity {(hasEntity ? "present" : "missing")}, {GameCode.Game.World.AllPlayers.Count} players in world)");
    }

    [HarmonyPatch(typeof(GameCode.XNetworkSYS), "Handle_C2S_RejoinPreloadDone")]
    internal static class SendRejoinDataNow
    {
        private static void Postfix(GameCode.XNetworkSYS __instance)
        {
            if (awaitingPreload.Count == 0) return;
            var ready = new Il2CppSystem.Collections.Generic.List<GameCode.XNetworkSYS.XNetPlayerData>();
            foreach (var p in __instance.allPlayerStates)
            {
                if (p == null || !awaitingPreload.Contains(p.playerIndex)) continue;
                if (p.connectionState != ConnState.ReconnectLoadingDone) continue;
                awaitingPreload.Remove(p.playerIndex);
                ready.Add(p);
            }
            if (ready.Count == 0) return;
            try
            {
                GameCode.XNetworkSYS.Send_RejoinData(GameCode.Game.World, ready);
                Plugin.Logger.LogInfo($"Online resume: sent the final rejoin data to {ready.Count} player(s) without waiting for a transition");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Online resume: Send_RejoinData failed: {e}");
            }
        }
    }
}
