// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Pxe.Tests;

public sealed class BootBuildsTests : IDisposable
{
    private readonly string _boot = Path.Combine(Path.GetTempPath(), "ddt-builds-" + Guid.NewGuid().ToString("N"));

    public BootBuildsTests()
    {
        Write(_boot, "by hand");
        Write(BootBuilds.FolderOf(_boot, "first"), "first");
        Write(BootBuilds.FolderOf(_boot, "second"), "second");
    }

    [Fact]
    public void WithoutACurrentBuildTheBootDirectoryItselfIsServed()
    {
        Assert.Null(BootBuilds.Current(_boot));
        Assert.Equal("by hand", Served());
    }

    [Fact]
    public void TheCurrentBuildIsServedAndGoingBackServesTheOneBefore()
    {
        BootBuilds.SetCurrent(_boot, "second");
        Assert.Equal("second", BootBuilds.Current(_boot));
        Assert.Equal("second", Served());

        BootBuilds.SetCurrent(_boot, "first");
        Assert.Equal("first", Served());

        BootBuilds.SetCurrent(_boot, null);
        Assert.Equal("by hand", Served());
    }

    [Theory]
    [InlineData("third")]
    [InlineData("..")]
    [InlineData("../../outside")]
    [InlineData("")]
    public void ACurrentThatNamesNoBuildServesTheBootDirectory(string name)
    {
        File.WriteAllText(Path.Combine(_boot, BootBuilds.MarkerName), name);

        Assert.Null(BootBuilds.Current(_boot));
        Assert.Equal("by hand", Served());
    }

    [Fact]
    public void ABuildBecomesCurrentWhileTheOldNameIsStillBeingRead()
    {
        BootBuilds.SetCurrent(_boot, "first");

        using (new FileStream(Path.Combine(_boot, BootBuilds.MarkerName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            BootBuilds.SetCurrent(_boot, "second");
        }

        Assert.Equal("second", Served());
    }

    [Fact]
    public void OnlyFoldersWithTheNameOfABuildAreListed()
    {
        Directory.CreateDirectory(Path.Combine(_boot, BootBuilds.FolderName, ".hidden"));

        Assert.Equal(["first", "second"], BootBuilds.List(_boot));
        Assert.Throws<ArgumentException>(() => BootBuilds.SetCurrent(_boot, "../elsewhere"));
    }

    public void Dispose() => Directory.Delete(_boot, recursive: true);

    private string Served()
    {
        Assert.True(new BootFileResolver(_boot).TryResolve(@"\Boot\BCD", out FileInfo? file));

        return File.ReadAllText(file.FullName);
    }

    private static void Write(string root, string content)
    {
        Directory.CreateDirectory(Path.Combine(root, "Boot"));
        File.WriteAllText(Path.Combine(root, "Boot", "BCD"), content);
    }
}
