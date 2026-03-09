using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace DebugMcpServer.Dap;

/// <summary>
/// Abstraction over a DAP debug session. Extracted to allow unit testing with fakes.
/// </summary>
internal interface IDapSession : IDisposable
{
    /// <summary>The currently active thread ID for stack/variable commands.</summary>
    int? ActiveThreadId { get; set; }

    /// <summary>Current lifecycle state of the session.</summary>
    SessionState State { get; }

    /// <summary>Breakpoints tracked per source file. Thread-safe.</summary>
    ConcurrentDictionary<string, List<SourceBreakpoint>> Breakpoints { get; }

    /// <summary>
    /// Lock for operations that consume events from the channel (continue, step, get_pending_events).
    /// Ensures only one event-consuming tool runs at a time per session.
    /// </summary>
    SemaphoreSlim EventConsumerLock { get; }

    /// <summary>Async channel of DAP events emitted by the debug adapter.</summary>
    ChannelReader<DapEvent> EventChannel { get; }

    /// <summary>Send a DAP request and return the response body.</summary>
    Task<JsonNode> SendRequestAsync(string command, object? args = null, CancellationToken cancellationToken = default);

    /// <summary>Transition session state to Running (after a continue/step command is sent).</summary>
    void TransitionToRunning();

    /// <summary>Whether this session was created from a dump file (execution control is disabled).</summary>
    bool IsDumpSession { get; set; }
}
