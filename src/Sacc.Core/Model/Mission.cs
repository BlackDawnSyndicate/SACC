using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Sacc.Core.Model;

/// <summary>Change notification for the mission model, which the Mission tab's editors bind to directly.</summary>
public abstract class Notifying : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }
}

public sealed class Mission : Notifying
{
    private string _name = "New mission";
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<MissionPhase> Phases { get; set; } = new();
}

public sealed class MissionPhase : Notifying
{
    private string _name = "Phase";
    private Vec3? _center;
    private double _radiusM = 150;
    private string _announce = "", _extraCleanup = "";

    public string Name { get => _name; set => Set(ref _name, value); }
    public Vec3? Center { get => _center; set { Set(ref _center, value); Raise(nameof(CenterText)); } }
    /// <summary>The phase's area: drawn on the map, and how far its cleanup reaches.</summary>
    public double RadiusM { get => _radiusM; set => Set(ref _radiusM, Math.Max(0, value)); }
    /// <summary>Sent as #Announce when the phase starts; empty = nothing.</summary>
    public string Announce { get => _announce; set => Set(ref _announce, value ?? ""); }
    /// <summary>Your own # commands added to the end of this phase's cleanup, one per line.</summary>
    public string ExtraCleanup { get => _extraCleanup; set => Set(ref _extraCleanup, value ?? ""); }
    public ObservableCollection<MissionSpawn> Spawns { get; set; } = new();

    [JsonIgnore]
    public string CenterText => Center is { } c
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Centre: {c.X:0} {c.Y:0}")
        : "No centre yet: use the Centre tool on the map.";
}

/// <summary>What became of a preplaced spawn. Values are saved in the profile, so don't renumber them.</summary>
public enum MissionSpawnState
{
    Pending = 0,
    /// <summary>Every one confirmed: the game said so, or refused none of a ground-checked climb.</summary>
    Spawned = 1,
    /// <summary>Some confirmed, the rest refused.</summary>
    Partial = 2,
    /// <summary>The game refused all of them.</summary>
    Refused = 3,
    /// <summary>Sent, and the game didn't say either way. Not counted as spawned.</summary>
    Sent = 4,
}

public sealed class MissionSpawn : Notifying
{
    private string _kind = "zombies", _type = "", _group = "", _waiting = "";
    private int _count = 1, _spawned, _refused;
    private Vec3 _at;
    private double _triggerRadiusM, _holdRadiusM;
    private MissionSpawnState _state;

    /// <summary>A GM action key (npcs, zombies, animals, …) or "item".</summary>
    public string Kind { get => _kind; set { Set(ref _kind, value ?? ""); Raise(nameof(Summary)); } }
    public string Type { get => _type; set { Set(ref _type, value ?? ""); Raise(nameof(Summary)); } }
    public int Count { get => _count; set { Set(ref _count, Math.Clamp(value, 1, 50)); Raise(nameof(Summary)); } }
    public Vec3 At { get => _at; set => Set(ref _at, value); }
    /// <summary>Over 0: wait until a player comes this close. 0: spawn as soon as the phase starts.</summary>
    public double TriggerRadiusM { get => _triggerRadiusM; set { Set(ref _triggerRadiusM, Math.Max(0, value)); Raise(nameof(RuleText)); } }
    /// <summary>Over 0: never spawn while a player is this close.</summary>
    public double HoldRadiusM { get => _holdRadiusM; set { Set(ref _holdRadiusM, Math.Max(0, value)); Raise(nameof(RuleText)); } }
    /// <summary>The prefab scene this pin came from, if any.</summary>
    public string Group { get => _group; set { Set(ref _group, value ?? ""); Raise(nameof(Summary)); } }

    public MissionSpawnState State { get => _state; set { Set(ref _state, value); Raise(nameof(StateText)); } }
    public int Spawned { get => _spawned; set { Set(ref _spawned, value); Raise(nameof(StateText)); } }
    public int Refused { get => _refused; set { Set(ref _refused, value); Raise(nameof(StateText)); } }
    /// <summary>Item codes this spawn put in the world, for its phase's cleanup.</summary>
    public List<string> ItemCodes { get; set; } = new();

    /// <summary>Why it hasn't fired yet, while the phase is running.</summary>
    [JsonIgnore] public string Waiting { get => _waiting; set { Set(ref _waiting, value ?? ""); Raise(nameof(StateText)); } }

    [JsonIgnore]
    public string Summary =>
        $"{Count} × {(Type.Length > 0 ? Type : Kind)}" + (Group.Length > 0 ? $"  ({Group})" : "");

    [JsonIgnore]
    public string RuleText => (TriggerRadiusM > 0 ? $"when a player is within {TriggerRadiusM:0} m" : "at phase start")
                              + (HoldRadiusM > 0 ? $", not while one is within {HoldRadiusM:0} m" : "");

    [JsonIgnore]
    public string StateText => State switch
    {
        MissionSpawnState.Pending => Waiting.Length > 0 ? Waiting : "Not spawned",
        MissionSpawnState.Spawned => $"Spawned {Spawned}",
        MissionSpawnState.Partial => $"Spawned {Spawned}, refused {Refused}",
        MissionSpawnState.Refused => $"Refused {Refused}",
        _ => "Sent, not confirmed",
    };

    /// <summary>A copy at another spot with no run history.</summary>
    public MissionSpawn CloneAt(Vec3 at) => new()
    {
        Kind = Kind, Type = Type, Count = Count, At = at,
        TriggerRadiusM = TriggerRadiusM, HoldRadiusM = HoldRadiusM, Group = Group,
    };
}

/// <summary>A reusable scene: placing it on the map adds one pin per member copy, offset from where you clicked.</summary>
public sealed class MissionPrefab : Notifying
{
    private string _name = "New prefab";
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<PrefabMember> Members { get; set; } = new();
}

public sealed class PrefabMember : Notifying
{
    private string _kind = "npcs", _type = "";
    private int _count = 1, _copies = 1;
    private double _offsetM;

    public string Kind { get => _kind; set => Set(ref _kind, value ?? ""); }
    public string Type { get => _type; set => Set(ref _type, value ?? ""); }
    /// <summary>How many each pin spawns.</summary>
    public int Count { get => _count; set => Set(ref _count, Math.Clamp(value, 1, 50)); }
    /// <summary>How many pins, spaced evenly in a ring.</summary>
    public int Copies { get => _copies; set => Set(ref _copies, Math.Clamp(value, 1, 50)); }
    /// <summary>The ring's radius in metres; 0 = on the spot you clicked.</summary>
    public double OffsetM { get => _offsetM; set => Set(ref _offsetM, Math.Max(0, value)); }
}
