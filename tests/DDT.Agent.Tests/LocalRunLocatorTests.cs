// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class LocalRunLocatorTests : IDisposable
{
    private readonly string _drives = Directory.CreateTempSubdirectory("ddt-locator-").FullName;

    public void Dispose() => Directory.Delete(_drives, recursive: true);

    [Fact]
    public void FindsTheVolumeWithTheNewestState()
    {
        string older = Drive("C", DateTime.UtcNow.AddDays(-2));
        string newer = Drive("D", DateTime.UtcNow.AddMinutes(-5));
        string without = Path.Combine(_drives, "E");
        Directory.CreateDirectory(without);

        LocalRunLocator locator = new([older, without, newer, Path.Combine(_drives, "missing")]);

        Assert.Equal(newer, locator.Find());
    }

    [Fact]
    public void FindsNothingWithoutAState()
    {
        string empty = Path.Combine(_drives, "C");
        Directory.CreateDirectory(Path.Combine(empty, "DDT", "run"));

        Assert.Null(new LocalRunLocator([empty]).Find());
    }

    [Fact]
    public void LeavesOutWindowsPEsOwnDrive()
    {
        string? own = Path.GetPathRoot(Environment.SystemDirectory);

        Assert.DoesNotContain(own, LocalRunLocator.FixedDrives(), StringComparer.OrdinalIgnoreCase);
    }

    private string Drive(string letter, DateTime written)
    {
        string root = Path.Combine(_drives, letter);
        string state = RunFiles.StatePathIn(root);
        Directory.CreateDirectory(Path.GetDirectoryName(state)!);
        File.WriteAllText(state, "{}");
        File.SetLastWriteTimeUtc(state, written);

        return root;
    }
}
