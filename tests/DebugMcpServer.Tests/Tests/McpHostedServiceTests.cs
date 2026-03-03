using System.Text.Json.Nodes;
using DebugMcpServer.Server;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class McpHostedServiceTests
{
    private static McpHostedService CreateService(IEnumerable<IMcpTool>? tools = null)
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        return new McpHostedService(
            NullLogger<McpHostedService>.Instance,
            lifetime,
            tools ?? []);
    }

    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    [TestMethod]
    public async Task HandleInitialize_ReturnsProtocolVersion()
    {
        var svc = CreateService();
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")!;

        var result = await svc.HandleRequestAsync(request, CancellationToken.None);

        result["result"]!["protocolVersion"]!.GetValue<string>().Should().Be("2024-11-05");
        result["result"]!["serverInfo"]!["name"]!.GetValue<string>().Should().Be("debug-mcp");
    }

    [TestMethod]
    public async Task HandleToolsList_ReturnsAllTools()
    {
        var tool1 = Substitute.For<IMcpTool>();
        tool1.Name.Returns("tool_one");
        tool1.Description.Returns("Desc one");
        tool1.GetInputSchema().Returns(JsonNode.Parse("""{"type":"object","properties":{}}""")!);

        var tool2 = Substitute.For<IMcpTool>();
        tool2.Name.Returns("tool_two");
        tool2.Description.Returns("Desc two");
        tool2.GetInputSchema().Returns(JsonNode.Parse("""{"type":"object","properties":{}}""")!);

        var svc = CreateService([tool1, tool2]);
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;

        var result = await svc.HandleRequestAsync(request, CancellationToken.None);

        var tools = result["result"]!["tools"] as JsonArray;
        tools.Should().HaveCount(2);
        tools![0]!["name"]!.GetValue<string>().Should().Be("tool_one");
        tools![1]!["name"]!.GetValue<string>().Should().Be("tool_two");
    }

    [TestMethod]
    public async Task HandleToolsCall_DispatchesToCorrectTool()
    {
        var tool = Substitute.For<IMcpTool>();
        tool.Name.Returns("my_tool");
        var expectedResult = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"ok"}],"isError":false}}""")!;
        tool.ExecuteAsync(Arg.Any<JsonNode?>(), Arg.Any<JsonNode?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var svc = CreateService([tool]);
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"my_tool","arguments":{}}}""")!;

        var result = await svc.HandleRequestAsync(request, CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
        await tool.Received(1).ExecuteAsync(Arg.Any<JsonNode?>(), Arg.Any<JsonNode?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandleToolsCall_UnknownTool_ReturnsError()
    {
        var svc = CreateService();
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"missing_tool"}}""")!;

        var result = await svc.HandleRequestAsync(request, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        result["error"]!["message"]!.GetValue<string>().Should().Contain("missing_tool");
    }

    [TestMethod]
    public async Task HandleResourcesList_ReturnsEmptyArray()
    {
        var svc = CreateService();
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"resources/list"}""")!;

        var result = await svc.HandleRequestAsync(request, CancellationToken.None);

        (result["result"]!["resources"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task HandlePromptsList_ReturnsEmptyArray()
    {
        var svc = CreateService();
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"prompts/list"}""")!;

        var result = await svc.HandleRequestAsync(request, CancellationToken.None);

        (result["result"]!["prompts"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task UnknownMethod_ReturnsMethodNotFoundError()
    {
        var svc = CreateService();
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"unknown/method"}""")!;

        var result = await svc.HandleRequestAsync(request, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32601);
    }
}
