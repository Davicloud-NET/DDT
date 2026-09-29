// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using DDT.Server.Deployments;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace DDT.Server.Tests;

// An account's password is bound to its destinations, and a run's account to its run. A password copied into another
// row or field must not decrypt there, or it would reach a destination it was never given for.
public sealed class CredentialProtectorTests
{
    private const string Password = "Winter2026!";

    private readonly EphemeralDataProtectionProvider _provider = new();

    [Fact]
    public void AnAccountsPasswordDecryptsOnlyForThatAccount()
    {
        AccountProtector protector = new(_provider);
        Guid account = Guid.NewGuid();

        string ciphertext = protector.Protect(account, Password);

        Assert.DoesNotContain(Password, ciphertext, StringComparison.Ordinal);
        Assert.Equal(Password, protector.Unprotect(account, ciphertext));
        Assert.Null(protector.Unprotect(Guid.NewGuid(), ciphertext));
    }

    [Fact]
    public void ARunsPasswordDecryptsOnlyForThatRunAndInput()
    {
        RunCredentialProtector protector = new(_provider);
        Guid run = Guid.NewGuid();

        string ciphertext = protector.Protect(run, "JoinAccount", Password);

        Assert.DoesNotContain(Password, ciphertext, StringComparison.Ordinal);
        Assert.Equal(Password, protector.Unprotect(run, "JoinAccount", ciphertext));
        Assert.Null(protector.Unprotect(Guid.NewGuid(), "JoinAccount", ciphertext));
        Assert.Null(protector.Unprotect(run, "ShareAccount", ciphertext));

        // The input name must match the sequence exactly. Another spelling is another field.
        Assert.Null(protector.Unprotect(run, "joinaccount", ciphertext));
    }

    // The same ID under the other purpose doesn't decrypt, even with the input named like the account's field.
    [Fact]
    public void AStoredAccountAndARunsAccountNeverReadAsEachOther()
    {
        AccountProtector accounts = new(_provider);
        RunCredentialProtector runs = new(_provider);
        Guid id = Guid.NewGuid();

        Assert.Null(runs.Unprotect(id, "password", accounts.Protect(id, Password)));
        Assert.Null(accounts.Unprotect(id, runs.Protect(id, "password", Password)));
    }

    [Fact]
    public void AnotherKeyRingReadsNothing()
    {
        Guid id = Guid.NewGuid();
        EphemeralDataProtectionProvider other = new();

        Assert.Null(new AccountProtector(other).Unprotect(id, new AccountProtector(_provider).Protect(id, Password)));
        Assert.Null(new RunCredentialProtector(other).Unprotect(id, "Join", new RunCredentialProtector(_provider).Protect(id, "Join", Password)));
    }
}
