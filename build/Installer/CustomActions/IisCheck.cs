// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace DDT.Installer.CustomActions;

// Shows IisDlg only with IIS, URL Rewrite and ARR. Runs unelevated, so no applicationHost.config: registry and files.
public static class IisCheck
{
    private const string Property = "IISCERTIFICATE";
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    [CustomAction]
    public static ActionResult FindIis(Session session)
    {
        bool ready = Iis() && UrlRewrite() && Arr();
        session.Log($"IIS with URL Rewrite and ARR: {ready}");
        session["IISREADY"] = ready ? "1" : string.Empty;

        if (ready)
        {
            ListCertificates(session);
        }

        return ActionResult.Success;
    }

    // For quiet installs, before anything is installed. A failing setup iis would roll the whole server back.
    [CustomAction]
    public static ActionResult CheckIis(Session session)
    {
        string host = session["IISHOSTNAME"];
        string thumbprint = session[Property].Replace(" ", string.Empty);

        session["IISPROBLEM"] =
            !(Iis() && UrlRewrite() && Arr()) ? "IISHOSTNAME needs IIS with URL Rewrite and Application Request Routing, which this server lacks."
            : Uri.CheckHostName(host) != UriHostNameType.Dns ? $"IISHOSTNAME: {host} isn't a host name."
            : !HasKey(thumbprint) ? $"IISCERTIFICATE: this computer's certificate store has no certificate {thumbprint} with its private key."
            : string.Empty;

        return ActionResult.Success;
    }

    private static bool HasKey(string thumbprint)
    {
        using X509Store store = new(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2Collection found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);

        try
        {
            return found.Count > 0 && found[0].HasPrivateKey;
        }
        finally
        {
            foreach (X509Certificate2 certificate in found)
            {
                certificate.Dispose();
            }
        }
    }

    internal static bool Iis() =>
        Registry64(@"SOFTWARE\Microsoft\InetStp", "MajorVersion") is int major && major >= 10;

    internal static bool UrlRewrite() =>
        Registry64(@"SOFTWARE\Microsoft\IIS Extensions\URL Rewrite", "Install") is int installed && installed == 1
        || File.Exists(Path.Combine(NativeSystem(), "inetsrv", "rewrite.dll"));

    internal static bool Arr() =>
        Registry64(@"SOFTWARE\Microsoft\IIS Extensions\Application Request Routing", "Install") is int installed && installed == 1
        || File.Exists(Path.Combine(
            Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "IIS", "Application Request Routing", "requestRouter.dll"));

    // Certificates IIS can serve a key, "valid now", for server authentication or anything
    private static void ListCertificates(Session session)
    {
        using X509Store store = new(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2Collection all = store.Certificates;
        DateTime now = DateTime.Now;

        X509Certificate2[] usable = [.. all.Cast<X509Certificate2>()
            .Where(certificate => certificate.HasPrivateKey
                && certificate.NotBefore <= now && now <= certificate.NotAfter
                && ForServers(certificate))
            .OrderBy(certificate => certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false), StringComparer.OrdinalIgnoreCase)];

        using View view = session.Database.OpenView("SELECT * FROM `ComboBox`");
        view.Execute();

        // Order 1 is the ListItem "Select" option
        int order = 2;

        foreach (X509Certificate2 certificate in usable)
        {
            using Record row = new(4);
            row[1] = Property;
            row[2] = order++;
            row[3] = certificate.Thumbprint;
            row[4] = string.Format(
                CultureInfo.InvariantCulture,
                "{0}, until {1:yyyy-MM-dd} ({2})",
                certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false),
                certificate.NotAfter,
                certificate.Thumbprint.Substring(0, 8));
            view.InsertTemporary(row);
        }

        if (usable.Length == 1 && string.IsNullOrEmpty(session[Property]))
        {
            session[Property] = usable[0].Thumbprint;
        }

        foreach (X509Certificate2 certificate in all)
        {
            certificate.Dispose();
        }
    }

    private static bool ForServers(X509Certificate2 certificate)
    {
        X509EnhancedKeyUsageExtension? usage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();

        return usage is null || usage.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(oid => oid.Value == ServerAuthentication);
    }

    private static object? Registry64(string key, string name)
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? found = machine.OpenSubKey(key);

        return found?.GetValue(name);
    }

    private static string NativeSystem() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.Is64BitProcess || !Environment.Is64BitOperatingSystem ? "System32" : "Sysnative");
}
