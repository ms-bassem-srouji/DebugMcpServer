using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class GetModulesToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (GetModulesTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("modules", JsonNode.Parse("""
            {
                "modules": [
                    {"id":1,"name":"MyApp.dll","path":"C:\\app\\MyApp.dll","version":"1.0.0","isOptimized":false,"symbolStatus":"Symbols loaded"},
                    {"id":2,"name":"System.Runtime.dll","path":"C:\\dotnet\\System.Runtime.dll","version":"8.0.0"}
                ]
            }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetModulesTool>>();
        return (new GetModulesTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Returns_Modules_With_Details()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(2);

        var modules = (json["modules"] as JsonArray)!;
        var first = modules[0]!;
        first["name"]!.GetValue<string>().Should().Be("MyApp.dll");
        first["path"]!.GetValue<string>().Should().Be(@"C:\app\MyApp.dll");
        first["version"]!.GetValue<string>().Should().Be("1.0.0");
        first["isOptimized"]!.GetValue<bool>().Should().BeFalse();
        first["symbolStatus"]!.GetValue<string>().Should().Be("Symbols loaded");
    }

    [TestMethod]
    public async Task Sends_Modules_Dap_Request()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "modules");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<GetModulesTool>>();
        var tool = new GetModulesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Error_Result()
    {
        var session = new FakeSession();
        session.SetupRequestError("modules", "Not supported");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetModulesTool>>();
        var tool = new GetModulesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("DAP error");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _) = CreateTool();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("{}")!, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }
}
