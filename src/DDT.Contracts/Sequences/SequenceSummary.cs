// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Contracts.Sequences;

// What lists and dialogs need of a sequence. With a ProblemCount above zero it is a draft and can't run.
public sealed record SequenceSummary(
    Guid Id,
    string Name,
    string? Description,
    long Revision,
    int StepCount,
    int ProblemCount,
    int WarningCount,
    bool ErasesDisk,
    bool NeedsComputerName,
    bool ContinuesInWindows,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy,
    string? RawImageName = null,
    ImageBootCapability? RawImageBootCapability = null,
    UefiCa? RawImageSignedUnder = null,
    SequenceKind Kind = SequenceKind.Windows,
    bool ContinuesInLinux = false,
    // The sequence version an agent has to run to be handed a run of it. Zero from a server that does not say.
    int Version = 0);
