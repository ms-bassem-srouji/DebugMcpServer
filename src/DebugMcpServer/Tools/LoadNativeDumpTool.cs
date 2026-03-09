using System.Text.Json.Nodes;
using DebugMcpServer.DbgEng;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class LoadNativeDumpTool : ToolBase, IMcpTool
{
    private readonly NativeDumpRegistry _registry;
    private readonly ILogger<LoadNativeDumpTool> _logger;

    public string Name => "load_native_dump";

    public string Description =>
        "Load a native Windows crash dump (.dmp) for analysis using DbgEng (the WinDbg engine). " +
        "Windows only. Returns a sessionId for running WinDbg commands via native_dump_command. " +
        "No external tools required — uses dbgeng.dll built into Windows. " +
        "On Linux/macOS, use load_dump_file with the 'cpp' or 'lldb' adapter instead.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "dumpPath": {
                    "type": "string",
                    "description": "Path to the Windows dump file (.dmp)"
                },
                "symbolPath": {
                    "type": "string",
                    "description": "Symbol path (e.g., 'srv*C:\\symbols*https://msdl.microsoft.com/download/symbols'). If omitted, uses default."
                }
            },
            "required": ["dumpPath"]
        }
        """)!;

    public LoadNativeDumpTool(NativeDumpRegistry registry, ILogger<LoadNativeDumpTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(CreateTextResult(id,
                "Native dump analysis (DbgEng) is only available on Windows. " +
                "On Linux/macOS, use: load_dump_file(adapter: 'cpp') for C/C++ core dumps, " +
                "or load_dotnet_dump for .NET dumps.",
                isError: true));
        }

        if (!TryGetString(arguments, "dumpPath", out var dumpPath, out var dumpError))
            return Task.FromResult(CreateErrorResponse(id, -32602, dumpError!));

        if (!File.Exists(dumpPath))
        {
            return Task.FromResult(CreateTextResult(id,
                $"Dump file not found: '{dumpPath}'. Verify the path exists.",
                isError: true));
        }

        var symbolPath = arguments?["symbolPath"]?.GetValue<string>();

        try
        {
            var session = DbgEngSession.Open(dumpPath, _logger);

            // Set symbol path if provided
            if (!string.IsNullOrWhiteSpace(symbolPath))
            {
                session.ExecuteCommand($".sympath {symbolPath}");
                session.ExecuteCommand(".reload");
            }

            var sessionId = _registry.Register(session);

            // Get basic info
            var threadCount = session.GetThreadCount();
            var versionInfo = session.ExecuteCommand("version").Trim();
            // Get first few lines of version info
            var versionLines = versionInfo.Split('\n', 3);
            var versionSummary = versionLines.Length > 0 ? versionLines[0].Trim() : "unknown";

            var result = new JsonObject
            {
                ["sessionId"] = sessionId,
                ["dumpPath"] = dumpPath,
                ["status"] = "ready",
                ["threadCount"] = threadCount,
                ["engineVersion"] = versionSummary,
                ["message"] = "Native dump loaded via DbgEng. Use native_dump_command to run WinDbg commands.",
                ["commonCommands"] = new JsonObject
                {
                    ["k"] = "Stack trace for current thread",
                    ["~*k"] = "Stack traces for ALL threads",
                    ["~"] = "List all threads",
                    ["~Ns"] = "Switch to thread N (e.g., ~0s, ~3s)",
                    ["dv"] = "Display local variables",
                    ["dv /V"] = "Display locals with addresses and types",
                    ["dt varname"] = "Display type/struct of a variable",
                    ["r"] = "Display registers",
                    ["lm"] = "List loaded modules",
                    ["u rip"] = "Disassemble at current instruction pointer",
                    ["u address L20"] = "Disassemble 20 instructions at address",
                    ["dd address"] = "Display memory as DWORDs",
                    ["db address"] = "Display memory as bytes + ASCII",
                    ["!analyze -v"] = "Automated crash analysis",
                    [".sympath"] = "Show/set symbol path",
                    [".reload"] = "Reload symbols"
                }
            };

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (PlatformNotSupportedException ex)
        {
            return Task.FromResult(CreateTextResult(id, ex.Message, isError: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoadNativeDump] Failed to open dump: {DumpPath}", dumpPath);
            return Task.FromResult(CreateTextResult(id,
                $"Failed to open dump file: {ex.Message}", isError: true));
        }
    }
}
