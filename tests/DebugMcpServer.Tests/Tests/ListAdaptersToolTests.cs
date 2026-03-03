using System.Text.Json.Nodes;
using DebugMcpServer.Options;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ListAdaptersToolTests
{
    private static ListAdaptersTool ToolWith(params AdapterConfig[] adapters)
    {
        var options = new DebugOptions();
        options.Adapters.AddRange(adapters);
        return new ListAdaptersTool(options);
    }

    private static JsonNode ParseResult(JsonNode result)
        => JsonNode.Parse(result["result"]!["content"]![0]!["text"]!.GetValue<string>())!;

    [TestMethod]
    public async Task Returns_Configured_Adapters()
    {
        var tool = ToolWith(
            new AdapterConfig { Name = "dotnet", Path = @"C:\tools\netcoredbg.exe", AdapterID = "coreclr" },
            new AdapterConfig { Name = "python", Path = @"C:\tools\debugpy.exe", AdapterID = "python" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        var adapters = (json["adapters"] as JsonArray)!;
        adapters.Should().HaveCount(2);
        adapters[0]!["name"]!.GetValue<string>().Should().Be("dotnet");
        adapters[0]!["path"]!.GetValue<string>().Should().Be(@"C:\tools\netcoredbg.exe");
        adapters[0]!["adapterID"]!.GetValue<string>().Should().Be("coreclr");
        adapters[1]!["name"]!.GetValue<string>().Should().Be("python");
    }

    [TestMethod]
    public async Task Count_Matches_Adapters_Array_Length()
    {
        var tool = ToolWith(
            new AdapterConfig { Name = "a", Path = "a.exe" },
            new AdapterConfig { Name = "b", Path = "b.exe" },
            new AdapterConfig { Name = "c", Path = "c.exe" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(3);
    }

    [TestMethod]
    public async Task Empty_Adapters_Returns_Empty_List()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["adapters"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Omits_AdapterID_When_Null()
    {
        var tool = ToolWith(new AdapterConfig { Name = "custom", Path = "custom.exe" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        var adapter = json["adapters"]![0]!;
        adapter["name"]!.GetValue<string>().Should().Be("custom");
        adapter["adapterID"].Should().BeNull();
    }

    [TestMethod]
    public async Task IsError_Is_False_On_Success()
    {
        var tool = ToolWith(new AdapterConfig { Name = "dotnet", Path = "netcoredbg.exe", AdapterID = "coreclr" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
    }
}
