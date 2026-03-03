using System.Text.Json;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class GetPendingEventsTool : ExecutionToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DebugOptions _options;
    private readonly ILogger<GetPendingEventsTool> _logger;

    public string Name => "get_pending_events";
    public string Description =>
        "Drain all queued DAP events (output, stopped, thread, etc.) from the session. " +
        "Use after continue_execution returns 'running' to poll for a breakpoint hit. " +
        "Optionally blocks up to waitForStopSeconds waiting for a stopped event.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": {
                    "type": "string",
                    "description": "Debug session ID"
                },
                "maxEvents": {
                    "type": "integer",
                    "description": "Maximum number of events to return (default 20, max 100)",
                    "default": 20
                },
                "waitForStopSeconds": {
                    "type": "integer",
                    "description": "If > 0, block up to this many seconds waiting for a stopped event. Max 25.",
                    "default": 0
                }
            },
            "required": ["sessionId"]
        }
        """)!;

    public GetPendingEventsTool(DapSessionRegistry registry, IOptions<DebugOptions> options, ILogger<GetPendingEventsTool> logger)
    {
        _registry = registry; _options = options.Value; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return SessionNotFound(id, sessionId);

        var maxEvents = arguments?["maxEvents"]?.GetValue<int>() ?? 20;
        maxEvents = Math.Clamp(maxEvents, 1, 100);

        var waitSec = arguments?["waitForStopSeconds"]?.GetValue<int>() ?? 0;
        waitSec = Math.Clamp(waitSec, 0, 25);

        // Only one event-consuming operation per session at a time
        await session.EventConsumerLock.WaitAsync(cancellationToken);
        try
        {
            return await DrainEventsAsync(session, id, sessionId, maxEvents, waitSec, cancellationToken);
        }
        finally
        {
            session.EventConsumerLock.Release();
        }
    }

    private async Task<JsonNode> DrainEventsAsync(
        IDapSession session, JsonNode? id, string sessionId,
        int maxEvents, int waitSec, CancellationToken cancellationToken)
    {
        var events = new JsonArray();

        // Non-blocking drain first
        while (events.Count < maxEvents && session.EventChannel.TryRead(out var evt))
        {
            events.Add(FormatEvent(evt, session));
        }

        // If no stopped event yet and waitForStopSeconds > 0, do a bounded wait
        var hasStop = events.Any(e => e?["type"]?.GetValue<string>() == "stopped");
        if (!hasStop && waitSec > 0 && events.Count < maxEvents)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                new CancellationTokenSource(TimeSpan.FromSeconds(waitSec)).Token);
            try
            {
                await foreach (var evt in session.EventChannel.ReadAllAsync(timeoutCts.Token))
                {
                    events.Add(FormatEvent(evt, session));
                    if (evt.EventType == "stopped" || evt.EventType == "terminated") break;
                    if (events.Count >= maxEvents) break;
                }
            }
            catch (OperationCanceledException) { /* timeout */ }
        }

        var result = new JsonObject
        {
            ["events"] = events,
            ["processState"] = session.State.ToString().ToLowerInvariant(),
            ["sessionId"] = sessionId,
            ["eventCount"] = events.Count
        };

        return CreateTextResult(id, result.ToJsonString());
    }

    private static JsonNode FormatEvent(DapEvent evt, IDapSession session)
    {
        var obj = new JsonObject { ["type"] = evt.EventType };

        switch (evt.EventType)
        {
            case "stopped":
                obj["reason"] = evt.Body?["reason"]?.GetValue<string>() ?? "unknown";
                obj["threadId"] = evt.Body?["threadId"]?.GetValue<int>() ?? session.ActiveThreadId ?? 0;
                obj["description"] = evt.Body?["description"]?.GetValue<string>();
                break;
            case "output":
                obj["category"] = evt.Body?["category"]?.GetValue<string>() ?? "console";
                obj["output"] = evt.Body?["output"]?.GetValue<string>() ?? "";
                break;
            case "thread":
                obj["threadId"] = evt.Body?["threadId"]?.GetValue<int>() ?? 0;
                obj["reason"] = evt.Body?["reason"]?.GetValue<string>() ?? "";
                break;
            case "terminated":
                obj["restart"] = evt.Body?["restart"];
                break;
        }

        return obj;
    }
}
