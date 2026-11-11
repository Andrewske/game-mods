using HarmonyLib;

namespace EmberKnightsTweaks.Patches;

// The online lobby only lets the host start with 2+ filled slots: CanStartGame counts
// occupied CPlayerInfo entries and returns count > 1, and the Start button is greyed out
// through SetHostGameButtonEnabled (on open, join and leave). Both are lifted for the host
// so a solo online run can be started, mainly for testing online save/resume alone.
internal static class SoloOnlineLobbyPatch
{
    private static bool Active
    {
        get
        {
            var net = GameCode.XNetwork;
            return Plugin.AllowSoloOnline.Value && net != null && net.IsServer;
        }
    }

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.CanStartGame))]
    internal static class CanStart
    {
        private static void Postfix(ref bool __result)
        {
            if (Active) __result = true;
        }
    }

    [HarmonyPatch(typeof(GameCode.UIPlayerSelectOnline), nameof(GameCode.UIPlayerSelectOnline.SetHostGameButtonEnabled))]
    internal static class StartButton
    {
        private static void Prefix(ref bool Enabled)
        {
            if (Active) Enabled = true;
        }
    }
}
