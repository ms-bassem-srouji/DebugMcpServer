using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.DbgEng;
using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DetachSessionTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DotnetDumpRegistry _dumpRegistry;
    private readonly NativeDumpRegistry _nativeRegistry;
    private readonly ILogger<DetachSessionTool> _logger;

    public string Name => "detach_session";
    public string Description => "Detach the debugger from the process and end the debug session. Works for DAP, dotnet-dump, and native dump sessions.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": {
                    "type": "string",
                    "description": "The debug session ID returned by attach_to_process, load_dump_file, load_dotnet_dump, or load_native_dump"
                }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DetachSessionTool(DapSessionRegistry registry, DotnetDumpRegistry dumpRegistry, NativeDumpRegistry nativeRegistry, ILogger<DetachSessionTool> logger)
    {
        _registry = registry;
        _dumpRegistry = dumpRegistry;
        _nativeRegistry = nativeRegistry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);

        if (!_registry.TryRemove(sessionId, out IDapSession? session) || session == null)
        {
            // Try dotnet-dump registry
            if (_dumpRegistry.TryRemove(sessionId, out var dumpSession) && dumpSession != null)
            {
                dumpSession.Dispose();
                _logger.LogInformation("Closed dotnet-dump session {SessionId}", sessionId);
                return CreateTextResult(id,
                    $"{{\"outcome\": \"closed\", \"sessionId\": \"{sessionId}\", \"message\": \"dotnet-dump session closed.\"}}");
            }

            // Try native dump registry
#pragma warning disable CA1416 // DbgEngSession is Windows-only but registry access is runtime-guarded
            if (_nativeRegistry.TryRemove(sessionId, out var nativeSession) && nativeSession != null)
            {
                nativeSession.Dispose();
#pragma warning restore CA1416
                _logger.LogInformation("Closed native dump session {SessionId}", sessionId);
                return CreateTextResult(id,
                    $"{{\"outcome\": \"closed\", \"sessionId\": \"{sessionId}\", \"message\": \"Native dump session closed.\"}}");
            }

            return CreateTextResult(id, $"Session '{sessionId}' not found or already ended.", isError: true);
        }

        try
        {
            // Send disconnect — tells vsdbg to detach (not terminate) the target process
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

            await session.SendRequestAsync("disconnect", new
            {
                restart = false,
                terminateDebuggee = false
            }, cts.Token);

            _logger.LogInformation("Detached session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending disconnect for session {SessionId} — cleaning up anyway", sessionId);
        }
        finally
        {
            session.Dispose();
        }

        return CreateTextResult(id, $"{{\"outcome\": \"detached\", \"sessionId\": \"{sessionId}\", \"message\": \"Debugger detached. Target process continues running.\"}}");
    }
}
