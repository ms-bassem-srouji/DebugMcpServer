using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class ListThreadsTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<ListThreadsTool> _logger;

    public string Name => "list_threads";
    public string Description => "List all threads in the target process. Works best when the process is paused.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public ListThreadsTool(DapSessionRegistry registry, ILogger<ListThreadsTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        try
        {
            var response = await session.SendRequestAsync("threads", null, cancellationToken);
            var threads = response["threads"] as JsonArray;
            var result = new JsonObject
            {
                ["threads"] = threads?.DeepClone() ?? new JsonArray(),
                ["activeThreadId"] = session.ActiveThreadId
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex) { return CreateTextResult(id, $"DAP error: {ex.Message}", isError: true); }
    }
}
