// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// What a person at the machine sees of the agent and answers: the state, the log, and one question at a time. Show and
// Write are called from any thread, in order, and return right away.
public interface IMachineConsole
{
    // False when nobody can answer here, for example when input is redirected.
    bool CanAsk { get; }

    // Replaces what the console shows with state.
    void Show(ConsoleState state);

    void Write(ConsoleLogLine line);

    // Completes with the answer, or with null once cancelled because the agent no longer needs it. For example, an
    // approval on the web takes the sign-in away.
    Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken);
}
