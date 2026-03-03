using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class WriteMemoryTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<WriteMemoryTool> _logger;

    public string Name => "write_memory";

    public string Description =>
        "Write raw bytes to a memory address. Provide data as a hex string (e.g., '48656C6C6F') " +
        "or base64. Use with caution — writing to invalid addresses can crash the target process.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "memoryReference": { "type": "string", "description": "Memory reference string (from a variable's memoryReference field, or a hex address)" },
                "offset": { "type": "integer", "description": "Byte offset from the memory reference (default 0)", "default": 0 },
                "data": { "type": "string", "description": "Data to write as a hex string (e.g., '4142FF00') or base64 string" },
                "encoding": { "type": "string", "description": "Encoding of the data field: 'hex' (default) or 'base64'", "default": "hex" }
            },
            "required": ["sessionId", "memoryReference", "data"]
        }
        """)!;

    public WriteMemoryTool(DapSessionRegistry registry, ILogger<WriteMemoryTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!TryGetString(arguments, "memoryReference", out var memRef, out var memErr))
            return CreateErrorResponse(id, -32602, memErr!);
        if (!TryGetString(arguments, "data", out var dataStr, out var dataErr))
            return CreateErrorResponse(id, -32602, dataErr!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);
        if (session.State != SessionState.Paused)
            return CreateTextResult(id, "Cannot write memory while the process is running. Use pause_execution first.", isError: true);

        var offset = arguments?["offset"]?.GetValue<int>() ?? 0;
        var encoding = arguments?["encoding"]?.GetValue<string>() ?? "hex";

        // Convert data to base64 (DAP requires base64)
        string base64Data;
        try
        {
            if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                base64Data = dataStr;
                // Validate it's valid base64
                Convert.FromBase64String(base64Data);
            }
            else
            {
                // Parse hex string to bytes then to base64
                var hexClean = dataStr.Replace(" ", "").Replace("-", "");
                var bytes = Convert.FromHexString(hexClean);
                base64Data = Convert.ToBase64String(bytes);
            }
        }
        catch (FormatException ex)
        {
            return CreateTextResult(id, $"Invalid data format: {ex.Message}. For hex encoding, provide pairs of hex digits (e.g., '48656C6C6F').", isError: true);
        }

        try
        {
            var response = await session.SendRequestAsync("writeMemory", new
            {
                memoryReference = memRef,
                offset,
                data = base64Data
            }, cancellationToken);

            var bytesWritten = response["bytesWritten"]?.GetValue<int>() ?? 0;

            var result = new JsonObject
            {
                ["address"] = memRef,
                ["offset"] = offset,
                ["bytesWritten"] = bytesWritten
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, DapErrorHelper.Humanize("writeMemory", ex.Message), isError: true);
        }
    }
}
