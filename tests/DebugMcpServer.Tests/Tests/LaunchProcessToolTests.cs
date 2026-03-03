using System.Text.Json.Nodes;
using DebugMcpServer.Options;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class LaunchProcessToolTests
{
    private static LaunchProcessTool ToolWith(DebugOptions? options = null)
    {
        var opts = options ?? new DebugOptions();
        return new LaunchProcessTool(
            Fakes.FakeSessionRegistry.Empty(),
            Microsoft.Extensions.Options.Options.Create(opts),
            NullLogger<LaunchProcessTool>.Instance);
    }

    [TestMethod]
    public void Name_Is_launch_process()
    {
        var tool = ToolWith();
        tool.Name.Should().Be("launch_process");
    }

    [TestMethod]
    public void Description_Is_Not_Empty()
    {
        var tool = ToolWith();
        tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void InputSchema_Has_Program_As_Required()
    {
        var tool = ToolWith();
        var schema = tool.GetInputSchema();

        var required = schema["required"] as JsonArray;
        required.Should().NotBeNull();
        required!.Select(r => r!.GetValue<string>()).Should().Contain("program");
    }

    [TestMethod]
    public void InputSchema_Has_All_Expected_Properties()
    {
        var tool = ToolWith();
        var schema = tool.GetInputSchema();

        var props = schema["properties"] as JsonObject;
        props.Should().NotBeNull();
        props!.ContainsKey("program").Should().BeTrue();
        props.ContainsKey("args").Should().BeTrue();
        props.ContainsKey("cwd").Should().BeTrue();
        props.ContainsKey("adapter").Should().BeTrue();
        props.ContainsKey("adapterPath").Should().BeTrue();
        props.ContainsKey("stopAtEntry").Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_Args_Is_Array_Of_Strings()
    {
        var tool = ToolWith();
        var schema = tool.GetInputSchema();

        var argsProp = schema["properties"]!["args"]!;
        argsProp["type"]!.GetValue<string>().Should().Be("array");
        argsProp["items"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [TestMethod]
    public void InputSchema_StopAtEntry_Is_Boolean()
    {
        var tool = ToolWith();
        var schema = tool.GetInputSchema();

        var prop = schema["properties"]!["stopAtEntry"]!;
        prop["type"]!.GetValue<string>().Should().Be("boolean");
    }

    [TestMethod]
    public async Task Missing_Program_Returns_Error()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["error"].Should().NotBeNull();
        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        result["error"]!["message"]!.GetValue<string>().Should().Contain("program");
    }

    [TestMethod]
    public async Task Empty_Program_Returns_Error()
    {
        var tool = ToolWith();
        var args = JsonNode.Parse("""{"program": ""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"].Should().NotBeNull();
        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Unknown_Adapter_Name_Returns_Error()
    {
        var options = new DebugOptions();
        options.Adapters.Add(new AdapterConfig { Name = "dotnet", Path = "netcoredbg.exe", AdapterID = "coreclr" });
        var tool = ToolWith(options);
        var args = JsonNode.Parse("""{"program": "test.dll", "adapter": "nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("Unknown adapter");
        text.Should().Contain("nonexistent");
        text.Should().Contain("dotnet");
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Explicit_AdapterPath_That_Does_Not_Exist_Returns_Error()
    {
        // Use an explicit adapterPath to a non-existent file so the legacy resolution
        // does not accidentally find a real adapter on the test machine
        var options = new DebugOptions();
        options.Adapters.Add(new AdapterConfig
        {
            Name = "fake",
            Path = @"C:\nonexistent_path_abc123\fake_adapter.exe",
            AdapterID = "coreclr"
        });
        var tool = ToolWith(options);
        var args = JsonNode.Parse("""{"program": "test.dll", "adapter": "fake"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        // The adapter path doesn't exist, so legacy fallback runs, and if no adapter is found
        // on the machine it returns the "Could not find" error. If a real adapter IS found,
        // it will fail to launch since "test.dll" doesn't exist — either way, isError should be true.
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Invalid_AdapterPath_Returns_Error()
    {
        var tool = ToolWith(new DebugOptions());
        var args = JsonNode.Parse("""{"program": "test.dll", "adapterPath": "C:\\nonexistent_xyz\\adapter.exe"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        // Either "Could not find a debug adapter" or a launch failure — always an error
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }
}
