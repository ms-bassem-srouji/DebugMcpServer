using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ToolBaseTests
{
    // Minimal subclass to expose protected static methods
    private sealed class TestTool : ToolBase
    {
        public static JsonNode TextResult(JsonNode? id, string text, bool isError = false)
            => CreateTextResult(id, text, isError);
        public static JsonNode ErrorResult(JsonNode? id, int code, string message)
            => CreateErrorResponse(id, code, message);
        public static bool GetStr(JsonNode? args, string key, out string val, out string? err)
            => TryGetString(args, key, out val, out err);
        public static bool GetInt(JsonNode? args, string key, out int val, out string? err)
            => TryGetInt(args, key, out val, out err);
    }

    [TestMethod]
    public void CreateTextResult_ReturnsCorrectJsonRpcStructure()
    {
        var result = TestTool.TextResult(JsonValue.Create(1), "hello");
        result["jsonrpc"]!.GetValue<string>().Should().Be("2.0");
        result["id"]!.GetValue<int>().Should().Be(1);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
        result["result"]!["content"]![0]!["type"]!.GetValue<string>().Should().Be("text");
        result["result"]!["content"]![0]!["text"]!.GetValue<string>().Should().Be("hello");
    }

    [TestMethod]
    public void CreateTextResult_WithIsError_SetsIsErrorTrue()
    {
        var result = TestTool.TextResult(JsonValue.Create(1), "oops", isError: true);
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public void CreateTextResult_WithNullId_SerializesNullId()
    {
        var result = TestTool.TextResult(null, "hello");
        // JsonNode.Parse("null") returns C# null, so result["id"] is null
        // But the key "id" exists in the JSON object
        var obj = result as JsonObject;
        obj.Should().NotBeNull();
        obj!.ContainsKey("id").Should().BeTrue();
    }

    [TestMethod]
    public void CreateErrorResponse_ReturnsErrorStructure()
    {
        var result = TestTool.ErrorResult(JsonValue.Create(5), -32602, "Invalid params");
        result["jsonrpc"]!.GetValue<string>().Should().Be("2.0");
        result["id"]!.GetValue<int>().Should().Be(5);
        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        result["error"]!["message"]!.GetValue<string>().Should().Be("Invalid params");
    }

    [TestMethod]
    [DataRow("key", "value", true)]
    [DataRow("key", "", false)]
    [DataRow("other", "anything", false)]
    public void TryGetString_ValidatesPresenceAndNonEmpty(string jsonKey, string jsonValue, bool expectedResult)
    {
        var args = new JsonObject { [jsonKey] = jsonValue };
        var ok = TestTool.GetStr(args, "key", out var val, out var err);
        ok.Should().Be(expectedResult);
        if (expectedResult) val.Should().Be(jsonValue);
        else err.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void TryGetString_NullArguments_ReturnsFalse()
    {
        var ok = TestTool.GetStr(null, "key", out _, out var err);
        ok.Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void TryGetInt_ReturnsValue()
    {
        var args = new JsonObject { ["n"] = 42 };
        var ok = TestTool.GetInt(args, "n", out var val, out _);
        ok.Should().BeTrue();
        val.Should().Be(42);
    }

    [TestMethod]
    public void TryGetInt_MissingKey_ReturnsFalse()
    {
        var ok = TestTool.GetInt(new JsonObject(), "n", out _, out var err);
        ok.Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void TryGetInt_NullArguments_ReturnsFalse()
    {
        var ok = TestTool.GetInt(null, "n", out _, out var err);
        ok.Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
    }
}
