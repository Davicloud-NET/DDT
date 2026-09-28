// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

public sealed record DomainJoinCheckView(
    // The join account can join a machine into Container now.
    bool CanJoin,
    string? Domain,
    string? UserName,
    string? Controller,
    // The organizational unit or the default Computers container. Null when the check stopped before getting there.
    string? Container,
    // What was checked, in order, and what to change.
    IReadOnlyList<DomainJoinFinding> Findings,
    DateTimeOffset CheckedUtc);
