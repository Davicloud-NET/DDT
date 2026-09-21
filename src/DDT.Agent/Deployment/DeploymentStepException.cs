// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A deployment step cannot go on. The message is a sentence for the operator and becomes the deployment's error.
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
}
