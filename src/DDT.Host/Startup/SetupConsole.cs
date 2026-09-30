// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;

namespace DDT.Host.Startup;

// Verbs the MSI runs as SYSTEM. trust-root: the server's own browser trusts DDT without a warning.
public static class SetupConsole
{
    public static bool Handles(string[] args) => args is ["setup", ..];

    // Tests pass configuration, so they don't read the machine's ddt.ini.
    public static int Run(string[] args, TextWriter output, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (args is not ["setup", "trust-root" or "untrust-root"] || !OperatingSystem.IsWindows())
        {
            output.WriteLine("Usage, on Windows: DDT.Host setup trust-root, or DDT.Host setup untrust-root.");

            return 2;
        }

        if (CertificateFiles.FromConfiguration(configuration ?? Configuration()) is not { } files)
        {
            output.WriteLine("The server uses a certificate of its own, so there's no DDT root to trust.");

            return 0;
        }

        if (!File.Exists(files.RootPath))
        {
            output.WriteLine($"{files.RootPath} doesn't exist. The service creates it at its first start.");

            return 1;
        }

        using X509Certificate2 root = X509Certificate2.CreateFromPem(File.ReadAllText(files.RootPath));
        using X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        if (args[1] == "trust-root")
        {
            store.Add(root);
            output.WriteLine($"This computer trusts DDT's root {root.Thumbprint} now.");
        }
        else
        {
            store.Remove(root);
            output.WriteLine($"This computer no longer trusts DDT's root {root.Thumbprint}.");
        }

        return 0;
    }

    private static IConfiguration Configuration()
    {
        ConfigurationBuilder builder = new();
        builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
        builder.AddEnvironmentVariables();
        BootstrapFile.Add(builder, BootstrapFile.DefaultPath);

        return builder.Build();
    }
}
