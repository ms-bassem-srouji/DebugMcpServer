using DebugMcpServer.Dap;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class SshHelperTests
{
    [TestMethod]
    public void Creates_Ssh_Command_With_Host_And_Default_Port()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("user@remotehost", 22, null, "netcoredbg --interpreter=vscode");

        psi.FileName.Should().Be("ssh");
        psi.Arguments.Should().Contain("user@remotehost");
        psi.Arguments.Should().Contain("-p 22");
        psi.Arguments.Should().Contain("netcoredbg --interpreter=vscode");
    }

    [TestMethod]
    public void Includes_Custom_Port()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("host", 2222, null, "cmd");

        psi.Arguments.Should().Contain("-p 2222");
    }

    [TestMethod]
    public void Includes_Key_Path()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("host", 22, "/home/user/.ssh/id_rsa", "cmd");

        psi.Arguments.Should().Contain("-i /home/user/.ssh/id_rsa");
    }

    [TestMethod]
    public void Includes_Both_Port_And_Key()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("user@host", 3333, "C:\\keys\\mykey.pem", "adapter --interpreter=vscode");

        psi.Arguments.Should().Contain("-p 3333");
        psi.Arguments.Should().Contain("-i C:\\keys\\mykey.pem");
        psi.Arguments.Should().Contain("user@host");
        psi.Arguments.Should().Contain("adapter --interpreter=vscode");
    }

    [TestMethod]
    public void Omits_Key_Flag_When_Null()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("host", 22, null, "cmd");

        psi.Arguments.Should().NotContain("-i");
    }

    [TestMethod]
    public void Omits_Key_Flag_When_Empty()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("host", 22, "", "cmd");

        psi.Arguments.Should().NotContain("-i");
    }

    [TestMethod]
    public void Sets_ProcessStartInfo_Properties_Correctly()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("host", 22, null, "cmd");

        psi.UseShellExecute.Should().BeFalse();
        psi.RedirectStandardInput.Should().BeTrue();
        psi.RedirectStandardOutput.Should().BeTrue();
        psi.RedirectStandardError.Should().BeTrue();
        psi.CreateNoWindow.Should().BeTrue();
    }

    [TestMethod]
    public void Includes_StrictHostKeyChecking_And_BatchMode()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("host", 22, null, "cmd");

        psi.Arguments.Should().Contain("StrictHostKeyChecking=accept-new");
        psi.Arguments.Should().Contain("BatchMode=yes");
    }

    [TestMethod]
    public void Host_With_Username_Is_Preserved()
    {
        var psi = SshHelper.CreateSshProcessStartInfo("admin@192.168.1.100", 22, null, "/usr/bin/netcoredbg --interpreter=vscode");

        psi.Arguments.Should().Contain("admin@192.168.1.100");
        psi.Arguments.Should().Contain("/usr/bin/netcoredbg --interpreter=vscode");
    }
}
