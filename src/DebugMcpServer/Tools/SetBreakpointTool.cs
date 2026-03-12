using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class SetBreakpointTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<SetBreakpointTool> _logger;

    public string Name => "set_breakpoint";
    public string Description => "Set a breakpoint at a specific source file and line number. Returns the verified breakpoint location. " +
        "If 'verified' is false, the debug adapter hasn't loaded that module yet — call continue_execution to let the runtime load it, then re-set the breakpoint.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "file": { "type": "string", "description": "Full path to the source file (e.g., C:\\repos\\App\\Program.cs)" },
                "line": { "type": "integer", "description": "Line number (1-based)" },
                "condition": { "type": "string", "description": "Optional condition expression — breakpoint only hits when this evaluates to true (e.g., 'i > 100')" },
                "hitCount": { "type": "string", "description": "Optional hit count condition — breakpoint hits when count reaches this value (e.g., '5')" }
            },
            "required": ["sessionId", "file", "line"]
        }
        """)!;

    public SetBreakpointTool(DapSessionRegistry registry, ILogger<SetBreakpointTool> logger)
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

        var condition = arguments?["condition"]?.GetValue<string>();
        var hitCount = arguments?["hitCount"]?.GetValue<string>();

        // Backup current list for rollback on failure
        var existing = session.Breakpoints.GetOrAdd(file, _ => new List<SourceBreakpoint>());
        var oldList = existing.ToList();
        // Remove old entry at same line (might have different condition) then add new one
        existing.RemoveAll(b => b.Line == line);
        existing.Add(new SourceBreakpoint(file, line, condition, hitCount));

        // Send full updated list for this source file to adapter — rollback on failure
        try
        {
            return await SendBreakpointsForFile(session, id, file, cancellationToken);
        }
        catch
        {
            session.Breakpoints[file] = oldList;
            throw;
        }
    }

    internal static async Task<JsonNode> SendBreakpointsForFile(IDapSession session, JsonNode? id, string file, CancellationToken ct)
    {
        var breakpointsForFile = session.Breakpoints.GetValueOrDefault(file) ?? new List<SourceBreakpoint>();
        var bpPayload = breakpointsForFile.Select(b =>
        {
            var bp = new Dictionary<string, object> { ["line"] = b.Line };
            if (b.Condition != null) bp["condition"] = b.Condition;
            if (b.HitCondition != null) bp["hitCondition"] = b.HitCondition;
            return bp;
        }).ToArray();

        try
        {
            var response = await session.SendRequestAsync("setBreakpoints", new
            {
                source = new { path = file, name = Path.GetFileName(file) },
                breakpoints = bpPayload
            }, ct);

            var bpArray = response["breakpoints"] as JsonArray;
            var verified = bpArray?.Select(bp => new JsonObject
            {
                ["id"] = bp?["id"]?.GetValue<int>() ?? 0,
                ["verified"] = bp?["verified"]?.GetValue<bool>() ?? false,
                ["line"] = bp?["line"]?.GetValue<int>() ?? 0,
                ["message"] = bp?["message"]?.GetValue<string>()
            }).ToArray();

            var result = new JsonObject
            {
                ["source"] = file,
                ["breakpoints"] = new JsonArray(verified?.Select(v => (JsonNode)v).ToArray() ?? [])
            };
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id?.ToJsonString() ?? "null"),
                ["result"] = new JsonObject
                {
                    ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result.ToJsonString() } },
                    ["isError"] = false
                }
            };
        }
        catch (DapSessionException ex)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id?.ToJsonString() ?? "null"),
                ["result"] = new JsonObject
                {
                    ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = $"DAP error: {ex.Message}" } },
                    ["isError"] = true
                }
            };
        }
    }
}
