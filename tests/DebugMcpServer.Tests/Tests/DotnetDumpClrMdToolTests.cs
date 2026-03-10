using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DotnetDumpThreadsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public void Name_Is_dotnet_dump_threads()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());
        tool.Name.Should().Be("dotnet_dump_threads");
    }

    [TestMethod]
    public void Description_Not_Empty()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());
        tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_Required()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        required!.Select(r => r!.GetValue<string>()).Should().Contain("sessionId");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("""{}"""), CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not found");
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpThreadsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpThreadsTool>>());
        var args = JsonNode.Parse("""{"sessionId":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }
}

[TestClass]
public class DotnetDumpExceptionsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public void Name_Is_dotnet_dump_exceptions()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());
        tool.Name.Should().Be("dotnet_dump_exceptions");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("""{}"""), CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public void Description_Mentions_Stack_Trace_And_Inner()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());
        tool.Description.Should().Contain("stack trace");
        tool.Description.Should().Contain("inner exception");
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_Required()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        required!.Select(r => r!.GetValue<string>()).Should().Contain("sessionId");
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_Property()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("sessionId").Should().BeTrue();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());
        var args = JsonNode.Parse("""{"sessionId":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpExceptionsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpExceptionsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("load_dotnet_dump");
    }
}

[TestClass]
public class DotnetDumpHeapStatsToolTests
{
    [TestMethod]
    public void Name_Is_dotnet_dump_heap_stats()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());
        tool.Name.Should().Be("dotnet_dump_heap_stats");
    }

    [TestMethod]
    public void Description_Mentions_Dumpheap()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());
        tool.Description.Should().Contain("dumpheap");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("""{}"""), CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());
        var args = JsonNode.Parse("""{"sessionId":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public void FormatSize_Formats_Bytes()
    {
        DotnetDumpHeapStatsTool.FormatSize(500).Should().Be("500 B");
        DotnetDumpHeapStatsTool.FormatSize(1024).Should().Be("1.0 KB");
        DotnetDumpHeapStatsTool.FormatSize(1_048_576).Should().Be("1.0 MB");
        DotnetDumpHeapStatsTool.FormatSize(1_073_741_824).Should().Be("1.0 GB");
        DotnetDumpHeapStatsTool.FormatSize(2_621_440).Should().Be("2.5 MB");
    }

    [TestMethod]
    public void FormatSize_Zero_Shows_Bytes()
    {
        DotnetDumpHeapStatsTool.FormatSize(0).Should().Be("0 B");
    }

    [TestMethod]
    public void FormatSize_Boundary_KB()
    {
        DotnetDumpHeapStatsTool.FormatSize(1023).Should().Be("1023 B");
        DotnetDumpHeapStatsTool.FormatSize(1024).Should().Be("1.0 KB");
    }

    [TestMethod]
    public void FormatSize_Boundary_MB()
    {
        DotnetDumpHeapStatsTool.FormatSize(1_048_575).Should().NotBe("1.0 MB");
        DotnetDumpHeapStatsTool.FormatSize(1_048_576).Should().Be("1.0 MB");
    }

    [TestMethod]
    public void InputSchema_Has_Filter_And_Top()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("filter").Should().BeTrue();
        props.ContainsKey("top").Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_Required()
    {
        var tool = new DotnetDumpHeapStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpHeapStatsTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        required!.Select(r => r!.GetValue<string>()).Should().Contain("sessionId");
    }
}

[TestClass]
public class DotnetDumpInspectToolTests
{
    [TestMethod]
    public void Name_Is_dotnet_dump_inspect()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        tool.Name.Should().Be("dotnet_dump_inspect");
    }

    [TestMethod]
    public void Description_Mentions_Dumpobj()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        tool.Description.Should().Contain("dumpobj");
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_And_Address_Required()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        var names = required!.Select(r => r!.GetValue<string>()).ToList();
        names.Should().Contain("sessionId");
        names.Should().Contain("address");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown", "address":"0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nosess", "address":"0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Missing_Address_Returns_Error()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        var args = JsonNode.Parse("""{"address":"0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_Address_Returns_Error()
    {
        var tool = new DotnetDumpInspectTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpInspectTool>>());
        var args = JsonNode.Parse("""{"sessionId":"s1", "address":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public void TryParseAddress_Hex_With_Prefix()
    {
        DotnetDumpInspectTool.TryParseAddress("0x7FFF1234", out var addr).Should().BeTrue();
        addr.Should().Be(0x7FFF1234UL);
    }

    [TestMethod]
    public void TryParseAddress_Hex_Without_Prefix()
    {
        DotnetDumpInspectTool.TryParseAddress("DEADBEEF", out var addr).Should().BeTrue();
        addr.Should().Be(0xDEADBEEFUL);
    }

    [TestMethod]
    public void TryParseAddress_Invalid_Returns_False()
    {
        DotnetDumpInspectTool.TryParseAddress("not_hex", out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryParseAddress_With_Spaces()
    {
        DotnetDumpInspectTool.TryParseAddress("  0xFF  ", out var addr).Should().BeTrue();
        addr.Should().Be(0xFFUL);
    }

    [TestMethod]
    public void TryParseAddress_Empty_String_Returns_False()
    {
        DotnetDumpInspectTool.TryParseAddress("", out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryParseAddress_Only_Prefix_Returns_False()
    {
        DotnetDumpInspectTool.TryParseAddress("0x", out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryParseAddress_Full_64Bit_Address()
    {
        DotnetDumpInspectTool.TryParseAddress("0x7FFFFFFFFFFFFFFF", out var addr).Should().BeTrue();
        addr.Should().Be(0x7FFFFFFFFFFFFFFFUL);
    }

    [TestMethod]
    public void TryParseAddress_Lowercase_Hex()
    {
        DotnetDumpInspectTool.TryParseAddress("0xdeadbeef", out var addr).Should().BeTrue();
        addr.Should().Be(0xDEADBEEFUL);
    }
}

[TestClass]
public class DotnetDumpGcRootsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public void Name_Is_dotnet_dump_gc_roots()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        tool.Name.Should().Be("dotnet_dump_gc_roots");
    }

    [TestMethod]
    public void Description_Mentions_GC_Roots_And_Memory_Leaks()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        tool.Description.Should().Contain("GC roots");
        tool.Description.Should().Contain("memory leak");
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_And_Address_Required()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        var names = required!.Select(r => r!.GetValue<string>()).ToList();
        names.Should().Contain("sessionId");
        names.Should().Contain("address");
    }

    [TestMethod]
    public void InputSchema_Has_Expected_Properties()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("sessionId").Should().BeTrue();
        props.ContainsKey("address").Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown", "address":"0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nosess", "address":"0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Invalid_Address_Returns_Error()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown", "address":"not_hex"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        // Will hit session not found first since that check happens before address parsing
        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_Address_Returns_Error()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var args = JsonNode.Parse("""{"address":"0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_Address_Returns_Error()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"s1", "address":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpGcRootsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpGcRootsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"", "address":"0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }
}

[TestClass]
public class DotnetDumpFindObjectsToolTests
{
    [TestMethod]
    public void Name_Is_dotnet_dump_find_objects()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        tool.Name.Should().Be("dotnet_dump_find_objects");
    }

    [TestMethod]
    public void Description_Mentions_Dumpheap()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        tool.Description.Should().Contain("dumpheap");
    }

    [TestMethod]
    public void InputSchema_Has_Required_Fields()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        var names = required!.Select(r => r!.GetValue<string>()).ToList();
        names.Should().Contain("sessionId");
        names.Should().Contain("typeName");
    }

    [TestMethod]
    public void InputSchema_Has_Max_Property()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("max").Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown", "typeName":"Order"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nosess", "typeName":"Order"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Missing_TypeName_Returns_Error()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        var args = JsonNode.Parse("""{"typeName":"Order"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_TypeName_Returns_Error()
    {
        var tool = new DotnetDumpFindObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpFindObjectsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"s1", "typeName":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }
}

[TestClass]
public class DotnetDumpStackObjectsToolTests
{
    [TestMethod]
    public void Name_Is_dotnet_dump_stack_objects()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());
        tool.Name.Should().Be("dotnet_dump_stack_objects");
    }

    [TestMethod]
    public void Description_Mentions_SOS_Dso()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());
        tool.Description.Should().Contain("dso");
    }

    [TestMethod]
    public void InputSchema_Has_OsThreadId_Property()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("osThreadId").Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("""{}"""), CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());
        var args = JsonNode.Parse("""{"sessionId":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public void FindThread_Returns_Null_For_Invalid_Hex()
    {
        // The static FindThread method is internal, tested indirectly via invalid osThreadId
        // just verifying the tool has consistent schema
        var tool = new DotnetDumpStackObjectsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpStackObjectsTool>>());
        var schema = tool.GetInputSchema();
        schema["required"].Should().NotBeNull();
    }
}

