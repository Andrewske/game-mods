using System;
using HarmonyLib;
using UnityEngine;

namespace ShipOfFoolsTweaks.Patches;

// A paddle hit runs a list of CollisionEffects inside PaddleAttack.ApplyEffects.
// Flinch is paddle-only, so it is always skipped. KnockBack is shared with
// projectiles, so it is only skipped on players while a paddle hit is in progress.
// DestunPlayerEffect is left alone so teammates can still be freed.
internal static class PaddleFriendlyFirePatch
{
    [ThreadStatic] private static bool inPaddleHit;

    private static bool Enabled => Plugin.NoPaddleFriendlyFire.Value;

    private static bool IsPlayer(GameObject target) =>
        target != null && target.GetComponentInParent<PlayerController>() != null;

    [HarmonyPatch(typeof(PaddleAttack), nameof(PaddleAttack.ApplyEffects))]
    internal static class TrackPaddleHit
    {
        private static void Prefix() => inPaddleHit = true;
        private static void Finalizer() => inPaddleHit = false;
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
            if (!Enabled || !inPaddleHit || !IsPlayer(collidedObject)) return true;
            __result = false;
            return false;
        }
    }
}
