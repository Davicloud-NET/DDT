// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Pxe.Tests;

public sealed class BootFileResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddt-boot-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "ddt-outside-" + Guid.NewGuid().ToString("N"));
    private readonly BootFileResolver _resolver;

    public BootFileResolverTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "x64"));
        Directory.CreateDirectory(Path.Combine(_root, "Boot"));
        Directory.CreateDirectory(_outside);

        File.WriteAllText(Path.Combine(_root, "x64", "bootmgfw.efi"), "boot");
        File.WriteAllText(Path.Combine(_root, "Boot", "BCD"), "bcd");
        File.WriteAllText(Path.Combine(_root, "boot.sdi"), "sdi");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "not yours");

        _resolver = new BootFileResolver(_root);
    }

    public void Dispose()
    {
        // A junction has to be removed as a link. Deleting recursively through one would take the
        // target's contents with it, which is the whole reason the resolver refuses to follow them.
        foreach (DirectoryInfo directory in new DirectoryInfo(_root).GetDirectories())
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                directory.Delete(recursive: false);
            }
        }

        Directory.Delete(_root, recursive: true);
        Directory.Delete(_outside, recursive: true);
    }

    [Theory]
    [InlineData("x64/bootmgfw.efi")]
    [InlineData("boot.sdi")]
    public void ServesAFileInsideTheBootDirectory(string requested)
    {
        Assert.True(_resolver.TryResolve(requested, out FileInfo? file));
        Assert.True(file.Exists);
        Assert.StartsWith(_resolver.Root, file.FullName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"x64\bootmgfw.efi")]
    [InlineData(@"\Boot\BCD")]
    [InlineData("/Boot/BCD")]
    public void AcceptsTheSeparatorsWindowsBootComponentsSend(string requested)
    {
        // The boot manager asks for "\Boot\BCD". The leading separator means the TFTP root.
        Assert.True(_resolver.TryResolve(requested, out FileInfo? file));
        Assert.StartsWith(_resolver.Root, file.FullName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("X64/BOOTMGFW.EFI")]
    [InlineData("boot/bcd")]
    public void MatchesNamesCaseInsensitivelyAndReturnsTheNameOnDisk(string requested)
    {
        Assert.True(_resolver.TryResolve(requested, out FileInfo? file));
        Assert.Contains(file.Name, new[] { "bootmgfw.efi", "BCD" });
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("x64/../../secret.txt")]
    [InlineData("x64/..\\..\\secret.txt")]
    [InlineData("./../../secret.txt")]
    [InlineData("x64/./../../secret.txt")]
    public void RefusesToEscapeTheBootDirectory(string requested)
    {
        Assert.False(_resolver.TryResolve(requested, out _));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32\\config\\SAM")]
    [InlineData("C:boot.sdi")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData("//server/share/file")]
    public void NeverLeavesTheRootForAbsoluteDriveOrUncPaths(string requested)
    {
        // A leading separator is stripped, so these name paths under the root, which do not exist.
        // What matters is that none of them reaches the filesystem outside it.
        Assert.False(_resolver.TryResolve(requested, out _));
    }

    [Theory]
    [InlineData("boot.sdi.")]
    [InlineData("boot.sdi ")]
    [InlineData(" boot.sdi")]
    [InlineData("boot.sdi::$DATA")]
    [InlineData("boot.sdi:hidden")]
    public void RefusesNamesWindowsWouldAliasToAnotherFile(string requested)
    {
        Assert.False(_resolver.TryResolve(requested, out _));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("x64/LPT1.efi")]
    public void RefusesReservedDeviceNames(string requested)
    {
        Assert.False(_resolver.TryResolve(requested, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("x64//bootmgfw.efi")]
    [InlineData("x64")]
    [InlineData("x64/")]
    [InlineData("does-not-exist.efi")]
    public void RefusesEmptyDirectoryAndMissingNames(string requested)
    {
        Assert.False(_resolver.TryResolve(requested, out _));
    }

    [Fact]
    public void RefusesAnEightDotThreeShortNameForALongName()
    {
        string longDirectory = Path.Combine(_root, "averylongdirectoryname");
        Directory.CreateDirectory(longDirectory);
        File.WriteAllText(Path.Combine(longDirectory, "file.efi"), "long");

        string alias = Path.Combine(_root, "AVERYL~1", "file.efi");

        Assert.SkipUnless(File.Exists(alias), "8.3 short names are not generated on this volume.");

        // The short name opens the same file, but it is not a name the directory lists, so it would
        // otherwise defeat any allowlist or audit built on requested names.
        Assert.False(_resolver.TryResolve("AVERYL~1/file.efi", out _));
        Assert.True(_resolver.TryResolve("averylongdirectoryname/file.efi", out _));
    }

    [Fact]
    public void RefusesAFileInsideADirectoryJunctionThatLeavesTheRoot()
    {
        string link = Path.Combine(_root, "linked");

        if (!TryCreateDirectoryLink(link, _outside))
        {
            Assert.Fail("Could not create a directory link, so the reparse point defence was not exercised.");
        }

        Assert.True(File.Exists(Path.Combine(link, "secret.txt")));
        Assert.False(_resolver.TryResolve("linked/secret.txt", out _));
    }

    [Fact]
    public void ServesAnOrdinaryFileThatSitsBesideALink()
    {
        string link = Path.Combine(_root, "linked2");
        _ = TryCreateDirectoryLink(link, _outside);

        Assert.True(_resolver.TryResolve("boot.sdi", out _));
    }

    [Fact]
    public void RefusesEverythingWhenTheBootDirectoryDoesNotExist()
    {
        BootFileResolver missing = new(Path.Combine(_root, "missing"));

        Assert.False(missing.TryResolve("boot.sdi", out _));
    }

    // A directory junction is the realistic attack on Windows: an unprivileged account can create
    // one, while a symbolic link needs SeCreateSymbolicLinkPrivilege or developer mode.
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", link, target },
        });

        if (process is null)
        {
            return false;
        }

        process.WaitForExit();

        return process.ExitCode == 0 && Directory.Exists(link);
    }
}
