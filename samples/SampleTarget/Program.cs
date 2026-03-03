// SampleTarget — A .NET 8 console app for testing the Debug MCP Server.
//
// HOW TO USE:
//   1. Run:  dotnet run --project samples/SampleTarget
//   2. Note the PID printed on startup
//   3. Use attach_to_process tool with that PID
//
// GOOD BREAKPOINT LINES are marked with: // <<< BREAKPOINT

using System.Diagnostics;

namespace DebugMcpServer.Samples;

internal static class Program
{
    // Static counter — incremented by every task on every iteration.
    // Visible under "Static members" scope in the variables view.
    private static int s_globalCounter = 0;

    private const int MaxParallelTasks = 4;
    private const int IterationsPerTask = 10;

    private static void Main()
    {
        Console.WriteLine($"SampleTarget started. PID = {Process.GetCurrentProcess().Id}");
        Console.WriteLine("Ready for debugger attach. Press Ctrl+C to exit.");
        Console.WriteLine($"Running {MaxParallelTasks} parallel tasks, {IterationsPerTask} iterations each.");
        Console.WriteLine();

        int wave = 0;
        while (true)
        {
            wave++;
            Console.WriteLine($"--- Wave {wave} ---");       // <<< BREAKPOINT: start of each wave

            var tasks = new Task[MaxParallelTasks];
            for (int i = 0; i < MaxParallelTasks; i++)
            {
                int taskIndex = i;
                tasks[i] = Task.Run(() => RunTask(wave, taskIndex));
            }

            Task.WaitAll(tasks);                              // <<< BREAKPOINT: all tasks finished

            Console.WriteLine($"--- Wave {wave} complete. Global counter = {s_globalCounter} ---");
            Console.WriteLine();
            Thread.Sleep(2000);
        }
    }

    /// <summary>
    /// Each task has its own local counter and increments the shared static counter
    /// on every iteration. Loops 10 times with a 1-second sleep between iterations.
    /// </summary>
    private static void RunTask(int wave, int taskIndex)
    {
        int localCounter = 0;                                 // <<< BREAKPOINT: task entry point

        for (int i = 1; i <= IterationsPerTask; i++)
        {
            localCounter++;
            int globalSnapshot = Interlocked.Increment(ref s_globalCounter);

            var workItem = new WorkItem(
                TaskIndex: taskIndex,
                Iteration: i,
                LocalCount: localCounter,
                GlobalSnapshot: globalSnapshot
            );

            ProcessItem(wave, workItem);                      // <<< BREAKPOINT: workItem constructed

            Thread.Sleep(1000);                               // <<< BREAKPOINT: between iterations
        }

        Console.WriteLine($"  [Wave {wave} Task {taskIndex}] Done. Local={localCounter}, Global={s_globalCounter}");
    }

    /// <summary>
    /// Leaf frame. Processes the work item and prints progress.
    /// Good for inspecting the WorkItem record and stepping through.
    /// </summary>
    private static void ProcessItem(int wave, WorkItem item)
    {
        // <<< BREAKPOINT: inspect 'item' (TaskIndex, Iteration, LocalCount, GlobalSnapshot)
        string summary = $"  [Wave {wave} Task {item.TaskIndex}] iter={item.Iteration}, " +
                          $"local={item.LocalCount}, global={item.GlobalSnapshot}";
        Console.WriteLine(summary);
    }
}

/// <summary>
/// Value type representing one unit of work from a parallel task.
/// Tests record type inspection with multiple int properties.
/// </summary>
internal sealed record WorkItem(int TaskIndex, int Iteration, int LocalCount, int GlobalSnapshot);
