using System.Security.Cryptography;
using System.Security.Principal;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WimImageApplierTests
{
    // wimlib keeps its error file open for the life of the process, so it lives next to the tests, not in a
    // directory a test would delete.
    private static readonly string s_errorLog = Path.Combine(AppContext.BaseDirectory, "wimlib-test.log");

    [Fact]
    public async Task WithoutTheRightsOfWindowsPeTheApplyFailsBeforeAnythingIsErased()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.SkipWhen(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "An elevated session has the privileges wimlib's strict mode asks for.");

        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);
        WimImageApplier applier = new(log, AppContext.BaseDirectory, s_errorLog);

        DeploymentStepException exception = Assert.Throws<DeploymentStepException>(applier.Prepare);

        Assert.StartsWith("wimlib cannot be used, so no image can be applied.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("(wimlib error 12)", exception.Message, StringComparison.Ordinal);

        ScriptedAgentServer server = new();

        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, server.SentLines.Select(line => line.Message)));
        Assert.All(server.SentLines, line => Assert.Equal(AgentLogLevel.Warning, line.Level));
    }

    [Fact]
    public async Task KeepsALibraryOfTheUsersOwnAndLogsThatItIsUsed()
    {
        string directory = Directory.CreateTempSubdirectory("ddt-wimlib-own-").FullName;

        try
        {
            byte[] own = [1, 2, 3];
            string path = Path.Combine(directory, WimLibraryFile.FileName);
            File.WriteAllBytes(path, own);

            AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);
            WimImageApplier applier = new(log, directory, s_errorLog);

            // The process loads wimlib from next to the tests, not from this directory, and only an elevated session
            // has the privileges its strict mode asks for, so preparing may fail either way.
            Exception? failure = Record.Exception(applier.Prepare);
            Assert.True(failure is null or DeploymentStepException, failure?.ToString());

            ScriptedAgentServer server = new();

            while (log.QueuedLines > 0)
            {
                await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
            }

            string ownSha256 = Convert.ToHexStringLower(SHA256.HashData(own));
            byte[] carried = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, WimLibraryFile.FileName));
            string carriedSha256 = Convert.ToHexStringLower(SHA256.HashData(carried));
            AgentLogLine line = Assert.Single(server.SentLines, sent => sent.Message.Contains(ownSha256, StringComparison.Ordinal));

            Assert.Equal(AgentLogLevel.Warning, line.Level);
            Assert.Equal(
                $"Using the libwim-15.dll at {path}, SHA-256 {ownSha256}, instead of the agent's own copy, SHA-256 {carriedSha256}. " +
                "Delete that file to make the agent use its own copy.",
                line.Message);
            Assert.Equal(own, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
