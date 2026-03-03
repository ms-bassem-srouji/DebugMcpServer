using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class PauseExecutionTool : ExecutionToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DebugOptions _options;
    private readonly ILogger<PauseExecutionTool> _logger;

    public string Name => "pause_execution";
    public string Description => "Pause (break) a running process at its current position. Use when the process is running and you want to inspect it.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public PauseExecutionTool(DapSessionRegistry registry, IOptions<DebugOptions> options, ILogger<PauseExecutionTool> logger)
    {
        _registry = registry; _options = options.Value; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return SessionNotFound(id, sessionId);

        if (session.State == SessionState.Paused)
            return CreateTextResult(id, "{\"outcome\": \"already_paused\", \"message\": \"Process is already paused.\"}");

        try
        {
            await session.SendRequestAsync("pause", new { threadId = session.ActiveThreadId ?? 1 }, cancellationToken);
            return await WaitForStoppedResultAsync(session, id, _options.StepTimeoutSeconds, _logger, cancellationToken);
        }
        catch (DapSessionException ex) { return CreateTextResult(id, $"DAP error: {ex.Message}", isError: true); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return CreateTextResult(id, $"Error: {ex.Message}", isError: true); }
    }
}
