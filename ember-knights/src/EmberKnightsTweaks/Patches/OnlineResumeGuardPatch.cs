using System;
using HarmonyLib;
using UnityEngine;

namespace EmberKnightsTweaks.Patches;

// Diagnostics for the 2-player resume: logs (Debug.LogError adds the Unity stack trace in
// Player.log) whenever a held lobby peer's player entity is removed during or after the resume.
// It found that ResumeSavedStateCR itself disposes restored players missing from JoinedPlayers;
// blocking that froze the loading, so it only watches now. Set WatchOnly = false to block again.
internal static class OnlineResumeGuardPatch
{
    private static readonly System.Collections.Generic.HashSet<int> heldIndexes = new();
    private static readonly System.Collections.Generic.Dictionary<IntPtr, int> heldEntities = new();
    private static bool active;
    private const bool WatchOnly = true;
    private static bool logOnly; // after the rejoin push the native flow owns the entity; just watch

    internal static void Begin(System.Collections.Generic.IEnumerable<int> playerIndexes)
    {
        heldIndexes.Clear();
        heldEntities.Clear();
        foreach (var i in playerIndexes) heldIndexes.Add(i);
        active = heldIndexes.Count > 0;
        logOnly = WatchOnly;
    }

    // Stops blocking once the rejoin data is sent, but keeps reporting removals for this run.
    internal static void StopBlocking()
    {
        if (active) Plugin.Logger.LogInfo($"Online resume guard: resume loaded, {heldEntities.Count} held player entities tracked; now watching only");
        logOnly = true;
    }

    private static bool Report(string what, int playerIndex)
    {
        var msg = $"[EKTweaks] Online resume guard: {(logOnly ? "saw" : "blocked")} {what} for player {playerIndex}";
        Plugin.Logger.LogWarning(msg);
        Debug.LogError(msg);
        return logOnly;
    }

    [HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.SpawnPlayer))]
    internal static class TrackSpawn
    {
        private static void Postfix(GameCode.SavedCharacterData SpawnData, GameCode.XEntity __result)
        {
            if (!active || SpawnData == null || __result == null || !heldIndexes.Contains(SpawnData.PlayerIndex)) return;
            heldEntities[__result.Pointer] = SpawnData.PlayerIndex;
            Plugin.Logger.LogInfo($"Online resume guard: player {SpawnData.PlayerIndex} spawned, guarding it");
        }
    }

    [HarmonyPatch(typeof(GameCode.XNetworkSYS), "CommonPlayerDisconnected")]
    internal static class GuardDisconnect
    {
        private static bool Prefix(int playerIndex)
        {
            if (!active || !heldIndexes.Contains(playerIndex)) return true;
            return Report("CommonPlayerDisconnected", playerIndex);
        }
    }

    [HarmonyPatch(typeof(GameCode.XNetworkSYS), "CleanupDisconnectedPlayers")]
    internal static class GuardCleanupList
    {
        private static void Prefix(GameCode.XNetworkSYS __instance)
        {
            if (!active) return;
            var list = __instance._PendingPlayerCleanup;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null || !heldEntities.TryGetValue(e.Pointer, out var idx)) continue;
                if (!Report("CleanupDisconnectedPlayers (it was queued for cleanup)", idx)) list.RemoveAt(i);
            }
        }
    }

    [HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.RemoveEntityFromLists))]
    internal static class GuardRemove
    {
        private static bool Prefix(GameCode.XEntity Entity) => Allow(Entity, "XWorld.RemoveEntityFromLists");
    }

    [HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.RemoveEntityFromLists_INTERNAL))]
    internal static class GuardRemoveInternal
    {
        private static bool Prefix(GameCode.XEntity Entity) => Allow(Entity, "XWorld.RemoveEntityFromLists_INTERNAL");
    }

    private static bool Allow(GameCode.XEntity entity, string what)
    {
        if (!active || entity == null || !heldEntities.TryGetValue(entity.Pointer, out var idx)) return true;
        return Report(what, idx);
    }
}
