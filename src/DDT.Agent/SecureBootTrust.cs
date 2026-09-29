// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using DDT.Agent.Deployment;
using DDT.Contracts.Images;
using DDT.Core.Boot;

namespace DDT.Agent;

// Which of Microsoft's third-party UEFI CAs the firmware trusts, read from its signature database db. These CAs sign
// the shims of Linux distributions. A machine with Secure Boot on won't start an image signed only by CAs it doesn't
// trust.
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

    // Null when there's no database, as on firmware in setup mode.
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
