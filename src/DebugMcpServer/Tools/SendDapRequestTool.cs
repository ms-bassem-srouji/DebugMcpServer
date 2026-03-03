using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class SendDapRequestTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<SendDapRequestTool> _logger;

    public string Name => "send_dap_request";
    public string Description =>
        "Send an arbitrary Debug Adapter Protocol (DAP) request to the debug adapter and return the raw response body. " +
        "Use this for commands not covered by the other tools, such as 'evaluate' (expression evaluation), " +
        "'loadedSources', 'modules', 'exceptionInfo', 'setExpression', 'completions', etc. " +
        "See https://microsoft.github.io/debug-adapter-protocol/specification for all available commands.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "command": { "type": "string", "description": "DAP command name (e.g. \"evaluate\", \"loadedSources\", \"modules\")" },
                "arguments": {
                    "type": "object",
                    "description": "Optional DAP arguments object for the command. Omit or pass null for commands that take no arguments.",
                    "additionalProperties": true
                }
            },
            "required": ["sessionId", "command"]
        }
        """)!;

    public SendDapRequestTool(DapSessionRegistry registry, ILogger<SendDapRequestTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!TryGetString(arguments, "command", out var command, out var cmdErr))
            return CreateErrorResponse(id, -32602, cmdErr!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        var dapArgs = arguments?["arguments"];

        _logger.LogDebug("send_dap_request: {Command}", command);

        try
        {
            JsonNode response;
            if (dapArgs != null)
            {
                // Pass arguments as a raw JsonNode — SendRequestAsync serializes it via JsonSerializer
                // We need to pass the JsonNode directly; wrap it in an anonymous object won't work,
                // so we use the overload that accepts object? and rely on JsonSerializer handling JsonNode.
                response = await session.SendRequestAsync(command, dapArgs, cancellationToken);
            }
            else
            {
                response = await session.SendRequestAsync(command, null, cancellationToken);
            }

            var result = new JsonObject
            {
                ["command"] = command,
                ["response"] = response.DeepClone()
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, $"DAP error for '{command}': {ex.Message}", isError: true);
        }
    }
}
