using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpStackObjectsTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpStackObjectsTool> _logger;

    public string Name => "dotnet_dump_stack_objects";

    public string Description =>
        "Show all .NET objects referenced from a specific thread's stack (locals, parameters, 'this' pointers). " +
        "Equivalent to SOS 'dso'. Use dotnet_dump_threads first to find the thread ID, then inspect " +
        "what data was in flight when the dump was captured.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" },
                "osThreadId": { "type": "string", "description": "OS thread ID (hex, from dotnet_dump_threads, e.g., '0x9F64'). If omitted, uses the first alive thread." }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DotnetDumpStackObjectsTool(DotnetDumpRegistry registry, ILogger<DotnetDumpStackObjectsTool> logger)
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

        var osThreadIdStr = arguments?["osThreadId"]?.GetValue<string>();

        try
        {
            var thread = FindThread(session, osThreadIdStr);
            if (thread == null)
            {
                return Task.FromResult(CreateTextResult(id,
                    "Thread not found. Use dotnet_dump_threads to list available threads.", isError: true));
            }

            var seen = new HashSet<ulong>();
            var objects = new JsonArray();

            foreach (var root in thread.EnumerateStackRoots())
            {
                var obj = session.Runtime.Heap.GetObject(root.Object.Address);
                if (!obj.IsValid || obj.Type == null) continue;
                if (!seen.Add(obj.Address)) continue;

                var entry = new JsonObject
                {
                    ["address"] = $"0x{obj.Address:X}",
                    ["type"] = obj.Type.Name,
                    ["size"] = (long)obj.Size
                };

                if (obj.Type.IsString)
                {
                    var str = obj.AsString();
                    entry["value"] = str?.Length > 200 ? str[..200] + "..." : str;
                }

                objects.Add(entry);
            }

            var result = new JsonObject
            {
                ["threadId"] = thread.ManagedThreadId,
                ["osThreadId"] = $"0x{thread.OSThreadId:X}",
                ["objectCount"] = objects.Count,
                ["objects"] = objects
            };

            if (objects.Count == 0)
                result["message"] = "No managed objects found on this thread's stack. " +
                                    "The thread may be in native code or have an empty managed stack.";
            else
                result["hint"] = "Use dotnet_dump_inspect with an address to see object fields and values.";

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpStackObjects] Error");
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }

    internal static Microsoft.Diagnostics.Runtime.ClrThread? FindThread(
        DotnetDumpSession session, string? osThreadIdStr)
    {
        if (string.IsNullOrWhiteSpace(osThreadIdStr))
            return session.Runtime.Threads.FirstOrDefault(t => t.IsAlive);

        var idStr = osThreadIdStr.Trim();
        if (idStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            idStr = idStr[2..];

        if (uint.TryParse(idStr, System.Globalization.NumberStyles.HexNumber, null, out var osId))
            return session.Runtime.Threads.FirstOrDefault(t => t.OSThreadId == osId);

        return null;
    }
}
