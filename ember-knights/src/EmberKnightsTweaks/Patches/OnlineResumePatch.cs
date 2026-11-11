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

    private static GameCode.SinglePlayerSavedState? SavedRun()
    {
        var slot = GameCode.Game?.CurrentSaveSlot;
        var saved = slot == null ? null : slot.MultiplayerSavedState ?? slot.SavedState;
        // DecodeData mutates the state, so the same object is never resumed twice.
        return saved == null || saved.Pointer == lastDecoded ? null : saved;
    }

    private static bool IsHost => GameCode.XNetwork != null && GameCode.XNetwork.IsServer;

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.SetContent))]
    internal static class AddButton
    {
        private static void Postfix(GameCode.UIPlayerSelectOnline __instance)
        {
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
            var visible = start.gameObject.activeSelf && SavedRun() != null;
            resumeButton.interactable = start.interactable;
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
            if (!resumeRequested) return true;
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
        resumeButton.SetText(saved == null ? "Resume" : $"Resume: {Pretty(saved.AreaName.ToString())}");
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
        var slot = GameCode.Game.CurrentSaveSlot;
        var which = slot.MultiplayerSavedState != null ? "MultiplayerSavedState" : "SavedState";
        Plugin.Logger.LogInfo($"Online resume: starting from {which} ({saved.AreaName})");
        lastDecoded = saved.Pointer;
        saved.DecodeData(lobby.World.GameMods);
        var players = new List<GameCode.ResumeSavedStateLocalMPPlayerData>();
        players.Add(new GameCode.ResumeSavedStateLocalMPPlayerData { PlayerIndex = 0, RWPlayerId = 0 });
        var cr = GameCode.Game.ResumeSavedStateCR(saved, players);
        OnlineRejoinPushPatch.HoldLobbyPeers(cr);
        GameCode.Run(cr);
        return true;
    }

    private static string Pretty(string area) => Regex.Replace(area.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
}
