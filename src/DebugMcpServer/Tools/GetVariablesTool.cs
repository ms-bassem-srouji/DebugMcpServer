using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class GetVariablesTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<GetVariablesTool> _logger;

    public string Name => "get_variables";
    public string Description =>
        "Get variables for a stack frame. Provide frameId from get_callstack. " +
        "Returns locals, arguments, and statics grouped by scope. " +
        "Variables with a non-zero variablesReference can be expanded by calling get_variables with that variablesReference instead of frameId.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "frameId": {
                    "type": "integer",
                    "description": "Stack frame ID from get_callstack (use this to get top-level scopes)"
                },
                "variablesReference": {
                    "type": "integer",
                    "description": "Variables reference from a previous get_variables call (to expand a nested object/array). Use either frameId OR variablesReference, not both."
                },
                "maxVariables": {
                    "type": "integer",
                    "description": "Maximum variables to return per scope (default 50)",
                    "default": 50
                }
            },
            "required": ["sessionId"]
        }
        """)!;

    public GetVariablesTool(DapSessionRegistry registry, ILogger<GetVariablesTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);
        if (session.State != SessionState.Paused)
            return CreateTextResult(id, "Cannot inspect variables while the process is running. Use pause_execution to pause first.", isError: true);

        var maxVars = Math.Clamp(arguments?["maxVariables"]?.GetValue<int>() ?? 50, 1, 200);

        // Direct variablesReference expansion (nested object/array)
        var directRef = arguments?["variablesReference"]?.GetValue<int>() ?? 0;
        if (directRef > 0)
        {
            try
            {
                var vars = await FetchVariablesAsync(session, directRef, maxVars, cancellationToken);
                var result = new JsonObject
                {
                    ["variablesReference"] = directRef,
                    ["variables"] = vars
                };
                return CreateTextResult(id, result.ToJsonString());
            }
            catch (DapSessionException ex) { return CreateTextResult(id, DapErrorHelper.Humanize("scopes", ex.Message), isError: true); }
        }

        // Frame-based: fetch scopes then variables for each scope
        var frameIdNode = arguments?["frameId"];
        if (frameIdNode == null)
            return CreateErrorResponse(id, -32602, "Either 'frameId' or 'variablesReference' is required.");
        var frameId = frameIdNode.GetValue<int>();

        try
        {
            var scopesResponse = await session.SendRequestAsync("scopes", new { frameId }, cancellationToken);
            var scopes = scopesResponse["scopes"] as JsonArray ?? new JsonArray();

            var scopeResults = new JsonArray();
            foreach (var scope in scopes)
            {
                if (scope == null) continue;
                var scopeName = scope["name"]?.GetValue<string>() ?? "unknown";
                var scopeRef = scope["variablesReference"]?.GetValue<int>() ?? 0;
                var isExpensive = scope["expensive"]?.GetValue<bool>() ?? false;

                var scopeObj = new JsonObject
                {
                    ["scope"] = scopeName,
                    ["variablesReference"] = scopeRef,
                    ["expensive"] = isExpensive
                };

                // Skip expensive scopes (e.g. globals) unless explicitly requested
                if (isExpensive)
                {
                    scopeObj["variables"] = new JsonArray();
                    scopeObj["note"] = "Scope marked expensive; call get_variables with this variablesReference to expand.";
                }
                else if (scopeRef > 0)
                {
                    scopeObj["variables"] = await FetchVariablesAsync(session, scopeRef, maxVars, cancellationToken);
                }

                scopeResults.Add(scopeObj);
            }

            var frameResult = new JsonObject
            {
                ["frameId"] = frameId,
                ["scopes"] = scopeResults
            };
            return CreateTextResult(id, frameResult.ToJsonString());
        }
        catch (DapSessionException ex) { return CreateTextResult(id, DapErrorHelper.Humanize("scopes", ex.Message), isError: true); }
    }

    private static async Task<JsonArray> FetchVariablesAsync(
        IDapSession session, int variablesReference, int maxVars, CancellationToken ct)
    {
        var response = await session.SendRequestAsync("variables", new
        {
            variablesReference,
            count = maxVars
        }, ct);

        var raw = response["variables"] as JsonArray ?? new JsonArray();
        var result = new JsonArray();

        foreach (var v in raw)
        {
            if (v == null) continue;
            var varObj = new JsonObject
            {
                ["name"] = v["name"]?.GetValue<string>() ?? "",
                ["value"] = v["value"]?.GetValue<string>() ?? "null",
                ["type"] = v["type"]?.GetValue<string>(),
                ["variablesReference"] = v["variablesReference"]?.GetValue<int>() ?? 0
            };

            // Hint when a variable can be expanded
            var childRef = v["variablesReference"]?.GetValue<int>() ?? 0;
            if (childRef > 0)
                varObj["expandable"] = true;

            result.Add(varObj);
        }

        return result;
    }
}
