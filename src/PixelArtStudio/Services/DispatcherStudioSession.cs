using System.Windows.Threading;
using PixelArtStudio.Mcp;

namespace PixelArtStudio.Services;

/// <summary>Runs MCP tool work on the UI thread, where the editor state lives.</summary>
public sealed class DispatcherStudioSession(Dispatcher dispatcher, IStudioEditor editor) : IStudioSession
{
    public Task<T> RunAsync<T>(Func<IStudioEditor, T> action)
    {
        // Exceptions go back to the caller (as tool errors), never to the app's unhandled-exception dialog.
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.InvokeAsync(() =>
        {
            try
            {
                result.SetResult(action(editor));
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        });
        return result.Task;
    }
}
