// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;

namespace DDT.Agent.LinuxPhase;

// The wait for cloud-init at the first start of the installed Linux, and for a restart its user-data asked for, so
// neither cuts a step off. Once over, it is over for the run: the file at markerPath says so after a restart too.
// At a later start only what cloud-init runs at every start is waited for, and not for long.
public sealed class CloudInitWait(ICloudInit cloudInit, FirstStartNote firstStart, string markerPath, AgentLog log, TimeProvider timeProvider) : ISetupProbe
{
    // A reboot in runcmd stopped the unit up to 2.7 s after cloud-init had finished, and nothing announces it.
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(12);

    // From the agent's start: how long cloud-init may take to show that it runs, and at a later start to finish.
    // On two images its first stage had written its status seconds before the unit started.
    public static readonly TimeSpan StartLimit = TimeSpan.FromMinutes(2);

    public const string StillRunning = "cloud-init is still running";

    // Its generator says enabled, and no unit of it has written a status yet. Whether one ever does is not known.
    public const string NotStarted = "cloud-init has not shown yet that it runs at this start";

    public const string RunsAgain = "cloud-init runs again at this later start of Linux";

    public const string RestartComes = "cloud-init has finished, and the machine restarts or powers off as its user-data asked";

    public const string Settling = "cloud-init has finished, and a restart its user-data may have asked for gets a moment to come";

    private readonly long _started = timeProvider.GetTimestamp();
    private long? _finished;
    private AnnouncedRestart? _announced;
    private bool _over;

    public static string NotRunning(TimeSpan waited) =>
        $"cloud-init is enabled but has written no status {LogText.Duration(waited)} after the agent started, so its units do not " +
        "run at this start of Linux. The run goes on without it.";

    public static string StillRunsAgain(TimeSpan waited) =>
        $"cloud-init has not finished {LogText.Duration(waited)} after the agent started at this later start of Linux. The run " +
        "goes on beside what it still does.";

    public string? Pending()
    {
        if (_over)
        {
            return null;
        }

        if (File.Exists(markerPath))
        {
            return AtALaterStart();
        }

        if (!cloudInit.IsEnabled)
        {
            log.Information(cloudInit.NotEnabled);

            return Over();
        }

        if (_finished is null)
        {
            if (cloudInit.Result() is not { } result)
            {
                return BeforeItFinished();
            }

            _finished = timeProvider.GetTimestamp();
            CloudInitReport.Write(result, cloudInit.Status(), log);

            if (firstStart.IsLaterStart && AnnouncedRestart.Read(cloudInit.Configuration(), null, timeProvider.GetUtcNow()) is { } spent)
            {
                log.Information($"The user-data's restart ({spent.What}) came at an earlier start of this Linux, so the run does not wait for it again.");
            }
        }

        return AfterItFinished(timeProvider.GetElapsedTime(_finished.Value));
    }

    // The run's wait is over. What cloud-init runs at every start still runs now, and a step should not run beside it.
    private string? AtALaterStart()
    {
        TimeSpan waited = timeProvider.GetElapsedTime(_started);

        if (cloudInit.IsEnabled && cloudInit.Result() is null)
        {
            if (waited < StartLimit)
            {
                return RunsAgain;
            }

            log.Information(StillRunsAgain(waited));
        }

        _over = true;

        return null;
    }

    // The generator says enabled before any unit runs. User-data that disabled the units and restarted leaves no
    // status at this start. A runcmd that never returns leaves one, and is waited for.
    private string? BeforeItFinished()
    {
        TimeSpan waited = timeProvider.GetElapsedTime(_started);

        if (cloudInit.Status() is not null)
        {
            return StillRunning;
        }

        if (waited < StartLimit)
        {
            return NotStarted;
        }

        log.Information(NotRunning(waited));

        return Over();
    }

    // A restart that is announced is waited for until it stops the agent, one that is not gets the settle time.
    private string? AfterItFinished(TimeSpan waited)
    {
        string? configuration = firstStart.IsLaterStart ? null : cloudInit.Configuration();

        if (_announced is null
            && AnnouncedRestart.Read(configuration, cloudInit.ScheduledShutdown(), timeProvider.GetUtcNow()) is { } announced)
        {
            _announced = announced with { Within = waited + announced.Within };
            log.Information(
                $"The user-data restarts or powers off this machine ({announced.What}), so the run waits for that before its next " +
                $"step, for {LogText.Duration(_announced.Within)} at most.");
        }

        if (_announced is not { } restart)
        {
            return waited < Settle ? Settling : Over();
        }

        if (waited < restart.Within)
        {
            return RestartComes;
        }

        log.Warning(
            $"The machine has not restarted {LogText.Duration(waited)} after cloud-init finished, though its user-data asked for it " +
            $"({restart.What}). The run goes on, and a restart that still comes stops the step that runs then.");

        return Over();
    }

    // A marker that cannot be written only costs the wait again after a restart.
    private string? Over()
    {
        _over = true;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath) ?? ".");
            File.WriteAllText(markerPath, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"That the wait for cloud-init is over could not be noted in {markerPath} ({exception.Message}).");
        }

        return null;
    }
}
