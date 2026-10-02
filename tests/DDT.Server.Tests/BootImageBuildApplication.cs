// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.BootImage;
using DDT.Server.BootImage;
using DDT.Server.Certificates;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// A host as on a Windows server that can build: a helper that answers and an ADK, both of them a test's own, and
// DDT's own certificate, so the page can compare a build with the server it was made for.
public sealed class BootImageBuildApplication : DdtApplication
{
    public FakeBootImageHelper Helper { get; } = new();

    public BootImageAdk? Adk { get; set; } = new(true, "10.1.26100.9457", true);

    // Where a release puts the build script, the agent and the console
    public BundledReleases Bundled => new(Path.Combine(StorePath, "program"));

    public CertificateFiles Files => new(Path.Combine(StorePath, "certs", "ddt.pem"), Path.Combine(StorePath, "certs", "ddt-key.pem"));

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Kestrel:Certificates:Default:Path", Files.CertificatePath);
        builder.UseSetting("Kestrel:Certificates:Default:KeyPath", Files.KeyPath);
        builder.UseSetting("Kestrel:Endpoints:Https:Url", "https://*:8443");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IBootImageHelper>(Helper);
            services.AddSingleton(new InstalledAdk(() => Adk));
            services.AddSingleton(Bundled);
        });
    }
}
