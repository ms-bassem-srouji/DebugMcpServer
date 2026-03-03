using System.Text.Json.Nodes;

namespace DebugMcpServer.Tools;

internal abstract class ToolBase
{
    /// <summary>Creates a successful text result response.</summary>
    protected static JsonNode CreateTextResult(JsonNode? id, string text, bool isError = false)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id?.ToJsonString() ?? "null"),
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                },
                ["isError"] = isError
            }
        };
    }

    /// <summary>Creates a JSON-RPC error response.</summary>
    protected static JsonNode CreateErrorResponse(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id?.ToJsonString() ?? "null"),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    /// <summary>Gets a required string argument, returns error result if missing.</summary>
    protected static bool TryGetString(JsonNode? arguments, string key, out string value, out string? error)
    {
        value = arguments?[key]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Required parameter '{key}' is missing or empty.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>Gets a required integer argument.</summary>
    protected static bool TryGetInt(JsonNode? arguments, string key, out int value, out string? error)
    {
        var node = arguments?[key];
        if (node == null)
        {
            value = 0;
            error = $"Required parameter '{key}' is missing.";
            return false;
        }
        value = node.GetValue<int>();
        error = null;
        return true;
    }
}
