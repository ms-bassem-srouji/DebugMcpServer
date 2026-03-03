using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class SetDataBreakpointTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<SetDataBreakpointTool> _logger;

    public string Name => "set_data_breakpoint";

    public string Description =>
        "Set a data breakpoint (watchpoint) that breaks when a variable's memory is accessed. " +
        "Provide either a raw dataId, or variablesReference + name to look it up automatically. " +
        "The process must be paused.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "dataId": { "type": "string", "description": "Raw data ID for the data breakpoint. If omitted, variablesReference and name must be provided to look it up." },
                "variablesReference": { "type": "integer", "description": "Variables reference of the scope/container holding the variable (from get_variables). Used with 'name' to query dataBreakpointInfo." },
                "name": { "type": "string", "description": "Variable name within the scope. Used with 'variablesReference' to query dataBreakpointInfo." },
                "accessType": { "type": "string", "description": "Access type: 'read', 'write', or 'readWrite' (default 'write')", "default": "write" },
                "condition": { "type": "string", "description": "Optional condition expression — breakpoint only hits when this evaluates to true" },
                "hitCondition": { "type": "string", "description": "Optional hit count condition — breakpoint hits when count reaches this value" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public SetDataBreakpointTool(DapSessionRegistry registry, ILogger<SetDataBreakpointTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);

        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        if (session.State != SessionState.Paused)
            return CreateTextResult(id, "Cannot set data breakpoints while the process is running. Use pause_execution to pause first.", isError: true);

        var dataId = arguments?["dataId"]?.GetValue<string>();
        var variablesRefNode = arguments?["variablesReference"];
        var name = arguments?["name"]?.GetValue<string>();
        var accessType = arguments?["accessType"]?.GetValue<string>() ?? "write";
        var condition = arguments?["condition"]?.GetValue<string>();
        var hitCondition = arguments?["hitCondition"]?.GetValue<string>();

        // Resolve dataId from variablesReference + name if not provided directly
        if (string.IsNullOrWhiteSpace(dataId))
        {
            if (variablesRefNode == null || string.IsNullOrWhiteSpace(name))
                return CreateErrorResponse(id, -32602, "Either 'dataId' or both 'variablesReference' and 'name' must be provided.");

            var variablesReference = variablesRefNode.GetValue<int>();

            try
            {
                var infoResponse = await session.SendRequestAsync("dataBreakpointInfo", new
                {
                    variablesReference,
                    name
                }, cancellationToken);

                dataId = infoResponse["dataId"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(dataId))
                {
                    var description = infoResponse["description"]?.GetValue<string>() ?? "unknown variable";
                    return CreateTextResult(id, $"Data breakpoints are not supported for '{description}'.", isError: true);
                }
            }
            catch (DapSessionException ex)
            {
                return CreateTextResult(id, $"Failed to query data breakpoint info: {DapErrorHelper.Humanize("dataBreakpointInfo", ex.Message)}", isError: true);
            }
        }

        // Build the data breakpoint entry
        var dbpEntry = new Dictionary<string, object> { ["dataId"] = dataId, ["accessType"] = accessType };
        if (condition != null) dbpEntry["condition"] = condition;
        if (hitCondition != null) dbpEntry["hitCondition"] = hitCondition;

        try
        {
            var response = await session.SendRequestAsync("setDataBreakpoints", new
            {
                breakpoints = new[] { dbpEntry }
            }, cancellationToken);

            var bpArray = response["breakpoints"] as JsonArray;
            var verified = bpArray?.Select(bp => new JsonObject
            {
                ["id"] = bp?["id"]?.GetValue<int>() ?? 0,
                ["dataId"] = bp?["dataId"]?.GetValue<string>(),
                ["verified"] = bp?["verified"]?.GetValue<bool>() ?? false,
                ["message"] = bp?["message"]?.GetValue<string>()
            }).ToArray();

            var result = new JsonObject
            {
                ["dataId"] = dataId,
                ["accessType"] = accessType,
                ["breakpoints"] = new JsonArray(verified?.Select(v => (JsonNode)v).ToArray() ?? [])
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, $"DAP error: {DapErrorHelper.Humanize("setDataBreakpoints", ex.Message)}", isError: true);
        }
    }
}
