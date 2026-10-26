using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Sacc.App.Infrastructure;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<Kit> Kits { get; private set; } = new();
    public ListCollectionView KitsView { get; private set; } = null!;

    private Kit? _selectedKit;
    public Kit? SelectedKit
    {
        get => _selectedKit;
        set { if (Set(ref _selectedKit, value)) { Raise(nameof(SelectedKitItems)); CommandManager.InvalidateRequerySuggested(); } }
    }

    /// <summary>Fresh list each selection so the grid shows edits made by Resolve.</summary>
    public IList<KitItem>? SelectedKitItems => SelectedKit?.Items;

    private string _kitSearch = "";
    public string KitSearch
    {
        get => _kitSearch;
        set { if (Set(ref _kitSearch, value)) KitsView.Refresh(); }
    }

    public ICommand ImportSahFolderCommand { get; private set; } = null!;
    public ICommand ImportSahFilesCommand { get; private set; } = null!;
    public ICommand ResolveKitsCommand { get; private set; } = null!;
    public ICommand SpawnKitCommand { get; private set; } = null!;
    public ICommand KitToMacroCommand { get; private set; } = null!;
    public ICommand CloneKitCommand { get; private set; } = null!;
    public ICommand DeleteKitCommand { get; private set; } = null!;
    public ICommand DeleteKitGroupCommand { get; private set; } = null!;

    private void InitKits()
    {
        Kits = new(_profile.Kits);
        KitsView = new ListCollectionView(Kits) { Filter = MatchesKitSearch };
        KitsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Kit.Group)));
        KitsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(Kit.Group), System.ComponentModel.ListSortDirection.Ascending));
        KitsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(Kit.Name), System.ComponentModel.ListSortDirection.Ascending));

        ImportSahFolderCommand = new AsyncCommand(ImportSahFolderAsync);
        ImportSahFilesCommand = new AsyncCommand(ImportSahFilesAsync);
        ResolveKitsCommand = new RelayCommand(ResolveKits, () => Kits.Any(k => k.UnresolvedCount > 0));
        SpawnKitCommand = new AsyncCommand(SpawnKitAsync, () => SelectedKit != null);
        KitToMacroCommand = new RelayCommand(KitToMacro, () => SelectedKit != null);
        CloneKitCommand = new RelayCommand(() =>
        {
            var copy = SelectedKit!.Clone(Kits.Select(k => k.Name));
            Kits.Add(copy);
            SelectedKit = copy;
            Save();
            Status = $"Cloned to '{copy.Name}'. Edit its name and items; re-importing the original won't touch it.";
        }, () => SelectedKit != null);
        DeleteKitCommand = new RelayCommand(() =>
        {
            if (SelectedKit != null && _confirm($"Delete kit '{SelectedKit.Label}'?")) { Kits.Remove(SelectedKit); Save(); }
        }, () => SelectedKit != null);
        DeleteKitGroupCommand = new RelayCommand(() =>
        {
            var group = SelectedKit!.Group;
            var doomed = Kits.Where(k => k.Group == group).ToList();
            if (!_confirm($"Delete all {doomed.Count} kit(s) in '{(group.Length == 0 ? "(no folder)" : group)}'?")) return;
            foreach (var k in doomed) Kits.Remove(k);
            Save();
        }, () => SelectedKit != null);
    }

    private bool MatchesKitSearch(object o)
    {
        if (string.IsNullOrWhiteSpace(_kitSearch)) return true;
        var k = (Kit)o;
        return k.Name.Contains(_kitSearch, StringComparison.OrdinalIgnoreCase)
            || k.Group.Contains(_kitSearch, StringComparison.OrdinalIgnoreCase)
            || k.Items.Any(i => i.Code?.Contains(_kitSearch, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static Dictionary<int, string> LoadSahItemMap()
    {
        try { return SahImport.LoadItemMap(AppPaths.ActiveSahItemMap); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            FileLog.Write("WARN", $"SAH item table unreadable: {ex.Message}");
            return new();
        }
    }

    private async Task ImportSahFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder of SCUM Admin Helper exports" };
        if (dialog.ShowDialog() != true) return;
        var map = LoadSahItemMap();
        var result = await Task.Run(() => SahImport.ImportFolder(dialog.FolderName, map));
        ApplyImport(result);
    }

    private async Task ImportSahFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose SCUM Admin Helper exports",
            Filter = "SAH exports (*.json;*.txt;*.zip)|*.json;*.txt;*.zip|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;
        var map = LoadSahItemMap();
        var result = await Task.Run(() => SahImport.ImportFiles(dialog.FileNames, map));
        ApplyImport(result);
    }

    private void ApplyImport(SahImport.Result result)
    {
        // Re-importing the same file replaces its kits instead of duplicating them.
        var sources = result.Kits.Select(k => (k.Source, k.Group)).ToHashSet();
        foreach (var stale in Kits.Where(k => k.Source.Length > 0 && sources.Contains((k.Source, k.Group))).ToList())
            Kits.Remove(stale);
        foreach (var kit in result.Kits) Kits.Add(kit);

        if (result.Players.Count > 0) SahImport.MergePlayers(Players, result.Players);

        foreach (var w in result.Warnings) FileLog.Write("IMPORT", w);
        Status = "Imported " + result.Summary
            + (result.Warnings.Count > 0 ? $" {result.Warnings.Count} warning(s) in the log." : "")
            + (result.Kits.Any(k => k.UnresolvedCount > 0) && LoadSahItemMap().Count == 0
                ? " No SAH item table found, so pack items stay as ids until one is added." : "");
        Save();
    }

    public void KitItemsChanged()
    {
        // Drop rows the user added but left without a code (imported rows keep their SAH id to resolve later).
        if (SelectedKit?.Items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Code) && i.SahId == null) > 0)
            Raise(nameof(SelectedKitItems));
        KitsView.Refresh();
        Save();
    }

    private void ResolveKits()
    {
        var n = SahImport.Resolve(Kits, LoadSahItemMap());
        Raise(nameof(SelectedKitItems));
        KitsView.Refresh();
        Status = $"Resolved {n} item(s); {Kits.Sum(k => k.UnresolvedCount)} still unresolved.";
        Save();
    }

    private List<string>? KitLines(Kit kit, out int skipped)
    {
        skipped = 0;
        var def = _catalog.Find("spawn-item");
        if (def == null) { Status = "The command catalog has no 'spawn-item' entry."; return null; }

        string? error = null;
        var lines = SahImport.KitLines(kit, (code, amount) =>
        {
            var r = TemplateRenderer.Render(def, new Dictionary<string, string?>
            {
                ["item"] = code,
                ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            if (!r.Ok) error ??= $"{kit.Name}: {code}: {string.Join("  ", r.Errors)}";
            return r.Text;
        }, Settings.MaxSpawnPerCommand, out skipped);
        if (error != null) { Status = error; return null; }
        return lines;
    }

    private async Task SpawnKitAsync()
    {
        var kit = SelectedKit!;
        var lines = KitLines(kit, out var skipped);
        if (lines == null) return;
        if (lines.Count == 0) { Status = "This kit has nothing to send."; return; }
        var note = skipped > 0 ? $"\n\n{skipped} unresolved item(s) will be skipped." : "";
        if (!_confirm($"Spawn kit '{kit.Label}'? This sends {lines.Count} command(s).{note}")) return;
        await ExecuteAsync(lines);
    }

    private void KitToMacro()
    {
        var kit = SelectedKit!;
        var lines = KitLines(kit, out var skipped);
        if (lines == null) return;
        var header = $"// From kit '{kit.Label}'" + (kit.Source.Length > 0 ? $" ({kit.Source})" : "")
                     + (skipped > 0 ? $"\n// {skipped} unresolved SAH item(s) left out" : "");
        var macro = new MacroViewModel(new Macro { Name = kit.Name, Script = header + "\n" + string.Join("\n", lines) });
        Macros.Add(macro);
        SelectedMacro = macro;
        Save();
        Status = $"Created macro '{kit.Name}'. Find it on the Macros tab.";
    }
}
