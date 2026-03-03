using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class ContinueExecutionTool : ExecutionToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DebugOptions _options;
    private readonly ILogger<ContinueExecutionTool> _logger;

    public string Name => "continue_execution";
    public string Description =>
        "Resume execution of the paused process. By default waits 3 seconds for a breakpoint hit. " +
        "Use waitSeconds to wait longer (e.g., 20) if you expect a breakpoint soon, or 0 to return immediately.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "waitSeconds": { "type": "integer", "description": "Seconds to wait for a stop event (default 3, max 60). Use 0 to return immediately.", "default": 3 }
            },
            "required": ["sessionId"]
        }
        """)!;

    public ContinueExecutionTool(DapSessionRegistry registry, IOptions<DebugOptions> options, ILogger<ContinueExecutionTool> logger)
    {
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return SessionNotFound(id, sessionId);

        var waitSeconds = Math.Clamp(arguments?["waitSeconds"]?.GetValue<int>() ?? 3, 0, 60);

        try
        {
            await session.SendRequestAsync("continue", new { threadId = session.ActiveThreadId ?? 1 }, cancellationToken);
            session.TransitionToRunning();
            return await WaitForStoppedResultAsync(session, id, waitSeconds, _logger, cancellationToken);
        }
        catch (DapSessionException ex) { return CreateTextResult(id, $"DAP error: {ex.Message}", isError: true); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return CreateTextResult(id, $"Error: {ex.Message}", isError: true); }
    }
}
