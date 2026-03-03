namespace DebugMcpServer.Dap;

internal enum SessionState
{
    Initializing,
    Running,
    Paused,
    Terminating,
    Terminated
}
