using System.Text.Json.Nodes;
using DebugMcpServer.Dap;

namespace DebugMcpServer.Tools;

internal sealed class ListBreakpointsTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;

    public string Name => "list_breakpoints";

    public string Description =>
        "List all breakpoints currently set in the debug session, grouped by source file.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public ListBreakpointsTool(DapSessionRegistry registry)
    {
        _registry = registry;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return Task.FromResult(CreateErrorResponse(id, -32602, err!));
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return Task.FromResult(CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true));

        var breakpoints = new JsonArray();
        foreach (var (file, bps) in session.Breakpoints)
        {
            foreach (var bp in bps)
            {
                var entry = new JsonObject
                {
                    ["file"] = file,
                    ["line"] = bp.Line
                };
                if (bp.Condition != null) entry["condition"] = bp.Condition;
                if (bp.HitCondition != null) entry["hitCondition"] = bp.HitCondition;
                breakpoints.Add(entry);
            }
        }

        var result = new JsonObject
        {
            ["breakpoints"] = breakpoints,
            ["count"] = breakpoints.Count
        };
        return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
    }
}
