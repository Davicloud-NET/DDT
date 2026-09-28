// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.Accounts;

// The purpose names the account, so a password copied into another account's row, with other destinations, does not
// decrypt there. Like the settings' secrets, this protects a copy of the database, not the volume the key ring is on.
public sealed class AccountProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "DDT.Accounts";
    private const string PasswordField = "password";

    public string Protect(Guid accountId, string password) => Protector(accountId).Protect(password);

    // Null for a ciphertext of another account or another key ring.
    public string? Unprotect(Guid accountId, string protectedPassword)
    {
        try
        {
            return Protector(accountId).Unprotect(protectedPassword);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector Protector(Guid accountId) => provider.CreateProtector(Purpose, accountId.ToString("D"), PasswordField);
}
