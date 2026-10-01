// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using System.Security.Principal;
using DDT.Server.BootImage;
using Microsoft.Extensions.Logging.EventLog;

namespace DDT.Host.Helper;

// DDT.Host helper: the DDT Helper service, which the installer registers to run as SYSTEM. DISM and the ADK's setup
// need an administrator, and the web server, which parses uploads, should not be one.
public static class HelperService
{
    public const string ServiceName = "DDTHelper";

    // The account of the DDT service, the only one the pipe lets in
    private const string WebServerAccount = @"NT SERVICE\DDT";

    public static bool Handles(string[] args) => args is ["helper"];

    [SupportedOSPlatform("windows")]
    public static async Task RunAsync()
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(windows => windows.ServiceName = ServiceName);

        // The source the MSI registers
        builder.Services.Configure<EventLogSettings>(eventLog => eventLog.SourceName = "DDT");

        SecurityIdentifier client = (SecurityIdentifier)new NTAccount(WebServerAccount).Translate(typeof(SecurityIdentifier));
        HelperJobs jobs = new(HelperPaths.ForThisServer());

        builder.Services.AddHostedService(services => new HelperPipeServer(
            PipeBootImageHelper.PipeName,
            client,
            jobs.RunAsync,
            services.GetRequiredService<ILogger<HelperPipeServer>>()));

        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}
