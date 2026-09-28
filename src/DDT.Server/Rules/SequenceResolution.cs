// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Core.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Sequences;

namespace DDT.Server.Rules;

// Deployment is the active one for Assigned and Console; Rule and Sequence are set for a rule. Match is what the rules
// say about the machine whatever chose its sequence, since they set values and give machine roles for every run, and
// Machine what they tested.
public sealed record SequenceResolution(
    SequenceResolutionSource Source,
    TaskSequence? Sequence,
    Rule? Rule,
    Deployment? Deployment,
    ServerMessage Explanation,
    RuleMatch Match,
    MachineVariables Machine);
