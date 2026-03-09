using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpThreadsTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpThreadsTool> _logger;

    public string Name => "dotnet_dump_threads";

    public string Description =>
        "List all managed threads with their stack traces from a .NET dump. " +
        "Shows thread ID, OS thread ID, exception info, and managed call stack for each thread.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID (from load_dotnet_dump)" },
                "maxFrames": { "type": "integer", "description": "Max stack frames per thread (default 50)", "default": 50 }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DotnetDumpThreadsTool(DotnetDumpRegistry registry, ILogger<DotnetDumpThreadsTool> logger)
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

        var maxFrames = arguments?["maxFrames"]?.GetValue<int>() ?? 50;

        try
        {
            var threads = new JsonArray();
            foreach (var thread in session.Runtime.Threads)
            {
                var threadObj = new JsonObject
                {
                    ["managedThreadId"] = thread.ManagedThreadId,
                    ["osThreadId"] = $"0x{thread.OSThreadId:X}",
                    ["isAlive"] = thread.IsAlive,
                    ["isFinalizer"] = thread.IsFinalizer,
                    ["isGc"] = thread.IsGc
                };

                if (thread.CurrentException != null)
                {
                    threadObj["exception"] = new JsonObject
                    {
                        ["type"] = thread.CurrentException.Type?.Name,
                        ["message"] = thread.CurrentException.Message
                    };
                }

                var frames = new JsonArray();
                int frameCount = 0;
                foreach (var frame in thread.EnumerateStackTrace())
                {
                    if (frameCount++ >= maxFrames) break;

                    var frameObj = new JsonObject
                    {
                        ["method"] = frame.Method != null
                            ? $"{frame.Method.Type?.Name}.{frame.Method.Name}"
                            : "[Native Frame]",
                        ["instructionPointer"] = $"0x{frame.InstructionPointer:X}"
                    };

                    if (frame.Method?.Type?.Module?.Name != null)
                        frameObj["module"] = Path.GetFileName(frame.Method.Type.Module.Name);

                    frames.Add(frameObj);
                }
                threadObj["stackTrace"] = frames;
                threadObj["frameCount"] = frameCount;

                threads.Add(threadObj);
            }

            var result = new JsonObject
            {
                ["threadCount"] = threads.Count,
                ["threads"] = threads
            };
            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpThreads] Error");
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }
}
