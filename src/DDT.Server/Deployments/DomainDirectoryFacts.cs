// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

public sealed record DomainDirectoryFacts(
    // How the server signed in, for the report.
    string Connection,
    // The domain the controller serves, as a distinguished name.
    string NamingContext,
    // The organizational unit, or the default Computers container; null when it does not exist.
    string? Container,
    // The account may create computer objects there by a right of its own, without the quota.
    bool CanCreateComputers,
    // ms-DS-MachineAccountQuota of the domain; null when it could not be read.
    int? MachineAccountQuota,
    // The computer accounts the join account created within that quota.
    int ComputersCreated);
