using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.DbgEng;

/// <summary>
/// Thread-safe registry of active DbgEng native dump sessions.
/// </summary>
internal sealed class NativeDumpRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, DbgEngSession> _sessions = new();
    private readonly ILogger<NativeDumpRegistry> _logger;

    public NativeDumpRegistry(ILogger<NativeDumpRegistry> logger)
    {
        _logger = logger;
    }

    public string Register(DbgEngSession session)
    {
        var id = $"native-{Guid.NewGuid().ToString("N")[..8]}";
        _sessions[id] = session;
        _logger.LogInformation("Registered native dump session {SessionId} for {DumpPath}", id, session.DumpPath);
        return id;
    }

    internal void Register(string sessionId, DbgEngSession session)
    {
        _sessions[sessionId] = session;
    }

    public bool TryGet(string sessionId, out DbgEngSession? session)
        => _sessions.TryGetValue(sessionId, out session);

    public IReadOnlyDictionary<string, DbgEngSession> GetAll() => _sessions;

    public bool TryRemove(string sessionId, out DbgEngSession? session)
    {
        var removed = _sessions.TryRemove(sessionId, out session);
        if (removed) _logger.LogInformation("Removed native dump session {SessionId}", sessionId);
        return removed;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing all {Count} native dump sessions", _sessions.Count);
        foreach (var (_, session) in _sessions)
            session.Dispose();
        _sessions.Clear();
    }
}
