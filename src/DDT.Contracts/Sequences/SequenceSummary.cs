// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Contracts.Sequences;

// A sequence with ProblemCount above zero is kept as a draft and cannot run. The facts let a dialog state what running
// it does without loading the whole document, including the raw disk image it writes, if any.
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
    UefiCa? RawImageSignedUnder = null);
