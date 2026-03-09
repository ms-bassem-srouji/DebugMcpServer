using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpMemoryStatsTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpMemoryStatsTool> _logger;

    public string Name => "dotnet_dump_memory_stats";

    public string Description =>
        "Show GC heap overview: generation sizes, segment counts, total committed memory, and finalization queue. " +
        "Equivalent to SOS 'eeheap -gc'. Use this to quickly assess if there's a memory leak or GC pressure issue.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DotnetDumpMemoryStatsTool(DotnetDumpRegistry registry, ILogger<DotnetDumpMemoryStatsTool> logger)
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

        try
        {
            var heap = session.Runtime.Heap;
            var segments = heap.Segments.ToList();

            // Calculate sizes by segment kind
            long ephemeralSize = 0, gen2Size = 0, lohSize = 0, pohSize = 0, frozenSize = 0;
            long totalCommitted = 0;
            int ephemeralCount = 0, gen2Count = 0, lohCount = 0, pohCount = 0, frozenCount = 0;

            foreach (var seg in segments)
            {
                var segSize = (long)seg.Length;
                var committed = (long)(seg.CommittedMemory.End - seg.CommittedMemory.Start);
                totalCommitted += committed;

                switch (seg.Kind)
                {
                    case GCSegmentKind.Ephemeral:
                        ephemeralSize += segSize;
                        ephemeralCount++;
                        break;
                    case GCSegmentKind.Generation2:
                        gen2Size += segSize;
                        gen2Count++;
                        break;
                    case GCSegmentKind.Large:
                        lohSize += segSize;
                        lohCount++;
                        break;
                    case GCSegmentKind.Pinned:
                        pohSize += segSize;
                        pohCount++;
                        break;
                    case GCSegmentKind.Frozen:
                        frozenSize += segSize;
                        frozenCount++;
                        break;
                }
            }

            // Count finalizable objects
            int finalizableCount = 0;
            foreach (var _ in heap.EnumerateFinalizableObjects())
                finalizableCount++;

            var generations = new JsonObject
            {
                ["ephemeral"] = new JsonObject
                {
                    ["size"] = FormatSize(ephemeralSize),
                    ["sizeBytes"] = ephemeralSize,
                    ["segments"] = ephemeralCount
                },
                ["gen2"] = new JsonObject
                {
                    ["size"] = FormatSize(gen2Size),
                    ["sizeBytes"] = gen2Size,
                    ["segments"] = gen2Count
                },
                ["largeObjectHeap"] = new JsonObject
                {
                    ["size"] = FormatSize(lohSize),
                    ["sizeBytes"] = lohSize,
                    ["segments"] = lohCount
                },
                ["pinnedObjectHeap"] = new JsonObject
                {
                    ["size"] = FormatSize(pohSize),
                    ["sizeBytes"] = pohSize,
                    ["segments"] = pohCount
                }
            };

            if (frozenCount > 0)
            {
                generations["frozen"] = new JsonObject
                {
                    ["size"] = FormatSize(frozenSize),
                    ["sizeBytes"] = frozenSize,
                    ["segments"] = frozenCount
                };
            }

            var totalHeapSize = ephemeralSize + gen2Size + lohSize + pohSize + frozenSize;

            var result = new JsonObject
            {
                ["totalHeapSize"] = FormatSize(totalHeapSize),
                ["totalHeapSizeBytes"] = totalHeapSize,
                ["totalCommittedMemory"] = FormatSize(totalCommitted),
                ["totalSegments"] = segments.Count,
                ["generations"] = generations,
                ["finalizableObjects"] = finalizableCount,
                ["canWalk"] = heap.CanWalkHeap
            };

            if (!heap.CanWalkHeap)
                result["warning"] = "Heap is not walkable — GC may have been in progress when the dump was captured. " +
                                    "Some analysis tools may return incomplete results.";

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpMemoryStats] Error");
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
