using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpExceptionsTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpExceptionsTool> _logger;

    public string Name => "dotnet_dump_exceptions";

    public string Description =>
        "Show exceptions on all threads from a .NET dump. " +
        "Includes exception type, message, stack trace, and inner exception chain.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DotnetDumpExceptionsTool(DotnetDumpRegistry registry, ILogger<DotnetDumpExceptionsTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return Task.FromResult(CreateErrorResponse(id, -32602, err!));
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return Task.FromResult(CreateTextResult(id,
                $"Session '{sessionId}' not found. Use load_dotnet_dump to open a dump.", isError: true));

        try
        {
            var exceptions = new JsonArray();
            foreach (var thread in session.Runtime.Threads)
            {
                var ex = thread.CurrentException;
                if (ex == null) continue;

                var exObj = new JsonObject
                {
                    ["threadId"] = thread.ManagedThreadId,
                    ["osThreadId"] = $"0x{thread.OSThreadId:X}",
                    ["type"] = ex.Type?.Name ?? "Unknown",
                    ["message"] = ex.Message,
                    ["address"] = $"0x{ex.Address:X}",
                    ["hResult"] = $"0x{ex.HResult:X8}"
                };

                // Stack trace from exception object
                var stackTrace = new JsonArray();
                foreach (var frame in ex.StackTrace)
                {
                    stackTrace.Add(frame.Method != null
                        ? $"{frame.Method.Type?.Name}.{frame.Method.Name}"
                        : "[Native Frame]");
                }
                if (stackTrace.Count > 0)
                    exObj["stackTrace"] = stackTrace;

                // Inner exceptions
                var inner = ex.Inner;
                var innerChain = new JsonArray();
                while (inner != null)
                {
                    innerChain.Add(new JsonObject
                    {
                        ["type"] = inner.Type?.Name ?? "Unknown",
                        ["message"] = inner.Message
                    });
                    inner = inner.Inner;
                }
                if (innerChain.Count > 0)
                    exObj["innerExceptions"] = innerChain;

                exceptions.Add(exObj);
            }

            var result = new JsonObject
            {
                ["exceptionCount"] = exceptions.Count,
                ["exceptions"] = exceptions
            };
            if (exceptions.Count == 0)
                result["message"] = "No exceptions found on any thread.";

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpExceptions] Error");
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }
}
