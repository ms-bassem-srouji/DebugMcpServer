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
            new AdapterConfig { Name = "dotnet", Path = "nonexistent_path_1", AdapterID = "coreclr" },
            new AdapterConfig { Name = "python", Path = "nonexistent_path_2", AdapterID = "python" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        var adapters = (json["adapters"] as JsonArray)!;
        adapters.Should().HaveCount(2);
        adapters[0]!["name"]!.GetValue<string>().Should().Be("dotnet");
        adapters[0]!["path"]!.GetValue<string>().Should().Be("nonexistent_path_1");
        adapters[0]!["adapterID"]!.GetValue<string>().Should().Be("coreclr");
        adapters[1]!["name"]!.GetValue<string>().Should().Be("python");
    }

    [TestMethod]
    public async Task Summary_Shows_Count()
    {
        var tool = ToolWith(
            new AdapterConfig { Name = "a", Path = "a.exe" },
            new AdapterConfig { Name = "b", Path = "b.exe" },
            new AdapterConfig { Name = "c", Path = "c.exe" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["summary"]!.GetValue<string>().Should().Contain("of 3 adapters");
    }

    [TestMethod]
    public async Task Empty_Adapters_Returns_Empty_List()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["summary"]!.GetValue<string>().Should().Contain("0 of 0");
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

    [TestMethod]
    public async Task Missing_Adapter_Shows_Not_Found_And_InstallHint()
    {
        var tool = ToolWith(new AdapterConfig
        {
            Name = "dotnet",
            Path = Path.Combine(Path.GetTempPath(), "nonexistent_dir_xyz", "adapter.exe"),
            AdapterID = "coreclr"
        });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        var adapter = json["adapters"]![0]!;
        adapter["status"]!.GetValue<string>().Should().Be("not_found");
        adapter["installHint"]!.GetValue<string>().Should().Contain("netcoredbg");
        adapter["message"]!.GetValue<string>().Should().Contain("not found");
    }

    [TestMethod]
    public async Task Bare_Command_Shows_Bare_Command_Status()
    {
        var tool = ToolWith(new AdapterConfig { Name = "lldb", Path = "lldb-dap", AdapterID = "lldb-dap" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        var adapter = json["adapters"]![0]!;
        adapter["status"]!.GetValue<string>().Should().Be("bare_command");
        adapter["message"]!.GetValue<string>().Should().Contain("PATH");
        adapter["installHint"].Should().BeNull(); // no install hint for bare commands
    }

    [TestMethod]
    public async Task DumpSupport_Shown_When_DumpArgumentName_Set()
    {
        var tool = ToolWith(new AdapterConfig { Name = "cpp", Path = "x.exe", AdapterID = "cppdbg", DumpArgumentName = "coreDumpPath" });

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["adapters"]![0]!["dumpSupport"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task DotnetDumpAnalysis_Shows_BuiltIn()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["dotnetDumpAnalysis"].Should().NotBeNull();
        json["dotnetDumpAnalysis"]!["status"]!.GetValue<string>().Should().Be("built-in");
    }

    [TestMethod]
    public async Task ConfigLocation_Is_Included()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["configLocation"].Should().NotBeNull();
        json["configLocation"]!.GetValue<string>().Should().Contain("appsettings.json");
    }

    [TestMethod]
    public async Task Hint_Is_Included()
    {
        var tool = ToolWith();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = ParseResult(result);
        json["hint"].Should().NotBeNull();
        json["hint"]!.GetValue<string>().Should().Contain("config");
    }

    [TestMethod]
    public void GetInstallHint_Returns_Hint_For_Known_Adapters()
    {
        ListAdaptersTool.GetInstallHint("dotnet").Should().Contain("netcoredbg");
        ListAdaptersTool.GetInstallHint("python").Should().Contain("debugpy");
        ListAdaptersTool.GetInstallHint("cpp").Should().Contain("cpptools");
        ListAdaptersTool.GetInstallHint("lldb").Should().Contain("lldb-dap");
        ListAdaptersTool.GetInstallHint("node").Should().Contain("js-debug");
        ListAdaptersTool.GetInstallHint("cppvsdbg").Should().Contain("Visual Studio");
    }

    [TestMethod]
    public void GetInstallHint_Returns_Generic_For_Unknown()
    {
        ListAdaptersTool.GetInstallHint("custom").Should().Contain("appsettings.json");
    }

    [TestMethod]
    public void ResolveStatus_FullPath_NotFound()
    {
        var (status, message) = ListAdaptersTool.ResolveStatus(Path.Combine(Path.GetTempPath(), "nonexistent_xyz.exe"));
        status.Should().Be("not_found");
        message.Should().Contain("not found");
    }

    [TestMethod]
    public void ResolveStatus_BareCommand()
    {
        var (status, message) = ListAdaptersTool.ResolveStatus("netcoredbg");
        status.Should().Be("bare_command");
        message.Should().Contain("PATH");
    }

    [TestMethod]
    public void ResolveStatus_Empty()
    {
        var (status, _) = ListAdaptersTool.ResolveStatus("");
        status.Should().Be("not_configured");
    }

    [TestMethod]
    public void ResolveStatus_Null()
    {
        var (status, _) = ListAdaptersTool.ResolveStatus(null);
        status.Should().Be("not_configured");
    }
}
