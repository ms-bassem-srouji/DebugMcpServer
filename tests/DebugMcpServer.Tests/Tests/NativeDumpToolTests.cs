using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class LoadNativeDumpToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public void Name_Is_load_native_dump()
    {
        var tool = new LoadNativeDumpTool(
            FakeNativeDumpRegistry.Empty(),
            NullLogger<LoadNativeDumpTool>.Instance);
        tool.Name.Should().Be("load_native_dump");
    }

    [TestMethod]
    public void Description_Mentions_DbgEng()
    {
        var tool = new LoadNativeDumpTool(
            FakeNativeDumpRegistry.Empty(),
            NullLogger<LoadNativeDumpTool>.Instance);
        tool.Description.Should().Contain("DbgEng");
        tool.Description.Should().Contain("Windows");
    }

    [TestMethod]
    public void InputSchema_Has_DumpPath_Required()
    {
        var tool = new LoadNativeDumpTool(
            FakeNativeDumpRegistry.Empty(),
            NullLogger<LoadNativeDumpTool>.Instance);
        var required = tool.GetInputSchema()["required"] as JsonArray;
        required!.Select(r => r!.GetValue<string>()).Should().Contain("dumpPath");
    }

    [TestMethod]
    public void InputSchema_Has_SymbolPath_Property()
    {
        var tool = new LoadNativeDumpTool(
            FakeNativeDumpRegistry.Empty(),
            NullLogger<LoadNativeDumpTool>.Instance);
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("symbolPath").Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_DumpPath_Returns_Error()
    {
        var tool = new LoadNativeDumpTool(
            FakeNativeDumpRegistry.Empty(),
            NullLogger<LoadNativeDumpTool>.Instance);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("""{}"""), CancellationToken.None);
        // On Linux: returns "Windows only" text error
        // On Windows: returns JSON-RPC error for missing dumpPath
        var hasJsonRpcError = result["error"] != null;
        var hasTextError = result["result"]?["isError"]?.GetValue<bool>() == true;
        (hasJsonRpcError || hasTextError).Should().BeTrue();
    }

    [TestMethod]
    public async Task Dump_File_Not_Found_Returns_Error()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only test

        var tool = new LoadNativeDumpTool(
            FakeNativeDumpRegistry.Empty(),
            NullLogger<LoadNativeDumpTool>.Instance);
        var nonexistent = Path.Combine(Path.GetTempPath(), "nonexistent_native_dump.dmp");
        var args = JsonNode.Parse($$"""{"dumpPath": "{{nonexistent.Replace("\\", "\\\\")}}"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not found");
    }

    [TestMethod]
    public async Task Non_Windows_Returns_Platform_Error()
    {
        if (OperatingSystem.IsWindows()) return; // Only run on non-Windows

        var tool = new LoadNativeDumpTool(
            FakeNativeDumpRegistry.Empty(),
            NullLogger<LoadNativeDumpTool>.Instance);
        var args = JsonNode.Parse("""{"dumpPath": "/tmp/core.123"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Windows");
    }
}

[TestClass]
public class NativeDumpCommandToolTests
{
    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public void Name_Is_native_dump_command()
    {
        var tool = new NativeDumpCommandTool(
            FakeNativeDumpRegistry.Empty(),
            Substitute.For<ILogger<NativeDumpCommandTool>>());
        tool.Name.Should().Be("native_dump_command");
    }

    [TestMethod]
    public void InputSchema_Has_Required_Fields()
    {
        var tool = new NativeDumpCommandTool(
            FakeNativeDumpRegistry.Empty(),
            Substitute.For<ILogger<NativeDumpCommandTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        var names = required!.Select(r => r!.GetValue<string>()).ToList();
        names.Should().Contain("sessionId");
        names.Should().Contain("command");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tool = new NativeDumpCommandTool(
            FakeNativeDumpRegistry.Empty(),
            Substitute.For<ILogger<NativeDumpCommandTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown", "command":"k"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_Command_Returns_Error()
    {
        var tool = new NativeDumpCommandTool(
            FakeNativeDumpRegistry.Empty(),
            Substitute.For<ILogger<NativeDumpCommandTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1),
            JsonNode.Parse("""{"sessionId":"x"}"""), CancellationToken.None);
        // On Linux: returns "Windows only" text error
        // On Windows: returns JSON-RPC error for missing command
        var hasJsonRpcError = result["error"] != null;
        var hasTextError = result["result"]?["isError"]?.GetValue<bool>() == true;
        (hasJsonRpcError || hasTextError).Should().BeTrue();
    }

    [TestMethod]
    public async Task Quit_Command_Is_Blocked()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tool = new NativeDumpCommandTool(
            FakeNativeDumpRegistry.Empty(),
            Substitute.For<ILogger<NativeDumpCommandTool>>());
        var args = JsonNode.Parse("""{"sessionId":"x", "command":"q"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Non_Windows_Returns_Platform_Error()
    {
        if (OperatingSystem.IsWindows()) return;

        var tool = new NativeDumpCommandTool(
            FakeNativeDumpRegistry.Empty(),
            Substitute.For<ILogger<NativeDumpCommandTool>>());
        var args = JsonNode.Parse("""{"sessionId":"x", "command":"k"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public void Description_Lists_Common_Commands()
    {
        var tool = new NativeDumpCommandTool(
            FakeNativeDumpRegistry.Empty(),
            Substitute.For<ILogger<NativeDumpCommandTool>>());
        tool.Description.Should().Contain("WinDbg");
        tool.Description.Should().Contain("!analyze");
    }
}
