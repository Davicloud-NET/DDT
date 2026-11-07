// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Images;

namespace DDT.Agent;

// Secure Boot as the firmware's own variables tell it: SecureBoot for the state, and db for what it trusts.
public sealed class LinuxSecureBoot(LinuxUefiVariables variables) : ISecureBootReader
{
    private const string SecureBoot = "SecureBoot";

    // Firmware that cannot do Secure Boot has no such variable, and so has it off. Null when the variables cannot be
    // read at all, as on a machine that did not start in UEFI mode.
    public bool? Enabled()
    {
        try
        {
            return variables.Read(SecureBoot) switch
            {
                { Length: > 0 } value => value[0] != 0,
                _ => variables.HasVariables ? false : null,
            };
        }
        catch (DeploymentStepException)
        {
            return null;
        }
    }

    public UefiCa? TrustedCas()
    {
        try
        {
            return SecureBootTrust.From(variables.ReadSignatureDatabase());
        }
        catch (DeploymentStepException)
        {
            return null;
        }
    }
}
