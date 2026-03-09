using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DisassembleTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<DisassembleTool> _logger;

    public string Name => "disassemble";

    public string Description =>
        "Disassemble machine code at a memory address. Essential for crash dump analysis when source code is unavailable. " +
        "Use a memoryReference from get_callstack (instruction pointer) or get_variables, or a hex address string.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "memoryReference": { "type": "string", "description": "Memory reference (hex address like '0x7FFE4A3B1000', or from a stack frame's instructionPointerReference)" },
                "instructionCount": { "type": "integer", "description": "Number of instructions to disassemble (default 20, max 200)", "default": 20 },
                "offset": { "type": "integer", "description": "Byte offset from the memory reference (default 0)", "default": 0 },
                "instructionOffset": { "type": "integer", "description": "Offset in instructions (negative = before, positive = after the reference). Use -10 to see 10 instructions before the crash site.", "default": 0 }
            },
            "required": ["sessionId", "memoryReference"]
        }
        """)!;

    public DisassembleTool(DapSessionRegistry registry, ILogger<DisassembleTool> logger)
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
            return CreateTextResult(id, "Cannot disassemble while the process is running. Use pause_execution first.", isError: true);

        var instructionCount = Math.Clamp(arguments?["instructionCount"]?.GetValue<int>() ?? 20, 1, 200);
        var offset = arguments?["offset"]?.GetValue<int>() ?? 0;
        var instructionOffset = arguments?["instructionOffset"]?.GetValue<int>() ?? 0;

        try
        {
            var response = await session.SendRequestAsync("disassemble", new
            {
                memoryReference = memRef,
                offset,
                instructionOffset,
                instructionCount,
                resolveSymbols = true
            }, cancellationToken);

            var instructions = response["instructions"] as JsonArray;
            if (instructions == null || instructions.Count == 0)
            {
                return CreateTextResult(id, new JsonObject
                {
                    ["address"] = memRef,
                    ["instructionCount"] = 0,
                    ["message"] = "No instructions returned. The adapter may not support disassembly, or the address is invalid."
                }.ToJsonString());
            }

            var formatted = new JsonArray();
            foreach (var instr in instructions)
            {
                if (instr == null) continue;
                var entry = new JsonObject
                {
                    ["address"] = instr["address"]?.DeepClone(),
                    ["instruction"] = instr["instruction"]?.DeepClone()
                };

                // Include source location if available
                var source = instr["location"];
                if (source != null)
                {
                    entry["source"] = source["path"]?.DeepClone() ?? source["name"]?.DeepClone();
                    entry["line"] = instr["line"]?.DeepClone();
                }

                // Include symbol if resolved
                var symbol = instr["symbol"];
                if (symbol != null)
                    entry["symbol"] = symbol.DeepClone();

                formatted.Add(entry);
            }

            var result = new JsonObject
            {
                ["address"] = memRef,
                ["instructionCount"] = formatted.Count,
                ["instructions"] = formatted
            };
            return CreateTextResult(id, result.ToJsonString());
        }
        catch (DapSessionException ex)
        {
            return CreateTextResult(id, DapErrorHelper.Humanize("disassemble", ex.Message), isError: true);
        }
    }
}
