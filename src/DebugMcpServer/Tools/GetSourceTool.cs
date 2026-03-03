using System.Text;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class GetSourceTool : ToolBase, IMcpTool
{
    private readonly DapSessionRegistry _registry;
    private readonly ILogger<GetSourceTool> _logger;

    public string Name => "get_source";
    public string Description => "Read source code around a given line. If file/line are omitted, auto-resolves from the active thread's current stop location (requires paused session).";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "Debug session ID" },
                "file": { "type": "string", "description": "Source file path. If omitted, resolved from the current frame." },
                "line": { "type": "integer", "description": "Line number to center on. If omitted, resolved from the current frame." },
                "linesAround": { "type": "integer", "description": "Number of lines to show before and after the current line (default 10).", "default": 10 }
            },
            "required": ["sessionId"]
        }
        """)!;

    public GetSourceTool(DapSessionRegistry registry, ILogger<GetSourceTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return CreateErrorResponse(id, -32602, err!);
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return CreateTextResult(id, $"Session '{sessionId}' not found.", isError: true);

        var file = arguments?["file"]?.GetValue<string>();
        var lineNode = arguments?["line"];
        int? line = lineNode != null ? lineNode.GetValue<int>() : null;
        var linesAround = arguments?["linesAround"]?.GetValue<int>() ?? 10;
        linesAround = Math.Clamp(linesAround, 0, 200);

        // Auto-resolve file/line from current frame if not provided
        if (string.IsNullOrWhiteSpace(file) || line == null)
        {
            try
            {
                var threadId = session.ActiveThreadId ?? 1;
                var response = await session.SendRequestAsync("stackTrace", new
                {
                    threadId,
                    startFrame = 0,
                    levels = 1
                }, cancellationToken);

                var frames = response["stackFrames"] as JsonArray;
                if (frames == null || frames.Count == 0)
                    return CreateTextResult(id, "No stack frames available. Is the process paused?", isError: true);

                var topFrame = frames[0]!;
                file ??= topFrame["source"]?["path"]?.GetValue<string>();
                line ??= topFrame["line"]?.GetValue<int>();

                if (string.IsNullOrWhiteSpace(file))
                    return CreateTextResult(id, "Current frame has no source file information.", isError: true);
                if (line == null)
                    return CreateTextResult(id, "Current frame has no line information.", isError: true);
            }
            catch (DapSessionException ex)
            {
                return CreateTextResult(id, DapErrorHelper.Humanize("stackTrace", ex.Message), isError: true);
            }
        }

        // Read the source file
        string[] allLines;
        try
        {
            allLines = await File.ReadAllLinesAsync(file!, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return CreateTextResult(id, $"Source file not found: {file}", isError: true);
        }
        catch (IOException ex)
        {
            return CreateTextResult(id, $"Cannot read source file '{file}': {ex.Message}", isError: true);
        }

        var currentLine = line!.Value;
        var startLine = Math.Max(1, currentLine - linesAround);
        var endLine = Math.Min(allLines.Length, currentLine + linesAround);

        var sb = new StringBuilder();
        var lineNumWidth = endLine.ToString().Length;

        for (var i = startLine; i <= endLine; i++)
        {
            var prefix = i == currentLine ? ">>>" : "   ";
            var lineNum = i.ToString().PadLeft(lineNumWidth);
            sb.AppendLine($"{prefix} {lineNum} | {allLines[i - 1]}");
        }

        var result = new JsonObject
        {
            ["file"] = file,
            ["currentLine"] = currentLine,
            ["startLine"] = startLine,
            ["endLine"] = endLine,
            ["source"] = sb.ToString()
        };

        return CreateTextResult(id, result.ToJsonString());
    }
}
