// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// A run's heartbeat and the store of its state, wired together: the heartbeat reports a state only once the store has
// written it, and the store writes each new run token the heartbeat's reports bring.
internal sealed class RunHeartbeatFactory(IAgentServer server, AgentLog log, TimeProvider timeProvider, TimeSpan interval)
{
    // saved sees each written state before the heartbeat does.
    public (FileRunStateStore Store, RunHeartbeat Heartbeat) Create(RunSession session, Action<SequenceState>? saved = null)
    {
        FileRunStateStore? store = null;
        RunHeartbeat heartbeat = new(server, log, session, call => store?.SaveTokenAsync(call) ?? Task.CompletedTask, interval, timeProvider);
        store = new FileRunStateStore(
            session.Tokens,
            state =>
            {
                saved?.Invoke(state);
                heartbeat.Update(state);
            });

        return (store, heartbeat);
    }
}
