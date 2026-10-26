using System.Globalization;
using System.Windows.Input;
using Sacc.App.Infrastructure;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

/// <summary>Creating kits and adding/removing their items.</summary>
public sealed partial class MainViewModel
{
    private const string DefaultKitFolder = "My kits";
    private ICommand? _newKit, _addKitItem, _removeKitItem;

    public ICommand NewKitCommand => _newKit ??= new RelayCommand(NewKit);
    public ICommand AddKitItemCommand => _addKitItem ??= new RelayCommand(AddKitItem, () => SelectedKit != null && !string.IsNullOrWhiteSpace(NewItemCode));
    public ICommand RemoveKitItemCommand => _removeKitItem ??= new RelayCommand(RemoveKitItem, () => SelectedKit != null && SelectedKitItem != null);

    /// <summary>Existing folders, for the kit folder picker.</summary>
    public IReadOnlyList<string> KitFolders =>
        Kits.Select(k => k.Group).Append(DefaultKitFolder).Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    public string SelectedKitFolder
    {
        get => SelectedKit?.Group ?? "";
        set
        {
            if (SelectedKit == null || SelectedKit.Group == (value ?? "").Trim()) return;
            SelectedKit.Group = (value ?? "").Trim().Replace('\\', '/');
            KitsView.Refresh();
            Raise(nameof(KitFolders));
            Save();
        }
    }

    private KitItem? _selectedKitItem;
    public KitItem? SelectedKitItem { get => _selectedKitItem; set { if (Set(ref _selectedKitItem, value)) CommandManager.InvalidateRequerySuggested(); } }

    // ---- add-item row

    private string _newItemCode = "";
    public string NewItemCode
    {
        get => _newItemCode;
        set
        {
            if (!Set(ref _newItemCode, value)) return;
            NewItemMatches = string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : ItemCatalog.Filter(_itemCatalog.Items, value, 60);
            // Hide the suggestions once the box holds exactly one known code.
            NewItemPopupOpen = NewItemMatches.Count > 0
                && !(NewItemMatches.Count == 1 && string.Equals(NewItemMatches[0], value.Trim(), StringComparison.OrdinalIgnoreCase));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private IReadOnlyList<string> _newItemMatches = Array.Empty<string>();
    public IReadOnlyList<string> NewItemMatches { get => _newItemMatches; private set => Set(ref _newItemMatches, value); }

    private bool _newItemPopupOpen;
    public bool NewItemPopupOpen { get => _newItemPopupOpen; set => Set(ref _newItemPopupOpen, value); }

    private string _newItemAmount = "1";
    public string NewItemAmount { get => _newItemAmount; set => Set(ref _newItemAmount, value); }

    private string _newItemStack = "";
    public string NewItemStack { get => _newItemStack; set => Set(ref _newItemStack, value); }

    private bool _newItemFull;
    public bool NewItemFull { get => _newItemFull; set => Set(ref _newItemFull, value); }

    /// <summary>
    /// Kit.Items is a plain list, so re-raising the property hands the grid the same instance and
    /// it won't redraw. Refreshing the list's default view makes added/removed rows appear.
    /// </summary>
    private void RefreshKitItems()
    {
        if (SelectedKit != null) System.Windows.Data.CollectionViewSource.GetDefaultView(SelectedKit.Items)?.Refresh();
        Raise(nameof(SelectedKitItems));
        KitsView.Refresh();
    }

    /// <summary>Put a suggestion into the box (keeps amount/stack as typed).</summary>
    public void PickNewItem(string code)
    {
        _newItemCode = code;
        Raise(nameof(NewItemCode));
        NewItemPopupOpen = false;
        CommandManager.InvalidateRequerySuggested();
    }

    private void NewKit()
    {
        var kit = new Kit
        {
            Name = Kit.UniqueName("New kit", Kits.Select(k => k.Name)),
            // Create it next to the kit you're looking at, so building a set stays in one folder.
            Group = SelectedKit is { Group.Length: > 0 } current ? current.Group : DefaultKitFolder,
        };
        Kits.Add(kit);
        SelectedKit = kit;
        Raise(nameof(SelectedKitFolder));
        Raise(nameof(KitFolders));
        Save();
        Status = $"Created '{kit.Name}' in {kit.Group}. Name it, then add items below.";
    }

    private void AddKitItem()
    {
        var kit = SelectedKit!;
        var code = NewItemCode.Trim();
        if (code.Any(char.IsWhiteSpace)) { Status = "Item codes have no spaces. Pick one from the suggestions."; return; }
        if (!int.TryParse(NewItemAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 1)
        { Status = "Amount must be a whole number of 1 or more."; return; }
        int? stack = null;
        if (!NewItemFull && !string.IsNullOrWhiteSpace(NewItemStack))
        {
            if (!int.TryParse(NewItemStack, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) || s < 1)
            { Status = "StackCount must be a whole number of 1 or more (or tick Full)."; return; }
            stack = s;
        }

        // Use the catalog's spelling when the code is known; unknown codes are allowed (new game patches).
        var known = _itemCatalog.Items.FirstOrDefault(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        var row = kit.AddItem(known ?? code, amount, stack, NewItemFull);

        RefreshKitItems();
        SelectedKitItem = row;
        Save();
        Status = (known == null ? $"Added '{code}', which isn't a known code; check the spelling. " : $"Added {known}. ")
                 + $"Kit now has {kit.Items.Count} item(s).";
        PickNewItem("");
        NewItemAmount = "1";
    }

    private void RemoveKitItem()
    {
        var kit = SelectedKit!;
        var item = SelectedKitItem!;
        kit.Items.Remove(item);
        SelectedKitItem = null;
        RefreshKitItems();
        Save();
        Status = $"Removed {item.Display} from '{kit.Name}'.";
    }
}
