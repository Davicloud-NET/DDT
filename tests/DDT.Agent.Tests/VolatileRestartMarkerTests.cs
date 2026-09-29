// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// The marker under a test key in the current user's hive, which the test deletes. The service's key is an ordinary key,
// like the hand-over registers it.
public sealed class VolatileRestartMarkerTests : IDisposable
{
    private const int ErrorChildMustBeVolatile = 1021;

    private readonly string _path = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public VolatileRestartMarkerTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
        _root.CreateSubKey(Path.GetDirectoryName(VolatileRestartMarker.KeyPath)!).Dispose();
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    // A marker that outlived the restart would have every later start of the service restart Windows again.
    [Fact]
    public void ARestartIsDueOnceSetAndTheMarkIsGoneWhenWindowsNextStarts()
    {
        VolatileRestartMarker marker = new(_root, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null));

        Assert.False(marker.IsSet);

        marker.Set();

        Assert.True(marker.IsSet);

        // Windows holds a volatile key in memory only, and only a volatile key may be created under one.
        using RegistryKey key = _root.OpenSubKey(VolatileRestartMarker.KeyPath, writable: true)!;
        IOException refused = Assert.Throws<IOException>(() => key.CreateSubKey("Lasting").Dispose());
        Assert.Equal(ErrorChildMustBeVolatile, refused.HResult & 0xFFFF);
    }
}