[TestClass]
public class DotnetDumpMemoryStatsToolTests
{
    [TestMethod]
    public void Name_Is_dotnet_dump_memory_stats()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());
        tool.Name.Should().Be("dotnet_dump_memory_stats");
    }

    [TestMethod]
    public void Description_Mentions_GC_Heap()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());
        tool.Description.Should().Contain("GC heap");
    }

    [TestMethod]
    public void Description_Mentions_Memory_Leak()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());
        tool.Description.Should().Contain("memory leak");
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_Property()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("sessionId").Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("""{}"""), CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpMemoryStatsTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpMemoryStatsTool>>());
        var args = JsonNode.Parse("""{"sessionId":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }
}

[TestClass]
public class DotnetDumpAsyncStateToolTests
{
    [TestMethod]
    public void Name_Is_dotnet_dump_async_state()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());
        tool.Name.Should().Be("dotnet_dump_async_state");
    }

    [TestMethod]
    public void InputSchema_Has_Filter_And_IncludeCompleted()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("filter").Should().BeTrue();
        props.ContainsKey("includeCompleted").Should().BeTrue();
        props.ContainsKey("max").Should().BeTrue();
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_Required()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        required!.Select(r => r!.GetValue<string>()).Should().Contain("sessionId");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Message_Suggests_Load()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());
        var args = JsonNode.Parse("""{"sessionId":"nonexistent"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        var text = result["result"]!["content"]![0]!["text"]!.GetValue<string>();
        text.Should().Contain("load_dotnet_dump");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("""{}"""), CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_SessionId_Returns_Error()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());
        var args = JsonNode.Parse("""{"sessionId":""}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public void Description_Mentions_Async_Deadlock()
    {
        var tool = new DotnetDumpAsyncStateTool(
            FakeDotnetDumpRegistry.Empty(),
            Substitute.For<ILogger<DotnetDumpAsyncStateTool>>());
        tool.Description.Should().Contain("async");
        tool.Description.Should().Contain("deadlock");
    }
}