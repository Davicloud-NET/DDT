// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;
using Xunit;

namespace DDT.Agent.Tests;

// The wait for cloud-init at the first start of the installed Linux, over a folder that stands in for /.
public sealed class CloudInitWaitTests : IDisposable
{
    private const string Clean = CloudInitRoot.Clean;

    private const string Failed =
        """{ "v1": { "datasource": "DataSourceNoCloud [seed=/dev/nvme0n1p2]", "errors": [ "('scripts-user', RuntimeError('Runparts: 1 failures (runcmd) in 1 attempted commands'))" ] }}""";

    // Two stages repeat the same warning, as on both images.
    private const string Degraded =
        """
        { "v1": { "datasource": "DataSourceNoCloud [seed=/dev/nvme0n1p2]",
          "init": { "errors": [], "finished": 5.76, "recoverable_errors": { "WARNING": [ "cloud-config failed schema validation! You may run 'sudo cloud-init schema --system' to check the details." ] }, "start": 5.42 },
          "init-local": { "errors": [], "finished": 3.57, "recoverable_errors": {}, "start": 3.23 },
          "modules-config": { "errors": [], "finished": 5.89, "recoverable_errors": { "WARNING": [ "cloud-config failed schema validation! You may run 'sudo cloud-init schema --system' to check the details." ] }, "start": 5.82 },
          "modules-final": { "errors": [], "finished": 6.7, "recoverable_errors": {}, "start": 5.95 },
          "stage": null } }
        """;

    // The line that says where cloud-init's output is, in place of the output.
    private const string OutputStays = $"INFO  {CloudInitReport.OutputStays}";

    private readonly CloudInitRoot _linux = new();

    public void Dispose() => _linux.Dispose();

    private string Marker => _linux.Marker;

    private CloudInitWait Wait() => _linux.Wait();

    private void Put(string path, string content = "") => _linux.Put(path, content);

    private string[] Lines() => _linux.Lines();

    [Fact]
    public void DoesNotWaitWhereCloudInitDoesNotRunAtThisStart()
    {
        // A file an earlier start left, which the wait must not take for this start's.
        Put("var/lib/cloud/instance/boot-finished");

        Assert.Null(Wait().Pending());

        Assert.Equal(["INFO  cloud-init does not run at this start, so the run does not wait for it."], Lines());
        Assert.True(File.Exists(Marker));
    }

