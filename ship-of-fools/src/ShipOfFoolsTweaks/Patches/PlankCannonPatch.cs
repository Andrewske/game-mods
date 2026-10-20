using HarmonyLib;

namespace ShipOfFoolsTweaks.Patches;

// CannonFocusable's primary action is "Load" while holding an item, "Use" otherwise.
// Refusing the action while holding planks blocks the load and hides the prompt.
[HarmonyPatch(typeof(CannonFocusable), nameof(CannonFocusable.CanDoPrimaryAction))]
internal static class PlankCannonPatch
{
    private static void Postfix(PlayerController player, ref bool __result)
    {
        if (!__result || !Plugin.BlockPlankLoading.Value || player == null) return;

        var held = player.GetHoldableInHands();
        if (held != null && held.IsPlanks()) __result = false;
    }
}
