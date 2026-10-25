using System.Text.Json;
using Sacc.Core.Model;

namespace Sacc.Core;

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T LoadOrDefault<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
    }

    /// <summary>Write to a temp file then swap it in, so a crash never leaves a half-written profile.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static CommandCatalog LoadCatalog(string path)
    {
        var catalog = LoadOrDefault<CommandCatalog>(path);
        var dupes = catalog.Commands.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            throw new InvalidDataException($"Duplicate command ids in catalog: {string.Join(", ", dupes)}");
        return catalog;
    }
}
