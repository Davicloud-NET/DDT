// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DDT.Host.Startup;

[SupportedOSPlatform("windows")]
internal sealed class WindowsAdkMachine : IAdkMachine
{
    private const string InstalledRoots = @"SOFTWARE\Microsoft\Windows Kits\Installed Roots";
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string DisplayName = "Windows Assessment and Deployment Kit";

    // The kits are 32-bit programs, so their keys are in the 32-bit view
    private static readonly RegistryView[] s_views = [RegistryView.Registry32, RegistryView.Registry64];

    public string? KitsRoot() =>
        s_views.Select(view => Read(view, InstalledRoots, "KitsRoot10")).FirstOrDefault(root => root is not null);

    public string? AdkVersion()
    {
        foreach (RegistryView view in s_views)
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? programs = machine.OpenSubKey(Uninstall);

            foreach (string name in programs?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? program = programs?.OpenSubKey(name);

                if (program?.GetValue("DisplayName") as string == DisplayName && program.GetValue("DisplayVersion") is string version)
                {
                    return version;
                }
            }
        }

        return null;
    }

    public void WaitForWindowsInstaller()
    {
        try
        {
            if (!Mutex.TryOpenExisting(@"Global\_MSIExecute", out Mutex? installing))
            {
                return;
            }

            using (installing)
            {
                if (installing.WaitOne(TimeSpan.FromMinutes(30)))
                {
                    installing.ReleaseMutex();
                }
            }
        }
        catch (AbandonedMutexException abandoned)
        {
            // An installer that died still held it, and now this thread does
            abandoned.Mutex?.ReleaseMutex();
        }
        catch (UnauthorizedAccessException)
        {
            // Can't tell. Microsoft's setup then says so itself if Windows Installer is busy.
        }
    }

    public void Download(Uri address, string path)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
        using HttpRequestMessage request = new(HttpMethod.Get, address);
        using HttpResponseMessage response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using Stream body = response.Content.ReadAsStream();
        using FileStream file = File.Create(path);
        body.CopyTo(file);
    }

    public CommandResult Run(string program, IReadOnlyList<string> arguments) => CommandResult.Run(program, arguments);

    private static string? Read(RegistryView view, string key, string name)
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using RegistryKey? found = machine.OpenSubKey(key);

        return found?.GetValue(name) as string;
    }
}
