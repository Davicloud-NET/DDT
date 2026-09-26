// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using DDT.Agent.Deployment;
using DDT.Contracts.Images;
using DDT.Core.Boot;

namespace DDT.Agent;

// Which of Microsoft's third-party UEFI CAs, which sign the shims of Linux distributions, the firmware trusts, from its
// signature database db. A machine with Secure Boot on starts no image signed only under CAs it does not trust.
public static class SecureBootTrust
{
    // Null when db cannot be read, as on firmware that did not start in UEFI mode, or when it is malformed.
    public static UefiCa? Read()
    {
        try
        {
            return From(new UefiVariables().ReadSignatureDatabase());
        }
        catch (Exception exception) when (exception is DeploymentStepException or Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Null for no database, which firmware in setup mode has.
    public static UefiCa? From(byte[]? database)
    {
        if (database is null)
        {
            return null;
        }

        try
        {
            return MicrosoftUefiCa.TrustedBy(database);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
