using System;
using EmberKnightsTweaks.Patches;
using UnityEngine;

namespace EmberKnightsTweaks;

// Injected MonoBehaviour so the hotkey is read every frame on any screen.
public class ResumeKeyWatcher : MonoBehaviour
{
    public ResumeKeyWatcher(IntPtr ptr) : base(ptr) { }

    private bool running;

    private void Update()
    {
        if (!running)
        {
            running = true;
            Plugin.Logger.LogInfo($"Resume key watcher running (key {Plugin.OnlineResumeKey.Value})");
        }
        if (Hotkey.WasPressed(Plugin.OnlineResumeKey.Value)) OnlineResumePatch.OnHotkey();
    }
}
