// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Server.Deployments;

// With a name set, a sequence's Join the domain step joins the machine to this domain in Windows. The account only
// needs the right to create computer objects in the organizational unit. Give it no more, because every Operator can
// read its password.
public sealed class DomainOptions
{
    public string? Name { get; set; }

    public string? OrganizationalUnit { get; set; }

    // DOMAIN\user or user@suffix, passed to the join as is.
    public string? UserName { get; set; }

    // A secret. It's stored encrypted, never in the section's values.
    [JsonIgnore]
    public string? Password { get; set; }

    // Where the join account is checked. If unset, it's the domain's name, which Active Directory's DNS resolves to its
    // controllers. Machines find their controller themselves.
    public string? Controller { get; set; }
}
