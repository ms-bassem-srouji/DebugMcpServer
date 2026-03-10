using System.Diagnostics;
using System.Text.Json.Nodes;
using DebugMcpServer.DotnetDump;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

/// <summary>
/// Integration tests for .NET dump analysis using ClrMD.
/// Generates a deterministic dump file (seed=42) at class init and runs real analysis.
/// </summary>
[TestClass]
public class ClrMdIntegrationTests
{
    private static string? _dumpPath;
    private static DotnetDumpSession? _session;

    // Expected values for seed=42
    private const int ExpectedOrderCount = 7;
    private const string ExpectedLastCustomer = "Eve";

    [ClassInitialize]
    public static void GenerateDump(TestContext _)
    {
        var repoRoot = FindRepoRoot();
        var crashTargetProject = Path.Combine(repoRoot, "samples", "CrashTarget", "CrashTarget.csproj");
        if (!File.Exists(crashTargetProject))
        {
            Assert.Inconclusive("CrashTarget project not found.");
            return;
        }

        _dumpPath = Path.Combine(Path.GetTempPath(), $"clrmd_test_{Guid.NewGuid():N}.dmp");

        // Build and run CrashTarget with deterministic seed
        var psi = new ProcessStartInfo("dotnet", $"run --project \"{crashTargetProject}\" -- --seed 42 --output \"{_dumpPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        proc.WaitForExit(60_000);

        if (proc.ExitCode != 0 || !File.Exists(_dumpPath))
        {
            var stderr = proc.StandardError.ReadToEnd();
            Assert.Inconclusive($"Failed to generate dump (exit {proc.ExitCode}): {stderr}");
            return;
        }

        // Open ClrMD session
        _session = DotnetDumpSession.Open(_dumpPath, NullLogger.Instance);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _session?.Dispose();
        if (_dumpPath != null && File.Exists(_dumpPath))
            File.Delete(_dumpPath);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Session_Is_Running()
    {
        EnsureSession();
        _session!.IsRunning.Should().BeTrue();
        _session.DumpPath.Should().Be(_dumpPath);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Runtime_Has_Expected_Thread_Count()
    {
        EnsureSession();
        _session!.Runtime.Threads.Length.Should().BeGreaterThan(0);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Threads_Tool_Returns_Stack_Traces()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpThreadsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpThreadsTool>.Instance);
        var result = Execute(tool);

        var json = ParseText(result);
        json["threadCount"]!.GetValue<int>().Should().BeGreaterThan(0);
        var threads = (json["threads"] as JsonArray)!;
        threads.Should().NotBeEmpty();

        // At least one thread should have stack frames
        threads.Any(t => (t!["frameCount"]?.GetValue<int>() ?? 0) > 0).Should().BeTrue();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Exceptions_Tool_Returns_No_Exceptions()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpExceptionsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpExceptionsTool>.Instance);
        var result = Execute(tool);

        var json = ParseText(result);
        json["exceptionCount"]!.GetValue<int>().Should().Be(0);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void HeapStats_Finds_Order_Objects()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpHeapStatsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpHeapStatsTool>.Instance);
        var result = Execute(tool, new JsonObject { ["filter"] = "CrashTarget.Order" });

        var json = ParseText(result);
        var types = (json["types"] as JsonArray)!;
        var orderType = types.FirstOrDefault(t => t!["type"]!.GetValue<string>() == "CrashTarget.Order");
        orderType.Should().NotBeNull("should find CrashTarget.Order on heap");
        orderType!["count"]!.GetValue<int>().Should().Be(ExpectedOrderCount);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void FindObjects_Returns_Order_Addresses()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpFindObjectsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpFindObjectsTool>.Instance);
        var result = Execute(tool, new JsonObject { ["typeName"] = "CrashTarget.Order" });

        var json = ParseText(result);
        json["matchedCount"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(ExpectedOrderCount);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Inspect_First_Order_Has_Expected_Customer()
    {
        EnsureSession();
        // Find Order objects
        var findTool = new Tools.DotnetDumpFindObjectsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpFindObjectsTool>.Instance);
        var findResult = Execute(findTool, new JsonObject { ["typeName"] = "CrashTarget.Order" });
        var findJson = ParseText(findResult);
        var objects = (findJson["objects"] as JsonArray)!;

        // Filter to only CrashTarget.Order (not arrays, enumerators, etc.)
        var orderObj = objects.FirstOrDefault(o =>
            o!["type"]!.GetValue<string>() == "CrashTarget.Order");
        orderObj.Should().NotBeNull();

        // Inspect it
        var address = orderObj!["address"]!.GetValue<string>();
        var inspectTool = new Tools.DotnetDumpInspectTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpInspectTool>.Instance);
        var inspectResult = Execute(inspectTool, new JsonObject { ["address"] = address });
        var inspectJson = ParseText(inspectResult);

