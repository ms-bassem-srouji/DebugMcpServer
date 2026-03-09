using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Tools;

internal sealed class DotnetDumpAsyncStateTool : ToolBase, IMcpTool
{
    private readonly DotnetDumpRegistry _registry;
    private readonly ILogger<DotnetDumpAsyncStateTool> _logger;

    public string Name => "dotnet_dump_async_state";

    public string Description =>
        "Analyze async Task and state machine objects from a .NET dump. " +
        "Shows all Task objects with their status (Running/WaitingForActivation/Faulted/Completed/Canceled) " +
        "and async state machines with their current state. Essential for diagnosing async deadlocks and stuck awaits.";

    public JsonNode GetInputSchema() => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "sessionId": { "type": "string", "description": "dotnet-dump session ID" },
                "filter": { "type": "string", "description": "Filter by type name (e.g., 'MyApp' to see only your tasks). If omitted, shows all non-completed tasks." },
                "includeCompleted": { "type": "boolean", "description": "Include completed/canceled tasks (default false — shows only active/faulted)", "default": false },
                "max": { "type": "integer", "description": "Maximum tasks to return (default 50)", "default": 50 }
            },
            "required": ["sessionId"]
        }
        """)!;

    public DotnetDumpAsyncStateTool(DotnetDumpRegistry registry, ILogger<DotnetDumpAsyncStateTool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, "sessionId", out var sessionId, out var err))
            return Task.FromResult(CreateErrorResponse(id, -32602, err!));
        if (!_registry.TryGet(sessionId, out var session) || session == null)
            return Task.FromResult(CreateTextResult(id,
                $"Session '{sessionId}' not found. Use load_dotnet_dump to open a dump.", isError: true));

        var filter = arguments?["filter"]?.GetValue<string>();
        var includeCompleted = arguments?["includeCompleted"]?.GetValue<bool>() ?? false;
        var max = arguments?["max"]?.GetValue<int>() ?? 50;

        try
        {
            var tasks = new JsonArray();
            int totalMatched = 0;
            var statusCounts = new Dictionary<string, int>();

            foreach (var obj in session.Runtime.Heap.EnumerateObjects())
            {
                if (!obj.IsValid || obj.Type == null) continue;

                // Match Task types and async state machines
                var typeName = obj.Type.Name;
                bool isTask = typeName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
                    && !typeName.Contains("Factory") && !typeName.Contains("Scheduler");
                // Async state machines are compiler-generated types with names like "<MethodAsync>d__5"
                bool isStateMachine = !isTask && typeName.Contains(">d__") && typeName.Contains('<');

                if (!isTask && !isStateMachine) continue;

                if (!string.IsNullOrEmpty(filter) &&
                    !typeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                string? status = null;
                if (isTask)
                {
                    status = ReadTaskStatus(obj);
                    if (!includeCompleted && (status == "RanToCompletion" || status == "Canceled"))
                        continue;
                }

                totalMatched++;
                var statusKey = status ?? (isStateMachine ? "StateMachine" : "Unknown");
                statusCounts[statusKey] = statusCounts.GetValueOrDefault(statusKey) + 1;

                if (tasks.Count >= max) continue; // still count, don't add

                var entry = new JsonObject
                {
                    ["address"] = $"0x{obj.Address:X}",
                    ["type"] = typeName,
                    ["kind"] = isTask ? "Task" : "AsyncStateMachine"
                };

                if (status != null)
                    entry["status"] = status;

                if (isStateMachine)
                {
                    var state = ReadStateMachineState(obj);
                    if (state.HasValue)
                        entry["state"] = state.Value; // -1 = initial, -2 = completed, 0+ = await point index
                }

                // Try to read the task's action/state machine type for context
                if (isTask)
                {
                    var actionType = ReadTaskActionType(obj, session.Runtime.Heap);
                    if (actionType != null)
                        entry["asyncMethod"] = actionType;
                }

                tasks.Add(entry);
            }

            var summary = new JsonObject();
            foreach (var (key, count) in statusCounts.OrderByDescending(kv => kv.Value))
                summary[key] = count;

            var result = new JsonObject
            {
                ["totalMatched"] = totalMatched,
                ["returned"] = tasks.Count,
                ["statusSummary"] = summary,
                ["tasks"] = tasks
            };

            if (totalMatched == 0)
                result["message"] = "No async Tasks or state machines found" +
                    (includeCompleted ? "." : " (excluding completed). Set includeCompleted=true to see all.");
            if (totalMatched > max)
                result["truncated"] = $"Showing {max} of {totalMatched}. Use 'max' parameter to see more.";

            return Task.FromResult(CreateTextResult(id, result.ToJsonString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DotnetDumpAsyncState] Error");
            return Task.FromResult(CreateTextResult(id, $"Error: {ex.Message}", isError: true));
        }
    }

    private static string? ReadTaskStatus(ClrObject obj)
    {
        try
        {
            // Task.m_stateFlags contains the status bits
            var flags = obj.ReadField<int>("m_stateFlags");

            // Status is in bits 21-23 (TASK_STATE_COMPLETED_MASK)
            const int TASK_STATE_RAN_TO_COMPLETION = 0x1000000;
            const int TASK_STATE_CANCELED = 0x400000;
            const int TASK_STATE_FAULTED = 0x200000;
            const int TASK_STATE_WAITING_FOR_ACTIVATION = 0x2000000;

            if ((flags & TASK_STATE_FAULTED) != 0) return "Faulted";
            if ((flags & TASK_STATE_CANCELED) != 0) return "Canceled";
            if ((flags & TASK_STATE_RAN_TO_COMPLETION) != 0) return "RanToCompletion";
            if ((flags & TASK_STATE_WAITING_FOR_ACTIVATION) != 0) return "WaitingForActivation";

            return "Running";
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadStateMachineState(ClrObject obj)
    {
        try
        {
            // Async state machines have a field called "<>1__state"
            return obj.ReadField<int>("<>1__state");
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadTaskActionType(ClrObject taskObj, ClrHeap heap)
    {
        try
        {
            // Task.m_action holds the delegate; its _target may be the state machine
            if (!taskObj.TryReadObjectField("m_action", out var action) || !action.IsValid) return null;
            if (!action.TryReadObjectField("_target", out var target) || !target.IsValid) return null;

            var name = target.Type?.Name;
            if (name == null) return null;

            // Clean up compiler-generated names for readability
            // e.g., "MyApp.OrderService+<ProcessOrderAsync>d__5" → "MyApp.OrderService.ProcessOrderAsync"
            if (name.Contains('+') && name.Contains('>'))
            {
                var plusIdx = name.IndexOf('+');
                var ltIdx = name.IndexOf('<', plusIdx);
                var gtIdx = name.IndexOf('>', ltIdx);
                if (ltIdx >= 0 && gtIdx > ltIdx)
                {
                    var className = name[..plusIdx];
                    var methodName = name[(ltIdx + 1)..gtIdx];
                    return $"{className}.{methodName}";
                }
            }

            return name;
        }
        catch
        {
            return null;
        }
    }
}
