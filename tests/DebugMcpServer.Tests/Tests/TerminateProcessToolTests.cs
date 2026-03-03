using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class TerminateProcessToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public async Task Terminates_Session_And_Returns_Success()
    {
        var session = new FakeSession();
        session.SetupRequest("terminate", JsonNode.Parse("{}")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<TerminateProcessTool>>();
        var tool = new TerminateProcessTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("terminated");
        text.Should().Contain("sess1");
    }

    [TestMethod]
    public async Task Sends_Terminate_Dap_Request()
    {
        var session = new FakeSession();
        session.SetupRequest("terminate", JsonNode.Parse("{}")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<TerminateProcessTool>>();
        var tool = new TerminateProcessTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "terminate");
    }

    [TestMethod]
    public async Task Removes_Session_From_Registry()
    {
        var session = new FakeSession();
        session.SetupRequest("terminate", JsonNode.Parse("{}")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<TerminateProcessTool>>();
        var tool = new TerminateProcessTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        registry.TryGet("sess1", out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task Disposes_Session()
    {
        var session = new FakeSession();
        session.SetupRequest("terminate", JsonNode.Parse("{}")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<TerminateProcessTool>>();
        var tool = new TerminateProcessTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.IsDisposed.Should().BeTrue();
    }

    [TestMethod]
    public async Task Succeeds_Even_If_Dap_Terminate_Fails()
    {
        var session = new FakeSession();
        session.SetupRequestError("terminate", "Not supported");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<TerminateProcessTool>>();
        var tool = new TerminateProcessTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.IsDisposed.Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<TerminateProcessTool>>();
        var tool = new TerminateProcessTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<TerminateProcessTool>>();
        var tool = new TerminateProcessTool(registry, logger);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("{}")!, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }
}
