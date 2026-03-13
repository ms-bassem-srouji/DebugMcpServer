using System.Diagnostics;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class ListProcessesTool : ToolBase, IMcpTool
{
    private readonly ILogger<ListProcessesTool> _logger;
    private readonly Func<Process[]> _getProcesses;

    public string Name => "list_processes";
    public string Description =>
        "List running processes with their PIDs and names. Supports filtering by name or by loaded module/DLL. " +
        "Supports remote process listing via SSH. " +
        "Use the returned pid with attach_to_process to start a debug session. " +
        "Tip: to find a .NET test process, use moduleFilter with the test DLL name (e.g., 'MyTests') " +
        "instead of filtering by process name.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "filter": {
                    "type": "string",
                    "description": "Optional name filter (case-insensitive substring match on process name). Omit to list all processes."
                },
                "moduleFilter": {
                    "type": "string",
                    "description": "Optional filter to find processes that have loaded a specific DLL/module. Case-insensitive substring match against the full path of each loaded module. Use this to find a process by what code it's running (e.g., 'MyApp.dll' or 'MyTests'). More precise than name filtering when multiple instances of the same process exist."
                },
                "host": {
                    "type": "string",
                    "description": "SSH host for listing remote processes (e.g., 'user@hostname'). When provided, runs 'ps aux' on the remote machine."
                },
                "sshPort": {
                    "type": "integer",
                    "description": "SSH port (default 22).",
                    "default": 22
                },
                "sshKey": {
                    "type": "string",
                    "description": "Path to SSH private key file for authentication."
                }
            },
            "required": []
        }
        """)!;

    public ListProcessesTool(ILogger<ListProcessesTool> logger)
        : this(logger, Process.GetProcesses) { }

    internal ListProcessesTool(ILogger<ListProcessesTool> logger, Func<Process[]> getProcesses)
    {
        _logger = logger;
        _getProcesses = getProcesses;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        var filter = arguments?["filter"]?.GetValue<string>();
        var moduleFilter = arguments?["moduleFilter"]?.GetValue<string>();
        var sshHost = arguments?["host"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(sshHost))
        {
            var sshPort = arguments?["sshPort"]?.GetValue<int>() ?? 22;
            var sshKey = arguments?["sshKey"]?.GetValue<string>();
            return await ListRemoteProcessesAsync(id, sshHost, sshPort, sshKey, filter, cancellationToken);
        }

        return ListLocalProcesses(id, filter, moduleFilter);
    }

    private JsonNode ListLocalProcesses(JsonNode? id, string? filter, string? moduleFilter)
    {
        var processes = new JsonArray();
        foreach (var p in _getProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            if (filter != null && !p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Module/DLL filter: check if the process has loaded a module matching the substring
            if (moduleFilter != null)
            {
                if (!HasMatchingModule(p, moduleFilter))
                    continue;
            }

            var entry = new JsonObject
            {
                ["pid"] = p.Id,
                ["name"] = p.ProcessName
            };

            try
            {
                if (!string.IsNullOrEmpty(p.MainWindowTitle))
                    entry["title"] = p.MainWindowTitle;
            }
            catch { /* access denied — omit title */ }

            // If moduleFilter was used, include the matched module path for context
            if (moduleFilter != null)
            {
                var matchedModule = FindMatchingModuleName(p, moduleFilter);
                if (matchedModule != null)
                    entry["matchedModule"] = matchedModule;
            }

            processes.Add(entry);
        }

        var result = new JsonObject
        {
            ["processes"] = processes,
            ["count"] = processes.Count
        };

        return CreateTextResult(id, result.ToJsonString());
    }

    private static bool HasMatchingModule(Process process, string moduleFilter)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                if (module.FileName.Contains(moduleFilter, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Access denied or 32/64-bit mismatch — skip this process
        }

        return false;
    }

    private static string? FindMatchingModuleName(Process process, string moduleFilter)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                if (module.FileName.Contains(moduleFilter, StringComparison.OrdinalIgnoreCase))
                    return module.FileName;
            }
        }
        catch
        {
            // Access denied or 32/64-bit mismatch
        }

        return null;
    }

    private async Task<JsonNode> ListRemoteProcessesAsync(
        JsonNode? id, string host, int port, string? keyPath, string? filter, CancellationToken ct)
    {
        _logger.LogInformation("[ListProcesses] Listing remote processes on {Host}", host);

        var psi = SshHelper.CreateSshProcessStartInfo(host, port, keyPath, "ps -eo pid,comm --no-headers");

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start SSH process");

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("[ListProcesses] SSH exited with code {Code}: {Stderr}", process.ExitCode, stderr);
                return CreateTextResult(id, $"SSH command failed (exit code {process.ExitCode}): {stderr.Trim()}", isError: true);
            }

            var processes = new JsonArray();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2 || !int.TryParse(parts[0], out var pid))
                    continue;

                var name = parts[1];
                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                processes.Add(new JsonObject
                {
                    ["pid"] = pid,
                    ["name"] = name
                });
            }

            var result = new JsonObject
            {
                ["processes"] = processes,
                ["count"] = processes.Count,
                ["remote"] = host
            };

            return CreateTextResult(id, result.ToJsonString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ListProcesses] Failed to list remote processes");
            return CreateTextResult(id, $"Failed to list remote processes: {ex.Message}", isError: true);
        }
    }
}
