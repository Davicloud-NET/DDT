// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;

namespace DDT.Host.Startup;

// Verbs the MSI runs as SYSTEM. trust-root: the server's own browser trusts DDT without a warning. iis: IIS serves DDT
// under a name and certificate of its own.
public static class SetupConsole
{
    private const string Usage =
        "Usage, on Windows: DDT.Host setup trust-root; DDT.Host setup untrust-root; " +
        "DDT.Host setup iis <host name> <certificate thumbprint>; or DDT.Host setup remove-iis.";

    public static bool Handles(string[] args) => args is ["setup", ..];

    // Tests pass configuration, so they don't read the machine's ddt.ini, and a site that runs no programs.
    public static int Run(string[] args, TextWriter output, IConfiguration? configuration = null, IisSite? iis = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (!OperatingSystem.IsWindows())
        {
            output.WriteLine(Usage);

            return 2;
        }

        switch (args)
        {
            case ["setup", "trust-root" or "untrust-root"]:
                return TrustRoot(args[1] == "trust-root", output, configuration ?? Configuration());

            case ["setup", "iis", string host, string thumbprint]:
                return PublishToIis(host, thumbprint, output, configuration ?? Configuration(), iis ?? IisSite.ForThisServer());

            case ["setup", "remove-iis"]:
                (iis ?? IisSite.ForThisServer()).Remove(output);

                return 0;

            default:
                output.WriteLine(Usage);

                return 2;
        }
    }

    [SupportedOSPlatform("windows")]
    public static void LogFailure(string[] args, string said)
    {
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            EventLog.WriteEntry("DDT", $"DDT.Host {string.Join(' ', args)} failed: {said}", EventLogEntryType.Error);
        }
        catch (Exception exception) when (exception is SecurityException or InvalidOperationException or Win32Exception)
        {
            // No source yet, or no right to write
        }
    }

    private static int TrustRoot(bool trust, TextWriter output, IConfiguration configuration)
    {
        if (CertificateFiles.FromConfiguration(configuration) is not { } files)
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

        if (trust)
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

    private static int PublishToIis(string host, string thumbprint, TextWriter output, IConfiguration configuration, IisSite iis)
    {
        thumbprint = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        string? problem = Uri.CheckHostName(host) != UriHostNameType.Dns
            ? $"{host} isn't a host name."
            : thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit)
                ? $"{thumbprint} isn't a certificate thumbprint."
                : HttpsPort(configuration) is not { } port
                    ? "Kestrel:Endpoints:Https:Url names no port, so there's nothing to forward to."
                    : port == 443
                        ? "DDT listens on 443 itself, which IIS needs. Give DDT another port in ddt.ini first."
                        : MachineCertificateProblem(thumbprint) ?? iis.Publish(host, thumbprint, port, output);

        if (problem is not null)
        {
            output.WriteLine(problem);

            return 1;
        }

        return 0;
    }

    private static int? HttpsPort(IConfiguration configuration) =>
        configuration["Kestrel:Endpoints:Https:Url"] is { } url
        && Uri.TryCreate(url.Replace('*', 'x').Replace('+', 'x'), UriKind.Absolute, out Uri? uri)
            ? uri.Port
            : null;

    // IIS reads the key as SYSTEM, so any certificate in the machine store with its key will do.
    private static string? MachineCertificateProblem(string thumbprint)
    {
        using X509Store store = new(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2Collection found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);

        try
        {
            return found.Count == 0
                ? $"The machine's certificate store has no certificate {thumbprint}."
                : !found[0].HasPrivateKey
                    ? $"The certificate {thumbprint} has no private key on this machine."
                    : null;
        }
        finally
        {
            foreach (X509Certificate2 certificate in found)
            {
                certificate.Dispose();
            }
        }
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
