// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.Records;

// The purpose names the machine and the protector, so a password copied into another key's row won't decrypt there.
// A lost or moved key ring loses every key.
public sealed class RecoveryKeyProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "DDT.RecoveryKeys";

    public string Protect(Guid machineId, Guid protectorId, string password) => Protector(machineId, protectorId).Protect(password);

    // Returns null if the ciphertext belongs to another key or another key ring.
    public string? Unprotect(Guid machineId, Guid protectorId, string protectedPassword)
    {
        try
        {
            return Protector(machineId, protectorId).Unprotect(protectedPassword);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector Protector(Guid machineId, Guid protectorId) =>
        provider.CreateProtector(Purpose, machineId.ToString("D"), protectorId.ToString("D"));
}
