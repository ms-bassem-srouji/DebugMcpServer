using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class SetFunctionBreakpointsTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<SetFunctionBreakpointsTool> _logger;

    public string Name => "set_function_breakpoints";
    public string Description => "Set breakpoints on function names. Replaces all previously set function breakpoints. Returns verified breakpoints from the debug adapter.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "breakpoints": {
                    "type": "array",
                    "description": "Array of function breakpoints to set (replaces all previous function breakpoints)",
                    "items": {
                        "type": "object",
                        "properties": {
                            "name": { "type": "string", "description": "Function name to break on (e.g., 'MyClass.MyMethod')" },
                            "condition": { "type": "string", "description": "Optional condition expression — breakpoint only hits when this evaluates to true" },
                            "hitCondition": { "type": "string", "description": "Optional hit count condition — breakpoint hits when count reaches this value" }
                        },
                        "required": ["name"]
                    }
                }
            },
            "required": ["sessionId", "breakpoints"]
        }
        """)!;

    public SetFunctionBreakpointsTool(DapSessionRegistry registry, ILogger<SetFunctionBreakpointsTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);

        var breakpointsNode = arguments?["breakpoints"] as JsonArray;
        if (breakpointsNode == null || breakpointsNode.Count == 0)
            return CreateErrorResponse(id, -32602, "Required parameter 'breakpoints' is missing or empty.");

        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        var functionBreakpoints = new List<Dictionary<string, object>>();
        foreach (var bpNode in breakpointsNode)
        {
            var name = bpNode?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
                return CreateErrorResponse(id, -32602, "Each breakpoint must have a 'name' property.");

            var bp = new Dictionary<string, object> { ["name"] = name };
            var condition = bpNode?["condition"]?.GetValue<string>();
            if (condition != null) bp["condition"] = condition;
            var hitCondition = bpNode?["hitCondition"]?.GetValue<string>();
            if (hitCondition != null) bp["hitCondition"] = hitCondition;

            functionBreakpoints.Add(bp);
        }

        try
        {
            var response = await session.SendRequestAsync("setFunctionBreakpoints", new
            {
                breakpoints = functionBreakpoints
            }, cancellationToken);

            var bpArray = response["breakpoints"] as JsonArray;
            var verified = bpArray?.Select(bp => new JsonObject
            {
                ["id"] = bp?["id"]?.GetValue<int>() ?? 0,
                ["verified"] = bp?["verified"]?.GetValue<bool>() ?? false,
                ["line"] = bp?["line"]?.GetValue<int>() ?? 0,
                ["message"] = bp?["message"]?.GetValue<string>()
            }).ToArray();

            var result = new JsonObject
            {
                ["breakpoints"] = new JsonArray(verified?.Select(v => (JsonNode)v).ToArray() ?? [])
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            var humanized = DapErrorHelper.Humanize("setFunctionBreakpoints", ex.Message);
            return CreateTextResult(id, $"DAP error: {humanized}", isError: true);
        }
    }
}
