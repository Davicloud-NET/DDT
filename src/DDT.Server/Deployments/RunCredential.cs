// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// The answer to an Account input of a run, kept for that run only: RunCredentialCleanup deletes it in the save that ends
// the run, whichever way it ends. ProtectedPassword is the password as RunCredentialProtector protects it for this run
// and input. Domain, Hosts, as DdtJsonContext writes a list of strings, and RunAs are the input's destination as the
// sequence declared it when the answer was given, so a later edit of the sequence never sends the password elsewhere.
// ProvidedAtMachine says it was typed at the machine rather than on the web.
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
