namespace DebugMcpServer.Dap;

/// <summary>
/// Translates raw DAP/HRESULT error messages into human-readable guidance.
/// </summary>
internal static class DapErrorHelper
{
    public static string Humanize(string command, string rawMessage)
    {
        if (rawMessage.Contains("0x80004005"))
        {
            return command switch
            {
                "next" or "stepIn" or "stepOut" =>
                    $"Cannot {command} on this thread — it is likely in native/unmanaged code (e.g., Thread.Sleep). " +
                    "Use continue_execution to proceed past native frames, or change_thread to switch to a different thread.",
                "scopes" or "variables" =>
                    "Frame ID is no longer valid. Frame IDs are ephemeral and change after every step/continue. " +
                    "Call get_callstack to get fresh frame IDs.",
                _ => $"Operation '{command}' failed (E_FAIL). The thread may be in an invalid state for this operation."
            };
        }

        if (rawMessage.Contains("0x80131302"))
        {
            return $"Cannot execute '{command}' on this thread — it may not be the thread that hit the breakpoint. " +
                   "Use list_threads to see all threads, and change_thread to switch to the correct one.";
        }

        return rawMessage;
    }
}
