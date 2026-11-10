using System;
using UnityEngine;

namespace EmberKnightsTweaks;

// The game ships both Unity input backends, so both are asked every time.
// Each one logs once when it first sees any key, to show which actually works under Proton.
internal static class Hotkey
{
    private static bool legacyBroken, legacySeen, inputSystemSeen;

    public static bool WasPressed(KeyCode key) => Legacy(key) | InputSystem(key);

    private static bool Legacy(KeyCode key)
    {
        if (legacyBroken) return false;
        try
        {
            if (!legacySeen && Input.anyKeyDown) LogFirst(ref legacySeen, "legacy Input");
            return Input.GetKeyDown(key);
        }
        catch (Exception e)
        {
            legacyBroken = true;
            Plugin.Logger.LogInfo($"Legacy Unity input unavailable ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    private static bool InputSystem(KeyCode key)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return false;
        if (!inputSystemSeen && keyboard.anyKey.wasPressedThisFrame) LogFirst(ref inputSystemSeen, "Input System");
        var control = keyboard.FindKeyOnCurrentKeyboardLayout(key.ToString());
        return control != null && control.wasPressedThisFrame;
    }

    private static void LogFirst(ref bool seen, string backend)
    {
        seen = true;
        Plugin.Logger.LogInfo($"Keyboard works through {backend}");
    }
}
