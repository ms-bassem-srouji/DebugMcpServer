using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpHeapStatsTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpHeapStatsTool> _logger;

    public string Name => "dotnet_dump_heap_stats";

    public string Description =>
        "Show heap statistics from a .NET dump — object counts and total size by type. " +
        "Equivalent to SOS 'dumpheap -stat'. Use filter to search for specific types.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" },
                "filter": { "type": "string", "description": "Filter type names (case-insensitive contains match). Example: 'String', 'MyApp.Order'" },
                "top": { "type": "integer", "description": "Return only the top N types by total size (default 30)", "default": 30 }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DotnetDumpHeapStatsTool(DotnetDumpRegistry registry, ILogger<DotnetDumpHeapStatsTool> logger)
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

        var filter = arguments?["filter"]?.GetValue<string>();
        var top = arguments?["top"]?.GetValue<int>() ?? 30;

        try
        {
            var stats = new Dictionary<string, (int Count, long Size)>();

            foreach (var obj in session.Runtime.Heap.EnumerateObjects())
            {
                if (!obj.IsValid) continue;

                var typeName = obj.Type?.Name ?? "<unknown>";

                if (!string.IsNullOrEmpty(filter) &&
                    !typeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (stats.TryGetValue(typeName, out var existing))
                    stats[typeName] = (existing.Count + 1, existing.Size + (long)obj.Size);
                else
                    stats[typeName] = (1, (long)obj.Size);
            }

            var sorted = stats
                .OrderByDescending(kv => kv.Value.Size)
                .Take(top);

            var types = new JsonArray();
            long totalSize = 0;
            long totalCount = 0;
            foreach (var (typeName, (count, size)) in sorted)
            {
                types.Add(new JsonObject
                {
                    ["type"] = typeName,
                    ["count"] = count,
                    ["totalSizeBytes"] = size,
                    ["totalSizeFormatted"] = FormatSize(size)
                });
                totalSize += size;
                totalCount += count;
            }

            var result = new JsonObject
            {
                ["typeCount"] = types.Count,
                ["types"] = types,
                ["totalObjects"] = totalCount,
                ["totalSize"] = FormatSize(totalSize)
            };
            if (!string.IsNullOrEmpty(filter))
                result["filter"] = filter;

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpHeapStats] Error");
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
