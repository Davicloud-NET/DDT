// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The folder that stands in for the firmware variables of a dry run's machine.
public sealed class FileVendorVariablesTests : IDisposable
{
    private static readonly Guid s_vendor = Guid.Parse("d34b9a64-3d20-47de-94c2-e70dfc2ecce0");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ddt-firmware-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void KeepsAVariableAsAFileNamedLikeEfivarfsNamesIt()
    {
        FileVendorVariables variables = new(_folder);

        Assert.Null(variables.Read("DdtResume", s_vendor));
        Assert.False(Directory.Exists(_folder));

        variables.Write("DdtResume", s_vendor, [1, 2, 0x41]);

        Assert.Equal([1, 2, 0x41], File.ReadAllBytes(Path.Combine(_folder, "DdtResume-d34b9a64-3d20-47de-94c2-e70dfc2ecce0")));
        Assert.Equal([1, 2, 0x41], variables.Read("DdtResume", s_vendor));
        Assert.Null(variables.Read("DdtResume", Guid.Empty));
    }

    [Fact]
    public void ReplacesAndDeletesAVariableAndDeletingNoneIsFine()
    {
        FileVendorVariables variables = new(_folder);
        variables.Delete("DdtResume", s_vendor);
        variables.Write("DdtResume", s_vendor, [1]);
        variables.Write("DdtResume", s_vendor, [2, 3]);

        Assert.Equal([2, 3], variables.Read("DdtResume", s_vendor));

        variables.Delete("DdtResume", s_vendor);
        variables.Delete("DdtResume", s_vendor);

        Assert.Null(variables.Read("DdtResume", s_vendor));
    }

    // The carrier only warns about a firmware that refuses, and it knows a refusal by this exception.
    [Fact]
    public void AFolderThatCannotBeWrittenRefusesLikeAFirmware()
    {
        File.WriteAllText(_folder, "a file where the folder should be");

        try
        {
            DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => new FileVendorVariables(_folder).Write("DdtResume", s_vendor, [1]));

            Assert.StartsWith("The firmware variable DdtResume cannot be written (", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(_folder);
        }
    }
}
