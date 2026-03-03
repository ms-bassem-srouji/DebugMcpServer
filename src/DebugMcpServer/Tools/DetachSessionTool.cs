using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DetachSessionTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<DetachSessionTool> _logger;

    public string Name => "detach_session";
    public string Description => "Detach the debugger from the process and end the debug session. The target process continues running normally.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": {
                    "type": "string",
                    "description": "The debug session ID returned by attach_to_process"
                }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DetachSessionTool(DapSessionRegistry registry, ILogger<DetachSessionTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);

        if (!_registry.TryRemove(sessionId, out IDapSession? session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found or already ended.", isError: true);

        try
        {
            // Send disconnect — tells vsdbg to detach (not terminate) the target process
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

            await session.SendRequestAsync("disconnect", new
            {
                restart = false,
                terminateDebuggee = false
            }, cts.Token);

            _logger.LogInformation("Detached session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending disconnect for session {SessionId} — cleaning up anyway", sessionId);
        }
        finally
        {
            session.Dispose();
        }

        return CreateTextResult(id, $"{{\"outcome\": \"detached\", \"sessionId\": \"{sessionId}\", \"message\": \"Debugger detached. Target process continues running.\"}}");
    }
}
