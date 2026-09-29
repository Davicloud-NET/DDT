// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.Deployments;

// A password given for one run is encrypted with the key ring, like settings and accounts. The purpose includes the run
// and the input, so a ciphertext copied into another run's or input's row won't decrypt there.
public sealed class RunCredentialProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "DDT.RunCredentials";

    public string Protect(Guid runId, string inputName, string password) => Protector(runId, inputName).Protect(password);

    // Returns null if the ciphertext belongs to another run, another input or another key ring.
    public string? Unprotect(Guid runId, string inputName, string protectedPassword)
    {
        try
        {
            return Protector(runId, inputName).Unprotect(protectedPassword);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector Protector(Guid runId, string inputName)
    {
        ArgumentNullException.ThrowIfNull(inputName);

        return provider.CreateProtector(Purpose, runId.ToString("D"), inputName);
    }
}
