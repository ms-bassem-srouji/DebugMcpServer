using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class GetSourceToolTests : IDisposable
{
    private readonly string _tempFile;

    public GetSourceToolTests()
    {
        _tempFile = Path.GetTempFileName();
        var lines = Enumerable.Range(1, 20).Select(i => $"Line {i}").ToArray();
        File.WriteAllLines(_tempFile, lines);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private (GetSourceTool tool, FakeSession session) CreateToolWithSession(string sessionId = "sess1")
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        var registry = FakeSessionRegistry.WithSession(sessionId, session);
        var logger = Substitute.For<ILogger<GetSourceTool>>();
        return (new GetSourceTool(registry, logger), session);
    }

    private JsonNode MakeArgs(string sessionId, string? file = null, int? line = null, int? linesAround = null)
    {
        var obj = new JsonObject { ["sessionId"] = sessionId };
        if (file != null) obj["file"] = file;
        if (line != null) obj["line"] = line.Value;
        if (linesAround != null) obj["linesAround"] = linesAround.Value;
        return obj;
    }

    [TestMethod]
    public async Task Returns_Source_With_Explicit_File_And_Line()
    {
        var (tool, _) = CreateToolWithSession();

        var args = MakeArgs("sess1", file: _tempFile, line: 10, linesAround: 3);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["currentLine"]!.GetValue<int>().Should().Be(10);
        parsed["startLine"]!.GetValue<int>().Should().Be(7);
        parsed["endLine"]!.GetValue<int>().Should().Be(13);

        var source = parsed["source"]!.GetValue<string>();
        source.Should().Contain(">>> ");
        source.Should().Contain("Line 10");
        source.Should().Contain("Line 7");
        source.Should().Contain("Line 13");
        source.Should().NotContain("Line 6");
        source.Should().NotContain("Line 14");
    }

    [TestMethod]
    public async Task Marks_Current_Line_With_Arrow()
    {
        var (tool, _) = CreateToolWithSession();

        var args = MakeArgs("sess1", file: _tempFile, line: 5, linesAround: 2);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        var source = parsed["source"]!.GetValue<string>();
        var lines = source.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var markedLine = lines.Single(l => l.Contains(">>>"));
        markedLine.Should().Contain("Line 5");

        lines.Where(l => !l.Contains(">>>")).Should().AllSatisfy(l => l.Should().StartWith("   "));
    }

    [TestMethod]
    public async Task Clamps_Start_To_Line_1()
    {
        var (tool, _) = CreateToolWithSession();

        var args = MakeArgs("sess1", file: _tempFile, line: 1, linesAround: 5);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["startLine"]!.GetValue<int>().Should().Be(1);
        parsed["endLine"]!.GetValue<int>().Should().Be(6);
    }

    [TestMethod]
    public async Task Clamps_End_To_File_Length()
    {
        var (tool, _) = CreateToolWithSession();

        var args = MakeArgs("sess1", file: _tempFile, line: 20, linesAround: 5);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["startLine"]!.GetValue<int>().Should().Be(15);
        parsed["endLine"]!.GetValue<int>().Should().Be(20);
    }

    [TestMethod]
    public async Task Auto_Resolves_File_And_Line_From_Stack_Frame()
    {
        var (tool, session) = CreateToolWithSession();
        var stackResponse = new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 1,
                    ["name"] = "Main",
                    ["line"] = 10,
                    ["column"] = 1,
                    ["source"] = new JsonObject
                    {
                        ["path"] = _tempFile,
                        ["name"] = "test.cs"
                    }
                }
            },
            ["totalFrames"] = 1
        };
        session.SetupRequest("stackTrace", stackResponse);

        var args = MakeArgs("sess1", linesAround: 2);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["currentLine"]!.GetValue<int>().Should().Be(10);
        parsed["file"]!.GetValue<string>().Should().Be(_tempFile);
    }

    [TestMethod]
    public async Task Returns_Error_When_No_Stack_Frames()
    {
        var (tool, session) = CreateToolWithSession();
        session.SetupRequest("stackTrace", JsonNode.Parse("""
        {
            "stackFrames": [],
            "totalFrames": 0
        }
        """)!);

        var args = MakeArgs("sess1");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("No stack frames");
    }

    [TestMethod]
    public async Task Returns_Error_When_File_Not_Found()
    {
        var (tool, _) = CreateToolWithSession();

        var args = MakeArgs("sess1", file: @"C:\nonexistent\file.cs", line: 1);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("file.cs");
    }

    [TestMethod]
    public async Task Returns_Error_When_Session_Not_Found()
    {
        var (tool, _) = CreateToolWithSession();

        var args = MakeArgs("nonexistent");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not found");
    }

    [TestMethod]
    public async Task Returns_Error_When_SessionId_Missing()
    {
        var (tool, _) = CreateToolWithSession();

        var args = JsonNode.Parse("""{}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"].Should().NotBeNull();
        result["error"]!["message"]!.GetValue<string>().Should().Contain("sessionId");
    }

    [TestMethod]
    public async Task Default_LinesAround_Is_10()
    {
        var (tool, _) = CreateToolWithSession();

        var args = MakeArgs("sess1", file: _tempFile, line: 10);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["startLine"]!.GetValue<int>().Should().BeLessThanOrEqualTo(1);
        parsed["endLine"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(20);
    }

    [TestMethod]
    public async Task Does_Not_Require_DAP_When_File_And_Line_Provided()
    {
        var (tool, session) = CreateToolWithSession();

        var args = MakeArgs("sess1", file: _tempFile, line: 5);
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.SentRequests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DAP_Error_Returns_IsError()
    {
        var (tool, session) = CreateToolWithSession();
        session.SetupRequestError("stackTrace", "thread not suspended");

        var args = MakeArgs("sess1");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("thread not suspended");
    }
}
