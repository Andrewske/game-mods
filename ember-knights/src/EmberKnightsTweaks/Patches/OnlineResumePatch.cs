using System;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmberKnightsTweaks.Patches;

// The main menu only offers offline resumes. This adds a "Resume" button next to Start in
// the online lobby (host only). It goes through the normal start flow (CanStartGame,
// countdown, StartGame) and only swaps the last step: instead of StartGame_Internal it does
// what UIMainMenu.ClickedResumeSingle does, i.e. decode the saved run and start
// GameInst.ResumeSavedStateCR with the host as saved player 0. Start still begins a fresh run.
// Unlike the game, the save is NOT cleared on resume; the next Save And Quit overwrites it.
internal static class OnlineResumePatch
{
    private static UITinyButton? resumeButton;
    private static bool resumeRequested;
    private static IntPtr lastDecoded = IntPtr.Zero;
    private static IntPtr labelled = IntPtr.Zero;

    private static GameCode.SinglePlayerSavedState? SavedRun()
    {
        var slot = GameCode.Game?.CurrentSaveSlot;
        if (slot == null) return null;
        // Save And Quit writes only one of the two (MultiplayerSavedState when 2+ players were in
        // the world, else SavedState) and never clears the other, so both can be live. Match the
        // lobby: with peers in it use the multiplayer save, alone prefer the solo one.
        var withPeers = OnlineRejoinPushPatch.LobbyPeerIndexes().Count > 0;
        var saved = withPeers ? slot.MultiplayerSavedState ?? slot.SavedState : slot.SavedState ?? slot.MultiplayerSavedState;
        // DecodeData mutates the state, so the same object is never resumed twice.
        return saved == null || saved.Pointer == lastDecoded ? null : saved;
    }

