using System.Text.Json.Nodes;
using DebugMcpServer.Options;
using Microsoft.Extensions.Options;

namespace DebugMcpServer.Tools;

internal sealed class ListAdaptersTool : ToolBase, IMcpTool
{
    private readonly DebugOptions _options;

    public string Name => "list_adapters";

    public string Description =>
        "List all configured debug adapters. Use the adapter name with attach_to_process to select which adapter to use.";

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
        foreach (var adapter in _options.Adapters)
        {
            var entry = new JsonObject
            {
                ["name"] = adapter.Name,
                ["path"] = adapter.Path
            };
            if (!string.IsNullOrEmpty(adapter.AdapterID))
                entry["adapterID"] = adapter.AdapterID;

            adapters.Add(entry);
        }

        var result = new JsonObject
        {
            ["adapters"] = adapters,
            ["count"] = adapters.Count
        };

        return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
    }
}
