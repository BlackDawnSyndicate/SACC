using System.Text.Json.Serialization;

namespace Sacc.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InputMode
{
    /// <summary>Put the line on the clipboard and press Ctrl+V in the chat box.</summary>
    Paste,
    /// <summary>Type the line character by character as Unicode key events.</summary>
    Type,
}

public sealed class Settings
{
    public string ProcessName { get; set; } = "SCUM";
    public string ChatKey { get; set; } = "T";
    public InputMode InputMode { get; set; } = InputMode.Paste;
    public int FocusDelayMs { get; set; } = 200;
    public int ChatOpenDelayMs { get; set; } = 150;
    public int BeforeEnterDelayMs { get; set; } = 80;
    public int AfterSendDelayMs { get; set; } = 300;
    public int ClipboardCaptureTimeoutMs { get; set; } = 2500;
    public bool RestoreClipboard { get; set; } = true;
    public bool ReturnFocusToSacc { get; set; }
    public bool DryRun { get; set; } = true;
    public bool ConfirmDangerous { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    /// <summary>Your own SteamID64, used as the default target for self-commands.</summary>
    public string MySteamId { get; set; } = "";
    public int HistoryLimit { get; set; } = 500;
}

public sealed class PlayerRecord
{
    public string SteamId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public long? Fame { get; set; }
    public double? Account { get; set; }
    public long? Gold { get; set; }
    public Vec3? Location { get; set; }
    public DateTime LastSeen { get; set; }

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Nickname) ? SteamId : $"{Nickname}  ({SteamId})";
}

public readonly record struct Vec3(double X, double Y, double Z)
{
    public override string ToString() =>
        FormattableString.Invariant($"{X:0.###} {Y:0.###} {Z:0.###}");
}

public sealed class Bookmark
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class Macro
{
    public string Name { get; set; } = "New macro";
    public string Script { get; set; } = "";
}

public sealed class HistoryEntry
{
    public DateTime Time { get; set; }
    public string Text { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class Profile
{
    public Settings Settings { get; set; } = new();
    public List<PlayerRecord> Players { get; set; } = new();
    public List<Bookmark> Bookmarks { get; set; } = new();
    public List<Macro> Macros { get; set; } = new();
    public List<string> FavoriteItems { get; set; } = new();
    public List<string> FavoriteVehicles { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();
}
