// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;

namespace DDT.Agent.LinuxPhase;

// The wait for cloud-init at the first start of the installed Linux, and for a restart its user-data asked for, so
// neither cuts a step off. Once over, it is over for the run: the file at markerPath says so after a restart too.
public sealed class CloudInitWait(ICloudInit cloudInit, string markerPath, AgentLog log, TimeProvider timeProvider) : ISetupProbe
{
    // A reboot in runcmd stopped the unit up to 2.7 s after cloud-init had finished, and nothing announces it.
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(12);

    public const string StillRunning = "cloud-init is still running";

    public const string RestartComes = "cloud-init has finished, and the machine restarts or powers off as its user-data asked";

    public const string Settling = "cloud-init has finished, and a restart its user-data may have asked for gets a moment to come";

    // Beside the marker, with the id of the start the agent first ran in.
    public const string FirstStartFile = "cloud-init-first-start";

    private long? _finished;
    private AnnouncedRestart? _announced;
    private bool? _laterStart;
    private bool _over;

    public string? Pending()
    {
        if (_over || File.Exists(markerPath))
        {
            return null;
        }

        _laterStart ??= IsALaterStart();

        if (!cloudInit.Enabled)
        {
            log.Information("cloud-init does not run at this start, so the run does not wait for it.");

            return Over();
        }

        if (_finished is null)
        {
            if (cloudInit.Result() is not { } result)
            {
                return StillRunning;
            }

            _finished = timeProvider.GetTimestamp();
            CloudInitReport.Write(result, cloudInit.Status(), log);

            if (_laterStart == true && AnnouncedRestart.Read(cloudInit.Configuration(), null, timeProvider.GetUtcNow()) is { } spent)
            {
                log.Information($"The user-data's restart ({spent.What}) came at an earlier start of this Linux, so the run does not wait for it again.");
            }
        }

        return AfterItFinished(timeProvider.GetElapsedTime(_finished.Value));
    }

    // cloud-init acts on power_state once, at the start in which it also runs the agent's script. That is the agent's
    // first start, so its id is noted. At every later start the power_state is still in the configuration and
    // announces nothing. Without an id the start counts as the first, which only costs the wait.
    private bool IsALaterStart()
    {
        if (cloudInit.StartId() is not { } start)
        {
            return false;
        }

        string note = Path.Combine(Path.GetDirectoryName(markerPath) ?? ".", FirstStartFile);

        try
        {
            if (File.Exists(note))
            {
                return File.ReadAllText(note).Trim() is { Length: > 0 } first && first != start;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(note) ?? ".");
            File.WriteAllText(note, start);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The start of Linux the agent first ran in could not be noted in {note} ({exception.Message}).");
        }

        return false;
    }

    // A restart that is announced is waited for until it stops the agent, one that is not gets the settle time.
    private string? AfterItFinished(TimeSpan waited)
    {
        string? configuration = _laterStart == true ? null : cloudInit.Configuration();

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
