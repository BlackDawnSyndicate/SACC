using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Sacc.App.ViewModels;

namespace Sacc.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(
            confirm: msg => MessageBox.Show(this, msg, "SACC", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes,
            ownWindow: () => new WindowInteropHelper(this).Handle);
        DataContext = _vm;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _vm.Save();
        base.OnClosing(e);
    }

    private void FreeformBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_vm.SendFreeformCommand.CanExecute(null)) _vm.SendFreeformCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _vm.RecallHistory(+1);
                FreeformBox.CaretIndex = FreeformBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Down:
                _vm.RecallHistory(-1);
                FreeformBox.CaretIndex = FreeformBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private void ItemFavorites_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemFavorites.SelectedItem is string code) _vm.SpawnItemCommand.Execute(code);
    }

    private void VehicleFavorites_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VehicleFavorites.SelectedItem is string code) _vm.SpawnVehicleCommand.Execute(code);
    }

    private void Bookmarks_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(_vm.Save));
}
