using System;
using EmberKnightsTweaks.Patches;
using UnityEngine;

namespace EmberKnightsTweaks;

// Injected MonoBehaviour so the hotkey is read every frame on any screen.
public class ResumeKeyWatcher : MonoBehaviour
{
    public ResumeKeyWatcher(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        if (Hotkey.WasPressed(Plugin.OnlineResumeKey.Value)) OnlineResumePatch.OnHotkey();
    }
}
