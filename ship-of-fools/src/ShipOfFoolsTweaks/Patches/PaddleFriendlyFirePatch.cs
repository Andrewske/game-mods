using System;
using HarmonyLib;
using UnityEngine;

namespace ShipOfFoolsTweaks.Patches;

// A paddle hit runs a list of CollisionEffects. Flinch is paddle-only, so it is
// always skipped. KnockBack and player recoil are shared with other sources, so
// they are only skipped on players while a paddle hit is in progress.
// DestunPlayerEffect is left alone so teammates can still be freed.
//
// The "in a paddle hit" window is tracked on OnTriggerEnter as well as
// ApplyEffects: IL2CPP can inline the small private ApplyEffects into its caller,
// and an inlined method never reaches its Harmony hook. Unity calls
// OnTriggerEnter directly, so that hook always fires.
internal static class PaddleFriendlyFirePatch
{
    [ThreadStatic] private static int paddleHitDepth;

    private static bool Enabled => Plugin.NoPaddleFriendlyFire.Value;
    private static bool InPaddleHit => paddleHitDepth > 0;

    private static bool IsPlayer(GameObject? target) =>
        target != null && target.GetComponentInParent<PlayerController>() != null;

    private static void Enter() => paddleHitDepth++;
    private static void Exit() => paddleHitDepth = Math.Max(0, paddleHitDepth - 1);

    [HarmonyPatch(typeof(PaddleAttack), nameof(PaddleAttack.OnTriggerEnter))]
    internal static class TrackPaddleTrigger
    {
        private static void Prefix() => Enter();
        private static void Finalizer() => Exit();
    }

    [HarmonyPatch(typeof(PaddleAttack), nameof(PaddleAttack.ApplyEffects))]
    internal static class TrackPaddleHit
    {
        private static void Prefix() => Enter();
        private static void Finalizer() => Exit();
    }

    [HarmonyPatch(typeof(FlinchPlayerEffect), nameof(FlinchPlayerEffect.ApplyEffect))]
    internal static class SkipFlinch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Enabled) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(KnockBackEffect), nameof(KnockBackEffect.ApplyEffect))]
    internal static class SkipPlayerKnockBack
    {
        private static bool Prefix(GameObject collidedObject, ref bool __result)
        {
            if (!Enabled || !InPaddleHit || !IsPlayer(collidedObject)) return true;
            Plugin.Logger.LogDebug($"Blocked paddle knockback on {collidedObject.name}");
            __result = false;
            return false;
        }
    }

    // Backstop in case the shove reaches the player without going through KnockBackEffect.
    [HarmonyPatch(typeof(KnockablePlayer), nameof(KnockablePlayer.TakeRecoil),
        new[] { typeof(Vector3), typeof(float), typeof(bool), typeof(bool) })]
    internal static class SkipPlayerRecoil
    {
        private static bool Prefix(KnockablePlayer __instance, ref bool __result)
        {
            if (!Enabled || !InPaddleHit) return true;
            Plugin.Logger.LogDebug($"Blocked paddle recoil on {__instance.name}");
            __result = false;
            return false;
        }
    }
}
