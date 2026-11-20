using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using PixelArtStudio.Mcp;
using PixelArtStudio.Services;
using PixelArtStudio.ViewModels;

namespace PixelArtStudio;

public partial class App : Application
{
    private const string PortVariable = "PIXELARTSTUDIO_MCP_PORT";

    private WebApplication? _mcp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0) window.OpenFile(e.Args[0]);
        _ = StartMcpAsync(window.ViewModel);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mcp is { } mcp)
        {
            // Stop on the thread pool: in-flight tool calls wait for this (UI) thread, so stopping here would deadlock.
            Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await mcp.StopAsync(timeout.Token);
                await mcp.DisposeAsync();
            }).Wait(TimeSpan.FromSeconds(3));
        }
        base.OnExit(e);
    }

    /// <summary>Starts the MCP server so Claude can drive the editor. Failure (e.g. a second instance) is reported, never fatal.</summary>
    private async Task StartMcpAsync(MainViewModel vm)
    {
        int port = McpHost.DefaultPort;
        string? setting = Environment.GetEnvironmentVariable(PortVariable);
        if (!string.IsNullOrWhiteSpace(setting) && (!int.TryParse(setting, out port) || port is < 1 or > 65535))
        {
            vm.StatusMessage = $"MCP server not started: {PortVariable} must be a port number from 1 to 65535.";
            return;
        }

        try
        {
            var session = new DispatcherStudioSession(Dispatcher, vm);
            _mcp = await Task.Run(() => McpHost.StartAsync(session, port));
            vm.McpStatus = $"MCP: 127.0.0.1:{port}";
        }
        catch (Exception ex)
        {
            vm.McpStatus = "MCP: off";
            vm.StatusMessage = $"MCP server not started: {ex.Message}";
        }
    }

    /// <summary>Last-resort handler: report the error instead of closing and losing unsaved work.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow!, $"Something went wrong:\n\n{e.Exception.Message}\n\nSave your work under a new name to be safe.",
            "PixelArtStudio", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
