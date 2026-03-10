using System.Diagnostics;
using System.Text.Json.Nodes;
using DebugMcpServer.DbgEng;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

/// <summary>
/// Integration tests for native dump analysis using DbgEng.
/// Generates a deterministic C++ dump file (seed=42) at class init and runs real WinDbg commands.
/// Windows only.
/// </summary>
[TestClass]
[TestCategory("WindowsOnly")]
[DoNotParallelize]
public class DbgEngFullIntegrationTests
{
    private static string? _dumpPath;
    private static string? _exePath;
    private static DbgEngSession? _session;

    [ClassInitialize]
    public static void GenerateDump(TestContext _)
    {
        if (!OperatingSystem.IsWindows()) return;

        var repoRoot = FindRepoRoot();
        _exePath = Path.Combine(repoRoot, "samples", "NativeCrashTarget", "build", "Debug", "NativeCrashTarget.exe");
        if (!File.Exists(_exePath))
        {
            // Try to build it
            var cmake = Process.Start(new ProcessStartInfo("cmake", "--build build --config Debug")
            {
                WorkingDirectory = Path.Combine(repoRoot, "samples", "NativeCrashTarget"),
                UseShellExecute = false, CreateNoWindow = true
            });
            cmake?.WaitForExit(30_000);
        }

        if (!File.Exists(_exePath))
        {
            Assert.Inconclusive("NativeCrashTarget.exe not built. Run: cmake --build build --config Debug");
            return;
        }

        _dumpPath = Path.Combine(Path.GetTempPath(), $"dbgeng_test_{Guid.NewGuid():N}.dmp");

        var psi = new ProcessStartInfo(_exePath, $"--seed 42 --output \"{_dumpPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        proc.WaitForExit(30_000);

        if (proc.ExitCode != 0 || !File.Exists(_dumpPath))
        {
            Assert.Inconclusive($"Failed to generate native dump (exit {proc.ExitCode})");
            return;
        }

        _session = DbgEngSession.Open(_dumpPath, NullLogger.Instance);

        // Set symbol path once so all tests can resolve symbols regardless of execution order
        var symPath = Path.Combine(repoRoot, "samples", "NativeCrashTarget", "build", "Debug");
        _session.ExecuteCommand($".sympath+ {symPath}");
        _session.ExecuteCommand(".reload /f");
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _session?.Dispose();
        if (_dumpPath != null && File.Exists(_dumpPath))
            File.Delete(_dumpPath);
    }

    [TestMethod]
    public void Session_Is_Running()
    {
        EnsureSession();
        _session!.IsRunning.Should().BeTrue();
    }

    [TestMethod]
    public void Version_Command_Returns_Output()
    {
        EnsureSession();
        var output = _session!.ExecuteCommand("version");
        output.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Thread_List_Shows_Threads()
    {
        EnsureSession();
        var output = _session!.ExecuteCommand("~");
        output.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Can_Find_Global_Variables()
    {
        EnsureSession();
        // Symbol path already set in ClassInitialize
        var output = _session!.ExecuteCommand("x NativeCrashTarget!g_*");
        output.Should().Contain("g_orderCount");
        output.Should().Contain("g_totalRevenue");
        output.Should().Contain("g_seed");
    }

    [TestMethod]
    public void Seed_Value_Is_42()
    {
        EnsureSession();
        var output = _session!.ExecuteCommand("x NativeCrashTarget!g_seed");
        // Output format: "00007ff... NativeCrashTarget!g_seed = 0n42"
        output.Should().Contain("42");
    }

    [TestMethod]
    public void Order_Count_Matches_Seed()
    {
        EnsureSession();
        var output = _session!.ExecuteCommand("x NativeCrashTarget!g_orderCount");
        output.Should().NotBeNullOrEmpty();
        // With seed 42, the order count is deterministic
    }

    [TestMethod]
    public void Can_Display_Order_Struct()
    {
        EnsureSession();
        var output = _session!.ExecuteCommand("x NativeCrashTarget!g_orders");
        output.Should().Contain("g_orders");

        // Get address and display struct
        var addrMatch = System.Text.RegularExpressions.Regex.Match(output, @"([0-9a-f]+`[0-9a-f]+)");
        if (addrMatch.Success)
        {
            var structOutput = _session.ExecuteCommand($"dt NativeCrashTarget!Order {addrMatch.Value}");
            structOutput.Should().Contain("customer");
            structOutput.Should().Contain("total");
            structOutput.Should().Contain("itemCount");
        }
    }

    [TestMethod]
    public void Registers_Command_Works()
    {
        EnsureSession();
        var output = _session!.ExecuteCommand("r");
        output.Should().Contain("rip=");
        output.Should().Contain("rsp=");
    }

    [TestMethod]
    public void Modules_List_Shows_NativeCrashTarget()
    {
        EnsureSession();
        var output = _session!.ExecuteCommand("lm");
        output.Should().Contain("NativeCrashTarget");
    }

    [TestMethod]
    public void LoadNativeDump_Tool_Works()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_dumpPath == null || !File.Exists(_dumpPath))
        { Assert.Inconclusive("No dump file"); return; }

        // Dispose shared session first — DbgEng COM does not support
        // multiple clients in the same process reliably.
        _session?.Dispose();
        _session = null;

        try
        {
            var registry = new NativeDumpRegistry(NullLogger<NativeDumpRegistry>.Instance);
            var tool = new LoadNativeDumpTool(registry, NullLogger<LoadNativeDumpTool>.Instance);

            var args = new JsonObject { ["dumpPath"] = _dumpPath };
            var result = tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None).Result;

            var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
            var json = JsonNode.Parse(text)!;
            json["status"]!.GetValue<string>().Should().Be("ready");
            json["sessionId"].Should().NotBeNull();

            // Cleanup: detach the session
            var sessionId = json["sessionId"]!.GetValue<string>();
            registry.TryRemove(sessionId, out var session);
            session?.Dispose();
        }
        finally
        {
            // Reopen shared session for subsequent tests
            ReopenSession();
        }
    }

