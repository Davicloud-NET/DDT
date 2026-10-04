// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Disks;
using Xunit;

namespace DDT.Core.Tests.Disks;

public sealed class VolumeSpaceTests
{
    [Fact]
    public void AFolderBelowTheRootMeasuresItsVolume()
    {
        string folder = Directory.CreateTempSubdirectory("ddt-space-").FullName;

        try
        {
            (long available, long total) = VolumeSpace.Of(folder);

            Assert.InRange(available, 1, total);
            Assert.Equal(new DriveInfo(Path.GetPathRoot(folder) ?? folder).TotalSize, total);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void AFolderThatIsNotThereIsAnError()
    {
        string missing = Path.Combine(Path.GetTempPath(), "ddt-space-" + Guid.NewGuid().ToString("N"), "nowhere");

        Assert.ThrowsAny<IOException>(() => VolumeSpace.Of(missing));
    }
}
