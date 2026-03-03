namespace DebugMcpServer.Options;

internal sealed class AdapterConfig
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? AdapterID { get; set; } // DAP adapterID (e.g., "coreclr", "python", "node")
    public string? RemotePath { get; set; } // adapter path on remote machines (e.g., "/usr/local/bin/netcoredbg")
}

internal sealed class DebugOptions
{
    public List<AdapterConfig> Adapters { get; set; } = new();
    public string? AdapterPath { get; set; } // legacy fallback
    public string? VsdbgPath { get; set; } // legacy, still supported
    public int AttachTimeoutSeconds { get; set; } = 30;
    public int StepTimeoutSeconds { get; set; } = 3;
    public int ContinueTimeoutSeconds { get; set; } = 25;
    public int MaxPendingEvents { get; set; } = 100;
}
