using System.Diagnostics;
using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ListProcessesToolTests
{
    // Process has no public constructor, so we inject the current process as a
    // known-good stand-in. The factory overload lets us control exactly what's returned.

    private static readonly Process CurrentProcess = Process.GetCurrentProcess();

    private static ListProcessesTool ToolWith(params Process[] processes)
        => new ListProcessesTool(NullLogger<ListProcessesTool>.Instance, () => processes);

    private static JsonNode ParseResult(JsonNode result)
        => JsonNode.Parse(result["result"]!["content"]![0]!["text"]!.GetValue<string>())!;

    [TestMethod]
    public async Task Returns_Pid_And_Name_For_Each_Process()
    {
        var tool = ToolWith(CurrentProcess);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        var processes = (json["processes"] as JsonArray)!;
        processes.Should().HaveCount(1);
        processes[0]!["pid"]!.GetValue<int>().Should().Be(CurrentProcess.Id);
        processes[0]!["name"]!.GetValue<string>().Should().Be(CurrentProcess.ProcessName);
    }

    [TestMethod]
    public async Task Count_Matches_Processes_Array_Length()
    {
        var tool = ToolWith(CurrentProcess, CurrentProcess, CurrentProcess);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(3);
    }

    [TestMethod]
    public async Task Empty_Factory_Returns_Empty_List()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["processes"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Filter_Matches_Case_Insensitively()
    {
        var tool = ToolWith(CurrentProcess);
        var upperFilter = CurrentProcess.ProcessName[..3].ToUpperInvariant();
        var args = JsonNode.Parse($$"""{"filter":"{{upperFilter}}"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Filter_No_Match_Returns_Empty()
    {
        var tool = ToolWith(CurrentProcess);
        var args = JsonNode.Parse("""{"filter":"__xyzzy_no_match__"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(0);
    }

    [TestMethod]
    public async Task No_Filter_Returns_All_Processes()
    {
        var tool = ToolWith(CurrentProcess, CurrentProcess);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(2);
    }

    [TestMethod]
    public async Task IsError_Is_False_On_Success()
    {
        var tool = ToolWith(CurrentProcess);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
    }
}
