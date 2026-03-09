using System.Text.Json.Nodes;
using DebugMcpServer.Options;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class LoadDumpFileToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static DebugOptions OptionsWithDumpAdapter() => new()
    {
        Adapters =
        [
            new AdapterConfig
            {
                Name = "cpp",
                Path = "OpenDebugAD7",
                AdapterID = "cppdbg",
                DumpArgumentName = "coreDumpPath"
            },
            new AdapterConfig
            {
                Name = "dotnet",
                Path = "netcoredbg",
                AdapterID = "coreclr"
                // No DumpArgumentName — doesn't support dumps
            }
        ]
    };

    private static LoadDumpFileTool ToolWith(DebugOptions? options = null)
    {
        var opts = options ?? OptionsWithDumpAdapter();
        return new LoadDumpFileTool(
            FakeSessionRegistry.Empty(),
            Microsoft.Extensions.Options.Options.Create(opts),
            NullLogger<LoadDumpFileTool>.Instance);
    }

    [TestMethod]
    public void Name_Is_load_dump_file()
    {
        ToolWith().Name.Should().Be("load_dump_file");
    }

    [TestMethod]
    public void Description_Is_Not_Empty()
    {
        ToolWith().Description.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void InputSchema_Has_DumpPath_As_Required()
    {
        var schema = ToolWith().GetInputSchema();
        var required = schema["required"] as JsonArray;
        required.Should().NotBeNull();
        required!.Select(r => r!.GetValue<string>()).Should().Contain("dumpPath");
    }

    [TestMethod]
    public void InputSchema_Has_All_Expected_Properties()
    {
        var schema = ToolWith().GetInputSchema();
        var props = schema["properties"] as JsonObject;
        props.Should().NotBeNull();
        props!.ContainsKey("dumpPath").Should().BeTrue();
        props.ContainsKey("program").Should().BeTrue();
        props.ContainsKey("adapter").Should().BeTrue();
        props.ContainsKey("adapterPath").Should().BeTrue();
        props.ContainsKey("sourceMapping").Should().BeTrue();
        props.ContainsKey("host").Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_DumpPath_Returns_Error()
    {
        var tool = ToolWith();
        var args = JsonNode.Parse("""{}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"].Should().NotBeNull();
        result["error"]!["message"]!.GetValue<string>().Should().Contain("dumpPath");
    }

    [TestMethod]
    public async Task Unknown_Adapter_Returns_Error()
    {
        var tool = ToolWith();
        var args = JsonNode.Parse("""{"dumpPath": "/tmp/core", "adapter": "nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Unknown adapter");
        GetText(result).Should().Contain("nonexistent");
    }

    [TestMethod]
    public async Task Adapter_Without_DumpSupport_Returns_Error()
    {
        var tool = ToolWith();
        var args = JsonNode.Parse("""{"dumpPath": "/tmp/core", "adapter": "dotnet"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("does not support dump file debugging");
        GetText(result).Should().Contain("cpp"); // should list adapters with dump support
    }

    [TestMethod]
    public async Task No_Adapter_With_DumpSupport_Returns_Error()
    {
        var options = new DebugOptions
        {
            Adapters =
            [
                new AdapterConfig { Name = "dotnet", Path = "netcoredbg", AdapterID = "coreclr" }
            ]
        };
        var tool = ToolWith(options);
        var args = JsonNode.Parse("""{"dumpPath": "/tmp/core"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("does not support dump file debugging");
    }

    [TestMethod]
    public async Task Local_Dump_File_Not_Found_Returns_Error()
    {
        var tool = ToolWith();
        var nonexistentPath = Path.Combine(Path.GetTempPath(), "nonexistent_dump_file_abc123");
        var args = JsonNode.Parse($$"""{"dumpPath": "{{nonexistentPath.Replace("\\", "\\\\")}}", "adapter": "cpp"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Dump file not found");
    }

    [TestMethod]
    public async Task Auto_Selects_First_Adapter_With_Dump_Support()
    {
        // With no adapter specified and no matching file, it should auto-select 'cpp' (which has DumpArgumentName)
        // but then fail at Process.Start since the adapter doesn't exist — the important thing is it
        // doesn't fail at "no dump support" validation
        var tool = ToolWith();
        var nonexistentPath = Path.Combine(Path.GetTempPath(), "nonexistent_dump_file_abc123");
        var args = JsonNode.Parse($$"""{"dumpPath": "{{nonexistentPath.Replace("\\", "\\\\")}}}"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        // Should fail at "file not found", NOT at "no dump support"
        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Dump file not found");
    }

    [TestMethod]
    public async Task Remote_Session_Skips_Local_File_Validation()
    {
        // When host is specified, should NOT check File.Exists locally
        // It will fail at Process.Start (adapter doesn't exist) but NOT at "file not found"
        var tool = ToolWith();
        var args = JsonNode.Parse("""{"dumpPath": "/remote/core.12345", "adapter": "cpp", "host": "user@remotehost"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        // Should fail at adapter launch, NOT at "Dump file not found"
        GetText(result).Should().NotContain("Dump file not found");
    }

    [TestMethod]
    public async Task Empty_Adapters_With_AdapterPath_And_No_DumpSupport_Returns_Error()
    {
        var options = new DebugOptions { Adapters = [] };
        var tool = ToolWith(options);
        var args = JsonNode.Parse("""{"dumpPath": "/tmp/core", "adapterPath": "/usr/bin/some-adapter"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("does not support dump file debugging");
    }

    [TestMethod]
    public async Task No_Adapter_Path_Returns_Error()
    {
        var options = new DebugOptions { Adapters = [] };
        var tool = ToolWith(options);
        var args = JsonNode.Parse("""{"dumpPath": "/tmp/core"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_DumpPath_Has_No_Extension_Restriction()
    {
        var schema = ToolWith().GetInputSchema();
        var dumpPathDesc = schema["properties"]!["dumpPath"]!["description"]!.GetValue<string>();
        // Should mention cross-platform dump formats
        dumpPathDesc.Should().Contain("core");
        dumpPathDesc.Should().Contain(".dmp");
    }

    [TestMethod]
    public async Task Remote_Without_RemotePath_Uses_AdapterPath()
    {
        var tool = ToolWith();
        var args = JsonNode.Parse("""{"dumpPath": "/remote/core", "adapter": "cpp", "host": "user@host"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        // Should attempt to launch (and fail at Process.Start), not fail at "no remote path"
        IsError(result).Should().BeTrue();
        GetText(result).Should().NotContain("No adapter path resolved for remote");
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["error"].Should().NotBeNull();
    }
}
