// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// With a password, every deployed machine gets this local administrator and the first start skips the account
// page. Every Operator can read the password by deploying a machine they control.
public sealed class LocalAdministratorOptions
{
    public string Name { get; init; } = "Admin";

    public string? Password { get; init; }
}