    [TestMethod]
    public void NativeDumpCommand_Tool_Works()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_dumpPath == null || !File.Exists(_dumpPath))
        { Assert.Inconclusive("No dump file"); return; }

        // Dispose shared session first — DbgEng COM does not support
        // multiple clients in the same process reliably.
        _session?.Dispose();
        _session = null;

        try
        {
            var registry = new NativeDumpRegistry(NullLogger<NativeDumpRegistry>.Instance);
            var loadTool = new LoadNativeDumpTool(registry, NullLogger<LoadNativeDumpTool>.Instance);
            var loadResult = loadTool.ExecuteAsync(JsonValue.Create(1),
                new JsonObject { ["dumpPath"] = _dumpPath }, CancellationToken.None).Result;
            var loadJson = JsonNode.Parse(loadResult["result"]!["content"]![0]!["text"]!.GetValue<string>())!;
            var sessionId = loadJson["sessionId"]!.GetValue<string>();

            try
            {
                var tool = new NativeDumpCommandTool(registry, NullLogger<NativeDumpCommandTool>.Instance);
                var args = new JsonObject { ["sessionId"] = sessionId, ["command"] = "lm" };
                var result = tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None).Result;

                var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
                var json = JsonNode.Parse(text)!;
                json["output"]!.GetValue<string>().Should().Contain("NativeCrashTarget");
            }
            finally
            {
                if (registry.TryRemove(sessionId, out var session))
                    session?.Dispose();
            }
        }
        finally
        {
            // Reopen shared session for subsequent tests
            ReopenSession();
        }
    }

    // --- Helpers ---

    private static void EnsureSession()
    {
        if (!OperatingSystem.IsWindows() || _session == null)
            Assert.Inconclusive("Windows-only or no dump generated.");
    }

    private static void ReopenSession()
    {
        if (_dumpPath == null || !File.Exists(_dumpPath)) return;

        _session = DbgEngSession.Open(_dumpPath, NullLogger.Instance);

        var repoRoot = FindRepoRoot();
        var symPath = Path.Combine(repoRoot, "samples", "NativeCrashTarget", "build", "Debug");
        _session.ExecuteCommand($".sympath+ {symPath}");
        _session.ExecuteCommand(".reload /f");
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
