using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class GetExceptionInfoTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<GetExceptionInfoTool> _logger;

    public string Name => "get_exception_info";

    public string Description =>
        "Get details about the current exception when stopped on an exception breakpoint. " +
        "Returns the exception type, message, and full stack trace.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public GetExceptionInfoTool(DapSessionRegistry registry, ILogger<GetExceptionInfoTool> logger)
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
            return CreateTextResult(id, "Cannot get exception info while the process is running. The process must be stopped on an exception.", isError: true);

        try
        {
            var response = await session.SendRequestAsync("exceptionInfo", new
            {
                threadId = session.ActiveThreadId ?? 1
            }, cancellationToken);

            var result = new JsonObject
            {
                ["exceptionId"] = response["exceptionId"]?.GetValue<string>(),
                ["description"] = response["description"]?.GetValue<string>(),
                ["breakMode"] = response["breakMode"]?.GetValue<string>()
            };

            // Include detailed exception info if available
            var details = response["details"];
            if (details != null)
            {
                var detailsObj = new JsonObject
                {
                    ["message"] = details["message"]?.GetValue<string>(),
                    ["typeName"] = details["typeName"]?.GetValue<string>(),
                    ["stackTrace"] = details["stackTrace"]?.GetValue<string>(),
                    ["source"] = details["source"]?.GetValue<string>()
                };

                var innerException = details["innerException"];
                if (innerException != null)
                {
                    detailsObj["innerException"] = new JsonObject
                    {
                        ["message"] = innerException["message"]?.GetValue<string>(),
                        ["typeName"] = innerException["typeName"]?.GetValue<string>(),
                        ["stackTrace"] = innerException["stackTrace"]?.GetValue<string>()
                    };
                }

                result["details"] = detailsObj;
            }

            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, DapErrorHelper.Humanize("exceptionInfo", ex.Message), isError: true);
        }
    }
}
