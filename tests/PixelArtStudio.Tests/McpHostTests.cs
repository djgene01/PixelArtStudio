using System.Net;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PixelArtStudio.Mcp;

namespace PixelArtStudio.Tests;

public class McpHostTests
{
    [Fact]
    public async Task Client_ListsToolsAndDraws()
    {
        var studio = new FakeStudio(4, 4);
        await using var app = await McpHost.StartAsync(studio, port: 0);
        var endpoint = new Uri(app.Urls.First() + McpHost.Path);

        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint }));
        Assert.Contains("0-based", client.ServerInstructions);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        foreach (string expected in new[] { "get_state", "draw", "paint_rows", "get_pixels", "view_frame", "view_animation", "add_frame", "export", "undo" })
            Assert.Contains(expected, names);

        var ops = JsonSerializer.Deserialize<JsonElement>("""[{"type":"line","color":"#FF0000","x0":0,"y0":1,"x1":3,"y1":1,"brush_size":1}]""");
        var draw = await client.CallToolAsync("draw", new Dictionary<string, object?> { ["ops"] = ops, ["frame"] = 0 });
        Assert.NotEqual(true, draw.IsError);
        Assert.Equal(0xFFFF0000u, studio.Document.GetCel(0, 0).Get(2, 1));

        var pixels = await client.CallToolAsync("get_pixels", new Dictionary<string, object?> { ["y"] = 1, ["height"] = 1 });
        string text = Assert.IsType<TextContentBlock>(Assert.Single(pixels.Content)).Text;
        using var json = JsonDocument.Parse(text);
        Assert.Equal("0000", json.RootElement.GetProperty("rows")[0].GetString());
    }

    [Fact]
    public async Task ToolErrors_ReachTheClientAsErrorResults()
    {
        await using var app = await McpHost.StartAsync(new FakeStudio(), port: 0);
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(app.Urls.First() + McpHost.Path) }));

        var result = await client.CallToolAsync("delete_frame", new Dictionary<string, object?>());
        Assert.True(result.IsError);
        Assert.Contains("at least one frame", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task ForeignOrigin_IsForbidden()
    {
        await using var app = await McpHost.StartAsync(new FakeStudio(), port: 0);
        using var http = new HttpClient();

        async Task<HttpStatusCode> PostWithOrigin(string origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, app.Urls.First() + McpHost.Path)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Accept", "application/json, text/event-stream");
            using var response = await http.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Forbidden, await PostWithOrigin("http://evil.example"));
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostWithOrigin("http://localhost:3000"));
    }
}
