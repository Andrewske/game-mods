using HarmonyLib;

namespace EmberKnightsTweaks.Patches;

// Save And Quit is offline-only because CanSaveState and GetSaveState both bail out
// unless XNetwork.IsLocal. IsLocal is just "_connectionType is neither Server (1) nor
// Client (2)", so while the host runs those two methods we report None and put the
// real value back afterwards. Clients are left alone: only the host can save.
// The pause menu then stores the run in MultiplayerSavedState when AllPlayers > 1,
// otherwise in SavedState, and tells peers the host closed the game.
internal static class OnlineSavePatch
{
    private const XNetworkBase.eConnectionType Server = XNetworkBase.eConnectionType.Server;

    private static bool TryPretendOffline()
    {
        var net = GameCode.XNetwork;
        if (!Plugin.OnlineSaveEnabled.Value || net == null || net._connectionType != Server) return false;
        net._connectionType = XNetworkBase.eConnectionType.None;
        return true;
    }

    private static void Restore(bool pretended)
    {
        if (pretended) GameCode.XNetwork._connectionType = Server;
    }

    [HarmonyPatch(typeof(GameCode.GameUtils), nameof(GameCode.GameUtils.CanSaveState))]
    internal static class CanSave
    {
        private static void Prefix(out bool __state) => __state = TryPretendOffline();
        private static void Finalizer(bool __state) => Restore(__state);
    }

    [HarmonyPatch(typeof(GameCode.GameUtils), nameof(GameCode.GameUtils.GetSaveState))]
    internal static class GetSave
    {
        private static void Prefix(out bool __state) => __state = TryPretendOffline();

        private static void Finalizer(bool __state, GameCode.SinglePlayerSavedState __result)
        {
            Restore(__state);
            if (__state) Plugin.Logger.LogInfo($"Online save {(__result == null ? "FAILED (null state)" : "built")}");
        }
    }
}
