using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class SetExceptionBreakpointsTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<SetExceptionBreakpointsTool> _logger;

    public string Name => "set_exception_breakpoints";
    public string Description =>
        "Configure exception breakpoints — pause execution when exceptions are thrown. " +
        "Common filters: 'all' (break on all exceptions), 'unhandled' (break only on unhandled), 'thrown' (break when thrown). " +
        "Available filters depend on the debug adapter.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "filters": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Exception filter IDs to enable (e.g., ['all'], ['unhandled'], ['thrown']). Pass an empty array to clear all exception breakpoints."
                }
            },
            "required": ["sessionId", "filters"]
        }
        """)!;

    public SetExceptionBreakpointsTool(DapSessionRegistry registry, ILogger<SetExceptionBreakpointsTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);

        var filtersNode = arguments?["filters"] as JsonArray;
        if (filtersNode == null)
            return CreateErrorResponse(id, -32602, "Required parameter 'filters' is missing or not an array.");

        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        var filters = filtersNode
            .Select(f => f?.GetValue<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToArray();

        try
        {
            var response = await session.SendRequestAsync("setExceptionBreakpoints", new
            {
                filters
            }, cancellationToken);

            var breakpointsArray = response["breakpoints"] as JsonArray;
            var result = new JsonObject
            {
                ["filters"] = new JsonArray(filters.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray())
            };

            if (breakpointsArray != null)
            {
                var bps = new JsonArray();
                foreach (var bp in breakpointsArray)
                {
                    if (bp == null) continue;
                    var entry = new JsonObject
                    {
                        ["verified"] = bp["verified"]?.GetValue<bool>() ?? false
                    };
                    var bpId = bp["id"];
                    if (bpId != null) entry["id"] = bpId.GetValue<int>();
                    var message = bp["message"]?.GetValue<string>();
                    if (message != null) entry["message"] = message;
                    bps.Add(entry);
                }
                result["breakpoints"] = bps;
            }

            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, $"DAP error: {DapErrorHelper.Humanize("setExceptionBreakpoints", ex.Message)}", isError: true);
        }
    }
}
