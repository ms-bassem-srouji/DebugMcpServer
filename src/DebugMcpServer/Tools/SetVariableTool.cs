using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class SetVariableTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<SetVariableTool> _logger;

    public string Name => "set_variable";
    public string Description =>
        "Set a variable's value in the debuggee. Requires the variablesReference (from get_variables) of the containing scope or object, plus the variable name and new value as a string.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "variablesReference": {
                    "type": "integer",
                    "description": "The variablesReference of the scope/object that contains the variable (from get_variables)"
                },
                "name": { "type": "string", "description": "Variable name to set" },
                "value": { "type": "string", "description": "New value as a string expression (e.g. \"42\", \"\\\"hello\\\"\", \"true\")" }
            },
            "required": ["sessionId", "variablesReference", "name", "value"]
        }
        """)!;

    public SetVariableTool(DapSessionRegistry registry, ILogger<SetVariableTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!TryGetInt(arguments, "variablesReference", out var variablesReference, out var refErr))
            return CreateErrorResponse(id, -32602, refErr!);
        if (!TryGetString(arguments, "name", out var name, out var nameErr))
            return CreateErrorResponse(id, -32602, nameErr!);
        if (!TryGetString(arguments, "value", out var value, out var valueErr))
            return CreateErrorResponse(id, -32602, valueErr!);

        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        try
        {
            var response = await session.SendRequestAsync("setVariable", new
            {
                variablesReference,
                name,
                value
            }, cancellationToken);

            var newValue = response["value"]?.GetValue<string>() ?? value;
            var newType = response["type"]?.GetValue<string>();
            var newRef = response["variablesReference"]?.GetValue<int>() ?? 0;

            var result = new JsonObject
            {
                ["outcome"] = "ok",
                ["name"] = name,
                ["value"] = newValue
            };
            if (newType != null) result["type"] = newType;
            if (newRef > 0) result["variablesReference"] = newRef;

            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex) { return CreateTextResult(id, $"DAP error: {ex.Message}", isError: true); }
    }
}
