// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Ldap;

// Tries values not yet saved against the directory, for the settings page.
public interface ILdapTester
{
    Task<LdapTestOutcome> TestAsync(LdapOptions options, string? userName, string? password, CancellationToken cancellationToken);
}

// UserFound and PasswordAccepted are null when nobody, or no password, was named. Text says what the directory answered,
// and Message is its English.
public sealed record LdapTestOutcome(bool Bound, bool? UserFound, bool? PasswordAccepted, IReadOnlyList<string> Groups, ServerMessage Text)
{
    public string Message => Text.Text;
}

public sealed class LdapTester(ILoggerFactory loggerFactory) : ILdapTester
{
    public Task<LdapTestOutcome> TestAsync(LdapOptions options, string? userName, string? password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new LdapAuthenticator(options, loggerFactory.CreateLogger<LdapAuthenticator>()).Test(userName, password));
    }
}
