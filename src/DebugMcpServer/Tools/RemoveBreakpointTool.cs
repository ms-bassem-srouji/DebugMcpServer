using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class RemoveBreakpointTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<RemoveBreakpointTool> _logger;

    public string Name => "remove_breakpoint";
    public string Description =>
        "Remove breakpoints. Supports several modes:\n" +
        "- Remove a specific line breakpoint: provide 'file' and 'line'\n" +
        "- Remove all breakpoints in a file: provide 'file' only (no 'line')\n" +
        "- Remove all source breakpoints: set 'all' to true\n" +
        "- Clear all function breakpoints: set 'allFunctions' to true";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "file": { "type": "string", "description": "Full path to the source file. If 'line' is omitted, removes all breakpoints in this file." },
                "line": { "type": "integer", "description": "Line number to remove breakpoint from. Requires 'file'." },
                "all": { "type": "boolean", "description": "If true, removes ALL source breakpoints across all files." },
                "allFunctions": { "type": "boolean", "description": "If true, clears all function breakpoints." }
            },
            "required": ["sessionId"]
        }
        """)!;

    public RemoveBreakpointTool(DapSessionRegistry registry, ILogger<RemoveBreakpointTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);

        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        var removeAll = arguments?["all"]?.GetValue<bool>() == true;
        var removeAllFunctions = arguments?["allFunctions"]?.GetValue<bool>() == true;
        var file = arguments?["file"]?.GetValue<string>();
        var hasLine = TryGetInt(arguments, "line", out var line, out _);

        if (removeAll)
            return await RemoveAllSourceBreakpoints(session, id, cancellationToken);

        if (removeAllFunctions)
            return await ClearFunctionBreakpoints(session, id, cancellationToken);

        if (file == null)
            return CreateErrorResponse(id, -32602,
                "Provide 'file' (and optionally 'line') to remove specific breakpoints, or 'all: true' / 'allFunctions: true' to clear all.");

        if (hasLine)
            return await RemoveByFileLine(session, id, file, line, cancellationToken);

        return await RemoveAllInFile(session, id, file, cancellationToken);
    }

    private async Task<JsonNode> RemoveByFileLine(IDapSession session, JsonNode? id, string file, int line, CancellationToken ct)
    {
        var oldList = session.Breakpoints.GetValueOrDefault(file)?.ToList() ?? new List<SourceBreakpoint>();

        if (session.Breakpoints.TryGetValue(file, out var bps))
            bps.RemoveAll(b => b.Line == line);

        try
        {
            return await SetBreakpointTool.SendBreakpointsForFile(session, id, file, ct);
        }
        catch
        {
            session.Breakpoints[file] = oldList;
            throw;
        }
    }

    private async Task<JsonNode> RemoveAllInFile(IDapSession session, JsonNode? id, string file, CancellationToken ct)
    {
        var oldList = session.Breakpoints.GetValueOrDefault(file)?.ToList() ?? new List<SourceBreakpoint>();
        session.Breakpoints[file] = new List<SourceBreakpoint>();

        try
        {
            return await SetBreakpointTool.SendBreakpointsForFile(session, id, file, ct);
        }
        catch
        {
            session.Breakpoints[file] = oldList;
            throw;
        }
    }

    private async Task<JsonNode> RemoveAllSourceBreakpoints(IDapSession session, JsonNode? id, CancellationToken ct)
    {
        var files = session.Breakpoints.Keys.ToList();
        var backup = files.ToDictionary(f => f, f => session.Breakpoints.GetValueOrDefault(f)?.ToList() ?? new List<SourceBreakpoint>());

        foreach (var file in files)
            session.Breakpoints[file] = new List<SourceBreakpoint>();

        try
        {
            foreach (var file in files)
            {
                await session.SendRequestAsync("setBreakpoints", new
                {
                    source = new { path = file, name = Path.GetFileName(file) },
                    breakpoints = Array.Empty<object>()
                }, ct);
            }

            session.Breakpoints.Clear();
            return CreateTextResult(id, $"Removed all source breakpoints across {files.Count} file(s).");
        }
        catch
        {
            foreach (var (file, bps) in backup)
                session.Breakpoints[file] = bps;
            throw;
        }
    }

    private async Task<JsonNode> ClearFunctionBreakpoints(IDapSession session, JsonNode? id, CancellationToken ct)
    {
        try
        {
            await session.SendRequestAsync("setFunctionBreakpoints", new
            {
                breakpoints = Array.Empty<object>()
            }, ct);

            return CreateTextResult(id, "Cleared all function breakpoints.");
        }
        catch (DapSessionException ex)
        {
            var humanized = DapErrorHelper.Humanize("setFunctionBreakpoints", ex.Message);
            return CreateTextResult(id, $"DAP error: {humanized}", isError: true);
        }
    }
}
