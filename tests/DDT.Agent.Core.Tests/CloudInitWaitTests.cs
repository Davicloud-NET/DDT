// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;
using Xunit;

namespace DDT.Agent.Tests;

// The wait for cloud-init in the installed Linux, over a folder that stands in for / with the files two real images
// wrote: Debian 13 with cloud-init 25.1.4 and Ubuntu 24.04 with 26.1.
public sealed class CloudInitWaitTests : IDisposable
{
    private const string Clean = """{ "v1": { "datasource": "DataSourceNoCloud [seed=/dev/nvme0n1p2]", "errors": [] }}""";

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

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-cloud-init-{Guid.NewGuid():N}");
    private readonly ManualTimeProvider _time = new();
    private readonly StringWriter _console = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        _console.Dispose();
    }

    private string Marker => Path.Combine(_root, "var", "lib", "ddt", "run", "cloud-init-waited");

    private CloudInitWait Wait() => new(new CloudInitFiles(_root), Marker, new AgentLog(_time, _console), _time);

    private void Put(string path, string content = "")
    {
        string file = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    // What the log got, without each line's time.
    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    [Fact]
    public void DoesNotWaitWhereCloudInitDoesNotRunAtThisStart()
    {
        // A file an earlier start left, which the wait must not take for this start's.
        Put("var/lib/cloud/instance/boot-finished");

        Assert.Null(Wait().Pending());

        Assert.Equal(["INFO  cloud-init does not run at this start, so the run does not wait for it."], Lines());
        Assert.True(File.Exists(Marker));
    }

    [Fact]
    public void WaitsForTheResultAndThenTheSettleTimeBeforeTheRunGoesOn()
    {
        Put(CloudInitFiles.EnabledPath);
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.StillRunning, wait.Pending());
        _time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(CloudInitWait.StillRunning, wait.Pending());

        Put(CloudInitFiles.ResultPath, Clean);
        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _time.Advance(CloudInitWait.Settle - TimeSpan.FromSeconds(1));
        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        Assert.False(File.Exists(Marker));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(wait.Pending());
        Assert.True(File.Exists(Marker));
        Assert.Equal(["INFO  cloud-init has finished at this start."], Lines());
    }

    [Fact]
    public void PutsCloudInitsErrorsIntoTheLogAsWarningsAndNeverFailsTheRunForThem()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, Failed);
        Put(CloudInitFiles.StatusPath, Degraded);
        Put(CloudInitFiles.OutputPath, "Cloud-init v. 25.1.4 running 'modules:final'\n\u001b[0mDDT: ddt-agent.service is started.\r\n\nCloud-init v. 25.1.4 finished\n");
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _time.Advance(CloudInitWait.Settle);
        Assert.Null(wait.Pending());

        Assert.Equal(
            [
                "INFO  cloud-init has finished at this start.",
                "WARN  cloud-init ended with an error: ('scripts-user', RuntimeError('Runparts: 1 failures (runcmd) in 1 attempted commands'))",
                "WARN  cloud-init WARNING: cloud-config failed schema validation! You may run 'sudo cloud-init schema --system' to check the details.",
                "INFO  The end of /var/log/cloud-init-output.log:",
                "INFO  cloud-init: Cloud-init v. 25.1.4 running 'modules:final'",
                "INFO  cloud-init:  [0mDDT: ddt-agent.service is started.",
                "INFO  cloud-init: Cloud-init v. 25.1.4 finished",
            ],
            Lines());
    }

    [Fact]
    public void AResultThatIsNotJsonIsSaidOnceAndTheRunGoesOn()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, "{ \"v1\": ");
        Put(CloudInitFiles.StatusPath, "not json either");
        CloudInitWait wait = Wait();

        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _time.Advance(CloudInitWait.Settle);
        Assert.Null(wait.Pending());

        Assert.Equal(["INFO  cloud-init has finished at this start.", $"WARN  {CloudInitReport.UnreadableResult}"], Lines());
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
        _time.Advance(TimeSpan.FromSeconds(209));
        Assert.Equal(CloudInitWait.RestartComes, wait.Pending());
        Assert.False(File.Exists(Marker));

        // One minute of delay, cloud-init's 30 s, and the margin.
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(wait.Pending());
        Assert.Equal(
            [
                "INFO  cloud-init has finished at this start.",
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

        _time.Advance(TimeSpan.FromSeconds(4));
        long usec = _time.GetUtcNow().AddSeconds(60).ToUnixTimeMilliseconds() * 1000;
        Put(CloudInitFiles.ScheduledShutdownPath, $"USEC={usec}\nWARN_WALL=1\nMODE=reboot\n");

        Assert.Equal(CloudInitWait.RestartComes, wait.Pending());
        _time.Advance(TimeSpan.FromSeconds(179));
        Assert.Equal(CloudInitWait.RestartComes, wait.Pending());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(wait.Pending());
        Assert.Contains(
            "INFO  The user-data restarts or powers off this machine (a scheduled reboot), so the run waits for that before its next step, for 3 min 4 s at most.",
            Lines());
    }

    // cloud-init acts on power_state at its first start alone and keeps it in its configuration for good. The restart
    // can come before the agent has seen that cloud-init finished, so the agent tells the starts apart by their ids.
    [Fact]
    public void DoesNotWaitAgainForAPowerStateThatRestartedTheMachineAtAnEarlierStart()
    {
        const string PowerState = """{ "power_state": { "delay": "now", "mode": "reboot" } }""";
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.StartIdPath, "8f2c6f6e-0f5c-4d43-9d1d-4a1f0e0c2a01\n");
        Assert.Equal(CloudInitWait.StillRunning, Wait().Pending());

        // The agent is started again at the same start of Linux, as after an error: the restart is still to come.
        Put(CloudInitFiles.ResultPath, Clean);
        Put(CloudInitFiles.ConfigurationPath, PowerState);
        Assert.Equal(CloudInitWait.RestartComes, Wait().Pending());

        // Linux has restarted. cloud-init finishes again, and its configuration still holds the power_state.
        Put(CloudInitFiles.StartIdPath, "c1a59e44-7b1e-4f0a-8a53-2f3b0e7d9b02\n");
        _console.GetStringBuilder().Clear();
        CloudInitWait later = Wait();

        Assert.Equal(CloudInitWait.Settling, later.Pending());
        _time.Advance(CloudInitWait.Settle);
        Assert.Null(later.Pending());
        Assert.Equal(
            [
                "INFO  cloud-init has finished at this start.",
                "INFO  The user-data's restart (power_state: reboot) came at an earlier start of this Linux, so the run does not wait for it again.",
            ],
            Lines());
    }

    // After a restart the agent starts while cloud-init runs again, and the run must not wait for it a second time.
    [Fact]
    public void IsOverForTheRunOnceItWasOverAlsoAfterARestart()
    {
        Put(CloudInitFiles.EnabledPath);
        Put(CloudInitFiles.ResultPath, Clean);
        CloudInitWait first = Wait();
        Assert.Equal(CloudInitWait.Settling, first.Pending());
        _time.Advance(CloudInitWait.Settle);
        Assert.Null(first.Pending());

        File.Delete(Path.Combine(_root, CloudInitFiles.ResultPath));
        _console.GetStringBuilder().Clear();

        Assert.Null(Wait().Pending());
        Assert.Null(first.Pending());
        Assert.Empty(Lines());
    }

    [Fact]
    public void ReadsOnlyTheEndOfALongOutput()
    {
        Put(CloudInitFiles.OutputPath, string.Concat(Enumerable.Range(1, 40_000).Select(number => $"line {number}\n")));

        IReadOnlyList<string> tail = new CloudInitFiles(_root).OutputTail(3);

        Assert.Equal(["line 39998", "line 39999", "line 40000"], tail);
        Assert.Empty(new CloudInitFiles(Path.Combine(_root, "nowhere")).OutputTail(3));
    }
}
