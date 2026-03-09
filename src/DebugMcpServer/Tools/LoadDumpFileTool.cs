using System.Diagnostics;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class LoadDumpFileTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly DebugOptions _options;
    private readonly ILogger<LoadDumpFileTool> _logger;

    public string Name => "load_dump_file";

    public string Description =>
        "Load a crash dump or core dump file for post-mortem debugging. Returns a sessionId for inspection commands " +
        "(get_callstack, get_variables, evaluate_expression, read_memory, disassemble, etc.). " +
        "Execution control (continue, step) is not available for dump sessions. " +
        "Requires an adapter that supports dump files (e.g., cpp, lldb, cppvsdbg).";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "dumpPath": {
                    "type": "string",
                    "description": "Path to the dump file (e.g., core.12345, app.dmp, core). No file extension restrictions — Linux core dumps, Windows .dmp, macOS Mach-O cores are all supported."
                },
                "program": {
                    "type": "string",
                    "description": "Path to the original executable for symbol resolution. Required by most adapters for meaningful stack traces."
                },
                "adapter": {
                    "type": "string",
                    "description": "Name of the configured adapter to use (must support dump files — see list_adapters). If omitted, uses the first adapter with dump support."
                },
                "adapterPath": {
                    "type": "string",
                    "description": "Optional explicit path to the debug adapter executable. Overrides adapter name lookup."
                },
                "sourceMapping": {
                    "type": "object",
                    "description": "Source path remapping (e.g., {\"/build/src\": \"/local/src\"}) for when source was built on a different machine."
                },
                "host": {
                    "type": "string",
                    "description": "SSH host for remote dump debugging (e.g., 'user@hostname'). The dump file and adapter must exist on the remote machine."
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
            "required": ["dumpPath"]
        }
        """)!;

    public LoadDumpFileTool(
        DapSessionRegistry registry,
        IOptions<DebugOptions> options,
        ILogger<LoadDumpFileTool> logger)
    {
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "dumpPath", out var dumpPath, out var dumpError))
            return CreateErrorResponse(id, -32602, dumpError!);

        var program = arguments?["program"]?.GetValue<string>();
        var adapterName = arguments?["adapter"]?.GetValue<string>();
        var adapterPath = arguments?["adapterPath"]?.GetValue<string>();
        var sourceMapping = arguments?["sourceMapping"] as JsonObject;
        var sshHost = arguments?["host"]?.GetValue<string>();
        var sshPort = arguments?["sshPort"]?.GetValue<int>() ?? 22;
        var sshKey = arguments?["sshKey"]?.GetValue<string>();
        var remoteAdapterPath = arguments?["remoteAdapterPath"]?.GetValue<string>();
        bool isRemote = !string.IsNullOrWhiteSpace(sshHost);

        // Resolve adapter — must have DumpArgumentName
        string? resolvedPath = null;
        string? resolvedAdapterID = null;
        AdapterConfig? resolvedConfig = null;
        string? dumpArgumentName = null;

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
            dumpArgumentName = resolvedConfig.DumpArgumentName;
        }
        else if (!string.IsNullOrWhiteSpace(adapterPath))
        {
            // Explicit path — caller must know what they're doing; we need a DumpArgumentName
            // Try to find a matching adapter config by path for the DumpArgumentName
            resolvedConfig = _options.Adapters.FirstOrDefault(a =>
                string.Equals(a.Path, adapterPath, StringComparison.OrdinalIgnoreCase));
            resolvedPath = adapterPath;
            resolvedAdapterID = resolvedConfig?.AdapterID;
            dumpArgumentName = resolvedConfig?.DumpArgumentName;
        }
        else
        {
            // Auto-select first adapter with dump support
            resolvedConfig = _options.Adapters.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.DumpArgumentName));
            if (resolvedConfig != null)
            {
                resolvedPath = resolvedConfig.Path;
                resolvedAdapterID = resolvedConfig.AdapterID;
                dumpArgumentName = resolvedConfig.DumpArgumentName;
                _logger.LogInformation("[LoadDump] Auto-selected adapter '{Name}' (first with dump support)", resolvedConfig.Name);
            }
        }

        if (string.IsNullOrWhiteSpace(dumpArgumentName))
        {
            var dumpCapable = _options.Adapters
                .Where(a => !string.IsNullOrWhiteSpace(a.DumpArgumentName))
                .Select(a => a.Name);
            var dumpList = string.Join(", ", dumpCapable);
            var hint = dumpList.Length > 0
                ? $" Adapters with dump support: [{dumpList}]."
                : " No adapters with dump support are configured. Add DumpArgumentName to adapter config.";
            return CreateTextResult(id,
                $"The selected adapter does not support dump file debugging (no DumpArgumentName configured).{hint}",
                isError: true);
        }

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return CreateTextResult(id,
                "No adapter path resolved. Provide 'adapter' name or 'adapterPath' parameter.",
                isError: true);
        }

        // For remote debugging, resolve the remote adapter path and skip local validation
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

            resolvedPath = effectiveRemotePath;
            _logger.LogInformation("[LoadDump] Remote dump debugging via SSH to {Host}. Adapter={Path}", sshHost, effectiveRemotePath);
        }
        else
        {
            // Local: validate dump file exists
            if (!File.Exists(dumpPath))
            {
                return CreateTextResult(id,
                    $"Dump file not found: '{dumpPath}'. Verify the path exists and is accessible.",
                    isError: true);
            }
        }

        resolvedAdapterID ??= "coreclr";
        _logger.LogInformation("[LoadDump] Using adapter at {AdapterPath} (adapterID={AdapterID}) for dump {DumpPath}",
            resolvedPath, resolvedAdapterID, dumpPath);

        // Launch debug adapter in DAP mode
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
            _logger.LogInformation("[LoadDump] Launching adapter: {Path} --interpreter=vscode", resolvedPath);
            adapterProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start debug adapter process");
            _logger.LogInformation("[LoadDump] Adapter launched. PID={AdapterPid}", adapterProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoadDump] Failed to launch debug adapter");
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
        session.IsDumpSession = true;
        session.StartReaderLoop();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            new CancellationTokenSource(TimeSpan.FromSeconds(_options.AttachTimeoutSeconds)).Token);

        try
        {
            // 1. Initialize
            _logger.LogInformation("[LoadDump] Step 1/5: Sending 'initialize'...");
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
            _logger.LogInformation("[LoadDump] Step 1/5: 'initialize' complete");

            // 2. Launch with dump file
            _logger.LogInformation("[LoadDump] Step 2/5: Sending 'launch' with dump={DumpPath}...", dumpPath);
            var launchArgs = new Dictionary<string, object?>
            {
                [dumpArgumentName] = dumpPath,
                ["justMyCode"] = false
            };
            if (!string.IsNullOrWhiteSpace(program))
                launchArgs["program"] = program;
            if (sourceMapping is { Count: > 0 })
                launchArgs["sourceFileMap"] = sourceMapping;

            await session.SendRequestAsync("launch", launchArgs, timeoutCts.Token);
            _logger.LogInformation("[LoadDump] Step 2/5: 'launch' complete");

            // 3. Wait for 'initialized' event
            _logger.LogInformation("[LoadDump] Step 3/5: Waiting for 'initialized' event (5s timeout)...");
            await WaitForInitializedAsync(session, timeoutCts.Token);
            _logger.LogInformation("[LoadDump] Step 3/5: 'initialized' done");

            // 4. configurationDone
            _logger.LogInformation("[LoadDump] Step 4/5: Sending 'configurationDone'...");
            try
            {
                await session.SendRequestAsync("configurationDone", null, timeoutCts.Token);
                _logger.LogInformation("[LoadDump] Step 4/5: 'configurationDone' succeeded");
            }
            catch (DapSessionException ex)
            {
                _logger.LogWarning("[LoadDump] Step 4/5: 'configurationDone' failed (non-fatal): {Msg}", ex.Message);
            }

            // 5. Wait for 'stopped' event (some adapters fire "stopped" when loading a dump, others don't)
            _logger.LogInformation("[LoadDump] Step 5/5: Waiting for 'stopped' event (5s timeout)...");
            using var stoppedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            try
            {
                var stoppedResult = await WaitForStoppedAsync(session, id, stoppedCts.Token);
                if (stoppedResult != null)
                {
                    _logger.LogWarning("[LoadDump] Step 5/5: WaitForStopped returned error");
                    return stoppedResult;
                }
                _logger.LogInformation("[LoadDump] Step 5/5: Dump loaded. ActiveThreadId={ThreadId}", session.ActiveThreadId);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // No 'stopped' event within 5s — this is normal for some adapters (e.g., vsdbg)
                // The dump is already loaded and ready for inspection after configurationDone
                _logger.LogInformation("[LoadDump] Step 5/5: No 'stopped' event received (normal for some adapters). Proceeding.");
            }

            // 6. Register session
            var sessionId = _registry.Register(session);
            _logger.LogInformation("[LoadDump] Session registered as {SessionId}", sessionId);

            // 7. Auto-resolve top frame location
            var locationText = await GetTopFrameLocationAsync(session, cancellationToken);

            _logger.LogInformation("[LoadDump] SUCCESS! Session={SessionId}, Dump={DumpPath}, Location={Location}",
                sessionId, dumpPath, locationText);

            return CreateTextResult(id, $$"""
                {
                  "outcome": "stopped",
                  "reason": "dump_loaded",
                  "sessionId": "{{sessionId}}",
                  "threadId": {{session.ActiveThreadId ?? 0}},
                  "isDumpSession": true,
                  "location": {{locationText}},
                  "message": "Dump file loaded. Use get_callstack, get_variables, evaluate_expression, read_memory, disassemble, list_threads to inspect. Execution control (continue/step) is NOT available for dump sessions."
                }
                """);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[LoadDump] TIMEOUT after {Seconds}s", _options.AttachTimeoutSeconds);
            session.Dispose();
            return CreateTextResult(id,
                $"Debug adapter did not respond within {_options.AttachTimeoutSeconds} seconds. " +
                $"The adapter may not support dump file debugging, or the dump file '{dumpPath}' may be invalid. Session cleaned up.",
                isError: true);
        }
        catch (DapSessionException ex)
        {
            _logger.LogError(ex, "[LoadDump] DAP error loading dump {DumpPath}", dumpPath);
            session.Dispose();
            return CreateTextResult(id, $"DAP error loading dump: {DapErrorHelper.Humanize("launch", ex.Message)}", isError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoadDump] Unexpected error loading dump {DumpPath}", dumpPath);
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
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[LoadDump] Timed out waiting for 'initialized' event — proceeding");
        }
    }

    private async Task<JsonNode?> WaitForStoppedAsync(DapSession session, JsonNode? id, CancellationToken ct)
    {
        await foreach (var evt in session.EventChannel.ReadAllAsync(ct))
        {
            _logger.LogInformation("[LoadDump] Event: {EventType}", evt.EventType);

            if (evt.EventType == "stopped")
                return null; // success

            if (evt.EventType == "terminated")
            {
                session.Dispose();
                return CreateTextResult(id, "Debug adapter terminated while loading dump file.", isError: true);
            }
        }

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
}
