using System.Text.Json.Nodes;
using DebugMcpServer.Options;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class ListAdaptersTool : ToolBase, IMcpTool
{
    private readonly DebugOptions _options;

    public string Name => "list_adapters";

    public string Description =>
        "List all configured debug adapters with their availability status. " +
        "Shows which adapters are found, missing, or configured as bare command names (resolved from PATH at runtime). " +
        "Also checks dotnet-dump availability. Use this tool first to diagnose adapter issues.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {},
            "required": []
        }
        """)!;

    public ListAdaptersTool(IOptions<DebugOptions> options)
    {
        _options = options.Value;
    }

    internal ListAdaptersTool(DebugOptions options)
    {
        _options = options;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        var adapters = new JsonArray();
        int foundCount = 0;
        int bareCount = 0;

        foreach (var adapter in _options.Adapters)
        {
            var (status, message) = ResolveStatus(adapter.Path);
            if (status == "found") foundCount++;
            if (status == "bare_command") bareCount++;

            var entry = new JsonObject
            {
                ["name"] = adapter.Name,
                ["path"] = adapter.Path,
                ["status"] = status
            };

            if (!string.IsNullOrEmpty(adapter.AdapterID))
                entry["adapterID"] = adapter.AdapterID;
            if (!string.IsNullOrEmpty(adapter.DumpArgumentName))
                entry["dumpSupport"] = true;
            if (message != null)
                entry["message"] = message;
            if (status == "not_found")
                entry["installHint"] = GetInstallHint(adapter.Name);

            adapters.Add(entry);
        }

        var summaryParts = new List<string>();
        summaryParts.Add($"{foundCount} of {_options.Adapters.Count} adapters found at configured path");
        if (bareCount > 0)
            summaryParts.Add($"{bareCount} configured as bare command name (resolved from PATH at runtime)");
        var notFound = _options.Adapters.Count - foundCount - bareCount;
        if (notFound > 0)
            summaryParts.Add($"{notFound} not found — check paths in config or run install hints");

        var result = new JsonObject
        {
            ["adapters"] = adapters,
            ["dotnetDumpAnalysis"] = new JsonObject
            {
                ["status"] = "built-in",
                ["description"] = ".NET dump analysis via ClrMD (Microsoft.Diagnostics.Runtime). No external tools required.",
                ["tools"] = "load_dotnet_dump, dotnet_dump_threads, dotnet_dump_exceptions, dotnet_dump_heap_stats, dotnet_dump_inspect, dotnet_dump_gc_roots"
            },
            ["summary"] = string.Join(". ", summaryParts),
            ["configLocation"] = GetConfigPath(),
            ["hint"] = "Edit the config file to set adapter paths. Use full paths for verified status, or bare command names if the adapter is on your PATH."
        };

        return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
    }

    /// <summary>
    /// Determines the status of an adapter path:
    /// - "found": full path exists on disk
    /// - "bare_command": no directory separator — will be resolved from PATH at runtime by Process.Start
    /// - "not_found": full path does not exist on disk
    /// - "not_configured": path is empty
    /// </summary>
    internal static (string status, string? message) ResolveStatus(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ("not_configured", "No path configured. Set the adapter path in appsettings.json.");

        // If path has no directory separator, it's a bare command name intended for PATH resolution
        if (!path.Contains(Path.DirectorySeparatorChar) && !path.Contains(Path.AltDirectorySeparatorChar))
            return ("bare_command", "Bare command name — will be resolved from PATH at runtime. Use attach_to_process or launch_process to verify it works.");

        // Full path — check if it exists
        if (File.Exists(path))
            return ("found", null);

        return ("not_found", $"File not found at configured path: {path}");
    }

    internal static string GetInstallHint(string adapterName) => adapterName.ToLowerInvariant() switch
    {
        "dotnet" => "Install netcoredbg: https://github.com/Samsung/netcoredbg/releases — then set the path in appsettings.json",
        "python" => "Install debugpy: pip install debugpy — then set the adapter path in appsettings.json",
        "node" => "The js-debug adapter is bundled with VS Code at .vscode/extensions/ms-vscode.js-debug-*/src/dapDebugServer.js — set the path in appsettings.json",
        "cpp" => "Install VS Code C++ extension: adapter is at .vscode/extensions/ms-vscode.cpptools-*/debugAdapters/bin/OpenDebugAD7 — set the path in appsettings.json",
        "lldb" => "Install lldb-dap: available via LLVM (apt install lldb, brew install llvm) — then set the path in appsettings.json",
        "cppvsdbg" => "Ships with Visual Studio — set the path in appsettings.json",
        _ => $"Set the correct path for '{adapterName}' in appsettings.json"
    };

    private static string GetConfigPath()
    {
        var userConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "debug-mcp-server", "appsettings.json");
        return File.Exists(userConfigDir)
            ? userConfigDir
            : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }
}
