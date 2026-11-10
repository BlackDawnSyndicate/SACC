using Sacc.App.Infrastructure;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

/// <summary>One row in the Spawner's category filter: a category name and how many codes fall in it.</summary>
public sealed record CategoryOption(string Name, int Count)
{
    public string Display => $"{Name}  ({Count})";
}

public sealed class ParamFieldViewModel : ObservableObject
{
    private readonly Action _changed;
    private string _value;

    public ParamFieldViewModel(ParamDef def, Action changed)
    {
        Def = def;
        _changed = changed;
        _value = def.Default ?? "";
    }

    public ParamDef Def { get; }
    public string Label => Def.Optional ? $"{Def.Name} (optional)" : Def.Name;
    public string Hint => Def.Hint ?? Def.Kind switch
    {
        ParamKind.Player => "SteamID64 or name",
        ParamKind.Integer => "whole number",
        ParamKind.Number => "number",
        ParamKind.Message => "free text",
        _ => "",
    };

    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value)) { Raise(nameof(BoolValue)); _changed(); } }
    }

    public bool BoolValue
    {
        get => bool.TryParse(_value, out var b) && b;
        set => Value = value ? "true" : "false";
    }
}

public sealed class MacroViewModel : ObservableObject
{
    private string _name;
    private string _script;

    public MacroViewModel(Macro m) { _name = m.Name; _script = m.Script; }

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Script { get => _script; set => Set(ref _script, value); }

    public Macro ToModel() => new() { Name = Name, Script = Script };
}
