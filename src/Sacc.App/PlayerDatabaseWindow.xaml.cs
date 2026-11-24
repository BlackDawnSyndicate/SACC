using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sacc.App;

/// <summary>The full player table in its own window (the old Players tab), bound to the main view model.
/// Row double-click raises <see cref="OpenPlayer"/> so the host can show the player details pop-out.</summary>
public partial class PlayerDatabaseWindow : Window
{
    public PlayerDatabaseWindow() => InitializeComponent();

    /// <summary>Set by the host; invoked when a row is double-clicked (a player is selected).</summary>
    public Action? OpenPlayer { get; set; }

    private void PlayersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on the header or empty space.
        if (ItemsControl.ContainerFromElement(PlayersGrid, (DependencyObject)e.OriginalSource) is DataGridRow)
            OpenPlayer?.Invoke();
    }

    // Right-click selects the row before its context menu opens, so "Show last location" acts on it.
    private void PlayerRow_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row) row.IsSelected = true;
    }
}
