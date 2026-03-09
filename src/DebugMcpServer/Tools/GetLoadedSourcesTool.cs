using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class GetLoadedSourcesTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<GetLoadedSourcesTool> _logger;

    public string Name => "get_loaded_sources";

    public string Description =>
        "List all source files the debug adapter knows about. Useful for dump debugging to discover " +
        "available source files when the codebase is unfamiliar.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public GetLoadedSourcesTool(DapSessionRegistry registry, ILogger<GetLoadedSourcesTool> logger)
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

        try
        {
            var response = await session.SendRequestAsync("loadedSources", null, cancellationToken);

            var sources = response["sources"] as JsonArray;
            if (sources == null || sources.Count == 0)
            {
                return CreateTextResult(id, new JsonObject
                {
                    ["count"] = 0,
                    ["sources"] = new JsonArray(),
                    ["message"] = "No source information available. The adapter may not support loadedSources, or no symbols are loaded."
                }.ToJsonString());
            }

            var formatted = new JsonArray();
            foreach (var src in sources)
            {
                if (src == null) continue;
                formatted.Add(new JsonObject
                {
                    ["name"] = src["name"]?.DeepClone(),
                    ["path"] = src["path"]?.DeepClone(),
                    ["origin"] = src["origin"]?.DeepClone()
                });
            }

            var result = new JsonObject
            {
                ["count"] = formatted.Count,
                ["sources"] = formatted
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, DapErrorHelper.Humanize("loadedSources", ex.Message), isError: true);
        }
    }
}
