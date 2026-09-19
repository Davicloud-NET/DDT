using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ToolRunnerTests
{
    private static readonly string s_cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task LogsTheCommandItsOutputAndItsExitCode()
    {
        (ToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();

        IReadOnlyList<string> output = await tools.RunAsync(s_cmd, ["/c", "echo out& echo err 1>&2"], TestContext.Current.CancellationToken);

        Assert.Equal(["out"], output);

        List<AgentLogLine> lines = await SentAsync(server, log);
        Assert.Equal($"Running {s_cmd} /c \"echo out& echo err 1>&2\"", lines[0].Message);
        Assert.Contains(lines, line => line is { Level: AgentLogLevel.Information, Message: "out" });
        Assert.Contains(lines, line => line is { Level: AgentLogLevel.Warning, Message: "err" });
        Assert.StartsWith("cmd.exe ended with exit code 0x00000000 after ", lines[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsOnANonZeroExitCodeNamingTheToolAndTheCode()
    {
        (ToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => tools.RunAsync(s_cmd, ["/c", "exit /b 5"], TestContext.Current.CancellationToken));

        Assert.Equal("cmd.exe failed with exit code 0x00000005. Its output is in the machine log.", exception.Message);
        Assert.Contains(await SentAsync(server, log), line => line.Message.StartsWith("cmd.exe ended with exit code 0x00000005", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsWhenTheToolIsMissing()
    {
        (ToolRunner tools, _, _) = Create();
        string missing = Path.Combine(Path.GetTempPath(), $"ddt-missing-{Guid.NewGuid():N}.exe");

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => tools.RunAsync(missing, [], TestContext.Current.CancellationToken));

        Assert.StartsWith($"{missing} cannot be started", exception.Message, StringComparison.Ordinal);
    }

    private static (ToolRunner Tools, ScriptedAgentServer Server, AgentLog Log) Create()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);

        return (new ToolRunner(log, TimeProvider.System), new ScriptedAgentServer(), log);
    }

    private static async Task<List<AgentLogLine>> SentAsync(ScriptedAgentServer server, AgentLog log)
    {
        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        return server.SentLines;
    }
}
