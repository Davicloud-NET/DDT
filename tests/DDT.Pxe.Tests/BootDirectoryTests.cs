// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class BootDirectoryTests
{
    private static readonly string s_base = Path.Combine(Path.GetTempPath(), "ddt-boot-directory-tests");
    private static readonly string s_store = Path.Combine(s_base, "ddt", "store");
    private static readonly string s_keyFolder = Path.Combine(s_base, "tls", "private");
    private static readonly IConfiguration s_kestrel = Configuration(("Kestrel:Certificates:Default:KeyPath", Path.Combine(s_keyFolder, "ddt-key.pem")));

    public static TheoryData<string, string> RefusedDirectories => new()
    {
        { Path.GetPathRoot(s_store)!, "is the root of a filesystem" },
        { s_store, "holds DDT:StorePath" },
        { Path.Combine(s_base, "ddt"), "holds DDT:StorePath" },
        { Path.Combine(s_store, "keys"), "is in the key ring folder" },
        { Path.Combine(s_store, "keys", "boot"), "is in the key ring folder" },
        { s_keyFolder, "holds the folder of the TLS certificate or key" },
        { Path.Combine(s_base, "tls"), "holds the folder of the TLS certificate or key" },
        { " ", "Must not be empty" },
    };

    [Fact]
    public void DefaultsToBootInTheStore()
    {
        PxeOptions options = new();

        Assert.Equal(Path.Combine(s_store, "boot"), PxeSetup.BootDirectoryIn(options.BootDirectory, s_store));
        Assert.Empty(PxeSetup.FindBootDirectoryProblems(options, s_store, s_kestrel));
    }

    [Fact]
    public void ARelativeDirectoryIsInTheStore()
    {
        Assert.Equal(Path.Combine(s_store, "netboot"), PxeSetup.BootDirectoryIn("netboot", s_store));
        Assert.Equal(Path.Combine(s_base, "boot"), PxeSetup.BootDirectoryIn(Path.Combine(s_base, "boot"), s_store));
    }

    [Theory]
    [MemberData(nameof(RefusedDirectories))]
    public void RefusesADirectoryThatWouldServeASecret(string bootDirectory, string expected)
    {
        SettingProblem problem = Assert.Single(
            PxeSetup.FindBootDirectoryProblems(new PxeOptions { BootDirectory = bootDirectory }, s_store, s_kestrel));

        Assert.Equal("BootDirectory", problem.Field);
        Assert.Contains(expected, problem.Message, StringComparison.Ordinal);
    }

    // A PFX holds its key and needs no KeyPath, and a PEM key is kept next to its certificate.
    [Theory]
    [InlineData("Kestrel:Certificates:Default:Path")]
    [InlineData("Kestrel:Endpoints:Https:Certificate:Path")]
    [InlineData("Kestrel:Endpoints:Https:Certificate:KeyPath")]
    [InlineData("Kestrel:Endpoints:Https:Sni:ddt.corp.example:Certificate:Path")]
    public void RefusesTheFolderOfEveryCertificateFile(string key)
    {
        string folder = Path.Combine(s_base, "certificates");
        IConfiguration kestrel = Configuration((key, Path.Combine(folder, "ddt.pfx")), ("Kestrel:Certificates:Default:Password", "secret"));

        SettingProblem problem = Assert.Single(
            PxeSetup.FindBootDirectoryProblems(new PxeOptions { BootDirectory = folder }, s_store, kestrel));

        Assert.Contains("holds the folder of the TLS certificate or key", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("netboot")]
    [InlineData("tls/private/boot")]
    [InlineData("tls/priv")]
    [InlineData("ddt/sto")]
    [InlineData("ddt/store-boot")]
    [InlineData("ddt/store/keys-public")]
    public void AcceptsADirectoryOfItsOwn(string relativeToBase)
    {
        PxeOptions options = new() { BootDirectory = Path.Combine(s_base, relativeToBase) };

        Assert.Empty(PxeSetup.FindBootDirectoryProblems(options, s_store, s_kestrel));
    }

    [Fact]
    public void TheHostServesTheBootDirectoryInTheStore()
    {
        Assert.Equal(Path.Combine(s_store, "boot"), PxeHostingExtensions.ReadBootstrap(Builder().Configuration, s_store).Files.Root);
        Assert.Equal(
            Path.Combine(s_store, "netboot"),
            PxeHostingExtensions.ReadBootstrap(Builder(("DDT:Pxe:BootDirectory", "netboot")).Configuration, s_store).Files.Root);
    }

    [Fact]
    public void TheHostRefusesTheFolderOfItsCertificate()
    {
        WebApplicationBuilder builder = Builder(("Kestrel:Certificates:Default:Path", Path.Combine(s_store, "boot", "ddt.pfx")));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => builder.AddDdtPxe(s_store, _ => throw new InvalidOperationException("No source is needed.")));

        Assert.Contains("holds the folder of the TLS certificate or key", refusal.Message, StringComparison.Ordinal);
    }

    private static WebApplicationBuilder Builder(params (string Key, string Value)[] settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Configuration.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));

        return builder;
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
