// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.Applications.Msix;
using DDT.Server.Images;
using DDT.Server.Packages;

namespace DDT.Server.Applications;

// Reads the stored files of an MSIX application as the agent will, so a layout DISM cannot take is refused on save.
internal static class MsixFilesCheck
{
    public static ServerMessage? ProblemOf(ImageStore store, Package files)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);

        try
        {
            using FileStream zip = new(store.ObjectPath(files.Sha256), FileMode.Open, FileAccess.Read, FileShare.Read);

            return MsixZipLayout.Read(zip, store.UploadsDirectory).Problem;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return ServerMessages.ApplicationFilesGone.With();
        }
    }
}
