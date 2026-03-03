using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging.Abstractions;

namespace DebugMcpServer.Tests.Fakes;

internal static class FakeSessionRegistry
{
    /// <summary>Creates a registry pre-populated with a named session.</summary>
    public static DapSessionRegistry WithSession(string sessionId, IDapSession session)
    {
        var registry = new DapSessionRegistry(NullLogger<DapSessionRegistry>.Instance);
        registry.Register(sessionId, session);
        return registry;
    }

    /// <summary>Creates an empty registry.</summary>
    public static DapSessionRegistry Empty()
        => new DapSessionRegistry(NullLogger<DapSessionRegistry>.Instance);
}
