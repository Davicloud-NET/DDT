// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Rules;
using DDT.Server.Deployments;
using DDT.Server.Sequences;

namespace DDT.Server.Rules;

// Deployment is the active one for Assigned and Console; Rule and Sequence are set for a rule.
public sealed record SequenceResolution(
    SequenceResolutionSource Source,
    TaskSequence? Sequence,
    AssignmentRule? Rule,
    Deployment? Deployment,
    string Explanation);
