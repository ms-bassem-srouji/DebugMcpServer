namespace DebugMcpServer.Dap;

internal sealed record SourceBreakpoint(string Source, int Line, string? Condition = null, string? HitCondition = null);
