// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// A deployment step can't continue. The message is a sentence for the operator and becomes the deployment's error.
public sealed class DeploymentStepException : Exception
{
    public DeploymentStepException()
    {
    }

    public DeploymentStepException(string message)
        : base(message)
    {
    }

    public DeploymentStepException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    // Names what failed, with the last P/Invoke error. So call it before any other call, including a handle's Dispose.
    internal static DeploymentStepException ForLastWin32Error(string what)
    {
        int error = Marshal.GetLastPInvokeError();

        return new DeploymentStepException($"{what} (error {error}): {new Win32Exception(error).Message}");
    }
}
