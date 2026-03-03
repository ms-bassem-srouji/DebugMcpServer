using System.Text.Json.Nodes;

namespace DebugMcpServer.Tools;

internal interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    JsonNode GetInputSchema();
    Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken);
}