    private static bool IsHost => GameCode.XNetwork != null && GameCode.XNetwork.IsServer;

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.SetContent))]
    internal static class AddButton
    {
        private static void Postfix(GameCode.UIPlayerSelectOnline __instance)
        {
            ResumedRunRejoinPatch.ResumedSession = false; // a new lobby means any earlier run is over
            if (!Plugin.OnlineResumeEnabled.Value || !IsHost) return;
            try
            {
                CreateButton(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Online resume: could not add the Resume button: {e}");
            }
        }
    }

    // Keeps the button in step with Start (hidden/disabled during countdowns) and the save.
    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.Tick))]
    internal static class SyncButton
    {
        private static void Postfix(GameCode.UIPlayerSelectOnline __instance)
        {
            if (resumeButton == null || resumeButton.WasCollected) return;
            var start = __instance.Refs.HostStartGameButton;
            var saved = SavedRun();
            var shown = saved == null ? IntPtr.Zero : saved.Pointer;
            if (shown != labelled)
            {
                labelled = shown; // which save is offered changes when a peer joins or leaves
                if (saved != null) resumeButton.SetText(Label(saved));
            }
            var visible = start.gameObject.activeSelf && saved != null;
            resumeButton.interactable = start.interactable && saved != null && RosterMatches(saved);
            if (visible == resumeButton.gameObject.activeSelf) return;
            resumeButton.gameObject.SetActive(visible);
            __instance.SetupNavigation(); // the postfix below splices Resume back in when visible
        }
    }

    // SetupNavigation links the lobby buttons explicitly (Start <-> Invite Friends), so a copied
    // button is unreachable by controller until it is spliced in: Start <-> Resume <-> next.
    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.SetupNavigation))]
    internal static class SpliceNavigation
    {
        private static void Postfix(GameCode.UIPlayerSelectOnline __instance)
        {
            if (resumeButton == null || resumeButton.WasCollected || !resumeButton.gameObject.activeSelf) return;
            try
            {
                Splice(__instance.Refs.HostStartGameButton, resumeButton);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Online resume: navigation splice failed: {e}");
            }
        }
    }

    private static void Splice(Selectable start, Selectable resume)
    {
        var startNav = start.navigation;
        var next = startNav.selectOnRight;
        var resumeNav = startNav;
        resumeNav.selectOnLeft = start;
        resumeNav.selectOnRight = next;
        resume.navigation = resumeNav;
        startNav.selectOnRight = resume;
        start.navigation = startNav;
        if (next == null) return;
        var nextNav = next.navigation;
        if (nextNav.selectOnLeft != null && nextNav.selectOnLeft.Pointer == start.Pointer) nextNav.selectOnLeft = resume;
        next.navigation = nextNav;
    }

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.CancelCountdown))]
    internal static class Cancelled
    {
        private static void Postfix() => resumeRequested = false;
    }

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.StartGame_Internal))]
    internal static class ResumeInsteadOfStart
    {
        private static bool Prefix(GameCode.UIPlayerSelectOnline __instance)
        {
            if (!resumeRequested)
            {
                ResumedRunRejoinPatch.ResumedSession = false; // a normal start sends its own GameId
                return true;
            }
            resumeRequested = false;
            try
            {
                return !TryResume(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Online resume failed, starting a normal run instead: {e}");
                return true;
            }
        }
    }

    private static void CreateButton(GameCode.UIPlayerSelectOnline lobby)
    {
        var start = lobby.Refs.HostStartGameButton;
        if (resumeButton == null || resumeButton.WasCollected)
        {
            var copy = UnityEngine.Object.Instantiate(start.gameObject, start.transform.parent);
            copy.name = "ResumeRunButton";
            copy.transform.SetSiblingIndex(start.transform.GetSiblingIndex() + 1);
            resumeButton = copy.GetComponent<UITinyButton>();
            resumeButton.onClick.RemoveAllListeners();
            resumeButton.SetCallback(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => OnResumeClicked(lobby))));
            // SetContent runs Initialize on every button in Refs.AllButtons, which plays the default
            // animation and hooks select/deselect to the highlight; the copy needs the same.
            var anim = copy.GetComponent<UIOptionsButtonRefs>() ?? copy.GetComponentInChildren<UIOptionsButtonRefs>();
            if (anim != null) anim.Initialize();
            else Plugin.Logger.LogWarning("Online resume: no UIOptionsButtonRefs on the copied button; highlight may stick");
            lobby.SetupNavigation();
        }
        var saved = SavedRun();
        resumeButton.SetText(saved == null ? "Resume" : Label(saved));
        Plugin.Logger.LogInfo($"Online resume: button ready (saved run: {(saved == null ? "none" : saved.AreaName.ToString())})");
    }

    private static void OnResumeClicked(GameCode.UIPlayerSelectOnline lobby)
    {
        Plugin.Logger.LogInfo("Online resume: Resume clicked");
        resumeRequested = true;
        lobby.HostStartGameClicked();
    }

    private static bool TryResume(GameCode.UIPlayerSelectOnline lobby)
    {
        if (!IsHost)
        {
            Plugin.Logger.LogWarning("Online resume: only the host can resume (not hosting a lobby)");
            return false;
        }
        var saved = SavedRun();
        if (saved == null)
        {
            Plugin.Logger.LogWarning("Online resume: no saved run in this save slot (or already resumed this session)");
            return false;
        }
        if (!RosterMatches(saved))
        {
            Plugin.Logger.LogWarning($"Online resume: the save has {saved.AllPlayerData.Count} players but the lobby has {LobbyPlayerCount()}; not resuming");
            return false;
        }
        var slot = GameCode.Game.CurrentSaveSlot;
        var which = slot.MultiplayerSavedState != null && saved.Pointer == slot.MultiplayerSavedState.Pointer ? "MultiplayerSavedState" : "SavedState";
        Plugin.Logger.LogInfo($"Online resume: starting from {which} ({saved.AreaName})");
        lastDecoded = saved.Pointer;
        saved.DecodeData(lobby.World.GameMods);
        var players = new List<GameCode.ResumeSavedStateLocalMPPlayerData>();
        players.Add(new GameCode.ResumeSavedStateLocalMPPlayerData { PlayerIndex = 0, RWPlayerId = 0 });
        // ResumeSavedStateCR disposes every restored player with no entry here (a local co-op player
        // who didn't come back), so lobby peers need one too. RWPlayerId -1 = not a local controller.
        foreach (var index in OnlineRejoinPushPatch.LobbyPeerIndexes())
            players.Add(new GameCode.ResumeSavedStateLocalMPPlayerData { PlayerIndex = index, RWPlayerId = -1 });
        var cr = GameCode.Game.ResumeSavedStateCR(saved, players);
        OnlineRejoinPushPatch.HoldLobbyPeers(cr);
        GameCode.Run(cr);
        ResumedRunRejoinPatch.ResumedSession = true;
        return true;
    }

    // Saved players carry no identity, so builds are matched to lobby slots by index. Only offer a
    // resume when the lobby has exactly as many players as the save, so none are dropped or swapped.
    private static int LobbyPlayerCount() => 1 + OnlineRejoinPushPatch.LobbyPeerIndexes().Count;

    private static bool RosterMatches(GameCode.SinglePlayerSavedState saved) =>
        saved.AllPlayerData != null && saved.AllPlayerData.Count == LobbyPlayerCount();

    private static string Label(GameCode.SinglePlayerSavedState saved) =>
        $"Resume: {Pretty(saved.AreaName.ToString())} ({saved.AllPlayerData?.Count ?? 0}P)";

    private static string Pretty(string area) => Regex.Replace(area.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
}
