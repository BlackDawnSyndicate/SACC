using System.Collections.ObjectModel;
using System.Windows.Input;
using Sacc.App.Game;
using Sacc.App.Infrastructure;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

/// <summary>Spawner → Container: build a "#SpawnInventoryFullOf" line (a container filled with stacks of items) and keep presets.</summary>
public sealed partial class MainViewModel
{
    private ICommand? _containerFromPicked, _addPickedToContainer, _addContainerItem, _removeContainerItem, _clearContainer,
                      _spawnContainer, _saveContainer, _removeContainerPreset, _spawnContainerPreset, _pasteContainer;

    public ObservableCollection<string> ContainerItems { get; } = new();
    public ObservableCollection<ContainerPreset> ContainerPresets { get; private set; } = new();

    private void InitContainers()
    {
        ContainerPresets = new(_profile.Containers);
        ContainerItems.CollectionChanged += (_, _) => Raise(nameof(ContainerPreview));
    }

    private string _containerCode = "";
    public string ContainerCode { get => _containerCode; set { if (Set(ref _containerCode, value)) Raise(nameof(ContainerPreview)); } }

    private string _containerOption = "0";
    public string ContainerOption { get => _containerOption; set { if (Set(ref _containerOption, value)) Raise(nameof(ContainerPreview)); } }

    private string _containerNewItem = "";
    public string ContainerNewItem { get => _containerNewItem; set => Set(ref _containerNewItem, value); }

    private ContainerPreset? _selectedContainerPreset;
    /// <summary>Picking a saved container loads it into the builder.</summary>
    public ContainerPreset? SelectedContainerPreset
    {
        get => _selectedContainerPreset;
        set { if (Set(ref _selectedContainerPreset, value) && value != null) LoadContainer(value); }
    }

    private ContainerPreset CurrentContainer() => new()
    {
        Container = ContainerCode.Trim(),
        Option = string.IsNullOrWhiteSpace(ContainerOption) ? "0" : ContainerOption.Trim(),
        Items = ContainerItems.ToList(),
    };

    public string ContainerPreview
    {
        get
        {
            var def = ContainerDef();
            if (def == null) return $"The command catalog has no '{ContainerFill.CatalogId}' entry.";
            var r = TemplateRenderer.Render(def, ContainerFill.Values(CurrentContainer()));
            return r.Ok ? r.Text : string.Join("  ", r.Errors);
        }
    }

    private void LoadContainer(ContainerPreset p)
    {
        ContainerCode = p.Container;
        ContainerOption = p.Option;
        ContainerItems.Clear();
        foreach (var i in p.Items) ContainerItems.Add(i);
    }

    private void AddContainerItems(string? text)
    {
        var added = 0;
        foreach (var code in ContainerFill.SplitCodes(text))
            if (!ContainerItems.Contains(code, StringComparer.OrdinalIgnoreCase)) { ContainerItems.Add(code); added++; }
        Status = added > 0 ? $"Container: {ContainerItems.Count} item(s)." : "Type or pick an item code first (it may already be in the list).";
    }

    public ICommand ContainerFromPickedCommand => _containerFromPicked ??= new RelayCommand(() =>
    {
        if (string.IsNullOrWhiteSpace(ItemCode)) { Status = "Type or pick the container in Items first, e.g. Hiking_Backpack_01_02."; return; }
        ContainerCode = ItemCode.Trim();
    });

    public ICommand AddPickedToContainerCommand => _addPickedToContainer ??= new RelayCommand(() => AddContainerItems(ItemCode));

    public ICommand AddContainerItemCommand => _addContainerItem ??= new RelayCommand(() => { AddContainerItems(ContainerNewItem); ContainerNewItem = ""; });

    public ICommand RemoveContainerItemCommand => _removeContainerItem ??= new RelayCommand(o => { if (o is string s) ContainerItems.Remove(s); });

    public ICommand ClearContainerCommand => _clearContainer ??= new RelayCommand(() => { ContainerCode = ""; ContainerOption = "0"; ContainerItems.Clear(); SelectedContainerPreset = null; });

    public ICommand SpawnContainerCommand => _spawnContainer ??= new AsyncCommand(() => SpawnContainerAsync(CurrentContainer()));

    public ICommand SpawnContainerPresetCommand => _spawnContainerPreset ??= new AsyncCommand(o => o is ContainerPreset p ? SpawnContainerAsync(p) : Task.CompletedTask);

    private Task SpawnContainerAsync(ContainerPreset p)
    {
        if (p.Items.Count == 0) { Status = "Add at least one item to fill the container with."; return Task.CompletedTask; }
        var def = ContainerDef();
        if (def == null) { Status = $"The command catalog has no '{ContainerFill.CatalogId}' entry."; return Task.CompletedTask; }
        var r = TemplateRenderer.Render(def, ContainerFill.Values(p));
        if (!r.Ok) { Status = $"{def.Name}: {string.Join("  ", r.Errors)}"; return Task.CompletedTask; }
        return RunDefAsync(def, r.Text);
    }

    private CommandDef? _bundledContainerDef;

    /// <summary>The catalog entry; a catalog copy edited before this entry existed falls back to the bundled one.</summary>
    private CommandDef? ContainerDef()
    {
        if (_catalog.Find(ContainerFill.CatalogId) is { } def) return def;
        if (_bundledContainerDef == null && AppPaths.ActiveCatalog != AppPaths.BundledCatalog)
        {
            try { _bundledContainerDef = JsonStore.LoadCatalog(AppPaths.BundledCatalog).Find(ContainerFill.CatalogId); }
            catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException or System.IO.InvalidDataException) { }
        }
        return _bundledContainerDef;
    }

    public ICommand SaveContainerCommand => _saveContainer ??= new RelayCommand(() =>
    {
        var p = CurrentContainer();
        if (p.Container.Length == 0 || p.Items.Count == 0) { Status = "A saved container needs a container code and at least one item."; return; }
        // The same container with the same items replaces the older preset.
        var same = ContainerPresets.FirstOrDefault(c => string.Equals(c.Summary, p.Summary, StringComparison.OrdinalIgnoreCase));
        if (same != null) ContainerPresets.Remove(same);
        ContainerPresets.Insert(0, p);
        Save();
        Status = $"Saved container {p.Summary}.";
    });

    public ICommand RemoveContainerPresetCommand => _removeContainerPreset ??= new RelayCommand(o =>
    {
        if (o is ContainerPreset p) { ContainerPresets.Remove(p); Save(); }
    });

    public ICommand PasteContainerCommand => _pasteContainer ??= new RelayCommand(() =>
    {
        var text = GameLink.TryGetClipboardText();
        if (ContainerFill.Parse(text) is not { } p) { Status = "The clipboard doesn't hold a #SpawnInventoryFullOf line."; return; }
        LoadContainer(p);
        Status = $"Loaded {p.Summary} from the clipboard.";
    });
}
