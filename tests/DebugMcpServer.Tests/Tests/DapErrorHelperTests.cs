using DebugMcpServer.Dap;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DapErrorHelperTests
{
    [TestMethod]
    [DataRow("next")]
    [DataRow("stepIn")]
    [DataRow("stepOut")]
    public void Step_Commands_With_E_FAIL_Suggest_Continue(string command)
    {
        var result = DapErrorHelper.Humanize(command, "Failed command 'next' : 0x80004005");

        result.Should().Contain("native");
        result.Should().Contain("continue_execution");
    }

    [TestMethod]
    public void Scopes_With_E_FAIL_Suggests_Fresh_FrameIds()
    {
        var result = DapErrorHelper.Humanize("scopes", "Failed command 'scopes' : 0x80004005");

        result.Should().Contain("Frame ID is no longer valid");
        result.Should().Contain("get_callstack");
    }

    [TestMethod]
    public void Variables_With_E_FAIL_Suggests_Fresh_FrameIds()
    {
        var result = DapErrorHelper.Humanize("variables", "Failed command 'variables' : 0x80004005");

        result.Should().Contain("Frame ID is no longer valid");
    }

    [TestMethod]
    public void StackTrace_With_Thread_Error_Suggests_ChangeThread()
    {
        var result = DapErrorHelper.Humanize("stackTrace", "Failed command 'stackTrace' : 0x80131302");

        result.Should().Contain("list_threads");
        result.Should().Contain("change_thread");
    }

    [TestMethod]
    public void Unknown_Command_With_E_FAIL_Returns_Generic_Message()
    {
        var result = DapErrorHelper.Humanize("someCommand", "Failed : 0x80004005");

        result.Should().Contain("E_FAIL");
        result.Should().Contain("someCommand");
    }

    [TestMethod]
    public void Unknown_Error_Code_Passes_Through()
    {
        var raw = "Some completely unknown error message";
        var result = DapErrorHelper.Humanize("next", raw);

        result.Should().Be(raw);
    }

    [TestMethod]
    public void Arbitrary_Command_With_0x80131302_Suggests_ChangeThread()
    {
        var result = DapErrorHelper.Humanize("evaluate", "Failed : 0x80131302");

        result.Should().Contain("change_thread");
    }
}
