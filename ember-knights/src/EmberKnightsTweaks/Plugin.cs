using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace EmberKnightsTweaks;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "dev.kevin.emberknights.tweaks";
    public const string Name = "Ember Knights Tweaks";
    public const string Version = "0.3.2";

    // Absolute Ambition, Pristine Band, Goliath Wargear, Call to Arms, Gilded Remains,
    // Titan's Wargear, Supreme Ambition, Spite Shield, Mimic King's Consort, Midas Mail
    private const string DefaultFavorites = "179,177,219,131,92,152,130,133,146,220";

    internal static ManualLogSource Logger { get; private set; } = null!;
    internal static ConfigEntry<bool> FavoritesEnabled { get; private set; } = null!;
    internal static ConfigEntry<string> FavoriteRelics { get; private set; } = null!;
    internal static ConfigEntry<bool> OnlineSaveEnabled { get; private set; } = null!;
    internal static ConfigEntry<bool> OnlineResumeEnabled { get; private set; } = null!;
    internal static ConfigEntry<bool> AllowSoloOnline { get; private set; } = null!;

    public override void Load()
    {
        Logger = Log;
        FavoritesEnabled = Config.Bind("NexusShop", "Enabled", true,
            "Stock the pre-run Nexus shop from FavoriteRelics before falling back to the game's random picks.");
        FavoriteRelics = Config.Bind("NexusShop", "FavoriteRelics", DefaultFavorites,
            "Relic ids in priority order, comma separated. The first stock takes the first ones; each reroll takes the next.");
        OnlineSaveEnabled = Config.Bind("OnlineRuns", "SaveEnabled", true,
            "EXPERIMENTAL. Host can Save And Quit an online run (pause menu > Main Menu).");
        OnlineResumeEnabled = Config.Bind("OnlineRuns", "ResumeEnabled", true,
            "EXPERIMENTAL. Adds a Resume button next to Start in the online lobby (host only).");
        AllowSoloOnline = Config.Bind("OnlineRuns", "AllowSoloOnline", true,
            "Host can start an online run with nobody else in the lobby (for testing).");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"{Name} {Version} loaded (favorites={FavoritesEnabled.Value}: {FavoriteRelics.Value})");
    }
}
