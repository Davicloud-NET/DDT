// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Values;

namespace DDT.Server.Deployments;

// A run's values as they stand, and what keeps it from starting.
public sealed record RunValueCheck(
    ValueResolution Resolution,
    // The required inputs with neither an answer nor a default.
    IReadOnlyList<InputDeclaration> Missing,
    IReadOnlyList<ValueProblem> Problems,
    // The unanswered inputs the machine asks, required or not.
    IReadOnlyList<AgentInput> AskedAtMachine,
    // An input only the machine asks has no answer.
    bool HasUnansweredMachineInput)
{
    // An input only the machine asks is asked on every run, also with a default. The console asked it at the pick.
    public bool AsksMachine(Deployment run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return HasUnansweredMachineInput && run.Source != DeploymentSource.Console;
    }

    // The run waits at its start. Answers the machine just gave may leave an input only it asks empty.
    public bool Waits(Deployment run, bool machineAnswered) => Missing.Count > 0 || (!machineAnswered && AsksMachine(run));
}
