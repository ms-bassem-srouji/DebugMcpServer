using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class StepOutTool : ExecutionToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DebugOptions _options;
    private readonly ILogger<StepOutTool> _logger;

    public string Name => "step_out";
    public string Description => "Step out of the current function, returning to the caller. Process must be paused.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "waitSeconds": { "type": "integer", "description": "Seconds to wait for the step to complete (default 3, max 30).", "default": 3 }
            },
            "required": ["sessionId"]
        }
        """)!;

    public StepOutTool(DapSessionRegistry registry, IOptions<DebugOptions> options, ILogger<StepOutTool> logger)
    {
        _registry = registry; _options = options.Value; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return SessionNotFound(id, sessionId);
        if (session.State != SessionState.Paused)
            return SessionNotPaused(id);

        var waitSeconds = Math.Clamp(arguments?["waitSeconds"]?.GetValue<int>() ?? 3, 1, 30);

        try
        {
            await session.SendRequestAsync("stepOut", new { threadId = session.ActiveThreadId ?? 1 }, cancellationToken);
            session.TransitionToRunning();
            return await WaitForStoppedResultAsync(session, id, waitSeconds, _logger, cancellationToken);
        }
        catch (DapSessionException ex) { return CreateTextResult(id, DapErrorHelper.Humanize("stepOut", ex.Message), isError: true); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return CreateTextResult(id, $"Error: {ex.Message}", isError: true); }
    }
}
