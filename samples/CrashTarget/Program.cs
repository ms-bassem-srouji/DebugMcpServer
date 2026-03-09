using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CrashTarget;

/// <summary>
/// A program that writes a crash dump of itself then terminates.
/// Use the generated .dmp file to test load_dump_file.
///
/// Usage:
///   dotnet run                        → writes dump to output directory, then exits
///   dotnet run -- --wait-for-collect  → pauses so you can use 'dotnet-dump collect' or 'procdump'
/// </summary>
internal static class Program
{
    // State that should be visible when inspecting the dump
#pragma warning disable CS0414 // Fields are intentionally assigned for dump inspection
    private static readonly List<Order> _orders = [];
    private static int _processedCount;
    private static string _currentCustomer = "unset";
    private static int _randomSeed;
    private static double _totalRevenue;
#pragma warning restore CS0414

    private static readonly string[] CustomerNames =
        ["Alice", "Bob", "Charlie", "Diana", "Eve", "Frank", "Grace", "Hank", "Iris", "Jack"];
    private static readonly string[] ItemNames =
        ["Widget", "Gadget", "Thingamajig", "Doohickey", "Whatsit", "Gizmo",
         "Contraption", "Doodad", "Sprocket", "Flanget", "Cog", "Lever"];

    static int Main(string[] args)
    {
        Console.WriteLine($"CrashTarget PID: {Environment.ProcessId}");
        Console.WriteLine($"Platform: {RuntimeInformation.RuntimeIdentifier}");
        Console.WriteLine();

        // Generate random orders
        var rng = new Random();
        _randomSeed = rng.Next();
        Console.WriteLine($"Random seed: {_randomSeed}");
        Console.WriteLine();

        int numOrders = rng.Next(3, 9);
        Console.WriteLine($"=== Generating {numOrders} random orders ===");
        Console.WriteLine();

        for (int o = 0; o < numOrders; o++)
        {
            var customer = CustomerNames[rng.Next(CustomerNames.Length)];
            int numItems = rng.Next(1, 6);
            var items = new List<string>();
            decimal total = 0;

            Console.WriteLine($"Order #{o + 1} for {customer} ({numItems} items):");
            for (int i = 0; i < numItems; i++)
            {
                var itemName = ItemNames[rng.Next(ItemNames.Length)];
                var price = Math.Round((decimal)(rng.NextDouble() * 500 + 1), 2);
                var qty = rng.Next(1, 10);
                items.Add(itemName);
                total += price * qty;
                Console.WriteLine($"  - {itemName}: ${price} x {qty} = ${price * qty}");
            }

            _orders.Add(new Order(o + 1, customer, total, items));
            Console.WriteLine($"  Subtotal: ${total:F2}");
            Console.WriteLine();
        }

        _processedCount = numOrders;
        _currentCustomer = _orders[^1].Customer;
        _totalRevenue = (double)_orders.Sum(o => o.Total);

        Console.WriteLine($"=== Summary ===");
        Console.WriteLine($"Orders: {numOrders}, Total Revenue: ${_totalRevenue:F2}, Last Customer: {_currentCustomer}");
        Console.WriteLine();

        // Local variables visible in the crash frame
        var activeOrder = _orders[^1];
        var totalValue = _orders.Sum(o => o.Total);
        var itemCount = _orders.SelectMany(o => o.Items).Count();

        if (args.Contains("--wait-for-collect"))
        {
            return WaitForExternalCollection();
        }

        // Default: write a minidump of ourselves
        var dumpPath = Path.Combine(AppContext.BaseDirectory, $"crash_{Environment.ProcessId}.dmp");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return WriteMiniDumpWindows(dumpPath, activeOrder, totalValue, itemCount);
        }
        else
        {
            return WriteDumpUnix(dumpPath);
        }
    }

    private static int WaitForExternalCollection()
    {
        Console.WriteLine("Process is running. Capture a dump using one of:");
        Console.WriteLine($"  dotnet-dump collect -p {Environment.ProcessId}");
        Console.WriteLine($"  procdump -ma {Environment.ProcessId} crash.dmp");
        Console.WriteLine();
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
        return 0;
    }

    private static int WriteMiniDumpWindows(string dumpPath, Order activeOrder, decimal totalValue, int itemCount)
    {
        // Keep these locals alive in the frame for dump inspection
        _ = activeOrder;
        _ = totalValue;
        _ = itemCount;

        using var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.ReadWrite);
        var process = Process.GetCurrentProcess();

        bool success = NativeMethods.MiniDumpWriteDump(
            process.Handle,
            (uint)Environment.ProcessId,
            fs.SafeFileHandle.DangerousGetHandle(),
            NativeMethods.MiniDumpWithFullMemory,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (!success)
        {
            Console.WriteLine($"MiniDumpWriteDump failed: 0x{Marshal.GetLastWin32Error():X8}");
            return 1;
        }

        var sizeMb = new FileInfo(dumpPath).Length / 1024.0 / 1024.0;
        Console.WriteLine($"Dump written: {dumpPath} ({sizeMb:F1} MB)");
        PrintUsageInstructions(dumpPath);
        return 0;
    }

    private static int WriteDumpUnix(string dumpPath)
    {
        // On Linux, use the bundled createdump tool from the .NET runtime
        var createdumpPath = Path.Combine(
            RuntimeEnvironment.GetRuntimeDirectory(),
            "createdump");

        if (!File.Exists(createdumpPath))
        {
            Console.WriteLine($"createdump not found at: {createdumpPath}");
            Console.WriteLine();
            Console.WriteLine("Alternative: install dotnet-dump and use --wait-for-collect mode:");
            Console.WriteLine("  dotnet tool install -g dotnet-dump");
            Console.WriteLine("  dotnet run -- --wait-for-collect");
            Console.WriteLine($"  dotnet-dump collect -p <PID> -o {dumpPath}");
            return 1;
        }

        Console.WriteLine($"Using createdump: {createdumpPath}");
        var psi = new ProcessStartInfo(createdumpPath, $"{Environment.ProcessId} -f {dumpPath} --full")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(30_000);
        var stderr = proc?.StandardError.ReadToEnd();

        if (proc?.ExitCode != 0)
        {
            Console.WriteLine($"createdump failed (exit code {proc?.ExitCode}): {stderr}");
            return 1;
        }

        var sizeMb = new FileInfo(dumpPath).Length / 1024.0 / 1024.0;
        Console.WriteLine($"Dump written: {dumpPath} ({sizeMb:F1} MB)");
        PrintUsageInstructions(dumpPath);
        return 0;
    }

    private static void PrintUsageInstructions(string dumpPath)
    {
        var escapedPath = dumpPath.Replace(@"\", @"\\");
        Console.WriteLine();
        Console.WriteLine("=== Test dump debugging with DebugMcpServer ===");
        Console.WriteLine();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("Option 1 - Windows native debugger (cppvsdbg):");
            Console.WriteLine($"  load_dump_file(dumpPath: \"{escapedPath}\", adapter: \"cppvsdbg\")");
            Console.WriteLine();
            Console.WriteLine("Option 2 - .NET managed debugger (vsdbg):");
            Console.WriteLine($"  load_dump_file(dumpPath: \"{escapedPath}\", adapter: \"dotnet-vsdbg\")");
        }
        else
        {
            Console.WriteLine("Load with .NET debugger:");
            Console.WriteLine($"  load_dump_file(dumpPath: \"{escapedPath}\", adapter: \"dotnet-vsdbg\")");
        }

        Console.WriteLine();
        Console.WriteLine("Then inspect:");
        Console.WriteLine("  get_callstack()                                    → see Main frame");
        Console.WriteLine("  get_variables(frameId: 0)                          → _orders, _currentCustomer, activeOrder");
        Console.WriteLine("  evaluate_expression(expression: \"_orders.Count\")   → 3");
        Console.WriteLine("  evaluate_expression(expression: \"totalValue\")      → 365.49");
        Console.WriteLine("  list_threads()                                     → all .NET threads");
        Console.WriteLine("  get_modules()                                      → loaded assemblies");
        Console.WriteLine("  get_loaded_sources()                               → available source files");
    }

    private static class NativeMethods
    {
        // MiniDumpWithFullMemory: captures all accessible memory (largest dump, most data for debugging)
        public const uint MiniDumpWithFullMemory = 0x00000002;

        [DllImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MiniDumpWriteDump(
            IntPtr hProcess, uint processId, IntPtr hFile,
            uint dumpType,
            IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
    }
}

internal record Order(int Id, string Customer, decimal Total, List<string> Items);
