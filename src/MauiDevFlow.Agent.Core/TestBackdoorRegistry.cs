using System.Collections.Concurrent;

namespace MauiDevFlow.Agent.Core;

/// <summary>
/// Thread-safe registry of named backdoor handlers for UI testing.
/// In-app code registers handlers that can be triggered remotely via the agent's
/// <c>POST /api/backdoor/{name}</c> endpoint, allowing test runners to drive
/// app-side logic (e.g. seed a database, configure state, trigger navigation)
/// without relying on the visual tree.
/// </summary>
/// <remarks>
/// <para>
/// Register handlers in your <c>MauiProgram.cs</c> or test setup class:
/// </para>
/// <code>
/// agent.Backdoor.Register("seed-data", async args =>
/// {
///     await SeedDatabaseAsync();
///     return """{"status":"ok"}""";
/// });
/// </code>
/// <para>
/// Handlers receive the raw JSON request body (or <c>null</c> when the caller
/// sends no body) and must return a JSON string (or <c>null</c> for an empty
/// 200 OK response).  Throw any exception to signal a failure — the agent
/// wraps it in a 500 error response.
/// </para>
/// </remarks>
public sealed class TestBackdoorRegistry
{
    private readonly ConcurrentDictionary<string, Func<string?, Task<string?>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a named handler.
    /// If a handler with the same name already exists it is replaced.
    /// </summary>
    /// <param name="name">Case-insensitive handler name used as the URL path segment.</param>
    /// <param name="handler">
    /// Async delegate that receives the raw JSON args body and returns a raw JSON result string.
    /// </param>
    public void Register(string name, Func<string?, Task<string?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[name] = handler;
    }

    /// <summary>Convenience overload for synchronous handlers.</summary>
    public void Register(string name, Func<string?, string?> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[name] = args => Task.FromResult(handler(args));
    }

    /// <summary>Removes a previously registered handler.  No-op if not found.</summary>
    public void Unregister(string name) => _handlers.TryRemove(name, out _);

    /// <summary>Returns the names of all currently registered handlers.</summary>
    public IReadOnlyList<string> GetNames() => _handlers.Keys.ToList();

    /// <summary>
    /// Invokes the handler with the given name.
    /// </summary>
    /// <param name="name">Handler name (case-insensitive).</param>
    /// <param name="argsJson">Raw JSON body passed by the caller, or <c>null</c>.</param>
    /// <returns>Raw JSON result string, or <c>null</c> for an empty response.</returns>
    /// <exception cref="KeyNotFoundException">When no handler with <paramref name="name"/> is registered.</exception>
    public async Task<string?> InvokeAsync(string name, string? argsJson)
    {
        if (!_handlers.TryGetValue(name, out var handler))
            throw new KeyNotFoundException($"No backdoor handler registered for '{name}'.");
        return await handler(argsJson).ConfigureAwait(false);
    }

    /// <summary>Returns <c>true</c> when a handler with the given name is registered.</summary>
    public bool Contains(string name) => _handlers.ContainsKey(name);
}
