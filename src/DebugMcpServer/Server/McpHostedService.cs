using System.Reflection;
using DebugMcpServer.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DebugMcpServer.Server;

/// <summary>
/// Hosted service that runs the MCP server using stdio transport.
/// </summary>
internal sealed class McpHostedService : IHostedService
{
    private readonly ILogger<McpHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IEnumerable<IMcpTool> _tools;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task? _runTask;

    internal static readonly string ServerVersion =
        typeof(McpHostedService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

    public McpHostedService(
        ILogger<McpHostedService> logger,
        IHostApplicationLifetime lifetime,
        IEnumerable<IMcpTool> tools)
    {
        _logger = logger;
        _lifetime = lifetime;
        _tools = tools;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Server starting on stdio transport");
        _runTask = Task.Run(() => RunServerAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        await RunServerAsync(stdin, stdout, cancellationToken);
    }

    internal async Task RunServerAsync(Stream stdin, Stream stdout, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(stdin, leaveOpen: true);
            using var writer = new StreamWriter(stdout, leaveOpen: true) { AutoFlush = true };

            _logger.LogInformation("MCP Server v{Version} ready — listening on stdin/stdout (concurrent request processing enabled)", ServerVersion);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonNode? request;
                try
                {
                    request = JsonNode.Parse(line);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Ignoring malformed JSON-RPC message: {Error}", ex.Message);
                    continue;
                }

                if (request == null)
                {
                    continue;
                }

                // Dispatch each request concurrently — responses are serialized via _writeLock
                _ = ProcessRequestAsync(request, writer, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("MCP Server shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Server error");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    internal async Task ProcessRequestAsync(JsonNode request, StreamWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            var response = await HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
            var responseJson = response.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

            // Serialize writes to stdout — only one response at a time
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Server shutting down — ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MCP request");

            // Send error response so the client isn't left hanging
            try
            {
                var id = request["id"];
                var errorResponse = CreateErrorResponse(id, -32603, $"Internal error: {ex.Message}");
                var errorJson = errorResponse.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

                await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await writer.WriteLineAsync(errorJson).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch
            {
                // Last resort: don't crash the server trying to report an error
            }
        }
    }

    internal async Task<JsonNode> HandleRequestAsync(JsonNode request, CancellationToken cancellationToken)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"];

        _logger.LogDebug("Received MCP request: {Method}", method);

        return method switch
        {
            "initialize" => HandleInitialize(id),
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolsCallAsync(id, request["params"], cancellationToken),
            "resources/list" => HandleEmptyList(id, "resources"),
            "prompts/list" => HandleEmptyList(id, "prompts"),
            _ => CreateErrorResponse(id, -32601, $"Method not found: {method}")
        };
    }

    private static readonly string ServerInstructions = """
        ## Debug MCP Server

        This server provides interactive debugging using the Debug Adapter Protocol (DAP).
        It supports multiple debug adapters (e.g., netcoredbg for .NET, debugpy for Python) configured in appsettings.json.

        ### Typical Workflow

        1. **Check available adapters**: Use `list_adapters` to see which debug adapters are configured.
        2. **Start debugging**:
           - **Attach to running process**: Use `list_processes` to find the PID, then `attach_to_process`.
           - **Launch a new process**: Use `launch_process` with the program path to start under the debugger from the first line.
           Both return a `sessionId` — all subsequent calls require it. The process is paused after attach/launch.
        3. **Set breakpoints** (can be done before or after resuming):
           - `set_breakpoint` — by file and line, with optional `condition` and `hitCount`.
           - `set_function_breakpoints` — by function name (e.g., "RunTask").
           - `set_exception_breakpoints` — break on thrown/unhandled exceptions (filters: "all", "unhandled", "thrown").
           - `set_data_breakpoint` — break when a variable's value changes (watchpoint).
           - `list_breakpoints` — see all active breakpoints.
        4. **Resume execution**: Call `continue_execution`. Blocks up to 25s waiting for the next stop.
        5. **Inspect state when stopped**:
           - `get_source` — view source code around the current stop location with line numbers.
           - `get_callstack` — stack frames with `id`, `name`, `source`, `line`. Frame IDs change after every step/continue.
           - `get_variables` with `frameId` — locals grouped by scope. Expandable variables have `variablesReference > 0`.
           - `get_variables` with `variablesReference` — expand nested objects/arrays.
           - `evaluate_expression` — evaluate any expression in the current frame context.
           - `list_threads` — shows all threads. Use `change_thread` to switch before inspecting.
           - `get_modules` — loaded modules/assemblies with version and symbol status.
           - `read_memory` / `write_memory` — read/write raw bytes at a memory address.
        6. **Step through code**: `step_over`, `step_in`, `step_out`. Each returns the new stopped location.
        7. **Modify state**: `set_variable` changes a variable's value while paused.
        8. **Detach**: Call `detach_session` to disconnect. The target process continues running.

        ### .NET Debugging

        - **Launch .NET apps**: Pass the `.dll` path directly as `program` (e.g., `bin/Debug/net8.0/MyApp.dll`). Do NOT use `dotnet` as the program with the DLL as args — netcoredbg expects the DLL directly.
        - **Debug .NET unit tests**: Use `launch_process` with the test DLL path as `program` (e.g., `bin/Debug/net8.0/MyTests.dll`). The test framework entry point in the DLL will execute all tests. To run specific tests, the DLL must be launched by the test runner — see the `attach_to_process` workflow below.
        - **Debug specific tests via attach**: Set env var `VSTEST_HOST_DEBUG=1`, run `dotnet test --filter "TestName"`. The test runner prints `Process Id: <PID>`. Use `attach_to_process` with that PID. **Important**: set breakpoints AFTER attaching and BEFORE calling `continue_execution` — breakpoints set during attach resolve only after assemblies are loaded. If breakpoints show as `verified: false`, call `continue_execution` once to let the runtime load assemblies, then re-set the breakpoints.
        - **Breakpoints not verified?**: This means the debug adapter hasn't loaded the assembly containing that source file yet. Common causes: (1) setting breakpoints before `continue_execution` when using `attach_to_process`, (2) the PDB source paths don't match the local file paths. Verify PDB paths match your local source tree.

        ### Important Notes

        - **Launch vs Attach**: Prefer `launch_process` over `attach_to_process` when possible — breakpoints are more reliable because the debugger controls module loading from the start. Use `attach_to_process` only when you need to debug a process you can't launch directly (e.g., a service, or a test host spawned by `dotnet test`).
        - **Multiple adapters**: Use `list_adapters` to see configured adapters, pass `adapter: "<name>"` to select one.
        - **Multiple sessions**: Use `list_sessions` to see all active debug sessions. Requests are processed concurrently — you can debug multiple processes in parallel.
        - **Breakpoint types**: Line breakpoints (`set_breakpoint`), function breakpoints (`set_function_breakpoints`), exception breakpoints (`set_exception_breakpoints`), and data/watch breakpoints (`set_data_breakpoint`).
        - **Source view**: `get_source` shows code around the current location — use it after each stop for context.
        - **Frame IDs are ephemeral**: Always call `get_callstack` after each stop to get fresh frame IDs.
        - **Remote debugging via SSH**: Pass `host: "user@hostname"` to `list_processes`, `attach_to_process`, or `launch_process` to debug on a remote machine. The DAP protocol is piped through SSH. Optional `sshPort`, `sshKey`, and `remoteAdapterPath` params available.
        - **`pause_execution`**: Break into a running process at any time.
        - **`get_pending_events`**: Poll for events after `continue_execution` returns 'running'.
        - **`send_dap_request`**: Escape hatch for any DAP command not covered by dedicated tools.
        """;

    private JsonNode HandleInitialize(JsonNode? id)
    {
        _logger.LogInformation("Client initialized MCP connection");

        return JsonNode.Parse($$"""
        {
            "jsonrpc": "2.0",
            "id": {{id?.ToJsonString() ?? "null"}},
            "result": {
                "protocolVersion": "2024-11-05",
                "capabilities": {
                    "tools": {}
                },
                "serverInfo": {
                    "name": "debug-mcp",
                    "version": "{{ServerVersion}}"
                },
                "instructions": {{JsonSerializer.Serialize(ServerInstructions)}}
            }
        }
        """)!;
    }

    private JsonNode HandleToolsList(JsonNode? id)
    {
        var toolsArray = new JsonArray();

        foreach (var tool in _tools)
        {
            toolsArray.Add(new JsonObject
            {
                ["name"] = JsonValue.Create(tool.Name),
                ["description"] = JsonValue.Create(tool.Description),
                ["inputSchema"] = JsonNode.Parse(tool.GetInputSchema().ToJsonString())
            });
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id?.ToJsonString() ?? "null"),
            ["result"] = new JsonObject
            {
                ["tools"] = toolsArray
            }
        };
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonNode? id, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var toolName = parameters?["name"]?.GetValue<string>();
        var arguments = parameters?["arguments"];

        var tool = _tools.FirstOrDefault(t => t.Name == toolName);
        if (tool != null)
        {
            return await tool.ExecuteAsync(id, arguments, cancellationToken);
        }

        return CreateErrorResponse(id, -32602, $"Unknown tool: {toolName}");
    }

    private static JsonNode HandleEmptyList(JsonNode? id, string listKey)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id?.ToJsonString() ?? "null"),
            ["result"] = new JsonObject
            {
                [listKey] = new JsonArray()
            }
        };
    }

    private static JsonNode CreateErrorResponse(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id?.ToJsonString() ?? "null"),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = JsonValue.Create(message)
            }
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Server stopping");
        return _runTask ?? Task.CompletedTask;
    }
}
