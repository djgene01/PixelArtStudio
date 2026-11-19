using System.Windows;
using System.Windows.Threading;

namespace PixelArtStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0) window.OpenFile(e.Args[0]);
    }

    /// <summary>Last-resort handler: report the error instead of closing and losing unsaved work.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow!, $"Something went wrong:\n\n{e.Exception.Message}\n\nSave your work under a new name to be safe.",
            "PixelArtStudio", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
