// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Opens a run: its session, its state as it starts or continues, its store and heartbeat, and the console's view of it.
internal sealed class SequenceRunFactory(RunHeartbeatFactory heartbeats, ConsoleStatus? status, AgentLog log)
{
    public SequenceRun Open(SequencePhase phase, Action recordRestart, RunRequest request)
    {
        AgentRun run = request.Run;
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(request.Tokens);
        ArgumentNullException.ThrowIfNull(request.Identity);

        RunSession session = new(request.MachineId, run, request.Tokens)
        {
            SecureBootEnabled = request.Identity.SecureBootEnabled,
            TrustedUefiCas = request.Identity.TrustedUefiCas,
        };
        SequenceState state = request.Resumed?.State ?? SequenceStates.Start(run.Id, run.Sequence);
        MachineVariables machine = RunMachine.Of(request.Identity, run);
        StepStateLog stepStates = new(log, state, machine);
        (FileRunStateStore store, RunHeartbeat heartbeat) = heartbeats.Create(session, stepStates.Saved);

        if (status is not null)
        {
            status.RunBegins(run, state);
            heartbeat.Changed += () => status.RunChanged(heartbeat);
        }

        heartbeat.Update(state);

        return new SequenceRun
        {
            Request = request,
            Phase = phase,
            RecordRestart = recordRestart,
            Session = session,
            Store = store,
            Heartbeat = heartbeat,
            State = state,
            Machine = machine,
        };
    }
}
