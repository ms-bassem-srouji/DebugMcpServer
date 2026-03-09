using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DebugMcpServer.Dap;

namespace DebugMcpServer.Tests.Fakes;

/// <summary>
/// Fake IDapSession for unit testing. Configure responses via SetupRequest() and events via EventWriter.
/// </summary>
internal sealed class FakeSession : IDapSession
{
    private readonly Dictionary<string, Func<object?, JsonNode>> _handlers = new();
    private readonly Channel<DapEvent> _channel = Channel.CreateUnbounded<DapEvent>();
    private bool _disposed;

    public int? ActiveThreadId { get; set; }
    public SessionState State { get; set; } = SessionState.Paused;
    public bool IsDumpSession { get; set; }
    public ConcurrentDictionary<string, List<SourceBreakpoint>> Breakpoints { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SemaphoreSlim EventConsumerLock { get; } = new(1, 1);
    public ChannelReader<DapEvent> EventChannel => _channel.Reader;

    /// <summary>Write events into the channel for tests to consume.</summary>
    public ChannelWriter<DapEvent> EventWriter => _channel.Writer;

    /// <summary>All commands sent to this fake, in order.</summary>
    public List<(string Command, JsonNode? Args)> SentRequests { get; } = new();

    /// <summary>Set up a response for a given command.</summary>
    public void SetupRequest(string command, JsonNode response)
        => _handlers[command] = _ => response.DeepClone();

    /// <summary>Set up a response factory for a given command.</summary>
    public void SetupRequest(string command, Func<object?, JsonNode> factory)
        => _handlers[command] = factory;

    /// <summary>Set up an exception to throw for a given command.</summary>
    public void SetupRequestError(string command, string errorMessage)
        => _handlers[command] = _ => throw new DapSessionException(errorMessage);

    /// <summary>Enqueue an event to be read from EventChannel.</summary>
    public void EnqueueEvent(DapEvent evt) => _channel.Writer.TryWrite(evt);

    /// <summary>Complete the event channel (simulates session terminating).</summary>
    public void CompleteEventChannel() => _channel.Writer.TryComplete();

    public Task<JsonNode> SendRequestAsync(string command, object? args = null, CancellationToken cancellationToken = default)
    {
        JsonNode? argsNode = args == null ? null : System.Text.Json.JsonSerializer.SerializeToNode(args);
        SentRequests.Add((command, argsNode));

        if (_handlers.TryGetValue(command, out var handler))
        {
            var result = handler(args);
            // Deep-clone to avoid "node already has a parent" errors when production code
            // re-parents child nodes from the response into new JsonObjects
            return Task.FromResult(result.DeepClone());
        }

        return Task.FromResult<JsonNode>(new JsonObject());
    }

    public void TransitionToRunning() => State = SessionState.Running;

    public void Dispose() => _disposed = true;

    public bool IsDisposed => _disposed;
}
