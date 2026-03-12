using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MauiDevFlow.Agent.Core;
using MauiDevFlow.Driver;

namespace MauiDevFlow.Tests;

/// <summary>
/// Tests for the TestBackdoorRegistry class and the /api/backdoor HTTP endpoints.
/// </summary>
public class BackdoorTests
{
    private readonly int _port;

    public BackdoorTests()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
    }

    // ── TestBackdoorRegistry unit tests ──────────────────────────────────────

    [Fact]
    public void Register_AddsHandler()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("ping", _ => Task.FromResult<string?>("""{"pong":true}"""));

        Assert.True(registry.Contains("ping"));
        Assert.Contains("ping", registry.GetNames());
    }

    [Fact]
    public async Task InvokeAsync_CallsRegisteredHandler()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("echo", args => Task.FromResult<string?>(args));

        var result = await registry.InvokeAsync("echo", """{"msg":"hello"}""");

        Assert.Equal("""{"msg":"hello"}""", result);
    }

    [Fact]
    public async Task InvokeAsync_SyncOverload_Works()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("answer", _ => """{"value":42}""");

        var result = await registry.InvokeAsync("answer", null);

        Assert.Equal("""{"value":42}""", result);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsNull_WhenHandlerReturnsNull()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("noop", _ => Task.FromResult<string?>(null));

        var result = await registry.InvokeAsync("noop", null);

        Assert.Null(result);
    }

    [Fact]
    public async Task InvokeAsync_ThrowsKeyNotFoundException_WhenNotRegistered()
    {
        var registry = new TestBackdoorRegistry();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            registry.InvokeAsync("missing", null));
    }

    [Fact]
    public void Unregister_RemovesHandler()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("temp", _ => Task.FromResult<string?>(null));
        registry.Unregister("temp");

        Assert.False(registry.Contains("temp"));
        Assert.Empty(registry.GetNames());
    }

    [Fact]
    public void Unregister_IsNoOpWhenNotRegistered()
    {
        var registry = new TestBackdoorRegistry();
        var ex = Record.Exception(() => registry.Unregister("ghost"));
        Assert.Null(ex);
    }

    [Fact]
    public void Register_IsCaseInsensitive()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("MyHandler", _ => Task.FromResult<string?>("ok"));

        Assert.True(registry.Contains("myhandler"));
        Assert.True(registry.Contains("MYHANDLER"));
    }

    [Fact]
    public async Task InvokeAsync_IsCaseInsensitive()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("MyHandler", _ => Task.FromResult<string?>("ok"));

        var result = await registry.InvokeAsync("MYHANDLER", null);
        Assert.Equal("ok", result);
    }

    [Fact]
    public void Register_ReplacesExistingHandler()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("ping", _ => Task.FromResult<string?>("first"));
        registry.Register("ping", _ => Task.FromResult<string?>("second"));

        Assert.Single(registry.GetNames());
    }

    [Fact]
    public async Task Register_ReplacedHandler_IsInvoked()
    {
        var registry = new TestBackdoorRegistry();
        registry.Register("ping", _ => Task.FromResult<string?>("first"));
        registry.Register("ping", _ => Task.FromResult<string?>("second"));

        var result = await registry.InvokeAsync("ping", null);
        Assert.Equal("second", result);
    }

    [Fact]
    public void Register_ThrowsOnNullOrWhiteSpaceName()
    {
        var registry = new TestBackdoorRegistry();
        Assert.Throws<ArgumentException>(() =>
            registry.Register("  ", _ => Task.FromResult<string?>(null)));
    }

    [Fact]
    public void Register_ThrowsOnNullHandler()
    {
        var registry = new TestBackdoorRegistry();
        Assert.Throws<ArgumentNullException>(() =>
            registry.Register("ok", (Func<string?, Task<string?>>)null!));
    }

    // ── HTTP endpoint tests via mock HTTP server ──────────────────────────────

    [Fact]
    public async Task BackdoorList_EndpointReturnsHandlerNames()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var body = """{"handlers":["seed-data","login"]}""";
        _ = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf);
            var req = Encoding.UTF8.GetString(buf, 0, read);
            Assert.Contains("GET /api/backdoor", req);

            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = new AgentClient("localhost", _port);
        var handlers = await agentClient.ListBackdoorHandlersAsync();

        Assert.Equal(2, handlers.Count);
        Assert.Contains("seed-data", handlers);
        Assert.Contains("login", handlers);

        listener.Stop();
    }

    [Fact]
    public async Task BackdoorInvoke_EndpointPostsCorrectly()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        string? capturedBody = null;
        var responseBody = """{"status":"seeded","count":5}""";

        _ = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf);
            var req = Encoding.UTF8.GetString(buf, 0, read);

            Assert.Contains("POST /api/backdoor/seed-data", req);
            // Parse out the request body (after \r\n\r\n separator)
            var sep = req.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (sep >= 0) capturedBody = req[(sep + 4)..];

            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n{responseBody}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = new AgentClient("localhost", _port);
        var result = await agentClient.InvokeBackdoorAsync("seed-data", new { count = 5 });

        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("status", out var status));
        Assert.Equal("seeded", status.GetString());

        listener.Stop();
    }

    [Fact]
    public async Task BackdoorInvoke_NoArgs_Works()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var responseBody = """{"ok":true}""";

        _ = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buf = new byte[4096];
            await stream.ReadAsync(buf);

            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n{responseBody}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = new AgentClient("localhost", _port);
        var result = await agentClient.InvokeBackdoorAsync("clear-state");

        Assert.NotNull(result);

        listener.Stop();
    }
}
