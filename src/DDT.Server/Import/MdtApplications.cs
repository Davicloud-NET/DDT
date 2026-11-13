// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Xml.Linq;
using DDT.Contracts.Applications;
using DDT.Contracts.Import;
using DDT.Core.Applications;
using DDT.Server.Applications;

namespace DDT.Server.Import;

// Reads the applications and bundles of an MDT deployment share's Control\Applications.xml. The Deployment Workbench
// writes it, but people edit it too, so every element may be missing and True may be spelt any way.
public static class MdtApplications
{
    // In the file's order, each guid once. An entry without a guid or a name is left out: nothing could name it.
    public static IReadOnlyList<MdtShareApplication> Read(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        Dictionary<string, MdtShareApplication> read = [];

        foreach (XElement entry in MdtShare.Elements(root, "Applications.xml", "application"))
        {
            string? name = ApplicationUploads.SuggestedName(Value(entry, "Name") ?? Value(entry, "DisplayName") ?? Value(entry, "ShortName"));

            if (Key(entry.Attribute("guid")?.Value) is not { } id || name is null || read.ContainsKey(id))
            {
                continue;
            }

            read[id] = new MdtShareApplication(
                id,
                name,
                Value(entry, "Version"),
                Value(entry, "Publisher"),
                Value(entry, "Comments"),
                Value(entry, "CommandLine"),
                Value(entry, "WorkingDirectory"),
                Value(entry, "Source"),
                Value(entry, "UninstallKey"),
                string.Equals(Value(entry, "Reboot"), "True", StringComparison.OrdinalIgnoreCase),
                [.. Children(entry, "Dependency").Select(dependency => Key(dependency.Value)).OfType<string>().Distinct()],
                !string.Equals(entry.Attribute("enable")?.Value.Trim(), "False", StringComparison.OrdinalIgnoreCase));
        }

        return [.. read.Values];
    }

    // libraryNames are the library's applications and bundles by ApplicationNames.Normalize.
    public static MdtApplication View(MdtShareApplication application, IReadOnlySet<string> libraryNames)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(libraryNames);

        return new MdtApplication(
            application.Id,
            application.Name,
            application.Version,
            application.Publisher,
            application.HasFiles,
            application.IsBundle ? ApplicationReferenceKind.Bundle : ApplicationReferenceKind.Application,
            libraryNames.Contains(ApplicationNames.Normalize(application.Name) ?? ""),
            application.Enabled);
    }

    // A guid in the form {lower case}, with or without braces in the file. Anything else stays as written, so a note can
    // name it.
    public static string? Key(string? value)
    {
        string trimmed = value?.Trim() ?? "";

        return trimmed.Length == 0 ? null : Guid.TryParse(trimmed, out Guid id) ? id.ToString("B") : trimmed;
    }

    private static string? Value(XElement entry, string name) =>
        Children(entry, name).Select(element => element.Value.Trim()).FirstOrDefault(value => value.Length > 0);

    private static IEnumerable<XElement> Children(XElement entry, string name) =>
        entry.Elements().Where(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
}
