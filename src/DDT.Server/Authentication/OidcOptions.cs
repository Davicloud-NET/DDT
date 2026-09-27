// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Server.Authentication;

public sealed class OidcOptions
{
    public const string SectionName = "DDT:Oidc";

    public const string SchemeName = "oidc";

    public bool Enabled { get; set; }

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    // Secret: stored encrypted, never in the section's values.
    [JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Single sign on";

    // Replaced as a whole, so a list without profile or email removes them.
    public IList<string> Scopes { get; set; } = ["openid", "profile", "email"];

    // Creates a NEW account keyed on issuer plus subject when an unknown identity signs in.
    // This is not the same as linking an external identity to an existing account by email
    // address, which DDT never does: an issuer that does not verify email addresses would then
    // be able to take over any account by asserting its address.
    public bool AutoProvision { get; set; }

    // Viewer or Operator. Administrator is refused: every identity the provider signs in that DDT has not seen would
    // become one. It is not used while GroupRoleMap has entries.
    public string AutoProvisionRole { get; set; } = DdtRoleNames.Viewer;

    // The claim that carries an identity's groups, one claim per group or one holding a JSON array. Keycloak and
    // Authentik send group names or paths, Entra ID the object ids of the groups.
    public string GroupsClaim { get; set; } = "groups";

    // Claim value to role, compared without regard to case. While it has entries, the groups decide the role of an
    // account single sign-on made, at each of its sign-ins, and an identity in none of them is refused. A local account
    // linked to an identity keeps the role an administrator gave it.
    public Dictionary<string, string> GroupRoleMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
