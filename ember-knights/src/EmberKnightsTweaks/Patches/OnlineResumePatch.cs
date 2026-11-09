using System;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;

namespace EmberKnightsTweaks.Patches;

// The main menu only offers offline resumes. This adds a hotkey on the online lobby
// (host only) that does what UIMainMenu.ClickedResumeSingle does: decode the saved
// run and start GameInst.ResumeSavedStateCR with the host as saved player 0.
// Unlike the game, the save is NOT cleared on resume, so a failed attempt can be retried;
// the next Save And Quit overwrites it.
[HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.Tick))]
internal static class OnlineResumePatch
{
    private static bool started;

    private static void Postfix(GameCode.UIPlayerSelectOnline __instance)
    {
        if (!Plugin.OnlineResumeEnabled.Value || !Hotkey.WasPressed(Plugin.OnlineResumeKey.Value)) return;
        if (started)
        {
            // DecodeData already ran on the in-memory state; restart the game to retry cleanly.
            Plugin.Logger.LogWarning("Online resume: already attempted this session; restart the game to try again");
            return;
        }
        try
        {
            TryResume(__instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"OnlineResumePatch failed: {e}");
        }
    }

    private static void TryResume(GameCode.UIPlayerSelectOnline lobby)
    {
        var net = GameCode.XNetwork;
        if (net == null || !net.IsServer)
        {
            Plugin.Logger.LogWarning("Online resume: only the host can resume (not hosting a lobby)");
            return;
        }
        var game = GameCode.Game;
        var slot = game.CurrentSaveSlot;
        var saved = slot.MultiplayerSavedState ?? slot.SavedState;
        if (saved == null)
        {
            Plugin.Logger.LogWarning("Online resume: no saved run in this save slot");
            return;
        }
        var which = slot.MultiplayerSavedState != null ? "MultiplayerSavedState" : "SavedState";
        Plugin.Logger.LogInfo($"Online resume: starting from {which}");
        saved.DecodeData(lobby.World.GameMods);
        var players = new List<GameCode.ResumeSavedStateLocalMPPlayerData>();
        players.Add(new GameCode.ResumeSavedStateLocalMPPlayerData { PlayerIndex = 0, RWPlayerId = 0 });
        started = true;
        GameCode.Run(game.ResumeSavedStateCR(saved, players));
    }
}
