using System.Diagnostics;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class AttachToProcessTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DebugOptions _options;
    private readonly ILogger<AttachToProcessTool> _logger;

    public string Name => "attach_to_process";

    public string Description =>
        "Attach the debugger to a running process by PID. Returns a sessionId for all subsequent debug commands. " +
        "The process will be paused at attach — use continue_execution or step commands to resume.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "pid": {
                    "type": "integer",
                    "description": "Process ID of the target process to attach to"
                },
                "adapter": {
                    "type": "string",
                    "description": "Name of the configured adapter to use (from list_adapters). If omitted, uses the first configured adapter."
                },
                "adapterPath": {
                    "type": "string",
                    "description": "Optional explicit path to the debug adapter executable. Overrides adapter name lookup."
                },
                "host": {
                    "type": "string",
                    "description": "SSH host for remote debugging (e.g., 'user@hostname'). When provided, the adapter is launched on the remote machine via SSH."
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
            "required": ["pid"]
        }
        """)!;

    public AttachToProcessTool(
        DapSessionRegistry registry,
        IOptions<DebugOptions> options,
        ILogger<AttachToProcessTool> logger)
    {
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetInt(arguments, "pid", out var pid, out var pidError))
            return CreateErrorResponse(id, -32602, pidError!);

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
            _logger.LogInformation("[Attach] Using named adapter '{Name}' at {Path}", resolvedConfig.Name, resolvedConfig.Path);
        }
        else if (!string.IsNullOrWhiteSpace(adapterPath))
        {
            resolvedPath = adapterPath;
            _logger.LogInformation("[Attach] Using explicit adapterPath: {Path}", adapterPath);
        }
        else if (_options.Adapters.Count > 0)
        {
            resolvedConfig = _options.Adapters[0];
            resolvedPath = resolvedConfig.Path;
            resolvedAdapterID = resolvedConfig.AdapterID;
            _logger.LogInformation("[Attach] Using default adapter '{Name}' at {Path}", resolvedConfig.Name, resolvedConfig.Path);
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
            _logger.LogInformation("[Attach] Remote debugging via SSH to {Host}. Adapter={Path}, AdapterID={AdapterID}",
                sshHost, effectiveRemotePath, resolvedAdapterID);

            var psi = SshHelper.CreateSshProcessStartInfo(
                sshHost!, sshPort, sshKey,
                $"{effectiveRemotePath} --interpreter=vscode");

            return await AttachWithPsi(psi, pid, resolvedAdapterID, id, cancellationToken);
        }

        // Local: fall through to legacy resolution if no adapter resolved yet
        // Skip File.Exists for bare command names (no directory separator) — they'll be resolved from PATH by Process.Start
        bool isBareCommand = !string.IsNullOrWhiteSpace(resolvedPath)
            && !resolvedPath.Contains(Path.DirectorySeparatorChar)
            && !resolvedPath.Contains(Path.AltDirectorySeparatorChar);
        if (!isBareCommand && (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath)))
        {
            _logger.LogInformation("[Attach] Falling back to legacy adapter resolution");
            resolvedPath = ResolveAdapterPath(resolvedPath);
        }

        if (resolvedPath == null)
        {
            _logger.LogError("[Attach] No debug adapter found");
            return CreateTextResult(id,
                "Could not find a debug adapter. Configure adapters in appsettings.json, " +
                "or set the NETCOREDBG_PATH environment variable, or provide the 'adapterPath' parameter.",
                isError: true);
        }

        resolvedAdapterID ??= "coreclr";
        _logger.LogInformation("[Attach] Using adapter at {AdapterPath} (adapterID={AdapterID}) for PID {Pid}",
            resolvedPath, resolvedAdapterID, pid);

        // Launch debug adapter in DAP mode
        var localPsi = new ProcessStartInfo(resolvedPath, "--interpreter=vscode")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        return await AttachWithPsi(localPsi, pid, resolvedAdapterID, id, cancellationToken);
    }

    private async Task<JsonNode> AttachWithPsi(ProcessStartInfo psi, int pid, string resolvedAdapterID, JsonNode? id, CancellationToken cancellationToken)
    {
        Process adapterProcess;
        try
        {
            _logger.LogInformation("[Attach] Launching adapter: {FileName} {Args}", psi.FileName, psi.Arguments);
            adapterProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start debug adapter process");
            _logger.LogInformation("[Attach] Adapter launched. Adapter PID={AdapterPid}", adapterProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Attach] Failed to launch debug adapter");
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
            _logger.LogInformation("[Attach] Step 1/9: Sending 'initialize'...");
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
                supportsRunInTerminalRequest = false,
                supportsProgressReporting = false,
                supportsMemoryReferences = true
            }, timeoutCts.Token);

            _logger.LogInformation("[Attach] Step 1/9: 'initialize' complete");

            // 2. Attach
            _logger.LogInformation("[Attach] Step 2/9: Sending 'attach' for PID {Pid}...", pid);
            await session.SendRequestAsync("attach", new
            {
                processId = pid,
                justMyCode = false,
                requireExactSource = false
            }, timeoutCts.Token);

            _logger.LogInformation("[Attach] Step 2/9: 'attach' complete");

            // 3. Wait for 'initialized' event
            _logger.LogInformation("[Attach] Step 3/9: Waiting for 'initialized' event (5s timeout)...");
            await WaitForInitializedAsync(session, timeoutCts.Token);
            _logger.LogInformation("[Attach] Step 3/9: 'initialized' done (received or timed out)");

            // 4. configurationDone
            _logger.LogInformation("[Attach] Step 4/9: Sending 'configurationDone'...");
            try
            {
                await session.SendRequestAsync("configurationDone", null, timeoutCts.Token);
                _logger.LogInformation("[Attach] Step 4/9: 'configurationDone' succeeded");
            }
            catch (DapSessionException ex)
            {
                _logger.LogWarning("[Attach] Step 4/9: 'configurationDone' failed (non-fatal): {Msg}", ex.Message);
            }

            // 5. Wait for thread events then pause
            //    After configurationDone, the adapter fires module/thread events as it loads.
            //    We need to drain these and find a valid thread ID before pausing.
            _logger.LogInformation("[Attach] Step 5/7: Waiting for thread events and pausing...");
            var stoppedResult = await WaitForThreadsThenPauseAsync(session, id, timeoutCts.Token);
            if (stoppedResult != null)
            {
                _logger.LogWarning("[Attach] Step 5/7: WaitForThreadsThenPause returned error");
                return stoppedResult;
            }
            _logger.LogInformation("[Attach] Step 5/7: Process paused. ActiveThreadId={ThreadId}", session.ActiveThreadId);

            // 6. Register session
            var sessionId = _registry.Register(session);
            _logger.LogInformation("[Attach] Step 6/7: Session registered as {SessionId}", sessionId);

            // 7. Auto-resolve top frame location
            _logger.LogInformation("[Attach] Step 7/7: Resolving top frame location...");
            var locationText = await GetTopFrameLocationAsync(session, cancellationToken);

            _logger.LogInformation("[Attach] SUCCESS! Session={SessionId}, PID={Pid}, Location={Location}",
                sessionId, pid, locationText);

            return CreateTextResult(id, $$"""
                {
                  "outcome": "stopped",
                  "reason": "entry",
                  "sessionId": "{{sessionId}}",
                  "threadId": {{session.ActiveThreadId ?? 0}},
                  "location": {{locationText}},
                  "message": "Debugger attached. Process is paused at entry point. Use continue_execution or step commands to proceed."
                }
                """);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[Attach] TIMEOUT after {Seconds}s. Session state={State}, ActiveThread={Thread}",
                _options.AttachTimeoutSeconds, session.State, session.ActiveThreadId);
            session.Dispose();
            return CreateTextResult(id,
                $"Debug adapter did not respond within {_options.AttachTimeoutSeconds} seconds. " +
                $"Possible causes: PID {pid} is not a .NET process, the process has already exited, " +
                $"or the adapter failed to attach. Session state was '{session.State}'. Session cleaned up.",
                isError: true);
        }
        catch (DapSessionException ex)
        {
            _logger.LogError(ex, "[Attach] DAP error during attach to PID {Pid}", pid);
            session.Dispose();
            return CreateTextResult(id, $"DAP error during attach: {ex.Message}", isError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Attach] Unexpected error during attach to PID {Pid}", pid);
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
            _logger.LogInformation("[WaitForInitialized] 'initialized' event was already received or just arrived");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[WaitForInitialized] Timed out after 5s — proceeding without 'initialized' event");
        }
    }

    /// <summary>
    /// Drains events from the channel until we see thread events, then sends pause and waits for stopped.
    /// This handles the netcoredbg flow where module/thread events arrive after configurationDone.
    /// </summary>
    private async Task<JsonNode?> WaitForThreadsThenPauseAsync(DapSession session, JsonNode? id, CancellationToken ct)
    {
        int eventsConsumed = 0;
        bool pauseSent = false;
        int? knownThreadId = null;

        // Use a short delay to let events arrive, then try pause even without thread events
        using var pauseDelayCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, pauseDelayCts.Token);

        await foreach (var evt in session.EventChannel.ReadAllAsync(ct))
        {
            eventsConsumed++;
            _logger.LogInformation("[WaitForThreadsThenPause] Event #{Count}: {EventType}", eventsConsumed, evt.EventType);

            if (evt.EventType == "stopped")
            {
                _logger.LogInformation("[WaitForThreadsThenPause] Got 'stopped' after {Count} events", eventsConsumed);
                return null; // success
            }

            if (evt.EventType == "terminated")
            {
                _logger.LogWarning("[WaitForThreadsThenPause] Process terminated");
                session.Dispose();
                return CreateTextResult(id, "Process terminated before attach completed.", isError: true);
            }

            // Track thread IDs from thread events
            if (evt.EventType == "thread" && evt.Body?["reason"]?.GetValue<string>() == "started")
            {
                var tid = evt.Body?["threadId"]?.GetValue<int>();
                if (tid.HasValue)
                {
                    knownThreadId ??= tid.Value;
                    _logger.LogInformation("[WaitForThreadsThenPause] Thread started: {ThreadId}", tid.Value);
                }
            }

            // Once we have a thread ID and haven't sent pause yet, send it
            if (knownThreadId.HasValue && !pauseSent)
            {
                pauseSent = true;
                _logger.LogInformation("[WaitForThreadsThenPause] Sending 'pause' for threadId={ThreadId}...", knownThreadId.Value);
                try
                {
                    await session.SendRequestAsync("pause", new { threadId = knownThreadId.Value }, ct);
                    _logger.LogInformation("[WaitForThreadsThenPause] 'pause' succeeded, waiting for 'stopped' event...");
                }
                catch (DapSessionException ex)
                {
                    _logger.LogWarning("[WaitForThreadsThenPause] 'pause' failed: {Msg} — will retry", ex.Message);
                    pauseSent = false; // allow retry with next thread
                    knownThreadId = null;
                }
            }
        }

        // If we exited the loop without getting stopped, try one more pause attempt
        if (!pauseSent)
        {
            _logger.LogWarning("[WaitForThreadsThenPause] No thread events received. Trying 'threads' request + pause as fallback...");
            try
            {
                var threadsResponse = await session.SendRequestAsync("threads", null, ct);
                var firstTid = threadsResponse["threads"]?[0]?["threadId"]?.GetValue<int>() ?? 1;
                _logger.LogInformation("[WaitForThreadsThenPause] Fallback: got threadId={ThreadId}, sending pause", firstTid);
                await session.SendRequestAsync("pause", new { threadId = firstTid }, ct);

                // Wait for stopped event with a short timeout
                using var stoppedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stoppedCts.CancelAfter(TimeSpan.FromSeconds(5));
                await foreach (var evt in session.EventChannel.ReadAllAsync(stoppedCts.Token))
                {
                    if (evt.EventType == "stopped") return null;
                    if (evt.EventType == "terminated")
                    {
                        session.Dispose();
                        return CreateTextResult(id, "Process terminated.", isError: true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WaitForThreadsThenPause] Fallback pause failed");
            }
        }

        _logger.LogWarning("[WaitForThreadsThenPause] Channel closed after {Count} events without 'stopped'", eventsConsumed);
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
