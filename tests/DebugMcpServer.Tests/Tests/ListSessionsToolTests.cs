using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ListSessionsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static ListSessionsTool CreateTool(DapSessionRegistry registry)
        => new ListSessionsTool(registry, FakeDotnetDumpRegistry.Empty(), FakeNativeDumpRegistry.Empty());

    [TestMethod]
    public async Task Returns_Active_Sessions()
    {
        var session1 = new FakeSession { State = SessionState.Paused, ActiveThreadId = 1 };
        var session2 = new FakeSession { State = SessionState.Running, ActiveThreadId = 5 };
        var registry = FakeSessionRegistry.WithSession("s1", session1);
        registry.Register("s2", session2);

        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(2);
        var sessions = (json["sessions"] as JsonArray)!;
        sessions.Should().HaveCount(2);

        // Find sessions by ID (order not guaranteed from ConcurrentDictionary)
        var s1 = sessions.First(s => s!["sessionId"]!.GetValue<string>() == "s1")!;
        s1["state"]!.GetValue<string>().Should().Be("Paused");
        s1["activeThreadId"]!.GetValue<int>().Should().Be(1);

        var s2 = sessions.First(s => s!["sessionId"]!.GetValue<string>() == "s2")!;
        s2["state"]!.GetValue<string>().Should().Be("Running");
        s2["activeThreadId"]!.GetValue<int>().Should().Be(5);
    }

    [TestMethod]
    public async Task Empty_Registry_Returns_Empty_List()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["sessions"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task IsError_Is_False()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
    }

    [TestMethod]
    public async Task DAP_Sessions_Show_Type_Dap()
    {
        var session = new FakeSession { State = SessionState.Paused, ActiveThreadId = 1 };
        var registry = FakeSessionRegistry.WithSession("s1", session);
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["type"]!.GetValue<string>().Should().Be("dap");
    }

    [TestMethod]
    public async Task DAP_DumpSession_Shows_IsDumpSession_True()
    {
        var session = new FakeSession { State = SessionState.Paused, IsDumpSession = true };
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["isDumpSession"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Both_Registries_Empty_Returns_Zero_Count()
    {
        var dapRegistry = FakeSessionRegistry.Empty();
        var dumpRegistry = FakeDotnetDumpRegistry.Empty();
        var tool = new ListSessionsTool(dapRegistry, dumpRegistry, FakeNativeDumpRegistry.Empty());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["sessions"] as JsonArray).Should().BeEmpty();
    }
}
