// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using DDT.Server.BootImage;

namespace DDT.Host.Startup;

[SupportedOSPlatform("windows")]
internal sealed class WindowsAdkMachine : IAdkMachine
{
    public string? KitsRoot() => InstalledAdk.KitsRoot();

    public string? AdkVersion() => InstalledAdk.ToolsVersion();

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
}
