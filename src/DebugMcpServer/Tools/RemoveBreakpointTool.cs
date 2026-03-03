using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class RemoveBreakpointTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<RemoveBreakpointTool> _logger;

    public string Name => "remove_breakpoint";
    public string Description => "Remove a breakpoint at a specific source file and line number.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "file": { "type": "string", "description": "Full path to the source file" },
                "line": { "type": "integer", "description": "Line number to remove breakpoint from" }
            },
            "required": ["sessionId", "file", "line"]
        }
        """)!;

    public RemoveBreakpointTool(DapSessionRegistry registry, ILogger<RemoveBreakpointTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err)) return CreateErrorResponse(id, -32602, err!);
        if (!TryGetString(arguments, "file", out var file, out var fileErr)) return CreateErrorResponse(id, -32602, fileErr!);
        if (!TryGetInt(arguments, "line", out var line, out var lineErr)) return CreateErrorResponse(id, -32602, lineErr!);

        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        // Backup current list for rollback on failure
        var oldList = session.Breakpoints.GetValueOrDefault(file)?.ToList() ?? new List<SourceBreakpoint>();

        if (session.Breakpoints.TryGetValue(file, out var bps))
            bps.RemoveAll(b => b.Line == line);

        // Send updated (reduced) list to adapter — rollback on failure
        try
        {
            return await SetBreakpointTool.SendBreakpointsForFile(session, id, file, cancellationToken);
        }
        catch
        {
            session.Breakpoints[file] = oldList;
            throw;
        }
    }
}