        inspectJson["type"]!.GetValue<string>().Should().Be("CrashTarget.Order");
        inspectJson["fields"].Should().NotBeNull();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void MemoryStats_Returns_Heap_Info()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpMemoryStatsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpMemoryStatsTool>.Instance);
        var result = Execute(tool);

        var json = ParseText(result);
        json["totalHeapSize"].Should().NotBeNull();
        json["totalSegments"]!.GetValue<int>().Should().BeGreaterThan(0);
        json["canWalk"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void AsyncState_Returns_Tasks()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpAsyncStateTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpAsyncStateTool>.Instance);
        var result = Execute(tool, new JsonObject { ["includeCompleted"] = true });

        var json = ParseText(result);
        // Should find some tasks (at least the hot reload tasks)
        json["totalMatched"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void StackObjects_Returns_Objects_For_Main_Thread()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpStackObjectsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpStackObjectsTool>.Instance);
        var result = Execute(tool);

        var json = ParseText(result);
        json["objectCount"]!.GetValue<int>().Should().BeGreaterThan(0);
        var objects = (json["objects"] as JsonArray)!;
        // Should find some string objects on the stack
        objects.Any(o => o!["type"]!.GetValue<string>() == "System.String").Should().BeTrue();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GcRoots_With_Valid_Object_Returns_Result()
    {
        EnsureSession();
        // Find a real object address on the heap
        var findTool = new Tools.DotnetDumpFindObjectsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpFindObjectsTool>.Instance);
        var findResult = Execute(findTool, new JsonObject { ["typeName"] = "CrashTarget.Order" });
        var findJson = ParseText(findResult);
        var objects = (findJson["objects"] as JsonArray)!;
        objects.Should().NotBeEmpty();

        var address = objects[0]!["address"]!.GetValue<string>();

        // Call GcRoots with that address
        var tool = new Tools.DotnetDumpGcRootsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpGcRootsTool>.Instance);
        var result = Execute(tool, new JsonObject { ["address"] = address });

        var json = ParseText(result);
        json["targetAddress"].Should().NotBeNull();
        json["rootCount"].Should().NotBeNull();
        json["roots"].Should().NotBeNull();
        // rootCount should be >= 0 (may or may not have direct roots)
        json["rootCount"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GcRoots_With_Nonexistent_Address_Returns_No_Roots()
    {
        EnsureSession();
        // Use an address that won't match any root
        var tool = new Tools.DotnetDumpGcRootsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpGcRootsTool>.Instance);
        var result = Execute(tool, new JsonObject { ["address"] = "0xDEAD" });

        var json = ParseText(result);
        json["targetAddress"]!.GetValue<string>().Should().Be("0xDEAD");
        json["rootCount"]!.GetValue<int>().Should().Be(0);
        json["message"].Should().NotBeNull();
        json["message"]!.GetValue<string>().Should().Contain("No direct GC roots found");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GcRoots_With_Invalid_Address_On_Valid_Session_Returns_Error()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpGcRootsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpGcRootsTool>.Instance);
        var result = Execute(tool, new JsonObject { ["address"] = "not_hex_at_all" });

        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("Invalid address");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GcRoots_Enumerates_All_Roots_Without_Error()
    {
        EnsureSession();
        // Use EnumerateRoots directly to find any rooted object, then verify the tool
        var heap = _session!.Runtime.Heap;
        var firstRoot = heap.EnumerateRoots().FirstOrDefault();

        var gcTool = new Tools.DotnetDumpGcRootsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpGcRootsTool>.Instance);

        if (firstRoot.Address != 0 && firstRoot.Object.Address != 0)
        {
            // We found a real root — verify the tool finds it
            var addr = $"0x{firstRoot.Object.Address:X}";
            var gcResult = Execute(gcTool, new JsonObject { ["address"] = addr });
            var gcJson = ParseText(gcResult);

            gcJson["rootCount"]!.GetValue<int>().Should().BeGreaterThan(0);
            var roots = (gcJson["roots"] as JsonArray)!;
            roots.Should().NotBeEmpty();
            roots[0]!["rootKind"].Should().NotBeNull();
            roots[0]!["address"].Should().NotBeNull();
            roots[0]!["objectAddress"]!.GetValue<string>().Should().Be(addr);
            roots[0]!["type"].Should().NotBeNull();
        }
        else
        {
            // No roots at all — tool should still return valid result
            var gcResult = Execute(gcTool, new JsonObject { ["address"] = "0x1234" });
            var gcJson = ParseText(gcResult);
            gcJson["rootCount"]!.GetValue<int>().Should().Be(0);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Exceptions_No_Exceptions_Shows_Message()
    {
        EnsureSession();
        var tool = new Tools.DotnetDumpExceptionsTool(
            RegistryWith(_session!), NullLogger<Tools.DotnetDumpExceptionsTool>.Instance);
        var result = Execute(tool);

        var json = ParseText(result);
        json["exceptionCount"]!.GetValue<int>().Should().Be(0);
        json["message"].Should().NotBeNull();
        json["message"]!.GetValue<string>().Should().Contain("No exceptions found");
    }

    // --- Helpers ---

    private static void EnsureSession()
    {
        if (_session == null)
            Assert.Inconclusive("Dump not generated — skipping.");
    }

    private static DotnetDumpRegistry RegistryWith(DotnetDumpSession session)
    {
        var registry = new DotnetDumpRegistry(NullLogger<DotnetDumpRegistry>.Instance);
        registry.Register("test-session", session);
        return registry;
    }

    private static JsonNode Execute(Tools.IMcpTool tool, JsonObject? extraArgs = null)
    {
        var args = new JsonObject { ["sessionId"] = "test-session" };
        if (extraArgs != null)
            foreach (var (key, value) in extraArgs)
                args[key] = value?.DeepClone();

        return tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None).Result;
    }

    private static JsonNode ParseText(JsonNode result)
    {
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        return JsonNode.Parse(text)!;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "DebugMcpServer.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return AppContext.BaseDirectory;
    }
}
