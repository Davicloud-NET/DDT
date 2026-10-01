// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Host.Startup;
using DDT.Pxe;
using DDT.Server.Configuration;

namespace DDT.Host.Helper;

// Where the helper reads and writes. It takes none of these from a request: the program folder is its own, the store
// and the boot directory are what ddt.ini names, and the work folder is one only administrators can open.
public sealed record HelperPaths(string ProgramFolder, string StorePath, string BootDirectory, string WorkRoot)
{
    public string Script => Path.Combine(ProgramFolder, "Build-BootImage.ps1");

    // Uploads are stored once under their hash, images and packages alike.
    public string ObjectPath(string sha256) => Path.Combine(StorePath, "images", "objects", sha256);

    public static HelperPaths ForThisServer()
    {
        ConfigurationBuilder builder = new();
        builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
        builder.AddEnvironmentVariables();
        BootstrapFile.Add(builder, BootstrapFile.DefaultPath);
        IConfigurationRoot configuration = builder.Build();

        DdtOptions ddt = configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();
        PxeOptions pxe = configuration.GetSection(PxeOptions.SectionName).Get<PxeOptions>() ?? new PxeOptions();
        string store = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ddt.StorePath));

        return new HelperPaths(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            store,
            PxeSetup.BootDirectoryIn(pxe.BootDirectory, store),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DDT Helper"));
    }
}
