using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.DotnetDump;

/// <summary>
/// Thread-safe registry of active dotnet-dump analyze sessions.
/// </summary>
internal sealed class DotnetDumpRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, DotnetDumpSession> _sessions = new();
    private readonly ILogger<DotnetDumpRegistry> _logger;

    public DotnetDumpRegistry(ILogger<DotnetDumpRegistry> logger)
    {
        _logger = logger;
    }

    public string Register(DotnetDumpSession session)
    {
        var id = $"dump-{Guid.NewGuid().ToString("N")[..8]}";
        _sessions[id] = session;
        _logger.LogInformation("Registered dotnet-dump session {SessionId} for {DumpPath}", id, session.DumpPath);
        return id;
    }

    /// <summary>Register with a specific ID. Used for testing.</summary>
    internal void Register(string sessionId, DotnetDumpSession session)
    {
        _sessions[sessionId] = session;
    }

    public bool TryGet(string sessionId, out DotnetDumpSession? session)
        => _sessions.TryGetValue(sessionId, out session);

    public IReadOnlyDictionary<string, DotnetDumpSession> GetAll() => _sessions;

    public bool TryRemove(string sessionId, out DotnetDumpSession? session)
    {
        var removed = _sessions.TryRemove(sessionId, out session);
        if (removed) _logger.LogInformation("Removed dotnet-dump session {SessionId}", sessionId);
        return removed;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing all {Count} dotnet-dump sessions", _sessions.Count);
        foreach (var (_, session) in _sessions)
            session.Dispose();
        _sessions.Clear();
    }
}
