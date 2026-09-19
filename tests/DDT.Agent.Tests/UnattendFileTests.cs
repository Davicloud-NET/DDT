using System.Text;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class UnattendFileTests : IDisposable
{
    private readonly string _windows = Directory.CreateTempSubdirectory("ddt-unattend-").FullName;

    public void Dispose() => Directory.Delete(_windows, recursive: true);

    [Fact]
    public async Task WritesTheAnswerFileWhereSetupLooksAndCreatesTheCleanupScript()
    {
        await UnattendFile.WriteAsync(_windows, TestImage.Unattend, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_windows, "Windows", "Panther", "unattend.xml"), UnattendFile.PathIn(_windows));
        Assert.Equal(TestImage.Unattend, await File.ReadAllTextAsync(UnattendFile.PathIn(_windows), TestContext.Current.CancellationToken));
        Assert.Equal(
            "del /q /f \"%WINDIR%\\Panther\\unattend.xml\"\r\n",
            await File.ReadAllTextAsync(Path.Combine(_windows, "Windows", "Setup", "Scripts", "SetupComplete.cmd"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutsTheCleanupBeforeTheImagesOwnSetupCompleteAndKeepsItsBytes()
    {
        string scripts = Path.Combine(_windows, "Windows", "Setup", "Scripts");
        Directory.CreateDirectory(scripts);

        // Code page 850 text and an exit that would skip every line after it.
        byte[] own = [.. "echo "u8, 0x84, .. "\r\nexit /b 0\r\n"u8];
        await File.WriteAllBytesAsync(Path.Combine(scripts, "SetupComplete.cmd"), own, TestContext.Current.CancellationToken);

        await UnattendFile.WriteAsync(_windows, TestImage.Unattend, TestContext.Current.CancellationToken);

        byte[] expected = [.. Encoding.ASCII.GetBytes($"{UnattendFile.CleanupLine}\r\n"), .. own];
        Assert.Equal(expected, await File.ReadAllBytesAsync(Path.Combine(scripts, "SetupComplete.cmd"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SummarizesWithoutSecrets()
    {
        string summary = UnattendFile.Summarize(TestImage.Unattend);

        Assert.Equal(
            "computer name PC-042, time zone the default for the locale, UI language en-US, locale de-DE, keyboard de-DE, " +
            "local administrator yes, domain join no",
            summary);
        Assert.DoesNotContain("c2VjcmV0", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnAnswerFileThatIsNotXml() =>
        Assert.Throws<DeploymentStepException>(() => UnattendFile.Summarize("<unattend>"));
}
