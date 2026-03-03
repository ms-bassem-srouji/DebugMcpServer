using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class GetModulesTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<GetModulesTool> _logger;

    public string Name => "get_modules";

    public string Description =>
        "List all loaded modules (assemblies/DLLs) in the debug session with name, path, and version info.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public GetModulesTool(DapSessionRegistry registry, ILogger<GetModulesTool> logger)
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
            var response = await session.SendRequestAsync("modules", new
            {
                startModule = 0,
                moduleCount = 1000
            }, cancellationToken);

            var rawModules = response["modules"] as JsonArray ?? new JsonArray();
            var modules = new JsonArray();
            foreach (var mod in rawModules)
            {
                if (mod == null) continue;
                var entry = new JsonObject
                {
                    ["id"] = mod["id"]?.DeepClone(),
                    ["name"] = mod["name"]?.GetValue<string>()
                };

                var path = mod["path"]?.GetValue<string>();
                if (path != null) entry["path"] = path;

                var version = mod["version"]?.GetValue<string>();
                if (version != null) entry["version"] = version;

                var isOptimized = mod["isOptimized"];
                if (isOptimized != null) entry["isOptimized"] = isOptimized.GetValue<bool>();

                var symbolStatus = mod["symbolStatus"]?.GetValue<string>();
                if (symbolStatus != null) entry["symbolStatus"] = symbolStatus;

                modules.Add(entry);
            }

            var result = new JsonObject
            {
                ["modules"] = modules,
                ["count"] = modules.Count
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, $"DAP error: {ex.Message}", isError: true);
        }
    }
}
