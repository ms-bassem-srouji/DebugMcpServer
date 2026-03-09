using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.DbgEng;
using DebugMcpServer.DotnetDump;

namespace DebugMcpServer.Tools;

internal sealed class ListSessionsTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DotnetDumpRegistry _dumpRegistry;
    private readonly NativeDumpRegistry _nativeRegistry;

    public string Name => "list_sessions";

    public string Description =>
        "List all active debug sessions (DAP, dotnet-dump, and native dump) with their state.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {},
            "required": []
        }
        """)!;

    public ListSessionsTool(DapSessionRegistry registry, DotnetDumpRegistry dumpRegistry, NativeDumpRegistry nativeRegistry)
    {
        _registry = registry;
        _dumpRegistry = dumpRegistry;
        _nativeRegistry = nativeRegistry;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        var sessions = new JsonArray();
        foreach (var (sessionId, session) in _registry.GetAll())
        {
            sessions.Add(new JsonObject
            {
                ["sessionId"] = sessionId,
                ["type"] = "dap",
                ["state"] = session.State.ToString(),
                ["activeThreadId"] = session.ActiveThreadId,
                ["isDumpSession"] = session.IsDumpSession
            });
        }

        foreach (var (sessionId, session) in _dumpRegistry.GetAll())
        {
            sessions.Add(new JsonObject
            {
                ["sessionId"] = sessionId,
                ["type"] = "dotnet-dump",
                ["state"] = session.IsRunning ? "running" : "exited",
                ["dumpPath"] = session.DumpPath
            });
        }

#pragma warning disable CA1416 // DbgEngSession is Windows-only but accessed through cross-platform registry (runtime-safe)
        foreach (var (sessionId, session) in _nativeRegistry.GetAll())
        {
            sessions.Add(new JsonObject
            {
                ["sessionId"] = sessionId,
                ["type"] = "native-dump",
                ["state"] = session.IsRunning ? "running" : "disposed",
                ["dumpPath"] = session.DumpPath
            });
        }
#pragma warning restore CA1416

        var result = new JsonObject
        {
            ["sessions"] = sessions,
            ["count"] = sessions.Count
        };
        return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
    }
}
