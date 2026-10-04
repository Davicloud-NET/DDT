// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using DDT.Contracts.BootImage;
using Microsoft.Win32;

namespace DDT.Server.BootImage;

// The Windows ADK on this server, found as Build-BootImage.ps1 finds it. Tests pass their own.
public sealed class InstalledAdk(Func<BootImageAdk?> read)
{
    // Older add-ons lack what the build script's trim and boot managers expect.
    public static readonly Version OldestAddOn = new(10, 1, 26100, 2454);

    private const string Kit = "Assessment and Deployment Kit";
    private const string InstalledRoots = @"SOFTWARE\Microsoft\Windows Kits\Installed Roots";
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string ToolsName = "Windows Assessment and Deployment Kit";
    private const string AddOnName = "Windows Assessment and Deployment Kit Windows Preinstallation Environment Add-ons";

    public InstalledAdk()
        : this(Find)
    {
    }

    // Null where there is no telling, as on Linux.
    public BootImageAdk? Read() => read();

    // KitsRoot10, the folder Windows kits install into. The kits are 32-bit programs, so their keys are in that view.
    [SupportedOSPlatform("windows")]
    public static string? KitsRoot() =>
        Views().Select(view => Value(view, InstalledRoots, "KitsRoot10")).FirstOrDefault(root => root is not null);

    // The version of the ADK's deployment tools, as their setup registered it.
    [SupportedOSPlatform("windows")]
    public static string? ToolsVersion() => RegisteredVersion(ToolsName);

    private static BootImageAdk? Find()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        bool installed = KitsRoot() is { } root
            && File.Exists(Path.Combine(root, Kit, "Deployment Tools", "amd64", "DISM", "dism.exe"))
            && File.Exists(Path.Combine(root, Kit, "Windows Preinstallation Environment", "copype.cmd"));
        string? version = installed ? RegisteredVersion(AddOnName) : null;
        bool supported = installed && (!Version.TryParse(version, out Version? addOn) || addOn >= OldestAddOn);

        return new BootImageAdk(installed, version, supported);
    }

    [SupportedOSPlatform("windows")]
    private static string? RegisteredVersion(string displayName)
    {
        foreach (RegistryView view in Views())
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? programs = machine.OpenSubKey(Uninstall);

            foreach (string name in programs?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? program = programs?.OpenSubKey(name);

                if (program?.GetValue("DisplayName") as string == displayName && program.GetValue("DisplayVersion") is string version)
                {
                    return version;
                }
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static RegistryView[] Views() => [RegistryView.Registry32, RegistryView.Registry64];

    [SupportedOSPlatform("windows")]
    private static string? Value(RegistryView view, string key, string name)
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using RegistryKey? found = machine.OpenSubKey(key);

        return found?.GetValue(name) as string;
    }
}
