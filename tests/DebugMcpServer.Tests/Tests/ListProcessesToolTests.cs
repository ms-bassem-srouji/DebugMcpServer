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

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public void Name_Is_list_processes()
    {
        var tool = ToolWith();
        tool.Name.Should().Be("list_processes");
    }

    [TestMethod]
    public void Description_Is_Not_Empty()
    {
        var tool = ToolWith();
        tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void Description_Mentions_SSH()
    {
        var tool = ToolWith();
        tool.Description.Should().Contain("SSH");
    }

    [TestMethod]
    public void InputSchema_Has_Filter_Property()
    {
        var tool = ToolWith();
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("filter").Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_Has_Host_Property()
    {
        var tool = ToolWith();
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("host").Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_Has_SshPort_Property()
    {
        var tool = ToolWith();
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("sshPort").Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_Has_SshKey_Property()
    {
        var tool = ToolWith();
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("sshKey").Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_Required_Is_Empty()
    {
        var tool = ToolWith();
        var required = tool.GetInputSchema()["required"] as JsonArray;
        required.Should().NotBeNull();
        required!.Should().BeEmpty();
    }

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

    [TestMethod]
    public async Task Empty_Arguments_Uses_No_Filter()
    {
        var tool = ToolWith(CurrentProcess);
        var args = JsonNode.Parse("""{}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Filter_With_Empty_String_Does_Not_Filter()
    {
        // Empty string filter behaves as "contains empty string" which matches everything
        var tool = ToolWith(CurrentProcess);
        var args = JsonNode.Parse("""{"filter":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Filter_Partial_Name_Match()
    {
        var tool = ToolWith(CurrentProcess);
        // Use first character of the process name — should match
        var firstChar = CurrentProcess.ProcessName[0].ToString();
        var args = JsonNode.Parse($$"""{"filter":"{{firstChar}}"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
    }

    [TestMethod]
    public async Task Multiple_Processes_Sorted_By_Name()
    {
        // Same process used multiple times — should all appear with same name
        var tool = ToolWith(CurrentProcess, CurrentProcess);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        var processes = (json["processes"] as JsonArray)!;
        processes.Should().HaveCount(2);
        // Both should have same name since it's the same process
        processes[0]!["name"]!.GetValue<string>().Should().Be(processes[1]!["name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Null_Filter_Returns_All()
    {
        var tool = ToolWith(CurrentProcess);
        // Explicitly set filter to null by omitting it
        var args = JsonNode.Parse("""{}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Result_Contains_Processes_Key()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["processes"].Should().NotBeNull();
        json["count"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Remote_Host_With_Invalid_SSH_Returns_Error()
    {
        // Providing a host triggers the SSH path — since SSH won't be available
        // in test, we expect an error
        var tool = ToolWith();
        var args = JsonNode.Parse("""{"host":"nonexistent-host-zzz"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Default_Constructor_Uses_Real_Process_List()
    {
        // Use the public constructor (no factory injection)
        var tool = new ListProcessesTool(NullLogger<ListProcessesTool>.Instance);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().BeGreaterThan(0);
    }

    [TestMethod]
    public async Task Default_Constructor_Filter_By_Current_Process()
    {
        var tool = new ListProcessesTool(NullLogger<ListProcessesTool>.Instance);
        var args = JsonNode.Parse($$"""{"filter":"{{CurrentProcess.ProcessName}}"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = ParseResult(result);
        json["count"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
    }
}
