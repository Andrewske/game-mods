using System;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;

namespace EmberKnightsTweaks.Patches;

// The main menu only offers offline resumes. This adds a hotkey on the online lobby
// (character select, host only) that does what UIMainMenu.ClickedResumeSingle does: decode the saved
// run and start GameInst.ResumeSavedStateCR with the host as saved player 0.
// Unlike the game, the save is NOT cleared on resume, so a failed attempt can be retried;
// the next Save And Quit overwrites it.
// Input is read by ResumeKeyWatcher every frame; this class only tracks the lobby screen
// (SetContent runs when it opens, Tick while it is up) and performs the resume.
internal static class OnlineResumePatch
{
    private static bool started;
    private static GameCode.UIPlayerSelectOnline? lobby;
    private static bool tickLogged;

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.SetContent))]
    internal static class TrackOpen
    {
        private static void Postfix(GameCode.UIPlayerSelectOnline __instance)
        {
            lobby = __instance;
            Plugin.Logger.LogInfo("Online lobby opened");
        }
    }

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.Tick))]
    internal static class TrackTick
    {
        private static void Postfix(GameCode.UIPlayerSelectOnline __instance)
        {
            lobby = __instance;
            if (tickLogged) return;
            tickLogged = true;
            Plugin.Logger.LogInfo("Online lobby Tick hook is running");
        }
    }

    public static void OnHotkey()
    {
        var open = lobby != null && lobby.IsOpened;
        Plugin.Logger.LogInfo($"Resume key pressed (lobby seen={lobby != null}, open={open})");
        if (!Plugin.OnlineResumeEnabled.Value || !open) return;
        if (started)
        {
            // DecodeData already ran on the in-memory state; restart the game to retry cleanly.
            Plugin.Logger.LogWarning("Online resume: already attempted this session; restart the game to try again");
            return;
        }
        try
        {
            TryResume(lobby!);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Online resume failed: {e}");
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
