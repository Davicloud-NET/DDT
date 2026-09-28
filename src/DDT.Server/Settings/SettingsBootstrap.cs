// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Pxe;
using DDT.Server.Configuration;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

// Only configuration decides these values. The PXE section takes them from there.
internal sealed record SettingsBootstrap(int HttpBootPort, string BootDirectory)
{
    public static SettingsBootstrap From(IConfiguration configuration)
    {
        PxeOptions defaults = new();
        string storePath = configuration["DDT:StorePath"] is { Length: > 0 } configuredStore ? configuredStore : new DdtOptions().StorePath;
        int port = int.TryParse(configuration["DDT:Pxe:HttpBootPort"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int configuredPort)
            ? configuredPort
            : defaults.HttpBootPort;
        string bootDirectory = configuration["DDT:Pxe:BootDirectory"] ?? defaults.BootDirectory;

        try
        {
            return new(port, string.IsNullOrWhiteSpace(bootDirectory) ? bootDirectory : PxeSetup.BootDirectoryIn(bootDirectory, storePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(port, bootDirectory);
        }
    }
}
