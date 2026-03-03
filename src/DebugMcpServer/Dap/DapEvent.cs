using System.Text.Json.Nodes;

namespace DebugMcpServer.Dap;

internal sealed record DapEvent(string EventType, JsonNode? Body);
