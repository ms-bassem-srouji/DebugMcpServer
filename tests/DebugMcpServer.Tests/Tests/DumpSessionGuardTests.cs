using System.Text.Json.Nodes;
using DebugMcpServer.Options;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DumpSessionGuardTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static FakeSession CreateDumpSession()
    {
        var session = new FakeSession { IsDumpSession = true };
        return session;
    }

    private static DebugOptions DefaultOptions() => new()
    {
        StepTimeoutSeconds = 3,
        ContinueTimeoutSeconds = 25
    };

    [TestMethod]
    public async Task ContinueExecution_Blocked_On_DumpSession()
    {
        var session = CreateDumpSession();
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = new ContinueExecutionTool(registry, MsOptions.Create(DefaultOptions()),
            Substitute.For<ILogger<ContinueExecutionTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1),
            JsonNode.Parse("""{"sessionId":"dump1"}"""), CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not available for dump file sessions");
        GetText(result).Should().Contain("get_callstack");
    }

    [TestMethod]
    public async Task StepOver_Blocked_On_DumpSession()
    {
        var session = CreateDumpSession();
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = new StepOverTool(registry, MsOptions.Create(DefaultOptions()),
            Substitute.For<ILogger<StepOverTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1),
            JsonNode.Parse("""{"sessionId":"dump1"}"""), CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not available for dump file sessions");
    }

    [TestMethod]
    public async Task StepIn_Blocked_On_DumpSession()
    {
        var session = CreateDumpSession();
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = new StepInTool(registry, MsOptions.Create(DefaultOptions()),
            Substitute.For<ILogger<StepInTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1),
            JsonNode.Parse("""{"sessionId":"dump1"}"""), CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not available for dump file sessions");
    }

    [TestMethod]
    public async Task StepOut_Blocked_On_DumpSession()
    {
        var session = CreateDumpSession();
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = new StepOutTool(registry, MsOptions.Create(DefaultOptions()),
            Substitute.For<ILogger<StepOutTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1),
            JsonNode.Parse("""{"sessionId":"dump1"}"""), CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not available for dump file sessions");
    }

    [TestMethod]
    public async Task PauseExecution_Blocked_On_DumpSession()
    {
        var session = CreateDumpSession();
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = new PauseExecutionTool(registry, MsOptions.Create(DefaultOptions()),
            Substitute.For<ILogger<PauseExecutionTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1),
            JsonNode.Parse("""{"sessionId":"dump1"}"""), CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not available for dump file sessions");
    }

    [TestMethod]
    public async Task ListSessions_Shows_DumpIndicator()
    {
        var session = CreateDumpSession();
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = new ListSessionsTool(registry, FakeDotnetDumpRegistry.Empty(), FakeNativeDumpRegistry.Empty());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["isDumpSession"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task ListSessions_NonDump_Shows_False()
    {
        var session = new FakeSession { IsDumpSession = false };
        var registry = FakeSessionRegistry.WithSession("live1", session);
        var tool = new ListSessionsTool(registry, FakeDotnetDumpRegistry.Empty(), FakeNativeDumpRegistry.Empty());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["isDumpSession"]!.GetValue<bool>().Should().BeFalse();
    }
}
