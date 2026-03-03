using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class EvaluateExpressionTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<EvaluateExpressionTool> _logger;

    public string Name => "evaluate_expression";

    public string Description =>
        "Evaluate an expression in the context of the current stack frame. " +
        "Returns the result value, its type, and a variablesReference (> 0 means expandable via get_variables). " +
        "The process must be paused.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "expression": { "type": "string", "description": "Expression to evaluate (e.g., 'myVar.Count', 'x + y', 'DateTime.Now')" },
                "frameId": { "type": "integer", "description": "Stack frame ID from get_callstack. If omitted, uses the top frame of the active thread." },
                "context": { "type": "string", "description": "Evaluation context: 'repl' (default), 'watch', 'hover', or 'clipboard'", "default": "repl" }
            },
            "required": ["sessionId", "expression"]
        }
        """)!;

    public EvaluateExpressionTool(DapSessionRegistry registry, ILogger<EvaluateExpressionTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!TryGetString(arguments, "expression", out var expression, out var exprErr))
            return CreateErrorResponse(id, -32602, exprErr!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);
        if (session.State != SessionState.Paused)
            return CreateTextResult(id, "Cannot evaluate expressions while the process is running. Use pause_execution to pause first.", isError: true);

        var context = arguments?["context"]?.GetValue<string>() ?? "repl";
        var frameIdNode = arguments?["frameId"];
        int? frameId = frameIdNode != null ? frameIdNode.GetValue<int>() : null;

        // If no frameId provided, resolve the top frame
        if (frameId == null)
        {
            try
            {
                var stackResponse = await session.SendRequestAsync("stackTrace", new
                {
                    threadId = session.ActiveThreadId ?? 1,
                    startFrame = 0,
                    levels = 1
                }, cancellationToken);
                frameId = stackResponse["stackFrames"]?[0]?["id"]?.GetValue<int>();
            }
            catch
            {
                // Fall through — try evaluate without frameId
            }
        }

        try
        {
            var evalArgs = new Dictionary<string, object> { ["expression"] = expression, ["context"] = context };
            if (frameId.HasValue) evalArgs["frameId"] = frameId.Value;

            var response = await session.SendRequestAsync("evaluate", evalArgs, cancellationToken);

            var result = new JsonObject
            {
                ["result"] = response["result"]?.GetValue<string>(),
                ["type"] = response["type"]?.GetValue<string>(),
                ["variablesReference"] = response["variablesReference"]?.GetValue<int>() ?? 0
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, $"Evaluation failed: {DapErrorHelper.Humanize("evaluate", ex.Message)}", isError: true);
        }
    }
}
