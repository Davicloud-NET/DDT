// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Host.Startup;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class BootstrapFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-bootstrap-").FullName;

    // As Windows Installer's IniFile table writes it.
    [Fact]
    public void ReadsTheMsiIniAsSectionKeys()
    {
        string path = Write("[DDT]\r\nStorePath=C:\\ProgramData\\DDT\\\r\nRoles=web,pxe\r\n[Kestrel:Endpoints:Https]\r\nUrl=https://*:8443\r\n");
        ConfigurationBuilder builder = new();

        BootstrapFile.Add(builder, path);
        IConfigurationRoot configuration = builder.Build();

        Assert.Equal("C:\\ProgramData\\DDT\\", configuration["DDT:StorePath"]);
        Assert.Equal("web,pxe", configuration["DDT:Roles"]);
        Assert.Equal("https://*:8443", configuration["Kestrel:Endpoints:Https:Url"]);
    }

    [Fact]
    public void OverridesAppSettingsAndYieldsToEnvironmentVariablesAndTheCommandLine()
    {
        string path = Write("[DDT]\r\nRoles=web,pxe\r\nStorePath=from-ini\r\nLocale=from-ini\r\n");
        const string variable = "DDTBOOTSTRAPTEST__Value";
        Environment.SetEnvironmentVariable(variable, "from-environment");

        try
        {
            ConfigurationBuilder builder = new();
            builder.AddInMemoryCollection([new("DDT:Roles", "web"), new("DDT:StorePath", "from-appsettings"), new("DDTBOOTSTRAPTEST:Value", "from-appsettings")]);
            builder.AddEnvironmentVariables("ASPNETCORE_");
            builder.AddEnvironmentVariables();
            builder.AddCommandLine(["--DDT:Locale=from-command-line"]);

            BootstrapFile.Add(builder, path);
            IConfigurationRoot configuration = builder.Build();

            Assert.Equal("web,pxe", configuration["DDT:Roles"]);
            Assert.Equal("from-ini", configuration["DDT:StorePath"]);
            Assert.Equal("from-environment", configuration["DDTBOOTSTRAPTEST:Value"]);
            Assert.Equal("from-command-line", configuration["DDT:Locale"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void AMissingFileAddsNothing()
    {
        ConfigurationBuilder builder = new();
        builder.AddInMemoryCollection([new("DDT:Roles", "web")]);

        BootstrapFile.Add(builder, Path.Combine(_directory, "missing.ini"));

        Assert.Equal("web", builder.Build()["DDT:Roles"]);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string content)
    {
        string path = Path.Combine(_directory, "ddt.ini");
        File.WriteAllText(path, content);

        return path;
    }
}
