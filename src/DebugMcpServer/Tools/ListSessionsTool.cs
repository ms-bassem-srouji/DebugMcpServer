using System.Text.Json.Nodes;
using DebugMcpServer.Dap;

namespace DebugMcpServer.Tools;

internal sealed class ListSessionsTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;

    public string Name => "list_sessions";

    public string Description =>
        "List all active debug sessions with their state and active thread ID.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {},
            "required": []
        }
        """)!;

    public ListSessionsTool(DapSessionRegistry registry)
    {
        _registry = registry;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        var sessions = new JsonArray();
        foreach (var (sessionId, session) in _registry.GetAll())
        {
            sessions.Add(new JsonObject
            {
                ["sessionId"] = sessionId,
                ["state"] = session.State.ToString(),
                ["activeThreadId"] = session.ActiveThreadId
            });
        }

        var result = new JsonObject
        {
            ["sessions"] = sessions,
            ["count"] = sessions.Count
        };
        return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
    }
}
