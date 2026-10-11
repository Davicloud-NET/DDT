// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

// What the agent of the Linux boot environment does before it registers, on a machine whose disk holds a run that was
// handed over to the installed Linux. A registration without the run's token ends that run, so none is made while the
// installed Linux may have it. It never throws for what a disk or the server does.
public sealed class LinuxBootReturn(IAgentServer server, AgentMachine machine, ConsoleStatus status, AgentLog log, TimeProvider timeProvider)
{
    // The start from the network at which a run the installed Linux never took over is given up.
    public const int MaxReturns = 3;

    // How long the screen says what comes next before the machine restarts, and a key can still change it.
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private const int MaxFlushes = 10;

    public required HandedOverSeeds Seeds { get; init; }

    public required IKeyWait Keys { get; init; }

    public required IDiskStart DiskStart { get; init; }

    // Issues the restart, which happens once the agent has ended.
    public required Func<Task> Restart { get; init; }

    public async Task<BootReturnOutcome> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await FindAsync(cancellationToken).ConfigureAwait(false) is not { } seed)
            {
                return BootReturnOutcome.GoesOn;
            }

            return seed.IsTaken
                ? await TakenAsync(seed, cancellationToken).ConfigureAwait(false)
                : await NotTakenAsync(seed, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BootReturnOutcome(AgentExitCodes.Stopped);
        }
    }

    // The seed the agent goes by, or null when no disk it would offer holds a run.
    private async Task<HandedOverSeed?> FindAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalDisk> disks;

        try
        {
            disks = await machine.Disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Warning(LinuxBootReturnTexts.DisksUnread(exception.Message));

            return null;
        }

        IReadOnlyList<HandedOverSeed> found = Seeds.Find(disks);

        if (found.Count > 1)
        {
            log.Information(LinuxBootReturnTexts.Believed(found));
        }

        return found.Count == 0 ? null : found[0];
    }

    // The installed Linux has the run, and only its agent holds the token. Nothing is said to the server.
    private async Task<BootReturnOutcome> TakenAsync(HandedOverSeed seed, CancellationToken cancellationToken)
    {
        log.Warning(LinuxBootReturnTexts.Taken(seed));

        if (await KeyAsync(cancellationToken).ConfigureAwait(false))
        {
            return StartOver(seed, LinuxBootReturnTexts.StartsOverTaken);
        }

        DiskStart.Next(seed.Disk, seed.SystemPartition, first: false);
        log.Information(LinuxBootReturnTexts.Restarts);

        return await RestartAsync().ConfigureAwait(false);
    }

    // The token on the seed still speaks for the run, so the server learns of this start and counts it.
    private async Task<BootReturnOutcome> NotTakenAsync(HandedOverSeed seed, CancellationToken cancellationToken)
    {
        log.Information(LinuxBootReturnTexts.NotTaken(seed));

        AgentRegistrationResult? registered = await new AgentRegistrar(server, machine, status, log, timeProvider)
            .RegisterAsync(null, seed.Token, cancellationToken)
            .ConfigureAwait(false);

        if (registered is null)
        {
            return new BootReturnOutcome(AgentExitCodes.Stopped);
        }

        if (registered is not { RunId: { } runId, Token: { } token })
        {
            return NoLongerRuns(seed, registered.ResumeToken);
        }

        RunCall call = new(registered.MachineId, runId, new DeploymentTokens(token, registered.ResumeToken ?? string.Empty));
        int returns = (registered.BootReturns ?? 0) + 1;

        return returns >= MaxReturns
            ? await GiveUpAsync(seed, call, returns, cancellationToken).ConfigureAwait(false)
            : await AgainAsync(seed, call, returns, cancellationToken).ConfigureAwait(false);
    }

    // A first start of the installed Linux may just have failed, so the machine goes round once more.
    private async Task<BootReturnOutcome> AgainAsync(HandedOverSeed seed, RunCall call, int returns, CancellationToken cancellationToken)
    {
        log.Warning(LinuxBootReturnTexts.Again(returns));
        AgentRunReport report = new(DeploymentState.Running, SequencePhase.Linux, [], null, 0, RunActivity.Restarting, null, BootReturns: returns);

        if (await TellAsync(call, report, "this start from the network", cancellationToken).ConfigureAwait(false) == Answer.Refused)
        {
            return NoLongerRuns(seed, call.Tokens.ResumeToken);
        }

        await FlushAsync(call, cancellationToken).ConfigureAwait(false);

        if (await KeyAsync(cancellationToken).ConfigureAwait(false))
        {
            return StartOver(seed, LinuxBootReturnTexts.StartsOverNotTaken);
        }

        DiskStart.Next(seed.Disk, seed.SystemPartition, first: true);
        log.Information(LinuxBootReturnTexts.Restarts);
        await FlushAsync(call, cancellationToken).ConfigureAwait(false);

        return await RestartAsync().ConfigureAwait(false);
    }

    // The run fails here, and its token must not stay on the disk of a failed run.
    private async Task<BootReturnOutcome> GiveUpAsync(HandedOverSeed seed, RunCall call, int returns, CancellationToken cancellationToken)
    {
        string error = LinuxBootReturnTexts.NeverTaken(returns);
        log.Error($"The run failed: {error}");
        status.RunFailed(error);
        await FlushAsync(call, cancellationToken).ConfigureAwait(false);
        await TellAsync(call, FailedRunReport.Of(SequencePhase.Linux, error) with { BootReturns = returns }, "the failure", cancellationToken).ConfigureAwait(false);
        TakeOff(seed);

        return BootReturnOutcome.GoesOn with { ResumeToken = call.Tokens.ResumeToken };
    }

    // Stopped, ended, or another run assigned since: what the hand-over left is of no use to anyone. One sentence
    // says all of it.
    private BootReturnOutcome NoLongerRuns(HandedOverSeed seed, string? resumeToken)
    {
        log.Warning(LinuxBootReturnTexts.NoLongerRuns(seed, Seeds.TakeOff(seed)));

        return BootReturnOutcome.GoesOn with { ResumeToken = resumeToken };
    }

    // The registration that follows carries no token, which is what ends the run on the server. A later start from the
    // network must not find the run on the seed again.
    private BootReturnOutcome StartOver(HandedOverSeed seed, string said)
    {
        log.Warning(said);
        TakeOff(seed);

        return BootReturnOutcome.GoesOn with { StartsOver = true };
    }

    private void TakeOff(HandedOverSeed seed)
    {
        if (Seeds.TakeOff(seed))
        {
            log.Information(HandedOverSeeds.RemovedMessage(seed.Disk));
        }
    }

    private async Task<BootReturnOutcome> RestartAsync()
    {
        status.Restarting(RestartReason.HandOver, RestartInto.InstalledSystem);
        await Restart().ConfigureAwait(false);

        return new BootReturnOutcome(AgentExitCodes.Restarting);
    }

    // True when someone pressed a key to start over instead of the restart.
    private Task<bool> KeyAsync(CancellationToken cancellationToken)
    {
        log.Information(Keys.CanPress ? LinuxBootReturnTexts.RestartsUnlessKey : LinuxBootReturnTexts.RestartsSoon);

        return Keys.PressedWithinAsync(Wait, cancellationToken);
    }

    // A refusal means the server has ended the run meanwhile. A server that cannot be reached is said and waited for
    // no longer than a run's own calls are.
    private async Task<Answer> TellAsync(RunCall call, AgentRunReport report, string what, CancellationToken cancellationToken)
    {
        try
        {
            AgentRunReportResult result = await ServerCallRules.CallAsync(
                attempt => server.ReportRunAsync(call.MachineId, call.Tokens.Token, call.RunId, report, attempt),
                what,
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
            call.Tokens.Update(result.Token, result.ResumeToken);

            return Answer.Taken;
        }
        catch (AgentTokenRejectedException)
        {
            return Answer.Refused;
        }
        catch (DeploymentStepException exception)
        {
            log.Warning(LinuxBootReturnTexts.NotTold(what, exception.Message));

            return exception.InnerException is { } inner && ServerCallRules.IsRefusal(inner) ? Answer.Refused : Answer.NotReached;
        }
    }

    // What the screen said so far goes to the run's log on the server. The restart is worth more than the lines.
    private async Task FlushAsync(RunCall call, CancellationToken cancellationToken)
    {
        try
        {
            for (int batch = 0; batch < MaxFlushes && log.QueuedLines > 0; batch++)
            {
                await log.FlushAsync(server, call.MachineId, call.Tokens.Token, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is AgentTokenRejectedException
            || (LoopCallRules.IsTransient(exception) && !cancellationToken.IsCancellationRequested))
        {
        }
    }

    private enum Answer
    {
        Taken,
        Refused,
        NotReached,
    }

    // The machine, the run and the tokens the server's answers renew.
    private sealed record RunCall(Guid MachineId, Guid RunId, DeploymentTokens Tokens);
}
