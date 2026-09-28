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

    // A secret. It's stored encrypted, never in the section's values.
    [JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Single sign on";

    // Replaced as a whole, so a list without profile or email removes them.
    public IList<string> Scopes { get; set; } = ["openid", "profile", "email"];

    // An unknown identity gets a new account, keyed on issuer and subject. DDT never links an identity to an account by
    // email address, because an issuer that doesn't verify addresses could take over any account.
    public bool AutoProvision { get; set; }

    // Viewer or Operator. Administrator is refused, because every new identity the provider signs in would become an
    // administrator. It isn't used while GroupRoleMap has entries.
    public string AutoProvisionRole { get; set; } = DdtRoleNames.Viewer;

    // The claim that carries an identity's groups, either one claim per group or one holding a JSON array. Keycloak and
    // Authentik send group names or paths. Entra ID sends the groups' object ids.
    public string GroupsClaim { get; set; } = "groups";

    // Maps a claim value to a role, ignoring case. While it has entries, the groups set the role of an account that
    // single sign-on created, at each sign-in. An identity in none of the groups is refused. A linked local account
    // keeps its role.
    public Dictionary<string, string> GroupRoleMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
