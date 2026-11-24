using System.Windows;

namespace Sacc.App;

/// <summary>A pop-out with every detail of, and every action for, the selected player. Bound to the
/// main view model, so it follows the one global selection and reuses its player commands.</summary>
public partial class PlayerDetailsWindow : Window
{
    public PlayerDetailsWindow() => InitializeComponent();
}
