using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpGcRootsTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpGcRootsTool> _logger;

    public string Name => "dotnet_dump_gc_roots";

    public string Description =>
        "Find GC roots keeping a .NET object alive. Shows the reference chain from root to object. " +
        "Equivalent to SOS 'gcroot'. Useful for diagnosing memory leaks.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" },
                "address": { "type": "string", "description": "Object address (hex, e.g., '0x7fff12340000')" }
            },
            "required": ["sessionId", "address"]
        }
        """)!;

    public DotnetDumpGcRootsTool(DotnetDumpRegistry registry, ILogger<DotnetDumpGcRootsTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return Task.FromResult(CreateErrorResponse(id, -32602, err!));
        if (!TryGetString(arguments, "address", out var addressStr, out var addrErr))
            return Task.FromResult(CreateErrorResponse(id, -32602, addrErr!));
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return Task.FromResult(CreateTextResult(id,
                $"Session '{sessionId}' not found. Use load_dotnet_dump to open a dump.", isError: true));

        if (!DotnetDumpInspectTool.TryParseAddress(addressStr, out var address))
            return Task.FromResult(CreateTextResult(id,
                $"Invalid address: '{addressStr}'. Use hex format like '0x7fff12340000'.", isError: true));

        try
        {
            var heap = session.Runtime.Heap;
            var roots = new JsonArray();

            foreach (var root in heap.EnumerateRoots())
            {
                if (root.Object.Address == address)
                {
                    roots.Add(new JsonObject
                    {
                        ["rootKind"] = root.RootKind.ToString(),
                        ["address"] = $"0x{root.Address:X}",
                        ["objectAddress"] = $"0x{root.Object.Address:X}",
                        ["type"] = root.Object.Type?.Name ?? "unknown"
                    });
                }
            }

            var result = new JsonObject
            {
                ["targetAddress"] = $"0x{address:X}",
                ["rootCount"] = roots.Count,
                ["roots"] = roots
            };

            if (roots.Count == 0)
                result["message"] = "No direct GC roots found. The object may be referenced indirectly " +
                                    "through other objects, or it may be eligible for collection.";

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpGcRoots] Error for {Address}", addressStr);
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }
}
