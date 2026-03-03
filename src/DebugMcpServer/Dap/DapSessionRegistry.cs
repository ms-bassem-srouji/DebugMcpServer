using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Dap;

internal sealed class DapSessionRegistry : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDapSession> _sessions = new();
    private readonly ILogger<DapSessionRegistry> _logger;

    public DapSessionRegistry(ILogger<DapSessionRegistry> logger)
    {
        _logger = logger;
    }

    public string Register(IDapSession session)
    {
        var id = Guid.NewGuid().ToString("N");
        _sessions[id] = session;
        _logger.LogInformation("Registered debug session {SessionId}", id);
        return id;
    }

    /// <summary>Register a session with a specific ID. Used for testing.</summary>
    internal void Register(string sessionId, IDapSession session)
    {
        _sessions[sessionId] = session;
        _logger.LogInformation("Registered debug session {SessionId}", sessionId);
    }

    public bool TryGet(string sessionId, out IDapSession? session)
        => _sessions.TryGetValue(sessionId, out session);

    public IReadOnlyDictionary<string, IDapSession> GetAll() => _sessions;

    public bool TryRemove(string sessionId, out IDapSession? session)
    {
        var removed = _sessions.TryRemove(sessionId, out session);
        if (removed) _logger.LogInformation("Removed debug session {SessionId}", sessionId);
        return removed;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing all {Count} debug sessions", _sessions.Count);
        foreach (var (_, session) in _sessions)
            session.Dispose();
        _sessions.Clear();
    }
}
