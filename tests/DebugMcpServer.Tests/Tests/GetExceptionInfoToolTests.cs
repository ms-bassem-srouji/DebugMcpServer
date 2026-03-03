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
public class GetExceptionInfoToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (GetExceptionInfoTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("exceptionInfo", JsonNode.Parse("""
            {
                "exceptionId": "System.NullReferenceException",
                "description": "Object reference not set to an instance of an object",
                "breakMode": "always",
                "details": {
                    "message": "Object reference not set to an instance of an object",
                    "typeName": "System.NullReferenceException",
                    "stackTrace": "   at MyApp.Program.Main() in Program.cs:line 42",
                    "source": "MyApp"
                }
            }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetExceptionInfoTool>>();
        return (new GetExceptionInfoTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Returns_Exception_Details()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["exceptionId"]!.GetValue<string>().Should().Be("System.NullReferenceException");
        json["description"]!.GetValue<string>().Should().Contain("Object reference");
        json["breakMode"]!.GetValue<string>().Should().Be("always");
    }

    [TestMethod]
    public async Task Returns_Detailed_Info()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var details = json["details"]!;
        details["typeName"]!.GetValue<string>().Should().Be("System.NullReferenceException");
        details["stackTrace"]!.GetValue<string>().Should().Contain("Program.cs:line 42");
        details["source"]!.GetValue<string>().Should().Be("MyApp");
    }

    [TestMethod]
    public async Task Sends_ExceptionInfo_With_ThreadId()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "exceptionInfo");
        var req = session.SentRequests.First(r => r.Command == "exceptionInfo");
        req.Args!["threadId"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _) = CreateTool();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("{}")!, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<GetExceptionInfoTool>>();
        var tool = new GetExceptionInfoTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Running_Session_Returns_Error()
    {
        var session = new FakeSession { State = SessionState.Running };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetExceptionInfoTool>>();
        var tool = new GetExceptionInfoTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("running");
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Humanized_Message()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequestError("exceptionInfo", "No exception available");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetExceptionInfoTool>>();
        var tool = new GetExceptionInfoTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }
}