    // A runcmd may take as long as it likes: cloud-init's status shows that it runs, and the wait has no end.
    [Fact]
    public void WaitsForTheResultAndThenTheSettleTimeBeforeTheRunGoesOn()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.StatusPath, """{ "v1": { "stage": "modules-final" } }""");
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.StillRunning, wait.Pending());
        _linux.Time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(CloudInitWait.StillRunning, wait.Pending());

        Put(CloudInitFiles.ResultPath, Clean);
        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _linux.Time.Advance(CloudInitWait.Settle - TimeSpan.FromSeconds(1));
        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        Assert.False(File.Exists(Marker));

        _linux.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(wait.Pending());
        Assert.True(File.Exists(Marker));
        Assert.Equal(["INFO  cloud-init has finished at this start.", OutputStays], Lines());
    }

    [Fact]
    public void PutsCloudInitsErrorsIntoTheLogAsWarningsAndNeverFailsTheRunForThem()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, Failed);
        Put(CloudInitFiles.StatusPath, Degraded);
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(wait.Pending());

        Assert.Equal(
            [
                "INFO  cloud-init has finished at this start.",
                "WARN  cloud-init ended with an error: ('scripts-user', RuntimeError('Runparts: 1 failures (runcmd) in 1 attempted commands'))",
                "WARN  cloud-init WARNING: cloud-config failed schema validation! You may run 'sudo cloud-init schema --system' to check the details.",
                OutputStays,
            ],
            Lines());
    }

    // cloud-init prints there what its modules and the administrator's scripts print, such as a password it made for
    // a user. Operators read the run's log, so the log names the file and holds no line of it.
    [Fact]
    public async Task SendsNoLineOfWhatCloudInitPrintedAndSaysWhereItIs()
    {
        string[] printed =
        [
            "Cloud-init v. 25.1.4 running 'modules:final'",
            "Set the following 'random' passwords",
            "ubuntu:Xk7-made-up-by-cloud-init",
            "DDT: ddt-agent.service is started.",
            "Cloud-init v. 25.1.4 finished at Sun, 11 Oct 2026 12:03:23 +0000.",
        ];
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, Failed);
        Put(CloudInitFiles.StatusPath, Degraded);
        Put(CloudInitFiles.OutputPath, string.Join('\n', printed) + "\n");
        AgentLog log = _linux.Log;
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(wait.Pending());

        ScriptedAgentServer server = new();

        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, Guid.NewGuid(), "session", TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            "What cloud-init and its scripts printed is in /var/log/cloud-init-output.log on the machine. It is not sent to the server, " +
            "because cloud-init can print passwords there.",
            server.SentLines[^1].Message);
        Assert.Equal(4, server.SentLines.Count);
        Assert.All(printed, line => Assert.DoesNotContain(server.SentLines, sent => sent.Message.Contains(line, StringComparison.Ordinal)));
        Assert.All(printed, line => Assert.DoesNotContain(line, _linux.Console.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain("Xk7", _linux.Console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AResultThatIsNotJsonIsSaidOnceAndTheRunGoesOn()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, "{ \"v1\": ");
        Put(CloudInitFiles.StatusPath, "not json either");
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(wait.Pending());

        Assert.Equal(["INFO  cloud-init has finished at this start.", $"WARN  {CloudInitReport.UnreadableResult}", OutputStays], Lines());
    }

    // The spike's user-data: power_state with delay 1, which stopped the unit 56 s after cloud-init had finished.
    [Fact]
    public void StaysWaitingForTheRestartAPowerStateAnnouncesUntilItsLimit()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, Clean);
        Put(CloudInitFiles.ConfigurationPath, """{ "power_state": { "delay": 1, "message": "the administrator's user-data restarts in a minute", "mode": "reboot" } }""");
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.RestartComes, wait.Pending());
        _linux.Time.Advance(TimeSpan.FromSeconds(209));
        Assert.Equal(CloudInitWait.RestartComes, wait.Pending());
        Assert.False(File.Exists(Marker));

        // One minute of delay, cloud-init's 30 s, and the margin.
        _linux.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(wait.Pending());
        Assert.Equal(
            [
                "INFO  cloud-init has finished at this start.",
                OutputStays,
                "INFO  The user-data restarts or powers off this machine (power_state: reboot), so the run waits for that before its next step, for 3 min 30 s at most.",
                "WARN  The machine has not restarted 3 min 30 s after cloud-init finished, though its user-data asked for it (power_state: reboot). " +
                    "The run goes on, and a restart that still comes stops the step that runs then.",
            ],
            Lines());
    }

    // What shutdown -r +1 leaves in /run/systemd/shutdown/scheduled, here seen only during the settle time.
    [Fact]
    public void StaysWaitingOnceAShutdownIsScheduledDuringTheSettleTime()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, Clean);
        CloudInitWait wait = Wait();
        Assert.Equal(CloudInitWait.Settling, wait.Pending());

        _linux.Time.Advance(TimeSpan.FromSeconds(4));
        long usec = _linux.Time.GetUtcNow().AddSeconds(60).ToUnixTimeMilliseconds() * 1000;
        Put(CloudInitFiles.ScheduledShutdownPath, $"USEC={usec}\nWARN_WALL=1\nMODE=reboot\n");

        Assert.Equal(CloudInitWait.RestartComes, wait.Pending());
        _linux.Time.Advance(TimeSpan.FromSeconds(179));
        Assert.Equal(CloudInitWait.RestartComes, wait.Pending());
        _linux.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(wait.Pending());
        Assert.Contains(
            "INFO  The user-data restarts or powers off this machine (a scheduled reboot), so the run waits for that before its next step, for 3 min 4 s at most.",
            Lines());
    }
}
