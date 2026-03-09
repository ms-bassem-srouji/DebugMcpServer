using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpInspectTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpInspectTool> _logger;

    public string Name => "dotnet_dump_inspect";

    public string Description =>
        "Inspect a .NET object at a given address from a dump. Shows type, size, and field values. " +
        "Equivalent to SOS 'dumpobj'. Get addresses from dotnet_dump_threads (stack objects) or dotnet_dump_heap_stats.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" },
                "address": { "type": "string", "description": "Object address (hex, e.g., '0x7fff12340000')" }
            },
            "required": ["sessionId", "address"]
        }
        """)!;

    public DotnetDumpInspectTool(DotnetDumpRegistry registry, ILogger<DotnetDumpInspectTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return Task.FromResult(CreateErrorResponse(id, -32602, err!));
        if (!TryGetString(arguments, "address", out var addressStr, out var addrErr))
            return Task.FromResult(CreateErrorResponse(id, -32602, addrErr!));
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return Task.FromResult(CreateTextResult(id,
                $"Session '{sessionId}' not found. Use load_dotnet_dump to open a dump.", isError: true));

        if (!TryParseAddress(addressStr, out var address))
            return Task.FromResult(CreateTextResult(id,
                $"Invalid address: '{addressStr}'. Use hex format like '0x7fff12340000'.", isError: true));

        try
        {
            var heap = session.Runtime.Heap;
            var obj = heap.GetObject(address);

            if (!obj.IsValid || obj.Type == null)
                return Task.FromResult(CreateTextResult(id,
                    $"No valid .NET object at address {addressStr}.", isError: true));

            var result = new JsonObject
            {
                ["address"] = $"0x{address:X}",
                ["type"] = obj.Type.Name,
                ["size"] = (long)obj.Size,
                ["isArray"] = obj.Type.IsArray,
                ["methodTable"] = $"0x{obj.Type.MethodTable:X}"
            };

            if (obj.Type.IsString)
            {
                result["value"] = obj.AsString();
            }
            else if (obj.Type.IsArray)
            {
                var arr = obj.AsArray();
                result["arrayLength"] = arr.Length;
                var elements = new JsonArray();
                var maxElements = Math.Min(arr.Length, 20);
                for (int i = 0; i < maxElements; i++)
                {
                    try
                    {
                        var elem = arr.GetObjectValue(i);
                        elements.Add(elem.IsValid
                            ? $"0x{elem.Address:X} ({elem.Type?.Name})"
                            : "null");
                    }
                    catch
                    {
                        elements.Add("<unreadable>");
                    }
                }
                result["elements"] = elements;
                if (arr.Length > 20)
                    result["truncated"] = true;
            }
            else
            {
                var fields = new JsonArray();
                foreach (var field in obj.Type.Fields)
                {
                    var fieldObj = new JsonObject
                    {
                        ["name"] = field.Name,
                        ["type"] = field.Type?.Name ?? "unknown",
                        ["offset"] = field.Offset
                    };

                    try
                    {
                        if (field.IsObjectReference)
                        {
                            var refObj = field.ReadObject(address, false);
                            fieldObj["value"] = refObj.IsValid
                                ? (refObj.Type?.IsString == true
                                    ? refObj.AsString()
                                    : $"0x{refObj.Address:X}")
                                : "null";
                        }
                        else
                        {
                            var val = field.Read<long>(address, false);
                            fieldObj["value"] = val;
                        }
                    }
                    catch
                    {
                        fieldObj["value"] = "<unreadable>";
                    }

                    fields.Add(fieldObj);
                }
                result["fields"] = fields;
            }

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpInspect] Error inspecting {Address}", addressStr);
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }

    internal static bool TryParseAddress(string input, out ulong address)
    {
        input = input.Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            input = input[2..];
        return ulong.TryParse(input, System.Globalization.NumberStyles.HexNumber, null, out address);
    }
}
