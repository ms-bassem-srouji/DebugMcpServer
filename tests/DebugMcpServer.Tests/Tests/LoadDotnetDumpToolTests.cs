using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class LoadDotnetDumpToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static LoadDotnetDumpTool CreateTool()
    {
        var registry = FakeDotnetDumpRegistry.Empty();
        return new LoadDotnetDumpTool(registry, NullLogger<LoadDotnetDumpTool>.Instance);
    }

    [TestMethod]
    public void Name_Is_load_dotnet_dump()
    {
        CreateTool().Name.Should().Be("load_dotnet_dump");
    }

    [TestMethod]
    public void Description_Is_Not_Empty()
    {
        CreateTool().Description.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void InputSchema_Has_DumpPath_Required()
    {
        var schema = CreateTool().GetInputSchema();
        var required = schema["required"] as JsonArray;
        required.Should().NotBeNull();
        required!.Select(r => r!.GetValue<string>()).Should().Contain("dumpPath");
    }

    [TestMethod]
    public void InputSchema_Has_Expected_Properties()
    {
        var schema = CreateTool().GetInputSchema();
        var props = schema["properties"] as JsonObject;
        props.Should().NotBeNull();
        props!.ContainsKey("dumpPath").Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_DumpPath_Returns_Error()
    {
        var tool = CreateTool();
        var args = JsonNode.Parse("""{}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"].Should().NotBeNull();
        result["error"]!["message"]!.GetValue<string>().Should().Contain("dumpPath");
    }

    [TestMethod]
    public async Task Dump_File_Not_Found_Returns_Error()
    {
        var tool = CreateTool();
        var nonexistentPath = Path.Combine(Path.GetTempPath(), "nonexistent_dump_abc123.dmp");
        var args = JsonNode.Parse($$"""{"dumpPath": "{{nonexistentPath.Replace("\\", "\\\\")}}"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Dump file not found");
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public void Description_Mentions_ClrMD_Tools()
    {
        var desc = CreateTool().Description;
        desc.Should().Contain("ClrMD");
        desc.Should().Contain("dotnet_dump_threads");
    }
}
