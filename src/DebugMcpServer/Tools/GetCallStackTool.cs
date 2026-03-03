using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class GetCallStackTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<GetCallStackTool> _logger;

    public string Name => "get_callstack";
    public string Description => "Get the call stack (stack frames) for the active thread. The process must be paused. Returns frame IDs needed for get_variables.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "levels": { "type": "integer", "description": "Maximum number of frames to return (default 20)", "default": 20 }
            },
            "required": ["sessionId"]
        }
        """)!;

    public GetCallStackTool(DapSessionRegistry registry, ILogger<GetCallStackTool> logger)
    {
        _registry = registry; _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        var levels = arguments?["levels"]?.GetValue<int>() ?? 20;
        levels = Math.Clamp(levels, 1, 100);

        var threadId = session.ActiveThreadId ?? 1;

        try
        {
            var response = await session.SendRequestAsync("stackTrace", new
            {
                threadId,
                startFrame = 0,
                levels
            }, cancellationToken);

            var frames = response["stackFrames"] as JsonArray ?? new JsonArray();
            var totalFrames = response["totalFrames"]?.GetValue<int>() ?? frames.Count;

            // Build a clean frames array
            var cleanFrames = new JsonArray();
            foreach (var frame in frames)
            {
                if (frame == null) continue;
                var cleanFrame = new JsonObject
                {
                    ["id"] = frame["id"]?.GetValue<int>() ?? 0,
                    ["name"] = frame["name"]?.GetValue<string>() ?? "<unknown>",
                    ["line"] = frame["line"]?.GetValue<int>() ?? 0,
                    ["column"] = frame["column"]?.GetValue<int>() ?? 0
                };

                var sourcePath = frame["source"]?["path"]?.GetValue<string>()
                    ?? frame["source"]?["name"]?.GetValue<string>();
                if (sourcePath != null)
                    cleanFrame["source"] = sourcePath;

                cleanFrames.Add(cleanFrame);
            }

            var result = new JsonObject
            {
                ["frames"] = cleanFrames,
                ["totalFrames"] = totalFrames,
                ["threadId"] = threadId
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex) { return CreateTextResult(id, DapErrorHelper.Humanize("stackTrace", ex.Message), isError: true); }
    }
}
