using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ShipOfFoolsTweaks;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "dev.kevin.shipoffools.tweaks";
    public const string Name = "Ship of Fools Tweaks";
    public const string Version = "0.1.1";

    internal static ManualLogSource Logger { get; private set; } = null!;
    internal static ConfigEntry<bool> BlockPlankLoading { get; private set; } = null!;
    internal static ConfigEntry<bool> NoPaddleFriendlyFire { get; private set; } = null!;

    public override void Load()
    {
        Logger = Log;
        BlockPlankLoading = Config.Bind("Cannons", "BlockPlankLoading", true,
            "Prevent planks (repair boards) from being loaded into cannons.");
        NoPaddleFriendlyFire = Config.Bind("Paddle", "NoFriendlyFire", true,
            "Paddle swings no longer flinch or knock back teammates. Un-stunning teammates still works.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"{Name} {Version} loaded (planks={BlockPlankLoading.Value}, paddleFF={NoPaddleFriendlyFire.Value})");
    }
}
