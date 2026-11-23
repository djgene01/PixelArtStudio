using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using PixelArtStudio.Mcp.Tools;

namespace PixelArtStudio.Mcp;

/// <summary>Hosts the MCP server (Streamable HTTP) on the loopback interface.</summary>
public static class McpHost
{
    public const int DefaultPort = 47821;
    public const string Path = "/mcp";

    public const string Instructions = """
        You are controlling Pixel Art Studio, a pixel art and sprite animation editor the user has open.
        Your edits appear live in their window and each tool call is one step on their undo stack.
        - Coordinates are 0-based pixels with the origin at the top-left.
        - Frame and layer indices are 0-based. Layer 0 is the bottom layer.
        - Colours are #RRGGBB, #RRGGBBAA or "transparent".
        - frame and layer arguments default to the active frame and layer.
        Recommended loop: get_state, then draw (draw or paint_rows), then check the result with view_frame
        or view_animation, then save (.pxs project) and/or export (PNG, GIF, sprite sheet...).
        For animations, build one frame, then add_frame with duplicate_of and adjust it (shift_cel, draw).
        Use set_active to show the user the frame you are working on.
        Canvases go up to 4096x4096. On big canvases, draw in parts and check detail with view_frame's x, y, width
        and height region; the whole frame is shrunk to fit 2048 px.
        Colour cycling (palette-swap animation): paint with exact palette colours from a contiguous range of palette
        slots (get_state lists them), then add_color_cycle over that range. Check it with view_frame cycle_time_ms
        and export with bake_cycles.
        """;

    /// <summary>Starts the server. Pass port 0 to pick a free port; the bound address is in <c>app.Urls</c>.</summary>
    public static async Task<WebApplication> StartAsync(IStudioSession session, int port, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));
        builder.Services.AddSingleton(session);
        builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation { Name = "pixel-art-studio", Title = "Pixel Art Studio", Version = "1.0.0" };
                o.ServerInstructions = Instructions;
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<DocumentTools>()
            .WithTools<DrawingTools>()
            .WithTools<ViewTools>()
            .WithTools<FrameTools>()
            .WithTools<LayerTools>()
            .WithTools<CanvasTools>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            // Browsers always send Origin; refusing foreign origins blocks web pages and DNS rebinding.
            string origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !IsLocalOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context);
        });
        app.MapMcp(Path);

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
        return app;
    }

    private static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host is "localhost" or "127.0.0.1" or "[::1]";
}
