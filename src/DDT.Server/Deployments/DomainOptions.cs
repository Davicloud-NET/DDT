// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// With a name, deployed machines join this domain at their first start. The account needs only the right to
// create computer objects in the organizational unit: every Operator can read its password.
public sealed class DomainOptions
{
    public string? Name { get; init; }

    public string? OrganizationalUnit { get; init; }

    // DOMAIN\user or user@suffix, written to the answer file whole.
    public string? UserName { get; init; }

    public string? Password { get; init; }
}
