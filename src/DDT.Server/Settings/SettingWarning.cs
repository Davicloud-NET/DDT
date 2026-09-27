// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// Field is a path inside the section, as SettingProblem's. Code names the warnings a save has to confirm; a warning
// without one only informs.
public sealed record SettingWarning(string Field, string Message, string? Code);

public static class SettingWarningCodes
{
    public const string LdapUnencrypted = "ldap.unencrypted";
    public const string LdapRekey = "ldap.rekey";
    public const string LdapNoAdministrator = "ldap.noAdministrator";
    public const string OidcOperatorRole = "oidc.operatorRole";
    public const string NoLocalAdministrator = "auth.noLocalAdministrator";
    public const string WideNetwork = "network.wide";
    public const string PxeBootUrl = "pxe.bootUrl";

    // Informs only: an entry of Interfaces names nothing on a host that runs the pxe role.
    public const string PxeInterfaceNotFound = "pxe.interfaceNotFound";

    // The warnings a save is refused for until the update confirms them.
    public static IReadOnlySet<string> NeedConfirmation { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        LdapUnencrypted,
        LdapRekey,
        LdapNoAdministrator,
        OidcOperatorRole,
        NoLocalAdministrator,
        WideNetwork,
        PxeBootUrl,
    };
}
