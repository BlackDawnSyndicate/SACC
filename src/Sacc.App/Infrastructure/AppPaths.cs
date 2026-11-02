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
