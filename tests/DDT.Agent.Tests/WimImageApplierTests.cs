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
}
