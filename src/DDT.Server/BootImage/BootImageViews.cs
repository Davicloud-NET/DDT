// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.BootImage;
using DDT.Pxe;
using DDT.Server.Certificates;
using DDT.Server.Data;
using DDT.Server.LinuxBoot;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.BootImage;

// What the Boot image page shows: the flagged drivers, the build that is served and whether it still fits this
// server, what else the boot directory holds, and whether the server can build one itself.
public sealed class BootImageViews(
    BootImageCatalog catalog,
    InstalledAdk adk,
    IBootImageHelper helper,
    CurrentBootImageJob jobs,
    ServerAddress address,
    IServiceProvider services)
{
    public const string DriversChanged = "drivers";
    public const string ServerAddressChanged = "serverAddress";
    public const string RootChanged = "root";
    public const string AdkChanged = "adk";

    // The address a build puts into the image: the server's DNS name, which its certificate carries, and its port.
    public string ServerUrl => address.Url;

    public async Task<BootImageView> ViewAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        List<BootImageDriver> drivers = await BootImageCatalog.FlaggedAsync(database, cancellationToken).ConfigureAwait(false);
        string? driverSetHash = BootImageCatalog.DriverSetHash(drivers.Select(d => (d.PackageId, d.Sha256)));
        BootImageBuild? build = catalog.ReadBuild();
        BootImageAdk? installed = adk.Read();
        List<string> reasons = [];

        if (build is null ? drivers.Count > 0 : driverSetHash != build.DriverSetHash)
        {
            reasons.Add(DriversChanged);
        }

        if (build is not null)
        {
            reasons.AddRange(Outdated(build, installed));
        }

        return new BootImageView(
            drivers,
            driverSetHash,
            build,
            reasons.Count > 0,
            reasons,
            new BootImageBuilder(helper.Available, ServerUrl, installed, services.GetRequiredService<BuilderPackage>().Available),
            jobs.Job,
            catalog.Builds(),
            Linux(services.GetRequiredService<LinuxBootEnvironment>().Current));
    }

    // The Linux boot environment as the page shows it, with the sentence that says why it cannot be started.
    public static LinuxBootView Linux(LinuxBootState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new LinuxBootView(
            state.Available,
            state.Problem?.Text,
            state.Problem,
            state.Snapshot,
            state.KernelRelease,
            state.ShimVersion,
            state.GrubVersion,
            state.ShimSignedUnder,
            state.Keymaps,
            PxeSetup.LinuxBootFile);
    }

    // What the build was made for and is no longer true. A build that recorded none of it is left alone.
    private IEnumerable<string> Outdated(BootImageBuild build, BootImageAdk? installed)
    {
        ServerCertificates? certificates = services.GetService<ServerCertificates>();

        if (build.ServerUrl is not null && certificates?.Current is { } certificate && !Reaches(build.ServerUrl, certificate))
        {
            yield return ServerAddressChanged;
        }

        if (build.RootSha256 is not null
            && certificates?.RootCertificatePem is { } root
            && !string.Equals(build.RootSha256, Sha256(root), StringComparison.OrdinalIgnoreCase))
        {
            yield return RootChanged;
        }

        if (build.AdkVersion is not null && installed is { Installed: true, Version: not null } && installed.Version != build.AdkVersion)
        {
            yield return AdkChanged;
        }
    }

    // The agent in the image validates the name against the certificate and connects to the port.
    private bool Reaches(string serverUrl, X509Certificate2 certificate) =>
        Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? built)
        && built.Port == address.Port
        && ServerNames.Of(certificate).Contains(built.Host, StringComparer.OrdinalIgnoreCase);

    private static string Sha256(string pem)
    {
        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(pem);

        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }
}
