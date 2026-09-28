// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// The answer to an Account input, kept for its run only: RunCredentialCleanup deletes it in the save that ends the run.
// Domain, Hosts and RunAs are what the input declared then, so a later edit never sends the password elsewhere.
public sealed class RunCredential
{
    // A variable name's longest, which an input's name is.
    public const int MaxInputNameLength = 64;

    public Guid DeploymentId { get; set; }

    public required string InputName { get; set; }

    public required string UserName { get; set; }

    public required string ProtectedPassword { get; set; }

    public string? Domain { get; set; }

    public string Hosts { get; set; } = "[]";

    public bool RunAs { get; set; }

    public Guid? ProvidedByUserId { get; set; }

    public string? ProvidedByName { get; set; }

    public bool ProvidedAtMachine { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
