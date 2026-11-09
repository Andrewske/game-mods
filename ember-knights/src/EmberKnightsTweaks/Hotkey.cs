using System;
using UnityEngine;

namespace EmberKnightsTweaks;

// The game ships both Unity input backends; whichever one is active answers.
internal static class Hotkey
{
    private static bool legacyBroken;

    public static bool WasPressed(KeyCode key)
    {
        if (!legacyBroken)
        {
            try { return Input.GetKeyDown(key); }
            catch (Exception) { legacyBroken = true; }
        }
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        var control = keyboard == null ? null : keyboard.FindKeyOnCurrentKeyboardLayout(key.ToString());
        return control != null && control.wasPressedThisFrame;
    }
}
