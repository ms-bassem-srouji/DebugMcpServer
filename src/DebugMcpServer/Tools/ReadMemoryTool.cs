using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class ReadMemoryTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<ReadMemoryTool> _logger;

    public string Name => "read_memory";

    public string Description =>
        "Read raw bytes from a memory address. Use the memoryReference from a variable's details " +
        "(obtained via get_variables) or a hex address string. Returns base64-encoded data and a hex dump.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "memoryReference": { "type": "string", "description": "Memory reference string (from a variable's memoryReference field, or a hex address like '0x7FFE4A3B1000')" },
                "offset": { "type": "integer", "description": "Byte offset from the memory reference (default 0)", "default": 0 },
                "count": { "type": "integer", "description": "Number of bytes to read (default 64, max 4096)", "default": 64 }
            },
            "required": ["sessionId", "memoryReference"]
        }
        """)!;

    public ReadMemoryTool(DapSessionRegistry registry, ILogger<ReadMemoryTool> logger)
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
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);
        if (session.State != SessionState.Paused)
            return CreateTextResult(id, "Cannot read memory while the process is running. Use pause_execution first.", isError: true);

        var offset = arguments?["offset"]?.GetValue<int>() ?? 0;
        var count = Math.Clamp(arguments?["count"]?.GetValue<int>() ?? 64, 1, 4096);

        try
        {
            var response = await session.SendRequestAsync("readMemory", new
            {
                memoryReference = memRef,
                offset,
                count
            }, cancellationToken);

            var address = response["address"]?.GetValue<string>() ?? memRef;
            var data = response["data"]?.GetValue<string>() ?? "";
            var unreadableBytes = response["unreadableBytes"]?.GetValue<int>() ?? 0;

            // Decode base64 to produce hex dump
            var bytes = Convert.FromBase64String(data);
            var hexDump = FormatHexDump(bytes, address);

            var result = new JsonObject
            {
                ["address"] = address,
                ["bytesRead"] = bytes.Length,
                ["unreadableBytes"] = unreadableBytes,
                ["data"] = data,
                ["hexDump"] = hexDump
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, DapErrorHelper.Humanize("readMemory", ex.Message), isError: true);
        }
    }

    private static string FormatHexDump(byte[] bytes, string baseAddress)
    {
        if (bytes.Length == 0) return "(empty)";

        // Try to parse the base address for offset display
        long baseAddr = 0;
        if (baseAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            long.TryParse(baseAddress[2..], System.Globalization.NumberStyles.HexNumber, null, out baseAddr);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < bytes.Length; i += 16)
        {
            var lineAddr = baseAddr + i;
            sb.Append($"  {lineAddr:X8}  ");

            // Hex bytes
            for (int j = 0; j < 16; j++)
            {
                if (i + j < bytes.Length)
                    sb.Append($"{bytes[i + j]:X2} ");
                else
                    sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }

            sb.Append(" |");
            // ASCII
            for (int j = 0; j < 16 && i + j < bytes.Length; j++)
            {
                var b = bytes[i + j];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }
            sb.AppendLine("|");
        }
        return sb.ToString().TrimEnd();
    }
}
