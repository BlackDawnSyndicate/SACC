using System.Collections.ObjectModel;
using System.Windows.Input;
using Sacc.App.Game;
using Sacc.App.Infrastructure;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

/// <summary>
/// Copy-outs in WhalleyBot's own formats, for pasting into its panel: macros as scheduled
/// tasks, kits as packs, and map spots as task lines. SACC never talks to WhalleyBot.
/// </summary>
public sealed partial class MainViewModel
{
    private ICommand? _macroToTask, _kitToPack, _spotLine, _addCoord, _copyCoords, _clearCoords;

    /// <summary>Map spots collected for a random-choice "#var_coords {a;b;c}" list.</summary>
    public ObservableCollection<Vec3> MapCoordList { get; } = new();

    public string AddCoordHeader => $"Add spot to coordinate list ({MapCoordList.Count})";
    public string CopyCoordsHeader => $"Copy coordinate list as #var_coords ({MapCoordList.Count})";

    public ICommand CopyMacroAsTaskCommand => _macroToTask ??= new RelayCommand(() =>
    {
        var e = WhalleyBotExport.MacroToTask(SelectedMacro!.Script, Settings.MySteamId, Settings.MaxSpawnPerCommand);
        GameLink.SetClipboardText(e.Text);
        Status = $"Copied '{SelectedMacro.Name}' as a WhalleyBot task." + (e.Notes.Count > 0 ? " " + string.Join(" ", e.Notes) : "");
    }, () => SelectedMacro != null);

    public ICommand CopyKitAsPackCommand => _kitToPack ??= new RelayCommand(() =>
    {
        var kit = SelectedKit!;
        var lines = KitLines(kit, out var skipped);
        if (lines == null) return;
        if (lines.Count == 0) { Status = "This kit has nothing to copy."; return; }
        GameLink.SetClipboardText(string.Join("\n", lines));
        Status = $"Copied '{kit.Label}' as {lines.Count} pack line(s) for WhalleyBot's Packs tab."
                 + (skipped > 0 ? $" {skipped} unresolved item(s) left out." : "");
    }, () => SelectedKit != null);

    public ICommand CopySpotLineCommand => _spotLine ??= new RelayCommand(o =>
    {
        if (MapPoint is not { } p || WhalleyBotExport.SpotLine(o as string ?? "", p) is not { } line) return;
        GameLink.SetClipboardText(line);
        Status = $"Copied: {line}" + (p.Z == 0 ? "  (Z is 0: right-click a marker for its real height, or edit Z)" : "");
    }, _ => MapPoint != null);

    public ICommand AddCoordCommand => _addCoord ??= new RelayCommand(() =>
    {
        MapCoordList.Add(MapPoint!.Value);
        RaiseCoordHeaders();
        Status = $"{MapCoordList.Count} spot(s) in the coordinate list.";
    }, () => MapPoint != null);

    public ICommand CopyCoordsCommand => _copyCoords ??= new RelayCommand(() =>
    {
        GameLink.SetClipboardText(WhalleyBotExport.CoordList("coords", MapCoordList));
        Status = $"Copied #var_coords with {MapCoordList.Count} spot(s). Use \"<coords>\" in the task; WhalleyBot picks one each time.";
    }, () => MapCoordList.Count > 0);

    public ICommand ClearCoordsCommand => _clearCoords ??= new RelayCommand(() =>
    {
        MapCoordList.Clear();
        RaiseCoordHeaders();
        Status = "Coordinate list cleared.";
    }, () => MapCoordList.Count > 0);

    private void RaiseCoordHeaders()
    {
        Raise(nameof(AddCoordHeader));
        Raise(nameof(CopyCoordsHeader));
    }
}
