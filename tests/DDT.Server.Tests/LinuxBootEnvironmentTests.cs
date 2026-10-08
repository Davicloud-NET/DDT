// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Pxe;
using DDT.Server.LinuxBoot;
using DDT.Server.Machines;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

// Where the server looks for the Linux boot environment, what it takes for one, and what it says is wrong with it.
public sealed class LinuxBootEnvironmentTests : IDisposable
{
    private readonly string _programFiles = Directory.CreateTempSubdirectory("ddt-linux-boot-tests-").FullName;

    public void Dispose() => Directory.Delete(_programFiles, recursive: true);

    private LinuxBootEnvironment Environment(string configured = "", Func<string?>? root = null) =>
        new(Options.Create(new LinuxBootOptions { Path = configured }), new BundledReleases(_programFiles), root ?? (() => TestLinuxBoot.Root));

    private static string Reason(LinuxBootState state) => (string)state.Problem!.Args["reason"];

    [Fact]
    public async Task AReleasePutsItNextToTheServersProgramFiles()
    {
        LinuxBootEnvironment environment = Environment();

        Assert.False(environment.Configured);
        Assert.Equal(Path.Combine(_programFiles, "linux-boot"), environment.Folder);
        Assert.Equal(Path.Combine(_programFiles, "linux-boot", "ddt-linux-boot.json"), environment.ManifestPath);
        Assert.False(environment.Available);
        Assert.Equal(ServerMessages.LinuxBootMissing.Code, environment.Current.Problem?.Code);
        Assert.Equal(
            $"This server has no Linux boot environment: {environment.Folder} holds no ddt-linux-boot.json. A release of DDT brings the folder with it.",
            environment.Current.Problem?.Text);

        TestLinuxBoot.Write(environment.Folder);

        Assert.True(environment.Available);
        await environment.CheckedAsync();
        Assert.True(environment.Available);
        Assert.Null(environment.Current.Problem);
    }

    [Fact]
    public void ConfigurationNamesAnotherFolder()
    {
        string elsewhere = Path.Combine(_programFiles, "from source");
        TestLinuxBoot.Write(Path.Combine(_programFiles, "linux-boot"));
        LinuxBootEnvironment environment = Environment(elsewhere);

        Assert.True(environment.Configured);
        Assert.Equal(elsewhere, environment.Folder);
        Assert.False(environment.Available);

        TestLinuxBoot.Write(elsewhere);

        Assert.True(environment.Available);
    }

