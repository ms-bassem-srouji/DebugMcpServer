using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpFindObjectsTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpFindObjectsTool> _logger;

    public string Name => "dotnet_dump_find_objects";

    public string Description =>
        "Find all instances of a .NET type on the heap and return their addresses. " +
        "Use the addresses with dotnet_dump_inspect to examine individual objects. " +
        "Equivalent to SOS 'dumpheap -type'.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" },
                "typeName": { "type": "string", "description": "Type name to search for (case-insensitive contains match). Example: 'Order', 'System.String'" },
                "max": { "type": "integer", "description": "Maximum number of objects to return (default 20)", "default": 20 }
            },
            "required": ["sessionId", "typeName"]
        }
        """)!;

    public DotnetDumpFindObjectsTool(DotnetDumpRegistry registry, ILogger<DotnetDumpFindObjectsTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return Task.FromResult(CreateErrorResponse(id, -32602, err!));
        if (!TryGetString(arguments, "typeName", out var typeName, out var typeErr))
            return Task.FromResult(CreateErrorResponse(id, -32602, typeErr!));
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return Task.FromResult(CreateTextResult(id,
                $"Session '{sessionId}' not found. Use load_dotnet_dump to open a dump.", isError: true));

        var max = arguments?["max"]?.GetValue<int>() ?? 20;

        try
        {
            var objects = new JsonArray();
            int totalMatched = 0;

            foreach (var obj in session.Runtime.Heap.EnumerateObjects())
            {
                if (!obj.IsValid || obj.Type == null) continue;

                var objTypeName = obj.Type.Name ?? "<unknown>";
                if (!objTypeName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                    continue;

                totalMatched++;

                if (objects.Count < max)
                {
                    var entry = new JsonObject
                    {
                        ["address"] = $"0x{obj.Address:X}",
                        ["type"] = objTypeName,
                        ["size"] = (long)obj.Size
                    };

                    // Include value for strings
                    if (obj.Type.IsString)
                    {
                        var str = obj.AsString();
                        entry["value"] = str?.Length > 200 ? str[..200] + "..." : str;
                    }

                    objects.Add(entry);
                }
            }

            var result = new JsonObject
            {
                ["typeName"] = typeName,
                ["matchedCount"] = totalMatched,
                ["returnedCount"] = objects.Count,
                ["objects"] = objects
            };

            if (totalMatched > max)
                result["message"] = $"Showing {max} of {totalMatched} objects. Use 'max' parameter to see more.";
            if (totalMatched > 0)
                result["hint"] = "Use dotnet_dump_inspect with an address to see object fields and values.";

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpFindObjects] Error searching for {TypeName}", typeName);
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }
}
