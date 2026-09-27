// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Server.Deployments;

// With a name, a sequence's Join the domain step joins the machine to this domain in Windows. The account needs only
// the right to create computer objects in the organizational unit: every Operator can read its password.
public sealed class DomainOptions
{
    public string? Name { get; set; }

    public string? OrganizationalUnit { get; set; }

    // DOMAIN\user or user@suffix, passed to the join whole.
    public string? UserName { get; set; }

    // Secret: stored encrypted, never in the section's values.
    [JsonIgnore]
    public string? Password { get; set; }

    // The domain controller the server asks when an administrator checks the join account, as a host name or an
    // address. Unset, the server asks the domain's name, which the DNS of an Active Directory domain resolves to its
    // controllers. The machines find their controller themselves and never use this.
    public string? Controller { get; set; }
}
