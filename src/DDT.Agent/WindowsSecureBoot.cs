// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using DDT.Agent.Deployment;
using DDT.Contracts.Images;

namespace DDT.Agent;

// Secure Boot as Windows tells it: the state from the registry, and db through the firmware variables.
public sealed class WindowsSecureBoot : ISecureBootReader
{
    public bool? Enabled() => SecureBootState.Read();

    // Null when db cannot be read, as on firmware that did not start in UEFI mode.
    public UefiCa? TrustedCas()
    {
        try
        {
            return SecureBootTrust.From(new UefiVariables().ReadSignatureDatabase());
        }
        catch (Exception exception) when (exception is DeploymentStepException or Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
