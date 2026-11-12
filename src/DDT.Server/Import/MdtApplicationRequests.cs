// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Server.Applications;
using DDT.Server.Images;

namespace DDT.Server.Import;

// An MDT application as the save of a DDT application. MDT restarted after an application with Reboot set whatever its
// installer returned; DDT restarts on a restart code, so then 0 and 3010 are both restart codes.
public static class MdtApplicationRequests
{
    // suggestion is what the upload of the files suggests. A package the command names becomes an MSIX application.
    public static SaveApplicationRequest Of(
        MdtShareApplication application,
        Guid? packageId,
        IReadOnlyList<Guid> dependsOn,
        ApplicationSuggestion? suggestion = null)
    {
        ArgumentNullException.ThrowIfNull(application);

        ApplicationSuggestion? msix = packageId is not null && NamesPackage(application.CommandLine) && suggestion is { Kind: ApplicationKind.Msix }
            ? suggestion
            : null;

        return new SaveApplicationRequest(
            0,
            application.Name,
            LibraryEntries.Bounded(application.Comments, ApplicationLimits.MaxDescriptionLength),
            ApplicationUploads.SuggestedText(application.Publisher, ApplicationLimits.MaxPublisherLength),
            ApplicationUploads.SuggestedText(application.Version, ApplicationLimits.MaxVersionLength),
            msix is null ? ApplicationKind.Command : ApplicationKind.Msix,
            packageId,
            msix is null ? application.CommandLine : null,
            application.Reboot ? [] : [0],
            application.Reboot ? [0, 3010] : [3010],
            Detection(application.UninstallKey) ?? msix?.Detection,
            dependsOn,
            null,
            null,
            ApplicationLimits.DefaultTimeoutMinutes,
            false);
    }

    // MDT starts the command in WorkingDirectory, DDT in the folder of the unpacked files, which is Source.
    public static ServerMessage? WorkingDirectoryNote(MdtShareApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.WorkingDirectory is { } folder && !string.Equals(Folder(folder), Folder(application.Source), StringComparison.OrdinalIgnoreCase)
            ? ServerMessages.ImportWorkingDirectory.With("folder", folder)
            : null;
    }

    // MDT provisions a command that ends in .appx or .appxbundle with DISM instead of running it.
    public static bool NamesPackage(string? commandLine) =>
        commandLine?.Trim().Trim('"') is { } path
        && (path.EndsWith(".appx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase));

    // MDT looks the value up under Uninstall, also a GUID, which may be a Burn bundle's and no MSI's product code.
    private static UninstallKeyDetection? Detection(string? uninstallKey) => uninstallKey is null ? null : new UninstallKeyDetection(uninstallKey);

    private static string Folder(string? path) => (path ?? "").Replace('/', '\\').TrimStart('.').Trim('\\').Trim();
}
