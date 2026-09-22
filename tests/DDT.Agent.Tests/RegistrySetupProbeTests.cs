// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// Setup's values as the probe reads them, under a key of the test's own in the current user's hive, which it deletes.
public sealed class RegistrySetupProbeTests : IDisposable
{
    private readonly string _path = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public RegistrySetupProbeTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    [Fact]
    public void SetupHasFinishedOnlyOnceNeitherPartRunsAndTheImageIsComplete()
    {
        using RegistryKey setup = _root.CreateSubKey(RegistrySetupProbe.SetupKeyPath);
        using RegistryKey state = _root.CreateSubKey(RegistrySetupProbe.StateKeyPath);
        setup.SetValue("SystemSetupInProgress", 1, RegistryValueKind.DWord);
        setup.SetValue("OOBEInProgress", 1, RegistryValueKind.DWord);
        state.SetValue("ImageState", "IMAGE_STATE_SPECIALIZE_RESEAL_TO_OOBE");
        RegistrySetupProbe probe = new(_root);

        Assert.Equal("Windows setup is still running", probe.Pending());

        setup.SetValue("SystemSetupInProgress", 0, RegistryValueKind.DWord);

        Assert.Equal("the out-of-box experience is still running", probe.Pending());

        setup.SetValue("OOBEInProgress", 0, RegistryValueKind.DWord);

        Assert.Equal("the image state is IMAGE_STATE_SPECIALIZE_RESEAL_TO_OOBE, not IMAGE_STATE_COMPLETE", probe.Pending());

        state.SetValue("ImageState", RegistrySetupProbe.Complete);

        Assert.Null(probe.Pending());
    }

    [Fact]
    public void AnImageStateThatWasNeverSetIsNotComplete()
    {
        Assert.Equal("the image state is not set, not IMAGE_STATE_COMPLETE", new RegistrySetupProbe(_root).Pending());
    }
}
