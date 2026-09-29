// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

public sealed record DomainDirectoryFacts(
    // How the server signed in, for the report.
    string Connection,
    // The domain the controller serves, as a distinguished name.
    string NamingContext,
    // The organizational unit, or the default Computers container. Null if it doesn't exist.
    string? Container,
    // The account has its own right to create computer objects there, without the quota.
    bool CanCreateComputers,
    // The domain's ms-DS-MachineAccountQuota. Null if it couldn't be read.
    int? MachineAccountQuota,
    // The computer accounts the join account created within that quota.
    int ComputersCreated);
