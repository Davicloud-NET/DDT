// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Host.Startup;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SetupConsoleTests
{
    [Fact]
    public void AnUnknownVerbPrintsTheUsage()
    {
        using StringWriter output = new();

        Assert.Equal(2, SetupConsole.Run(["setup", "trust-everything"], output, Configuration()));
        Assert.StartsWith("Usage", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithACertificateOfItsOwnThereIsNoRootToTrust()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The root is trusted on Windows only.");
        using StringWriter output = new();

        Assert.Equal(0, SetupConsole.Run(["setup", "trust-root"], output, Configuration()));
        Assert.Contains("certificate of its own", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BeforeTheServiceMadeItsRootItFails()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The root is trusted on Windows only.");
        string folder = Path.Combine(Path.GetTempPath(), "ddt-setup-" + Guid.NewGuid().ToString("N"));
        using StringWriter output = new();

        int exit = SetupConsole.Run(
            ["setup", "trust-root"],
            output,
            Configuration(("Kestrel:Certificates:Default:Path", Path.Combine(folder, "ddt.pem")), ("Kestrel:Certificates:Default:KeyPath", Path.Combine(folder, "ddt-key.pem"))));

        Assert.Equal(1, exit);
        Assert.Contains(Path.Combine(folder, "ddt-root.pem"), output.ToString(), StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value))).Build();
}
