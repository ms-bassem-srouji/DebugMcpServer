using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class TerminateProcessTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<TerminateProcessTool> _logger;

    public string Name => "terminate_process";

    public string Description =>
        "Terminate (kill) the debugged process and end the debug session. " +
        "Unlike detach_session which lets the process continue running, this stops the process entirely.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public TerminateProcessTool(DapSessionRegistry registry, ILogger<TerminateProcessTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryRemove(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found or already ended.", isError: true);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

            await session.SendRequestAsync("terminate", new
            {
                restart = false
            }, cts.Token);

            _logger.LogInformation("Terminated process for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending terminate for session {SessionId} — cleaning up anyway", sessionId);
        }
        finally
        {
            session.Dispose();
        }

        return CreateTextResult(id,
            $"{{\"outcome\": \"terminated\", \"sessionId\": \"{sessionId}\", \"message\": \"Process terminated and debug session ended.\"}}");
    }
}
