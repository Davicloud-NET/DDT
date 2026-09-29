// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Machines;
using DDT.Server.Rules;

namespace DDT.Server.Deployments;

// What an assignment, an approval or a pick asks of a new run. Giver is who requests the run and answers its inputs.
// ComputerName is the name the request gives the machine.
public sealed record RunRequest(
    Machine Machine,
    CheckedSequence Sequence,
    IReadOnlyList<InputAnswer>? Answers,
    string? ComputerName,
    bool AllowSecureBootMismatch,
    RunCredentialGiver Giver,
    string? Address)
{
    // The rules already walked for the machine, or null to walk them for the answers.
    public SequenceResolution? Resolution { get; init; }
}
