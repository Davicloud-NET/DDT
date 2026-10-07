// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.LinuxBoot;
using DDT.Server.Machines;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

// Where the server looks for the Linux boot environment, and what it takes for one.
public sealed class LinuxBootEnvironmentTests : IDisposable
{
    private readonly string _programFiles = Directory.CreateTempSubdirectory("ddt-linux-boot-tests-").FullName;

    public void Dispose() => Directory.Delete(_programFiles, recursive: true);

    private LinuxBootEnvironment Environment(string configured = "") =>
        new(Options.Create(new LinuxBootOptions { Path = configured }), new BundledReleases(_programFiles));

    private static void Describe(string folder, string json)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, LinuxBootEnvironment.ManifestName), json);
    }

    [Fact]
    public void AReleasePutsItNextToTheServersProgramFiles()
    {
        LinuxBootEnvironment environment = Environment();

        Assert.False(environment.Configured);
        Assert.Equal(Path.Combine(_programFiles, "linux-boot"), environment.Folder);
        Assert.Equal(Path.Combine(_programFiles, "linux-boot", "ddt-linux-boot.json"), environment.ManifestPath);
        Assert.False(environment.Available);

        Describe(environment.Folder, """{"snapshot":"20261006T203214Z","kernelRelease":"7.2.6+deb13-amd64","files":{"vmlinuz":{"size":1,"sha256":"00"}}}""");

        Assert.True(environment.Available);
    }

    [Fact]
    public void ConfigurationNamesAnotherFolder()
    {
        string elsewhere = Path.Combine(_programFiles, "from source");
        Describe(Path.Combine(_programFiles, "linux-boot"), "{}");
        LinuxBootEnvironment environment = Environment(elsewhere);

        Assert.True(environment.Configured);
        Assert.Equal(elsewhere, environment.Folder);
        Assert.False(environment.Available);

        Describe(elsewhere, "{}");

        Assert.True(environment.Available);
    }

    // The folder alone, or a description nobody can read, is no boot environment.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    public void ADescriptionThatCannotBeReadIsNoBootEnvironment(string json)
    {
        LinuxBootEnvironment environment = Environment();
        Describe(environment.Folder, json);

        Assert.False(environment.Available);
    }

    // The file is read again only when it changed, and then it is.
    [Fact]
    public void NoticesADescriptionThatWasReplacedOrRemoved()
    {
        LinuxBootEnvironment environment = Environment();
        Describe(environment.Folder, "not json at all");
        Assert.False(environment.Available);

        Describe(environment.Folder, """{"snapshot":"20261006T203214Z"}""");
        Assert.True(environment.Available);

        File.Delete(environment.ManifestPath);
        Assert.False(environment.Available);
    }
}
