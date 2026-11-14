// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.InstalledPhase;

// One deployment record. Result is Done, Failed or Stopped. A server before these facts leaves SequenceId, SequenceRevision,
// ImageName and ServerName out.
public sealed record DeploymentRecordEntry(
    string SequenceName,
    Guid? SequenceId,
    long? SequenceRevision,
    Guid RunId,
    string Result,
    DateTimeOffset DeployedUtc,
    string? ImageName,
    string DdtVersion,
    string? ServerName)
{
    // The record's values by name, as text. A missing one is null.
    public IReadOnlyList<(string Name, string? Value)> Values() =>
    [
        (nameof(SequenceName), SequenceName),
        (nameof(SequenceId), SequenceId?.ToString("D")),
        (nameof(SequenceRevision), SequenceRevision?.ToString(CultureInfo.InvariantCulture)),
        (nameof(RunId), RunId.ToString("D")),
        (nameof(Result), Result),
        (nameof(DeployedUtc), DeployedUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        (nameof(ImageName), ImageName),
        (nameof(DdtVersion), DdtVersion),
        (nameof(ServerName), ServerName),
    ];
}
