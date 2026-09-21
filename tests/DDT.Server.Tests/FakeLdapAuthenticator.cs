// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Ldap;

namespace DDT.Server.Tests;

// A directory in which every user exists, belongs to the operators group and has the test password.
public sealed class FakeLdapAuthenticator : ILdapAuthenticator
{
    // No "=" as in a real DN: the test host passes settings to Program as --key=value arguments.
    public const string OperatorsGroup = "operators";

    // Runs once inside the next bind, which is the slow step a race has to hit.
    public Func<Task>? DuringBind { get; set; }

    public async Task<LdapIdentity?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        if (DuringBind is { } hook)
        {
            DuringBind = null;
            await hook();
        }

        return password == DdtApplication.Password
            ? new LdapIdentity($"id-{userName}", $"cn={userName}", userName, userName, null, [OperatorsGroup])
            : null;
    }
}
