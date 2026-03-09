using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class LoadDotnetDumpTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<LoadDotnetDumpTool> _logger;

    public string Name => "load_dotnet_dump";

    public string Description =>
        "Load a .NET crash dump for analysis using ClrMD (Microsoft.Diagnostics.Runtime). " +
        "Returns a sessionId for .NET diagnostic tools: dotnet_dump_threads, dotnet_dump_exceptions, " +
        "dotnet_dump_heap_stats, dotnet_dump_inspect, dotnet_dump_gc_roots. " +
        "No external tools required — analysis runs in-process. MIT-licensed.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "dumpPath": {
                    "type": "string",
                    "description": "Path to the .NET dump file (.dmp on Windows, core dump on Linux/macOS)"
                }
            },
            "required": ["dumpPath"]
        }
        """)!;

    public LoadDotnetDumpTool(DotnetDumpRegistry registry, ILogger<LoadDotnetDumpTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "dumpPath", out var dumpPath, out var dumpError))
            return Task.FromResult(CreateErrorResponse(id, -32602, dumpError!));

        if (!File.Exists(dumpPath))
        {
            return Task.FromResult(CreateTextResult(id,
                $"Dump file not found: '{dumpPath}'. Verify the path exists and is accessible.",
                isError: true));
        }

        try
        {
            var session = DotnetDumpSession.Open(dumpPath, _logger);
            var sessionId = _registry.Register(session);

            var runtime = session.Runtime;
            var threads = runtime.Threads;
            var exceptionCount = threads.Count(t => t.CurrentException != null);

            var result = new JsonObject
            {
                ["sessionId"] = sessionId,
                ["dumpPath"] = dumpPath,
                ["status"] = "ready",
                ["runtimeVersion"] = runtime.ClrInfo.Version.ToString(),
                ["threadCount"] = threads.Length,
                ["threadsWithExceptions"] = exceptionCount,
                ["appDomains"] = runtime.AppDomains.Length,
                ["message"] = "Dump loaded via ClrMD. Use dotnet_dump_threads, dotnet_dump_exceptions, " +
                              "dotnet_dump_heap_stats, dotnet_dump_inspect, dotnet_dump_gc_roots to analyze.",
                ["availableTools"] = new JsonObject
                {
                    ["dotnet_dump_threads"] = "List all managed threads with stack traces",
                    ["dotnet_dump_exceptions"] = "Show exceptions on all threads",
                    ["dotnet_dump_heap_stats"] = "Heap statistics — object counts and sizes by type",
                    ["dotnet_dump_inspect"] = "Inspect a .NET object at a given address",
                    ["dotnet_dump_gc_roots"] = "Find GC roots keeping an object alive"
                }
            };

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(CreateTextResult(id, ex.Message, isError: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoadDotnetDump] Failed to open dump: {DumpPath}", dumpPath);
            return Task.FromResult(CreateTextResult(id,
                $"Failed to open dump file: {ex.Message}", isError: true));
        }
    }
}
