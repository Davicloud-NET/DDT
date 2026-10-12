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

// What the agent of the Linux boot environment does before its loop registers, on a machine whose disk holds a run that
// was handed over to the installed Linux. A registration without the run's token ends that run, so without the token
// the agent asks the server first, which then changes nothing. It never throws for what a disk or the server does.
public sealed class LinuxBootReturn(IAgentServer server, AgentMachine machine, ConsoleStatus status, AgentLog log, TimeProvider timeProvider)
{
    // The start from the network at which a run the installed Linux never took over is given up.
    public const int MaxReturns = 3;

    // How long the screen says what comes next before the machine restarts, and a key can still change it.
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // How long the server is tried for the question. The machine got its boot environment from it a moment ago.
    public static readonly TimeSpan AskTime = TimeSpan.FromSeconds(30);

    // How long the last lines stay on the screen before the restart takes them.
    public static readonly TimeSpan Shown = TimeSpan.FromSeconds(2);

    private const int MaxFlushes = 10;

    private readonly AgentRegistrar _registrar = new(server, machine, status, log, timeProvider);

    public required HandedOverSeeds Seeds { get; init; }

    // What lists the disks for the look. Without it the machine's own does, which the registration asks again.
    public IDisks? Disks { get; init; }

    public required StartOverQuestion StartOver { get; init; }

    public required IDiskStart DiskStart { get; init; }

    // Issues the restart, which happens once the agent has ended.
    public required Func<Task> Restart { get; init; }

    // Takes the resume token an agent left in the firmware before a restart, for the look's first registration. The
    // loop would find it too late: by then the look's registration has started the machine over.
    public Func<string?>? Carried { get; init; }

    public async Task<BootReturnOutcome> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await FindAsync(cancellationToken).ConfigureAwait(false) is not { } seed)
            {
                return BootReturnOutcome.GoesOn;
            }

            return seed.IsTaken
                ? await AskAsync(seed, cancellationToken).ConfigureAwait(false)
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
            disks = await (Disks ?? machine.Disks).ListDisksAsync(cancellationToken).ConfigureAwait(false);
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

    // No token is on the seed, so the installed Linux may have the run, or the seed is a leftover of one that is over.
    // The server knows which. A seed that names its run goes back to the disk when the server cannot be asked.
    private async Task<BootReturnOutcome> AskAsync(HandedOverSeed seed, CancellationToken cancellationToken)
    {
        log.Warning(LinuxBootReturnTexts.Asks(seed));

        string? carried = Carried?.Invoke();
        AgentRegistrationResult? answer = await _registrar
            .RegisterAsync(
                new RegistrationClaims { ResumeToken = carried, HandedOver = new AgentHandedOverRun(seed.RunId), GiveUpAfter = seed.RunId is null ? null : AskTime },
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (answer is null)
        {
            log.Warning(LinuxBootReturnTexts.NotAsked);
        }
        else if (answer.InstalledRun is { } installed)
        {
            log.Information(LinuxBootReturnTexts.GoesOn(seed, installed));
        }
        else if (answer.RunId is null)
        {
            return NoLongerRuns(seed, answer.ResumeToken);
        }

        return await BackToDiskAsync(seed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BootReturnOutcome> BackToDiskAsync(HandedOverSeed seed, CancellationToken cancellationToken)
    {
        if (await StartsOverAsync(taken: true, cancellationToken).ConfigureAwait(false))
        {
            return await StartOverAsync(seed, LinuxBootReturnTexts.StartsOverTaken, cancellationToken).ConfigureAwait(false);
        }

        DiskStart.Next(seed.Disk, seed.SystemPartition, first: false);
        log.Information(LinuxBootReturnTexts.Restarts);

        return await RestartAsync(cancellationToken).ConfigureAwait(false);
    }

    // The token on the seed still speaks for the run, so the server learns of this start and counts it.
    private async Task<BootReturnOutcome> NotTakenAsync(HandedOverSeed seed, CancellationToken cancellationToken)
    {
        log.Information(LinuxBootReturnTexts.NotTaken(seed));

        AgentRegistrationResult? registered = await _registrar
            .RegisterAsync(new RegistrationClaims { ResumeToken = Carried?.Invoke(), RunToken = seed.Token }, cancellationToken)
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

        if (await StartsOverAsync(taken: false, cancellationToken).ConfigureAwait(false))
        {
            return await StartOverAsync(seed, LinuxBootReturnTexts.StartsOverNotTaken, cancellationToken).ConfigureAwait(false);
        }

        DiskStart.Next(seed.Disk, seed.SystemPartition, first: true);
        log.Information(LinuxBootReturnTexts.Restarts);
        await FlushAsync(call, cancellationToken).ConfigureAwait(false);

        return await RestartAsync(cancellationToken).ConfigureAwait(false);
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

    // A later start from the network must not find the run on the seed again, so that goes first. The one registration
    // that says someone chose to start over carries no token, which is what ends the run on the server.
    private async Task<BootReturnOutcome> StartOverAsync(HandedOverSeed seed, string said, CancellationToken cancellationToken)
    {
        log.Warning(said);
        TakeOff(seed);

        AgentRegistrationResult? registered = await _registrar
            .RegisterAsync(new RegistrationClaims { StartsOver = true }, cancellationToken)
            .ConfigureAwait(false);

        return registered is null
            ? new BootReturnOutcome(AgentExitCodes.Stopped)
            : BootReturnOutcome.GoesOn with { ResumeToken = registered.ResumeToken };
    }

    private void TakeOff(HandedOverSeed seed)
    {
        if (Seeds.TakeOff(seed))
        {
            log.Information(HandedOverSeeds.RemovedMessage(seed.Disk));
        }
    }

    private async Task<BootReturnOutcome> RestartAsync(CancellationToken cancellationToken)
    {
        status.Restarting(RestartReason.HandOver, RestartInto.InstalledSystem);
        await Task.Delay(Shown, timeProvider, cancellationToken).ConfigureAwait(false);
        await Restart().ConfigureAwait(false);

        return new BootReturnOutcome(AgentExitCodes.Restarting);
    }

    // True when someone stopped the countdown with a key and then confirmed to start over instead of the restart.
    private Task<bool> StartsOverAsync(bool taken, CancellationToken cancellationToken)
    {
        log.Information(StartOver.CanAsk ? $"{LinuxBootReturnTexts.RestartsSoon} {StartOverQuestion.Offer}" : LinuxBootReturnTexts.RestartsSoon);

        return StartOver.ConfirmedWithinAsync(Wait, taken, cancellationToken);
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
