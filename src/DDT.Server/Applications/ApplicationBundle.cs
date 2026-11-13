// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications;

// A named list of applications. Members is a JSON array of application ids, in install order.
public sealed class ApplicationBundle
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // ApplicationNames.Normalize of the name, unique across bundles and applications.
    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    public string Members { get; set; } = "[]";

    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
