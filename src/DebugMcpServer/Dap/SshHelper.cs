using System.Diagnostics;

namespace DebugMcpServer.Dap;

/// <summary>
/// Helper for building SSH ProcessStartInfo to pipe DAP protocol through SSH.
/// </summary>
internal static class SshHelper
{
    /// <summary>
    /// Creates a ProcessStartInfo that runs a command on a remote host via SSH.
    /// stdin/stdout are redirected for DAP communication.
    /// </summary>
    public static ProcessStartInfo CreateSshProcessStartInfo(
        string host, int port, string? keyPath, string remoteCommand)
    {
        var sshArgs = new List<string>
        {
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "BatchMode=yes",
            "-p", port.ToString()
        };

        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            sshArgs.Add("-i");
            sshArgs.Add(keyPath);
        }

        sshArgs.Add(host);
        sshArgs.Add(remoteCommand);

        return new ProcessStartInfo("ssh", string.Join(" ", sshArgs))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }
}
