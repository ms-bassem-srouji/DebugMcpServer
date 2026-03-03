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
public class EvaluateExpressionToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (EvaluateExpressionTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("stackTrace", JsonNode.Parse("""
            {"stackFrames":[{"id":100,"name":"Main","line":10,"source":{"path":"Program.cs"}}]}
            """)!);
        session.SetupRequest("evaluate", JsonNode.Parse("""
            {"result":"42","type":"int","variablesReference":0}
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<EvaluateExpressionTool>>();
        return (new EvaluateExpressionTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Returns_Evaluated_Result()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","expression":"x + 1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["result"]!.GetValue<string>().Should().Be("42");
        json["type"]!.GetValue<string>().Should().Be("int");
        json["variablesReference"]!.GetValue<int>().Should().Be(0);
    }

    [TestMethod]
    public async Task Sends_Evaluate_With_Expression_And_Context()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","expression":"myVar","context":"watch"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "evaluate");
        var evalArgs = session.SentRequests.First(r => r.Command == "evaluate").Args;
        evalArgs!["expression"]!.GetValue<string>().Should().Be("myVar");
        evalArgs!["context"]!.GetValue<string>().Should().Be("watch");
    }

    [TestMethod]
    public async Task Uses_Provided_FrameId()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","expression":"x","frameId":55}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var evalArgs = session.SentRequests.First(r => r.Command == "evaluate").Args;
        evalArgs!["frameId"]!.GetValue<int>().Should().Be(55);
    }

    [TestMethod]
    public async Task Resolves_Top_Frame_When_FrameId_Omitted()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","expression":"x"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "stackTrace");
        var evalArgs = session.SentRequests.First(r => r.Command == "evaluate").Args;
        evalArgs!["frameId"]!.GetValue<int>().Should().Be(100);
    }

    [TestMethod]
    public async Task Missing_Expression_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<EvaluateExpressionTool>>();
        var tool = new EvaluateExpressionTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown","expression":"x"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Error_Result()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("stackTrace", JsonNode.Parse("""
            {"stackFrames":[{"id":100,"name":"Main","line":10}]}
            """)!);
        session.SetupRequestError("evaluate", "CS1234: Invalid expression");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<EvaluateExpressionTool>>();
        var tool = new EvaluateExpressionTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","expression":"bad!!!"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Evaluation failed");
    }
}
