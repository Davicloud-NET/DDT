// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Refuses a run whose kind starts in another boot environment than the one this agent runs in.
public sealed class BootEnvironmentCheck(AgentEnvironment environment) : IPreflightCheck
{
    public bool AfterDiskChoice => false;

    public static string Message(SequenceKind kind, AgentEnvironment started) =>
        kind == SequenceKind.Linux
            ? $"This sequence writes a raw disk image, which only the Linux boot environment does, and this machine started {AgentEnvironments.Name(started)}. Nothing was erased."
            : $"This sequence installs Windows, which only Windows PE does, and this machine started {AgentEnvironments.Name(started)}. Nothing was erased.";

    public void Check(IReadOnlyList<SequenceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        SequenceKind kind = SequenceKinds.Of(steps);

        if (SequenceKinds.BootEnvironment(kind) != environment)
        {
            throw new DeploymentStepException(Message(kind, environment));
        }
    }
}