    [Fact]
    public void SaysWhatTheDescriptionSaysForThePages()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);

        LinuxBootState state = environment.Current;

        Assert.Equal("20261006T203214Z", state.Snapshot);
        Assert.Equal("7.2.6+deb13-amd64", state.KernelRelease);
        Assert.Equal("1.51~1+deb13u1+16.1-2~deb13u1", state.ShimVersion);
        Assert.Equal("1+2.12+9+deb13u2", state.GrubVersion);
        Assert.Equal(TestLinuxBoot.Keymaps, state.Keymaps);
        Assert.Equal(TestLinuxBoot.Names.Order(), state.Files.Keys.Order());
        Assert.Equal(environment.Folder, state.Folder);

        // The test's shim is no program, so no certificate authority is known for it.
        Assert.Null(state.ShimSignedUnder);
    }

    // The server reads the shim's signatures itself, as firmware does, and holds the description to it.
    [Fact]
    public void ReadsTheCertificateAuthoritiesFromTheShimsOwnSignatures()
    {
        LinuxBootEnvironment environment = Environment();
        byte[] shim = TestPe.Fixture("shimx64.efi.dualsigned");

        TestLinuxBoot.Write(environment.Folder, shim, manifest => manifest["shimSignedUnder"] = new JsonArray("Microsoft Corporation UEFI CA 2011"));

        Assert.True(environment.Available);
        Assert.Equal(UefiCa.Microsoft2011, environment.Current.ShimSignedUnder);
    }

    [Fact]
    public void ADescriptionThatNamesOtherAuthoritiesThanTheShimCarriesIsDamaged()
    {
        LinuxBootEnvironment environment = Environment();
        byte[] shim = TestPe.Fixture("shimx64.efi.dualsigned");

        TestLinuxBoot.Write(
            environment.Folder,
            shim,
            manifest => manifest["shimSignedUnder"] = new JsonArray("Microsoft Corporation UEFI CA 2011", "Microsoft UEFI CA 2023"));

        Assert.False(environment.Available);
        Assert.Equal("signatures", Reason(environment.Current));
        Assert.Contains("shimx64.efi carries other signatures than its description says", environment.Current.Problem!.Text, StringComparison.Ordinal);
    }

    // The folder alone, or a description nobody can read, is no boot environment.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"files":[]}""")]
    [InlineData("""{"files":{"vmlinuz":{"size":1,"sha256":"00"}}}""")]
    [InlineData("""{"files":[{"name":"../ddt-key.pem","size":1,"sha256":"00"}]}""")]
    [InlineData("""{"files":[{"name":"sub/vmlinuz","size":1,"sha256":"00"}]}""")]
    [InlineData("""{"files":[{"size":1,"sha256":"00"}]}""")]
    [InlineData("""{"files":[{"name":"","size":1,"sha256":"00"}]}""")]
    [InlineData("""{"files":[{"name":"..","size":1,"sha256":"00"}]}""")]
    [InlineData("""{"files":[{"name":"vmlinuz ","size":1,"sha256":"00"}]}""")]
    [InlineData("""{"files":[{"name":"C:vmlinuz","size":1,"sha256":"00"}]}""")]
    public void ADescriptionThatCannotBeReadIsDamaged(string json)
    {
        LinuxBootEnvironment environment = Environment();
        Directory.CreateDirectory(environment.Folder);
        File.WriteAllText(environment.ManifestPath, json);

        LinuxBootState state = environment.Current;

        Assert.False(state.Available);
        Assert.Equal(ServerMessages.LinuxBootDamaged.Code, state.Problem?.Code);
        Assert.Equal("unreadable", Reason(state));
        Assert.Empty(state.Files);
    }

    // The description lists a handful of files and packages. One of a megabyte and more is not a release's.
    [Theory]
    [InlineData(1024 * 1024, true)]
    [InlineData((1024 * 1024) + 1, false)]
    public void ADescriptionOverAMegabyteIsNotRead(int length, bool read)
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        string json = File.ReadAllText(environment.ManifestPath);

        // JSON takes white space after its last brace, so the description is the same one, only longer.
        File.WriteAllText(environment.ManifestPath, json + new string(' ', length - json.Length));

        Assert.Equal(length, new FileInfo(environment.ManifestPath).Length);
        Assert.Equal(read, environment.Available);
        Assert.Equal(read ? null : "unreadable", environment.Current.Problem?.Args["reason"]);
    }

    // Netboot serves a listed name to everyone, so a name is at most 64 characters of a small set, and listed once.
    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void AListedNameMayHaveSixtyFourCharactersAndNotOneMore(int length, bool read)
    {
        LinuxBootEnvironment environment = Environment();
        string name = new string('a', length - 4) + ".img";
        TestLinuxBoot.Write(environment.Folder, change: manifest => ((JsonArray)manifest["files"]!).Add(TestLinuxBoot.Listed(environment.Folder, name, "one more initrd")));

        Assert.Equal(read, environment.Available);
        Assert.Equal(read ? null : "unreadable", environment.Current.Problem?.Args["reason"]);
        Assert.Equal(read, environment.Current.Files.ContainsKey(name));
    }

    [Fact]
    public void ADescriptionThatListsANameTwiceIsDamaged()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder, change: manifest =>
        {
            JsonArray files = (JsonArray)manifest["files"]!;
            files.Add(files[2]!.DeepClone());
        });

        Assert.False(environment.Available);
        Assert.Equal("unreadable", Reason(environment.Current));
        Assert.Empty(environment.Current.Files);
    }

    // A link could lead anywhere on the volume, and its target is not the release's file.
    [Fact]
    public void AListedFileThatIsALinkIsDamage()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        string kernel = Path.Combine(environment.Folder, "vmlinuz");
        string elsewhere = Path.Combine(_programFiles, "vmlinuz elsewhere");
        File.Move(kernel, elsewhere);

        try
        {
            File.CreateSymbolicLink(kernel, elsewhere);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This account may not create a symbolic link: " + exception.Message);
        }

        environment.Refresh();

        Assert.False(environment.Available);
        Assert.Equal("vmlinuz", environment.Current.Problem!.Args["name"]);
    }

    [Fact]
    public void ADescriptionWithoutOneOfTheFilesAMachineNeedsIsDamaged()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder, change: manifest => ((JsonArray)manifest["files"]!).RemoveAt(2));

        Assert.False(environment.Available);
        Assert.Equal("incomplete", Reason(environment.Current));
        Assert.Contains("its description does not list vmlinuz", environment.Current.Problem!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AListedFileThatIsNotThereIsDamage()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        File.Delete(Path.Combine(environment.Folder, "base.img"));

        Assert.False(environment.Available);
        Assert.Equal(
            $"The Linux boot environment in {environment.Folder} is damaged: base.img is not there. Install this release of DDT again, which brings the folder anew.",
            environment.Current.Problem!.Text);
    }

    [Fact]
    public void AListedFileOfAnotherSizeIsDamage()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        File.AppendAllText(Path.Combine(environment.Folder, "ddt.img"), "cut off or grown");

        Assert.False(environment.Available);
        Assert.Equal("size", Reason(environment.Current));
        Assert.Equal("ddt.img", environment.Current.Problem!.Args["name"]);
    }

    // The same size and other content: only reading the file tells, which happens once and in the background.
    [Fact]
    public async Task AListedFileWithOtherContentIsFoundDamagedOnceItsHashWasCompared()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        File.WriteAllText(Path.Combine(environment.Folder, "vmlinuz"), "TEST vmlinuz");

        await environment.CheckedAsync();

        Assert.False(environment.Available);
        Assert.Equal("hash", Reason(environment.Current));
        Assert.Contains("vmlinuz is not the file its description names", environment.Current.Problem!.Text, StringComparison.Ordinal);
    }

    // Machines trust the server only through the root they are handed, so without one none can be started.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithoutARootCertificateToHandOutNoMachineCanBeStarted(string? root)
    {
        LinuxBootEnvironment environment = Environment(root: () => root);
        TestLinuxBoot.Write(environment.Folder);

        LinuxBootState state = environment.Current;

        Assert.False(state.Available);
        Assert.Equal(ServerMessages.LinuxBootNoRoot.Code, state.Problem?.Code);
        Assert.StartsWith("The Linux boot environment cannot be started, because this server has no root certificate of its own", state.Problem!.Text, StringComparison.Ordinal);

        // What the pages show of it is still there.
        Assert.Equal("7.2.6+deb13-amd64", state.KernelRelease);
    }

    [Fact]
    public void ARootThatArrivesLaterMakesItAvailableWithoutAnotherLookAtTheFiles()
    {
        string? root = null;
        LinuxBootEnvironment environment = Environment(root: () => root);
        TestLinuxBoot.Write(environment.Folder);
        Assert.False(environment.Available);

        root = TestLinuxBoot.Root;

        Assert.True(environment.Available);
        Assert.Equal(TestLinuxBoot.Root, environment.RootCertificatePem);
    }

    // The description is read again only when it changed, and then it is.
    [Fact]
    public void NoticesADescriptionThatWasReplacedOrRemoved()
    {
        LinuxBootEnvironment environment = Environment();
        Directory.CreateDirectory(environment.Folder);
        File.WriteAllText(environment.ManifestPath, "not json at all");
        Assert.False(environment.Available);

        TestLinuxBoot.Write(environment.Folder);
        Assert.True(environment.Available);

        TestLinuxBoot.Remove(environment.Folder);
        Assert.False(environment.Available);
        Assert.Equal(ServerMessages.LinuxBootMissing.Code, environment.Current.Problem?.Code);
    }

    [Fact]
    public void LooksAtTheFilesOnceForADescriptionAndNotAtEveryQuestion()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        Assert.True(environment.Available);

        // Gone behind the catalogue's back: a question does not look. The look netboot asks for every few seconds does.
        File.Delete(Path.Combine(environment.Folder, "base.img"));

        Assert.True(environment.Available);
        Assert.True(environment.Known.Available);

        environment.Refresh();

        Assert.False(environment.Available);
        Assert.Equal("missing", Reason(environment.Current));
        Assert.Equal("base.img", environment.Current.Problem!.Args["name"]);
    }

    // The description is unchanged, so only the file's own time tells. The folder is then read whole before a machine
    // is started from it again, and found not to be what the description names.
    [Fact]
    public async Task AFileReplacedAtTheSameSizeUnderAnUnchangedDescriptionIsFoundAtTheNextLook()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        await environment.CheckedAsync();
        Assert.True(environment.Available);
        string kernel = Path.Combine(environment.Folder, "vmlinuz");
        string genuine = File.ReadAllText(kernel);
        (long Length, DateTime WriteTimeUtc) description = (new FileInfo(environment.ManifestPath).Length, File.GetLastWriteTimeUtc(environment.ManifestPath));

        File.WriteAllText(kernel, genuine.ToUpperInvariant());
        File.SetLastWriteTimeUtc(kernel, File.GetLastWriteTimeUtc(kernel) + TimeSpan.FromSeconds(2));
        environment.Refresh();

        Assert.Equal(description, (new FileInfo(environment.ManifestPath).Length, File.GetLastWriteTimeUtc(environment.ManifestPath)));
        Assert.False(environment.Available);
        Assert.False(environment.Known.Available);
        Assert.Equal("hash", Reason(environment.Current));
        Assert.Contains("vmlinuz is not the file its description names", environment.Current.Problem!.Text, StringComparison.Ordinal);

        // A folder that stays as it is is not read whole again at every look.
        LinuxBootState damaged = environment.Current;
        environment.Refresh();
        Assert.Same(damaged, environment.Current);

        // Put back, it is read again and found to be the release's file.
        File.WriteAllText(kernel, genuine);
        environment.Refresh();

        Assert.True(environment.Available);
        Assert.True(environment.Known.Available);
    }

    [Fact]
    public async Task AFileRemovedUnderAnUnchangedDescriptionIsFoundAtTheNextLookAndSoIsItsReturn()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        await environment.CheckedAsync();
        Assert.True(environment.Available);
        string image = Path.Combine(environment.Folder, "ddt.img");
        byte[] content = File.ReadAllBytes(image);

        File.Delete(image);
        environment.Refresh();

        Assert.False(environment.Known.Available);
        Assert.Equal("missing", Reason(environment.Known));

        File.WriteAllBytes(image, content);
        environment.Refresh();

        Assert.True(environment.Known.Available);
    }

    // Netboot found the file changed in the moment it was to send it, which is sooner than the next look.
    [Fact]
    public async Task AFileNetbootFindsChangedMakesTheEnvironmentUnavailableAtOnceAndUntilItWasReadAgain()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        await environment.CheckedAsync();
        string kernel = Path.Combine(environment.Folder, "vmlinuz");
        File.WriteAllText(kernel, File.ReadAllText(kernel).ToUpperInvariant());

        environment.Changed("vmlinuz");

        Assert.False(environment.Known.Available);
        Assert.False(environment.Available);
        Assert.Equal("changed", Reason(environment.Current));
        Assert.Equal(
            $"The Linux boot environment in {environment.Folder} is damaged: vmlinuz was replaced or removed after the server had checked it. " +
            "Install this release of DDT again, which brings the folder anew.",
            environment.Current.Problem!.Text);

        environment.Refresh();

        Assert.False(environment.Available);
        Assert.Equal("hash", Reason(environment.Current));
    }

    // The hashes are compared in the background. Whichever of the two ends last, what netboot found is the newer finding.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhatNetbootFindsWhileTheHashesAreComparedStaysOnceTheyWere(bool otherContent)
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);

        if (otherContent)
        {
            File.WriteAllText(Path.Combine(environment.Folder, "ddt.img"), "TEST ddt.img");
        }

        Task compared = environment.CheckedAsync();
        environment.Changed("vmlinuz");
        await compared;

        Assert.False(environment.Known.Available);
        Assert.Equal("changed", Reason(environment.Known));
        Assert.Equal("vmlinuz", environment.Known.Problem!.Args["name"]);
    }

    // Only the file's time moved, as a copy of the folder leaves it. Read again, it is the same file as before.
    [Fact]
    public async Task AFileThatWasOnlyTouchedIsServedAgainOnceItWasReadAgain()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        await environment.CheckedAsync();
        string kernel = Path.Combine(environment.Folder, "vmlinuz");
        BootFileStamp before = environment.Known.Files["vmlinuz"];

        File.SetLastWriteTimeUtc(kernel, before.WriteTimeUtc + TimeSpan.FromMinutes(1));
        environment.Changed("vmlinuz");
        Assert.False(environment.Known.Available);

        environment.Refresh();

        Assert.True(environment.Known.Available);
        Assert.Equal(before with { WriteTimeUtc = before.WriteTimeUtc + TimeSpan.FromMinutes(1) }, environment.Known.Files["vmlinuz"]);
    }

    // The packet loops ask for what is known. They must never be the ones to read the folder.
    [Fact]
    public void WhatIsKnownIsNotLookedUpAgainWhenTheDescriptionChanges()
    {
        LinuxBootEnvironment environment = Environment();
        TestLinuxBoot.Write(environment.Folder);
        Assert.True(environment.Known.Available);

        TestLinuxBoot.Remove(environment.Folder);

        Assert.True(environment.Known.Available);

        environment.Refresh();

        Assert.False(environment.Known.Available);
        Assert.Equal(ServerMessages.LinuxBootMissing.Code, environment.Known.Problem?.Code);
    }
}
