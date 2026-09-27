// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Consoles;

// What the console at the machine shows, kept whole and handed to it after every change, in order. The update check,
// the loop and the run change it, from their own threads. The console to ask questions on comes with it.
public sealed class ConsoleStatus
{
    private readonly Lock _lock = new();
    private ConsoleState _state;

    public ConsoleStatus(IMachineConsole console, string agentVersion, Uri server, string? keyboardLayout, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(server);

        Console = console;
        _state = new ConsoleState(
            ConsoleStage.Starting,
            agentVersion,
            dryRun,
            new ConsoleServer(server.AbsoluteUri),
            new ConsoleMachine(null, null, null, null, [], [], null, null, null, keyboardLayout),
            null,
            null,
            null,
            null,
            null);
        console.Show(_state);
    }

    public IMachineConsole Console { get; }

    public ConsoleState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    // The agent registers, or registers again after the server refused its token.
    public void Registering() => Change(state => state with { Stage = ConsoleStage.Connecting });

    // A call to the server failed; the agent tries again by itself.
    public void Unreachable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Change(state => state with
        {
            Server = state.Server with
            {
                Problem = exception.Message,
                FailedStage = ConnectionFailure.StageOf(exception),
                Failures = state.Server.Failures + 1,
            },
        });
    }

    // A call to the server got through, before the machine is registered.
    public void Answered() => Change(state => state with { Server = state.Server with { Problem = null, FailedStage = null, Failures = 0 } });

    public void Identified(MachineIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Change(state => state with
        {
            Machine = state.Machine with
            {
                Manufacturer = identity.Manufacturer,
                Model = identity.Model,
                SerialNumber = identity.SerialNumber,
                SmbiosUuid = identity.SmbiosUuid,
                MacAddresses = identity.MacAddresses,
                IpAddresses = identity.IpAddresses ?? [],
                SecureBootEnabled = identity.SecureBootEnabled,
                TrustedUefiCas = ConsoleValues.ToConsole(identity.TrustedUefiCas),
            },
        });
    }

    public void DisksRead(IReadOnlyList<LocalDisk> disks)
    {
        ArgumentNullException.ThrowIfNull(disks);

        Change(state => state with { Machine = state.Machine with { Disks = [.. disks.Select(disk => disk.ToConsoleDisk())] } });
    }

    // A registration or a poll got through, and says what state the machine is in. choosing says that the person at the
    // machine can choose a sequence now.
    public void Reached(Guid machineId, MachineState machineState, string? signedInBy, bool choosing = false) =>
        Change(state => state with
        {
            Stage = machineState switch
            {
                MachineState.Pending => ConsoleStage.WaitingForAuthorization,
                _ when choosing => ConsoleStage.Choosing,
                MachineState.Failed => ConsoleStage.Failed,
                _ => ConsoleStage.WaitingForSequence,
            },
            Server = state.Server with { Problem = null, FailedStage = null, Failures = 0 },
            MachineId = machineId,
            SignedInBy = machineState == MachineState.Pending ? signedInBy : null,
            Restart = null,
        });

    // The language the server has the console speak, from the registration: en, de, or null for the language of Windows.
    public void SetLanguage(string? language) => Change(state => state with { Language = language });

    // The logo the console shows, as the path of a PNG ConsoleLogo downloaded, or null for none.
    public void SetLogo(string? path) => Change(state => state with { Logo = path });

    // The agent in the installed Windows registered again to go on with its run, which it shows next.
    public void Registered(Guid machineId) =>
        Change(state => state with { Server = state.Server with { Problem = null, FailedStage = null, Failures = 0 }, MachineId = machineId });

    public void Rejected(Guid machineId) =>
        Change(state => state with
        {
            Stage = ConsoleStage.Stopped,
            MachineId = machineId,
            Problem = new ConsoleProblem(
                "An administrator rejected this machine. To take that back, an operator removes it on the Machines page, and it " +
                "registers as a new machine when it starts from the network again.",
                ConsoleRemedy.Restart),
        });

    public void Stopped(string reason) =>
        Change(state => state with { Stage = ConsoleStage.Stopped, Problem = new ConsoleProblem(reason, ConsoleRemedy.Restart) });

    // A run begins, or goes on after a restart. The heartbeat tells of every change after this.
    public void RunBegins(AgentRun run, SequenceState sequenceState)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(sequenceState);

        Change(state => state with
        {
            Stage = ConsoleStage.Running,
            Run = new ConsoleRun(run.Id, run.SequenceName, Steps(sequenceState), null, null, ConsoleActivity.Preparing),
            Restart = null,
            Problem = null,
        });
    }

    // In the installed Windows, before the run goes on: Windows setup has yet to finish.
    public void WaitingForSetup(AgentRun run, SequenceState sequenceState)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(sequenceState);

        Change(state => state with
        {
            Stage = ConsoleStage.Running,
            Run = new ConsoleRun(run.Id, run.SequenceName, Steps(sequenceState), null, null, ConsoleActivity.WaitingForWindowsSetup),
            Restart = null,
            Problem = null,
        });
    }

    public void RunChanged(RunHeartbeat heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);

        lock (_lock)
        {
            // Read under this lock, so a change made meanwhile on another thread is never shown before an older one.
            (SequenceState? sequenceState, Guid? stepId, int? percent, RunActivity activity) = heartbeat.Position;

            if (_state.Run is not { } run || sequenceState is null || sequenceState.RunId != run.Id)
            {
                return;
            }

            Show(_state with
            {
                Run = run with
                {
                    Steps = Steps(sequenceState),
                    CurrentStepId = stepId,
                    Percent = stepId is null ? null : percent,
                    Activity = ConsoleValues.ToConsole(activity),
                },
            });
        }
    }

    public void RunFailed(string error) =>
        Change(state => state with { Stage = ConsoleStage.Failed, Problem = new ConsoleProblem(error, ConsoleRemedy.RunAgain) });

    public void RunFinished() => Change(state => state with { Stage = ConsoleStage.Finished });

    public void Restarting(RestartReason reason, RestartInto into) =>
        Change(state => state with
        {
            Stage = ConsoleStage.Restarting,
            Restart = new ConsoleRestart(reason, into == RestartInto.WindowsPE ? RestartTarget.WindowsPE : RestartTarget.InstalledSystem),
        });

    // The machine was to restart but could not; the agent stops, and someone has to restart it.
    public void RestartFailed(string reason) =>
        Change(state => state with { Stage = ConsoleStage.Stopped, Problem = new ConsoleProblem(reason, ConsoleRemedy.Restart) });

    // Every step of the run's sequence, with its state where the run has one.
    private static ConsoleStep[] Steps(SequenceState state) =>
    [
        .. state.Definition.Steps.Select((step, index) =>
        {
            StepRunState? run = state.Steps.ElementAtOrDefault(index);

            return new ConsoleStep(
                step.Id,
                step.Name,
                ConsoleValues.KindOf(step),
                ConsoleValues.ToConsole(SequencePhases.Of(state.Definition, index)),
                run is null ? ConsoleStepState.Pending : ConsoleValues.ToConsole(run.State),
                run?.Error);
        }),
    ];

    private void Change(Func<ConsoleState, ConsoleState> change)
    {
        lock (_lock)
        {
            Show(change(_state));
        }
    }

    // Under the lock, so the console gets every state in the order it came about. A console only keeps it and returns.
    private void Show(ConsoleState state)
    {
        _state = state;
        Console.Show(state);
    }
}
