using System.IO;

namespace Sacc.App.Infrastructure;

public static class AppPaths
{
    /// <summary>SACC_DATA_DIR redirects all state (profile, log, catalog copy), e.g. for test runs.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SACC_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SACC");

    public static string Profile => Path.Combine(Root, "profile.json");
    public static string Log => Path.Combine(Root, "sacc.log");
    /// <summary>Ground heights learned from GM spawn climbs; kept apart from the profile so it can be shared.</summary>
    public static string Ground => Path.Combine(Root, "ground.json");
    /// <summary>Tracked player positions, appended to while tracking is on.</summary>
    public static string Tracks => Path.Combine(Root, "tracks.csv");

    /// <summary>Your own landscape height grid (.scgh), written by GM spawn → Load…; overrides any bundled grid.</summary>
    public static string UserTerrain => Path.Combine(Root, "terrain.scgh");
    /// <summary>A terrain grid shipped with SACC in data\terrain\ (optional), used when you haven't loaded your own.</summary>
    public static string BundledTerrain => Path.Combine(AppContext.BaseDirectory, "data", "terrain", "terrain.scgh");
    /// <summary>The grid SACC actually uses: your own if present, else the bundled one. The returned path may not exist.</summary>
    public static string ActiveTerrain => File.Exists(UserTerrain) ? UserTerrain : BundledTerrain;

    /// <summary>Your own map overlays (map-features.json), loaded in the Map tab; overrides any bundled one.</summary>
    public static string UserMapFeatures => Path.Combine(Root, "map-features.json");
    /// <summary>Map overlays shipped with SACC in data\mapfeatures\ (optional), used when you haven't loaded your own.</summary>
    public static string BundledMapFeatures => Path.Combine(AppContext.BaseDirectory, "data", "mapfeatures", "map-features.json");
    /// <summary>The overlays SACC actually uses: your own if present, else the bundled one. The returned path may not exist.</summary>
    public static string ActiveMapFeatures => File.Exists(UserMapFeatures) ? UserMapFeatures : BundledMapFeatures;

    /// <summary>The user's editable catalog, created on demand from the bundled one.</summary>
    public static string UserCatalog => Path.Combine(Root, "commands.json");
    public static string BundledCatalog => Path.Combine(AppContext.BaseDirectory, "data", "commands.json");

    public static string ActiveCatalog => File.Exists(UserCatalog) ? UserCatalog : BundledCatalog;

    /// <summary>SAH item id → spawn code table used to import SAH packs; a copy in the data folder overrides the bundled one.</summary>
    public static string ActiveSahItemMap =>
        File.Exists(Path.Combine(Root, "sah-item-ids.json"))
            ? Path.Combine(Root, "sah-item-ids.json")
            : Path.Combine(AppContext.BaseDirectory, "data", "sah-item-ids.json");

    /// <summary>The game's #ListItems list; Spawner → Refresh from game writes a newer copy to the data folder.</summary>
    public static string UserGameItems => Path.Combine(Root, "game-items.json");
    public static string ActiveGameItems =>
        File.Exists(UserGameItems) ? UserGameItems : Path.Combine(AppContext.BaseDirectory, "data", "game-items.json");

    /// <summary>SCUM's client log, where #ListItems output ends up.</summary>
    public static string ScumClientLog =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCUM", "Saved", "Logs", "SCUM.log");
}

public static class FileLog
{
    private static readonly object Lock = new();

    public static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(AppPaths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}|{level}|{message}{Environment.NewLine}");
            }
        }
        catch (IOException) { /* logging must never take the app down */ }
    }
}
