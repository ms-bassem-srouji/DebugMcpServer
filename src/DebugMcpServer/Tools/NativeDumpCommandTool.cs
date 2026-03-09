using System.Text.Json.Nodes;
using DebugMcpServer.DbgEng;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class NativeDumpCommandTool : ToolBase, IMcpTool
{
    private readonly NativeDumpRegistry _registry;
    private readonly ILogger<NativeDumpCommandTool> _logger;

    public string Name => "native_dump_command";

    public string Description =>
        "Run a WinDbg command on a native dump session. Returns the command output as text. " +
        "Common commands: k (stack trace), ~*k (all thread stacks), dv (locals), r (registers), " +
        "lm (modules), u (disassemble), dd/db (memory), !analyze -v (crash analysis), " +
        "dt (display type), ~ (threads), ~Ns (switch thread).";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": {
                    "type": "string",
                    "description": "Native dump session ID (from load_native_dump)"
                },
                "command": {
                    "type": "string",
                    "description": "WinDbg command to execute (e.g., 'k', '~*k', 'dv', 'lm', '!analyze -v')"
                }
            },
            "required": ["sessionId", "command"]
        }
        """)!;

    public NativeDumpCommandTool(NativeDumpRegistry registry, ILogger<NativeDumpCommandTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(CreateTextResult(id,
                "Native dump analysis (DbgEng) is only available on Windows.",
                isError: true));
        }

        if (!TryGetString(arguments, "sessionId", out var sessionId, out var sessionErr))
            return Task.FromResult(CreateErrorResponse(id, -32602, sessionErr!));
        if (!TryGetString(arguments, "command", out var command, out var cmdErr))
            return Task.FromResult(CreateErrorResponse(id, -32602, cmdErr!));

        if (!_registry.TryGet(sessionId, out var session) || session == null)
        {
            return Task.FromResult(CreateTextResult(id,
                $"Native dump session '{sessionId}' not found. Use load_native_dump to open a dump.",
                isError: true));
        }

        if (!session.IsRunning)
        {
            _registry.TryRemove(sessionId, out _);
            return Task.FromResult(CreateTextResult(id,
                "DbgEng session has been disposed. Use load_native_dump to open a new session.",
                isError: true));
        }

        // Block commands that would terminate the session
        var trimmedCmd = command.Trim().ToLowerInvariant();
        if (trimmedCmd is "q" or "qq" or "qd" || trimmedCmd.StartsWith("q "))
        {
            return Task.FromResult(CreateTextResult(id,
                "Use detach_session to close the session instead of quit commands.",
                isError: true));
        }

        try
        {
            var output = session.ExecuteCommand(command);

            var result = new JsonObject
            {
                ["sessionId"] = sessionId,
                ["command"] = command,
                ["output"] = output
            };
            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NativeDumpCommand] Error executing '{Command}'", command);
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }
}
