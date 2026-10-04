// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace DDT.Installer.CustomActions;

// DependenciesDlg
public static class Dependencies
{
    private const string Kit = "Assessment and Deployment Kit";

    // install.ps1 names the same files and hashes
    private static readonly (string Name, string File, string Address, string Sha256, Func<bool> Installed)[] s_modules =
    [
        (
            "URL Rewrite",
            "rewrite_amd64_en-US.msi",
            "https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi",
            "37342FF2F585F263F34F48E9DE59EB1051D61015A8E967DBDE4075716230A32A",
            IisCheck.UrlRewrite),
        (
            "Application Request Routing",
            "requestRouter_amd64.msi",
            "https://download.microsoft.com/download/e/9/8/e9849d6a-020e-47e4-9fd0-a023e99b54eb/requestRouter_amd64.msi",
            "FB61FDB7101795A34D5129CB37EEE43AB675C7ED76BA3A3B23B039D8C90C2A4B",
            IisCheck.Arr),
    ];

    [CustomAction]
    public static ActionResult FindDependencies(Session session)
    {
        bool adk = AdkInstalled();
        bool modulesMissing = ModulesMissing();
        session.Log($"Windows ADK with Windows PE: {adk}. IIS without URL Rewrite or ARR: {modulesMissing}");

        session["ADKMISSING"] = adk ? string.Empty : "1";
        session["IISMODULESMISSING"] = modulesMissing ? "1" : string.Empty;
        session["DEPENDENCIESPAGE"] = !adk || modulesMissing ? "1" : string.Empty;

        // The page's box starts ticked. A quiet install never gets here, and only installs the ADK when asked.
        if (!adk && string.IsNullOrEmpty(session["INSTALLADK"]))
        {
            session["INSTALLADK"] = "1";
        }

        return ActionResult.Success;
    }

    // On the page's Next, before DDT's own install: Windows runs one installer at a time, and IisDlg needs the modules.
    [CustomAction]
    public static ActionResult InstallIisModules(Session session)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ddt-setup-" + Guid.NewGuid().ToString("N"));
        string problem = string.Empty;

        try
        {
            Directory.CreateDirectory(folder);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            foreach ((string name, string file, string address, string sha256, Func<bool> installed) in s_modules)
            {
                if (problem.Length == 0 && !installed())
                {
                    problem = Install(name, address, sha256, Path.Combine(folder, file));
                }
            }
        }
        catch (Exception exception) when (exception is WebException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            problem = $"Setup couldn't get the IIS modules from Microsoft: {exception.Message}";
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            session.Log($"{folder} stays: {exception.Message}");
        }

        session.Log($"IIS modules: {(problem.Length == 0 ? "installed" : problem)}");
        session["DEPENDENCYPROBLEM"] = problem;
        session["IISMODULESMISSING"] = ModulesMissing() ? "1" : string.Empty;

        return IisCheck.FindIis(session);
    }

    // Returns what stopped it, or nothing
    private static string Install(string name, string address, string sha256, string path)
    {
        using (WebClient client = new())
        {
            client.DownloadFile(address, path);
        }

        if (!string.Equals(Sha256(path), sha256, StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} from Microsoft isn't the file this setup was made for, so it wasn't installed.";
        }

        // Not quiet, so Windows Installer asks for elevation itself and names Microsoft as the publisher
        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/i \"{path}\" /passive /norestart") { UseShellExecute = false };
        using Process installer = Process.Start(start);
        installer.WaitForExit();

        return installer.ExitCode is 0 or 3010 ? string.Empty
            : installer.ExitCode == 1602 ? $"The install of {name} was cancelled."
            : $"{name} didn't install: Windows Installer ended with code {installer.ExitCode}.";
    }

    private static bool ModulesMissing() => IisCheck.Iis() && !(IisCheck.UrlRewrite() && IisCheck.Arr());

    // As Build-BootImage.ps1 finds it. The kits are 32-bit programs, so their key is in the 32-bit view.
    private static bool AdkInstalled()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? roots = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");

            if (roots?.GetValue("KitsRoot10") is string root)
            {
                return File.Exists(Path.Combine(root, Kit, "Deployment Tools", "amd64", "DISM", "dism.exe"))
                    && File.Exists(Path.Combine(root, Kit, "Windows Preinstallation Environment", "copype.cmd"));
            }
        }

        return false;
    }

    private static string Sha256(string path)
    {
        using SHA256 algorithm = SHA256.Create();
        using FileStream file = File.OpenRead(path);

        return BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", string.Empty);
    }
}
