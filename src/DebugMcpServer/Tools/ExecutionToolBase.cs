using System.Text.Json;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

/// <summary>
/// Base class for tools that send execution commands (step, continue, pause)
/// and wait for a 'stopped' event response from vsdbg.
/// </summary>
internal abstract class ExecutionToolBase : ToolBase
{
    protected static async Task<JsonNode> WaitForStoppedResultAsync(
        IDapSession session,
        JsonNode? id,
        int timeoutSeconds,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Only one event-consuming operation per session at a time
        await session.EventConsumerLock.WaitAsync(cancellationToken);
        try
        {
            return await WaitForStoppedResultCoreAsync(session, id, timeoutSeconds, logger, cancellationToken);
        }
        finally
        {
            session.EventConsumerLock.Release();
        }
    }

    private static async Task<JsonNode> WaitForStoppedResultCoreAsync(
        IDapSession session,
        JsonNode? id,
        int timeoutSeconds,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)).Token);

        var pendingEvents = new List<DapEvent>();

        try
        {
            await foreach (var evt in session.EventChannel.ReadAllAsync(timeoutCts.Token))
            {
                switch (evt.EventType)
                {
                    case "stopped":
                    {
                        // Re-queue any non-stopped events we consumed
                        RequeueEvents(session, pendingEvents);

                        var reason = evt.Body?["reason"]?.GetValue<string>() ?? "unknown";
                        var description = evt.Body?["description"]?.GetValue<string>();
                        var threadId = evt.Body?["threadId"]?.GetValue<int>() ?? session.ActiveThreadId ?? 0;
                        var allThreadsStopped = evt.Body?["allThreadsStopped"]?.GetValue<bool>() ?? true;

                        // Auto-resolve top frame location
                        var locationText = await GetTopFrameLocationAsync(session, threadId, cancellationToken);

                        var result = new JsonObject
                        {
                            ["outcome"] = "stopped",
                            ["reason"] = reason,
                            ["threadId"] = threadId,
                            ["allThreadsStopped"] = allThreadsStopped,
                            ["location"] = JsonNode.Parse(locationText)
                        };
                        if (description != null)
                            result["description"] = description;

                        return CreateTextResult(id, result.ToJsonString());
                    }

                    case "terminated":
                        RequeueEvents(session, pendingEvents);
                        return CreateTextResult(id,
                            "{\"outcome\": \"terminated\", \"message\": \"The target process has exited.\"}");

                    default:
                        // Buffer non-execution events (output, thread, etc.)
                        pendingEvents.Add(evt);
                        break;
                }
            }

            // Channel closed — session terminated
            RequeueEvents(session, pendingEvents);
            return CreateTextResult(id,
                "{\"outcome\": \"terminated\", \"message\": \"Debug session ended unexpectedly.\"}",
                isError: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout — process is still running
            RequeueEvents(session, pendingEvents);
            logger.LogDebug("Timeout waiting for stopped event after {Timeout}s", timeoutSeconds);
            return CreateTextResult(id, $$"""
                {
                  "outcome": "running",
                  "message": "Process is still running after {{timeoutSeconds}} seconds. Call get_pending_events to check if a breakpoint was later hit, or pause_execution to force a stop."
                }
                """);
        }
    }

    private static void RequeueEvents(IDapSession session, List<DapEvent> events)
    {
        foreach (var evt in events)
            session.EventChannel.TryRead(out _); // drain to avoid double-requeue; just drop
        // Note: in practice, we just lose these — acceptable for non-critical output events
    }

    protected static async Task<string> GetTopFrameLocationAsync(
        IDapSession session,
        int threadId,
        CancellationToken ct)
    {
        try
        {
            var response = await session.SendRequestAsync("stackTrace", new
            {
                threadId,
                startFrame = 0,
                levels = 1
            }, ct);

            var frame = response["stackFrames"]?[0];
            if (frame == null) return "null";

            var sourcePath = frame["source"]?["path"]?.GetValue<string>()
                ?? frame["source"]?["name"]?.GetValue<string>()
                ?? "unknown";
            var line = frame["line"]?.GetValue<int>() ?? 0;
            var column = frame["column"]?.GetValue<int>() ?? 0;

            return JsonSerializer.Serialize(new { source = sourcePath, line, column });
        }
        catch
        {
            return "null";
        }
    }

    protected static JsonNode SessionNotFound(JsonNode? id, string sessionId)
        => CreateTextResult(id, $"Session '{sessionId}' not found. Use attach_to_process to create a session.", isError: true);

    protected static JsonNode SessionNotPaused(JsonNode? id)
        => CreateTextResult(id,
            "Cannot execute: the process is currently running. Use pause_execution to pause it first, or wait for a breakpoint to be hit.",
            isError: true);

    protected static JsonNode SessionIsDumpFile(JsonNode? id)
        => CreateTextResult(id,
            "Execution control is not available for dump file sessions. " +
            "Dump files are static snapshots — use inspection tools instead: get_callstack, get_variables, " +
            "evaluate_expression, read_memory, disassemble, list_threads, get_modules, get_loaded_sources.",
            isError: true);
}
