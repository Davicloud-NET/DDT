// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;

namespace DDT.Server.Applications;

// An application in the library. Its files are a package of kind Application. The lists are JSON columns, written by
// ApplicationColumns: a few hundred applications are read whole, so they need no tables of their own.
public sealed class Application
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // ApplicationNames.Normalize of the name. Unique here, and the editors keep it unique across bundles too.
    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    public string? Publisher { get; set; }

    public string? Version { get; set; }

    public ApplicationKind Kind { get; set; }

    public Guid? PackageId { get; set; }

    public string? Command { get; set; }

    public string SuccessExitCodes { get; set; } = "[0]";

    public string RestartExitCodes { get; set; } = "[]";

    // An ApplicationDetection as JSON, or null to install every time.
    public string? Detection { get; set; }

    public string DependsOn { get; set; } = "[]";

    public string? WingetId { get; set; }

    public string? WingetVersion { get; set; }

    public int TimeoutMinutes { get; set; }

    public bool ContinueOnFailure { get; set; }

    // Every save checks it and raises it.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
