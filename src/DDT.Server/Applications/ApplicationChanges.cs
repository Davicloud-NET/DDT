// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Server.Packages;

namespace DDT.Server.Applications;

// Describes an application save for its audit row.
internal static class ApplicationChanges
{
    public static string Created(ApplicationFields fields, ApplicationRows rows)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(rows);

        return string.Join(
            ", ",
            $"kind {fields.Kind}",
            $"files {Files(fields.PackageId, rows)}",
            $"command '{fields.Command}'",
            $"detection {Detection(ApplicationColumns.WriteDetection(fields.Detection))}",
            $"depends on {Names(fields.DependsOn, rows)}");
    }

    public static List<string> Of(Application application, ApplicationFields fields, ApplicationRows rows)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(rows);

        List<string> changes = [];
        void Text(string field, string? before, string? after)
        {
            if (before != after)
            {
                changes.Add($"{field} '{before}' to '{after}'");
            }
        }

        Text("name", application.Name, fields.Name);
        Text("description", application.Description, fields.Description);
        Text("publisher", application.Publisher, fields.Publisher);
        Text("version", application.Version, fields.Version);
        Text("kind", application.Kind.ToString(), fields.Kind.ToString());
        Text("files", Files(application.PackageId, rows), Files(fields.PackageId, rows));
        Text("command", application.Command, fields.Command);
        Text("success codes", application.SuccessExitCodes, ApplicationColumns.WriteCodes(fields.SuccessExitCodes));
        Text("restart codes", application.RestartExitCodes, ApplicationColumns.WriteCodes(fields.RestartExitCodes));
        Text("detection", Detection(application.Detection), Detection(ApplicationColumns.WriteDetection(fields.Detection)));
        Text("depends on", Names(ApplicationColumns.ReadIds(application.DependsOn), rows), Names(fields.DependsOn, rows));
        Text("winget id", application.WingetId, fields.WingetId);
        Text("winget version", application.WingetVersion, fields.WingetVersion);
        Text("timeout", Minutes(application.TimeoutMinutes), Minutes(fields.TimeoutMinutes));
        Text("continue on failure", OnOff(application.ContinueOnFailure), OnOff(fields.ContinueOnFailure));

        return changes;
    }

    private static string Files(Guid? packageId, ApplicationRows rows) =>
        packageId is { } id && rows.Files.TryGetValue(id, out Package? package) ? ApplicationUploads.Files(package).Name : "none";

    private static string Detection(string? json) => json ?? "none";

    private static string Names(IReadOnlyList<Guid> ids, ApplicationRows rows) => ids.Count == 0 ? "none" : string.Join("; ", rows.NamesOf(ids));

    private static string Minutes(int minutes) => string.Create(CultureInfo.InvariantCulture, $"{minutes} min");

    private static string OnOff(bool value) => value ? "on" : "off";
}
