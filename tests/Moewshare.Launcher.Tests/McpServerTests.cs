using System.Text.Json;
using System.Text.Json.Nodes;
using Moewshare.Editor.Configuration;
using Moewshare.Editor.Services;
using Moewshare.Editor.ViewModels;
using Xunit;

namespace Moewshare.Launcher.Tests;

public class McpServerTests
{
    [Fact]
    public void EditorSettings_defaults_are_correct()
    {
        var settings = new EditorSettings();
        Assert.True(settings.McpEnabled);
        Assert.Equal(45124, settings.McpPort);
        Assert.True(settings.AutoStartMcp);
        Assert.Equal(60, settings.FpsLimit);
        Assert.True(settings.ShowGrid);
    }

    [Fact]
    public async Task McpServer_can_start_and_stop()
    {
        var vm = new EditorViewModel(new MockEditorUi());
        var server = new MoewshareMcpServer(vm);

        // Pick an unused port for testing
        int testPort = 45999;
        await server.StartAsync(testPort);
        Assert.True(server.IsRunning);
        Assert.Equal(testPort, server.Port);

        await server.StopAsync();
        Assert.False(server.IsRunning);

        server.Dispose();
        vm.Dispose();
    }

    [Fact]
    public async Task McpServer_returns_status_and_tools_via_http()
    {
        var vm = new EditorViewModel(new MockEditorUi());
        var server = new MoewshareMcpServer(vm);
        int testPort = 45998;

        await server.StartAsync(testPort);

        using var client = new HttpClient();

        // 1. GET /status
        var statusResp = await client.GetAsync($"http://127.0.0.1:{testPort}/status");
        Assert.True(statusResp.IsSuccessStatusCode);
        var statusJson = await statusResp.Content.ReadAsStringAsync();
        Assert.Contains("Moewshare Engine MCP Server", statusJson);

        // 2. POST /mcp (initialize)
        var initContent = new StringContent(
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { }
            }),
            System.Text.Encoding.UTF8,
            "application/json"
        );
        var initResp = await client.PostAsync($"http://127.0.0.1:{testPort}/mcp", initContent);
        Assert.True(initResp.IsSuccessStatusCode);
        var initJson = await initResp.Content.ReadAsStringAsync();
        Assert.Contains("moewshare-editor", initJson);

        // 3. POST /mcp (tools/list)
        var toolsContent = new StringContent(
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            }),
            System.Text.Encoding.UTF8,
            "application/json"
        );
        var toolsResp = await client.PostAsync($"http://127.0.0.1:{testPort}/mcp", toolsContent);
        Assert.True(toolsResp.IsSuccessStatusCode);
        var toolsJson = await toolsResp.Content.ReadAsStringAsync();
        Assert.Contains("moewshare_create_object", toolsJson);
        Assert.Contains("moewshare_get_scene", toolsJson);
        Assert.Contains("moewshare_add_event_node", toolsJson);

        await server.StopAsync();
        server.Dispose();
        vm.Dispose();
    }
}
