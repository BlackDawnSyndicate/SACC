using System.Windows;
using System.Windows.Threading;
using Sacc.App.Infrastructure;

namespace Sacc.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => FileLog.Write("FATAL", args.ExceptionObject.ToString() ?? "");
        FileLog.Write("INFO", $"SACC {typeof(App).Assembly.GetName().Version} starting; data dir {AppPaths.Root}");
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLog.Write("ERROR", e.Exception.ToString());
        MessageBox.Show($"Unexpected error: {e.Exception.Message}\n\nDetails were written to {AppPaths.Log}.", "SACC",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
