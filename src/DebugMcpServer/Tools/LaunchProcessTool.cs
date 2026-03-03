using System.Diagnostics;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class LaunchProcessTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DebugOptions _options;
    private readonly ILogger<LaunchProcessTool> _logger;

    public string Name => "launch_process";

    public string Description =>
        "Launch a process under the debugger. Returns a sessionId for all subsequent debug commands. " +
        "The process will be paused at entry (by default) — use continue_execution or step commands to proceed.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "program": {
                    "type": "string",
                    "description": "Path to the executable or DLL to launch (e.g., bin/Debug/net8.0/MyApp.dll)"
                },
                "args": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Command line arguments to pass to the program"
                },
                "cwd": {
                    "type": "string",
                    "description": "Working directory for the launched process"
                },
                "adapter": {
                    "type": "string",
                    "description": "Name of the configured adapter to use (from list_adapters). If omitted, uses the first configured adapter."
                },
                "adapterPath": {
                    "type": "string",
                    "description": "Optional explicit path to the debug adapter executable. Overrides adapter name lookup."
                },
                "stopAtEntry": {
                    "type": "boolean",
                    "description": "Whether to stop at the entry point of the program (default: true)"
                },
                "console": {
                    "type": "string",
                    "description": "Where to launch the program's console: 'integratedTerminal' (default, visible terminal window), 'externalTerminal' (separate terminal window), or 'internalConsole' (output captured as DAP events only, no visible window).",
                    "default": "integratedTerminal"
                },
                "host": {
                    "type": "string",
                    "description": "SSH host for remote debugging (e.g., 'user@hostname'). When provided, the adapter and program run on the remote machine via SSH."
                },
                "sshPort": {
                    "type": "integer",
                    "description": "SSH port (default 22).",
                    "default": 22
                },
                "sshKey": {
                    "type": "string",
                    "description": "Path to SSH private key file for authentication."
                },
                "remoteAdapterPath": {
                    "type": "string",
                    "description": "Override the adapter path on the remote machine. Falls back to adapter config RemotePath, then Path."
                }
            },
            "required": ["program"]
        }
        """)!;

    public LaunchProcessTool(
        DapSessionRegistry registry,
        IOptions<DebugOptions> options,
        ILogger<LaunchProcessTool> logger)
    {
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "program", out var program, out var programError))
            return CreateErrorResponse(id, -32602, programError!);

        var argsArray = arguments?["args"] as JsonArray;
        var cwd = arguments?["cwd"]?.GetValue<string>();
        var stopAtEntry = arguments?["stopAtEntry"]?.GetValue<bool>() ?? true;
        var console = arguments?["console"]?.GetValue<string>() ?? "integratedTerminal";
        var adapterName = arguments?["adapter"]?.GetValue<string>();
        var adapterPath = arguments?["adapterPath"]?.GetValue<string>();
        var sshHost = arguments?["host"]?.GetValue<string>();
        var sshPort = arguments?["sshPort"]?.GetValue<int>() ?? 22;
        var sshKey = arguments?["sshKey"]?.GetValue<string>();
        var remoteAdapterPath = arguments?["remoteAdapterPath"]?.GetValue<string>();
        bool isRemote = !string.IsNullOrWhiteSpace(sshHost);
        string? resolvedAdapterID = null;
        Options.AdapterConfig? resolvedConfig = null;

        // Resolution order: adapter name → explicit adapterPath → first in array → legacy fallback
        string? resolvedPath = null;
        if (!string.IsNullOrWhiteSpace(adapterName))
        {
            resolvedConfig = _options.Adapters.FirstOrDefault(a =>
                string.Equals(a.Name, adapterName, StringComparison.OrdinalIgnoreCase));
            if (resolvedConfig == null)
            {
                var available = string.Join(", ", _options.Adapters.Select(a => a.Name));
                return CreateTextResult(id,
                    $"Unknown adapter '{adapterName}'. Available adapters: [{available}]. Use list_adapters to see all configured adapters.",
                    isError: true);
            }
            resolvedPath = resolvedConfig.Path;
            resolvedAdapterID = resolvedConfig.AdapterID;
            _logger.LogInformation("[Launch] Using named adapter '{Name}' at {Path}", resolvedConfig.Name, resolvedConfig.Path);
        }
        else if (!string.IsNullOrWhiteSpace(adapterPath))
        {
            resolvedPath = adapterPath;
            _logger.LogInformation("[Launch] Using explicit adapterPath: {Path}", adapterPath);
        }
        else if (_options.Adapters.Count > 0)
        {
            resolvedConfig = _options.Adapters[0];
            resolvedPath = resolvedConfig.Path;
            resolvedAdapterID = resolvedConfig.AdapterID;
            _logger.LogInformation("[Launch] Using default adapter '{Name}' at {Path}", resolvedConfig.Name, resolvedConfig.Path);
        }

        // For remote debugging, resolve the remote adapter path and skip local File.Exists
        if (isRemote)
        {
            var effectiveRemotePath = remoteAdapterPath
                ?? resolvedConfig?.RemotePath
                ?? resolvedPath;

            if (string.IsNullOrWhiteSpace(effectiveRemotePath))
            {
                return CreateTextResult(id,
                    "No adapter path resolved for remote debugging. Set RemotePath in adapter config or provide remoteAdapterPath.",
                    isError: true);
            }

            resolvedAdapterID ??= "coreclr";
            resolvedPath = effectiveRemotePath;
            _logger.LogInformation("[Launch] Remote debugging via SSH to {Host}. Adapter={Path}", sshHost, effectiveRemotePath);
        }

        // Local: fall through to legacy resolution if no adapter resolved yet
        if (!isRemote && (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath)))
        {
            _logger.LogInformation("[Launch] Falling back to legacy adapter resolution");
            resolvedPath = ResolveAdapterPath(resolvedPath);
        }

        if (resolvedPath == null)
        {
            _logger.LogError("[Launch] No debug adapter found");
            return CreateTextResult(id,
                "Could not find a debug adapter. Configure adapters in appsettings.json, " +
                "or set the NETCOREDBG_PATH environment variable, or provide the 'adapterPath' parameter.",
                isError: true);
        }

        resolvedAdapterID ??= "coreclr"; // default for backward compatibility
        _logger.LogInformation("[Launch] Using adapter at {AdapterPath} (adapterID={AdapterID}) for program {Program}",
            resolvedPath, resolvedAdapterID, program);

        // Launch debug adapter in DAP mode (local or via SSH)
        ProcessStartInfo psi;
        if (isRemote)
        {
            psi = SshHelper.CreateSshProcessStartInfo(
                sshHost!, sshPort, sshKey,
                $"{resolvedPath} --interpreter=vscode");
        }
        else
        {
            psi = new ProcessStartInfo(resolvedPath, "--interpreter=vscode")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        Process adapterProcess;
        try
        {
            _logger.LogInformation("[Launch] Launching adapter: {Path} --interpreter=vscode", resolvedPath);
            adapterProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start debug adapter process");
            _logger.LogInformation("[Launch] Adapter launched. Adapter PID={AdapterPid}", adapterProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Launch] Failed to launch debug adapter");
            return CreateTextResult(id, $"Failed to launch debug adapter: {ex.Message}", isError: true);
        }

        // Pipe adapter stderr to our logger
        _ = Task.Run(async () =>
        {
            try
            {
                while (!adapterProcess.StandardError.EndOfStream)
                {
                    var line = await adapterProcess.StandardError.ReadLineAsync();
                    if (line != null) _logger.LogDebug("[adapter stderr] {Line}", line);
                }
            }
            catch { /* ignore */ }
        }, CancellationToken.None);

        var session = new DapSession(adapterProcess, _logger, _options.MaxPendingEvents);
        session.StartReaderLoop();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            new CancellationTokenSource(TimeSpan.FromSeconds(_options.AttachTimeoutSeconds)).Token);

        try
        {
            // 1. Initialize
            _logger.LogInformation("[Launch] Step 1/5: Sending 'initialize'...");
            await session.SendRequestAsync("initialize", new
            {
                clientID = "DebugMcpServer",
                clientName = "Debug MCP Server",
                adapterID = resolvedAdapterID,
                locale = "en-US",
                linesStartAt1 = true,
                columnsStartAt1 = true,
                pathFormat = "path",
                supportsVariableType = true,
                supportsRunInTerminalRequest = true,
                supportsProgressReporting = false
            }, timeoutCts.Token);

            _logger.LogInformation("[Launch] Step 1/5: 'initialize' complete");

            // 2. Launch
            _logger.LogInformation("[Launch] Step 2/5: Sending 'launch' for program {Program}...", program);

            var launchArgs = new Dictionary<string, object?>
            {
                ["program"] = program,
                ["stopAtEntry"] = stopAtEntry,
                ["justMyCode"] = false,
                ["console"] = console
            };
            if (argsArray is { Count: > 0 })
                launchArgs["args"] = argsArray.Select(a => a?.GetValue<string>()).ToArray();
            if (!string.IsNullOrWhiteSpace(cwd))
                launchArgs["cwd"] = cwd;

            await session.SendRequestAsync("launch", launchArgs, timeoutCts.Token);

            _logger.LogInformation("[Launch] Step 2/5: 'launch' complete");

            // 3. Wait for 'initialized' event
            _logger.LogInformation("[Launch] Step 3/5: Waiting for 'initialized' event (5s timeout)...");
            await WaitForInitializedAsync(session, timeoutCts.Token);
            _logger.LogInformation("[Launch] Step 3/5: 'initialized' done (received or timed out)");

            // 4. configurationDone
            _logger.LogInformation("[Launch] Step 4/5: Sending 'configurationDone'...");
            try
            {
                await session.SendRequestAsync("configurationDone", null, timeoutCts.Token);
                _logger.LogInformation("[Launch] Step 4/5: 'configurationDone' succeeded");
            }
            catch (DapSessionException ex)
            {
                _logger.LogWarning("[Launch] Step 4/5: 'configurationDone' failed (non-fatal): {Msg}", ex.Message);
            }

            // 5. Wait for 'stopped' event (entry point)
            _logger.LogInformation("[Launch] Step 5/5: Waiting for 'stopped' event...");
            var stoppedResult = await WaitForStoppedAsync(session, id, timeoutCts.Token);
            if (stoppedResult != null)
            {
                _logger.LogWarning("[Launch] Step 5/5: WaitForStopped returned error");
                return stoppedResult;
            }
            _logger.LogInformation("[Launch] Step 5/5: Process stopped. ActiveThreadId={ThreadId}", session.ActiveThreadId);

            // 6. Register session
            var sessionId = _registry.Register(session);
            _logger.LogInformation("[Launch] Session registered as {SessionId}", sessionId);

            // 7. Auto-resolve top frame location
            _logger.LogInformation("[Launch] Resolving top frame location...");
            var locationText = await GetTopFrameLocationAsync(session, cancellationToken);

            _logger.LogInformation("[Launch] SUCCESS! Session={SessionId}, Program={Program}, Location={Location}",
                sessionId, program, locationText);

            return CreateTextResult(id, $$"""
                {
                  "outcome": "stopped",
                  "reason": "entry",
                  "sessionId": "{{sessionId}}",
                  "threadId": {{session.ActiveThreadId ?? 0}},
                  "location": {{locationText}},
                  "message": "Process launched and paused. Use continue_execution or step commands to proceed."
                }
                """);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[Launch] TIMEOUT after {Seconds}s. Session state={State}, ActiveThread={Thread}",
                _options.AttachTimeoutSeconds, session.State, session.ActiveThreadId);
            session.Dispose();
            return CreateTextResult(id,
                $"Debug adapter did not respond within {_options.AttachTimeoutSeconds} seconds. " +
                $"Possible causes: program '{program}' does not exist, is not a valid executable, " +
                $"or the adapter failed to launch it. Session state was '{session.State}'. Session cleaned up.",
                isError: true);
        }
        catch (DapSessionException ex)
        {
            _logger.LogError(ex, "[Launch] DAP error during launch of {Program}", program);
            session.Dispose();
            return CreateTextResult(id, $"DAP error during launch: {ex.Message}", isError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Launch] Unexpected error during launch of {Program}", program);
            session.Dispose();
            return CreateTextResult(id, $"Unexpected error: {ex.Message}", isError: true);
        }
    }

    private async Task WaitForInitializedAsync(DapSession session, CancellationToken ct)
    {
        using var initCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        try
        {
            await session.InitializedTask.WaitAsync(initCts.Token);
            _logger.LogInformation("[WaitForInitialized] 'initialized' event received");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[WaitForInitialized] Timed out after 5s — proceeding without 'initialized' event");
        }
    }

    private async Task<JsonNode?> WaitForStoppedAsync(DapSession session, JsonNode? id, CancellationToken ct)
    {
        await foreach (var evt in session.EventChannel.ReadAllAsync(ct))
        {
            _logger.LogInformation("[WaitForStopped] Event: {EventType}", evt.EventType);

            if (evt.EventType == "stopped")
            {
                _logger.LogInformation("[WaitForStopped] Got 'stopped' event");
                return null; // success
            }

            if (evt.EventType == "terminated")
            {
                _logger.LogWarning("[WaitForStopped] Process terminated before stopping");
                session.Dispose();
                return CreateTextResult(id, "Process terminated before reaching entry point.", isError: true);
            }
        }

        _logger.LogWarning("[WaitForStopped] Event channel closed without 'stopped' event");
        return CreateTextResult(id, "DAP event channel closed unexpectedly.", isError: true);
    }

    private static async Task<string> GetTopFrameLocationAsync(DapSession session, CancellationToken ct)
    {
        try
        {
            var response = await session.SendRequestAsync("stackTrace", new
            {
                threadId = session.ActiveThreadId ?? 1,
                startFrame = 0,
                levels = 1
            }, ct);

            var frame = response["stackFrames"]?[0];
            if (frame == null) return "null";

            var source = frame["source"]?["path"]?.GetValue<string>() ?? "unknown";
            var line = frame["line"]?.GetValue<int>() ?? 0;
            return $$"""{"source": "{{source}}", "line": {{line}}}""";
        }
        catch
        {
            return "null";
        }
    }

    private string? ResolveAdapterPath(string? explicitPath)
    {
        _logger.LogInformation("[ResolveAdapter] Starting resolution. ExplicitPath={Path}, ConfigAdapter={Config}, ConfigVsdbg={Vsdbg}",
            explicitPath ?? "(null)", _options.AdapterPath ?? "(null)", _options.VsdbgPath ?? "(null)");

        // 1. Explicit parameter
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(explicitPath))
            {
                _logger.LogInformation("[ResolveAdapter] Using explicit path: {Path}", explicitPath);
                return explicitPath;
            }
            _logger.LogWarning("[ResolveAdapter] Explicit path does not exist: {Path}", explicitPath);
        }

        // 2. Config (supports both new and legacy option names)
        if (!string.IsNullOrWhiteSpace(_options.AdapterPath))
        {
            if (File.Exists(_options.AdapterPath))
            {
                _logger.LogInformation("[ResolveAdapter] Using config AdapterPath: {Path}", _options.AdapterPath);
                return _options.AdapterPath;
            }
            _logger.LogWarning("[ResolveAdapter] Config AdapterPath does not exist: {Path}", _options.AdapterPath);
        }
        if (!string.IsNullOrWhiteSpace(_options.VsdbgPath))
        {
            if (File.Exists(_options.VsdbgPath))
            {
                _logger.LogInformation("[ResolveAdapter] Using config VsdbgPath: {Path}", _options.VsdbgPath);
                return _options.VsdbgPath;
            }
            _logger.LogWarning("[ResolveAdapter] Config VsdbgPath does not exist: {Path}", _options.VsdbgPath);
        }

        // 3. Environment variable
        var envPath = Environment.GetEnvironmentVariable("NETCOREDBG_PATH");
        _logger.LogInformation("[ResolveAdapter] NETCOREDBG_PATH={Path}", envPath ?? "(not set)");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        envPath = Environment.GetEnvironmentVariable("VSDBG_PATH");
        _logger.LogInformation("[ResolveAdapter] VSDBG_PATH={Path}", envPath ?? "(not set)");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        // 4. Common netcoredbg install paths (Windows)
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            @"C:\tools\netcoredbg\netcoredbg\netcoredbg.exe",
            Path.Combine(programFiles, "netcoredbg", "netcoredbg.exe"),
            Path.Combine(localAppData, "netcoredbg", "netcoredbg.exe"),
            Path.Combine(userProfile, ".dotnet", "tools", "netcoredbg.exe"),
            Path.Combine(userProfile, "netcoredbg", "netcoredbg.exe"),
        };

        foreach (var candidate in candidates)
        {
            var exists = File.Exists(candidate);
            _logger.LogInformation("[ResolveAdapter] Checking {Path} => {Exists}", candidate, exists);
            if (exists)
                return candidate;
        }

        _logger.LogError("[ResolveAdapter] Could not find netcoredbg.exe in any known location");
        return null;
    }
}
