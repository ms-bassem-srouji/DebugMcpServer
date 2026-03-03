using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class ChangeThreadTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<ChangeThreadTool> _logger;

    public string Name => "change_thread";
    public string Description => "Set the active thread for subsequent get_callstack, get_variables, and step commands. Use list_threads to find thread IDs.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "threadId": { "type": "integer", "description": "Thread ID to make active (from list_threads)" }
            },
            "required": ["sessionId", "threadId"]
        }
        """)!;

    public ChangeThreadTool(DapSessionRegistry registry, ILogger<ChangeThreadTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return Task.FromResult(CreateErrorResponse(id, -32602, err!));
        if (!TryGetInt(arguments, "threadId", out var threadId, out var threadErr))
            return Task.FromResult(CreateErrorResponse(id, -32602, threadErr!));
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return Task.FromResult(CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true));

        session.ActiveThreadId = threadId;
        return Task.FromResult(CreateTextResult(id,
            $"{{\"outcome\": \"ok\", \"activeThreadId\": {threadId}, \"message\": \"Active thread set to {threadId}.\"}}"));
    }
}
