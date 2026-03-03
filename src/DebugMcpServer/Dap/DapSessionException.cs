namespace DebugMcpServer.Dap;

internal sealed class DapSessionException : Exception
{
    public DapSessionException(string message) : base(message) { }
    public DapSessionException(string message, Exception inner) : base(message, inner) { }
}
