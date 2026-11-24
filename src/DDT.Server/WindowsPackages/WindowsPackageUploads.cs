// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.WindowsPackages;
using DDT.Server.Applications;
using DDT.Server.Images;

namespace DDT.Server.WindowsPackages;

// A Windows package arrives as a zip of .msu and .cab files, or as one of them, which the server zips.
internal static class WindowsPackageUploads
{
    // The zip to store, or null for a file that is neither. A zip of the file is written next to the part file, where the
    // sweeper finds what a stop left.
    public static async Task<string?> SourceAsync(ImageStore store, ImageUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(upload);

        string part = store.PartPath(upload.Id);
        string name = Path.GetFileName(upload.FileName.Replace('\\', '/'));

        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return part;
        }

        if (!WindowsPackageNames.IsPackageFile(name))
        {
            return null;
        }

        string wrapped = store.RawPath(upload.Id);
        await SingleFileZip.WriteAsync(part, wrapped, name, cancellationToken).ConfigureAwait(false);

        return wrapped;
    }
}
