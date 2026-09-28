// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// Setup's values as the probe reads them, under a test key in the current user's hive, which the test deletes.
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

    // Setup's first user checks for updates after the machine's part and may restart Windows, which cuts a step off.
    [Fact]
    public void SetupHasNotFinishedWhileItsFirstUserIsSignedIn()
    {
        using RegistryKey state = _root.CreateSubKey(RegistrySetupProbe.StateKeyPath);
        state.SetValue("ImageState", RegistrySetupProbe.Complete);
        List<string> signedIn = ["defaultuser0"];
        RegistrySetupProbe probe = new(_root, () => signedIn);

        Assert.Equal("Windows still sets up its first user", probe.Pending());

        signedIn = ["DDTDeploy"];

        Assert.Null(probe.Pending());
    }

    // Between a restart of setup and the next sign-in of its first user, the account is there without a session.
    [Fact]
    public void TheFirstUsersAccountHoldsTheRunUpOnlyForAWhileWithoutASession()
    {
        using RegistryKey state = _root.CreateSubKey(RegistrySetupProbe.StateKeyPath);
        state.SetValue("ImageState", RegistrySetupProbe.Complete);
        ManualTimeProvider time = new();
        bool exists = true;
        RegistrySetupProbe probe = new(_root, () => [], () => exists, time);

        Assert.Equal("Windows still sets up its first user, and signs it in again", probe.Pending());

        time.Advance(RegistrySetupProbe.LeftoverAfter);

        // One setup left behind.
        Assert.Null(probe.Pending());

        exists = false;
        Assert.Null(probe.Pending());
    }

    // The answer file's auto-logon is the last thing setup does, whatever it leaves behind of its first user.
    [Fact]
    public void DdtsSessionEndsTheWaitAtOnce()
    {
        using RegistryKey state = _root.CreateSubKey(RegistrySetupProbe.StateKeyPath);
        state.SetValue("ImageState", RegistrySetupProbe.Complete);
        RegistrySetupProbe probe = new(_root, () => ["DDTDeploy"], () => true, new ManualTimeProvider());

        Assert.Null(probe.Pending());
    }

    [Fact]
    public void AnImageStateThatWasNeverSetIsNotComplete()
    {
        Assert.Equal("the image state is not set, not IMAGE_STATE_COMPLETE", new RegistrySetupProbe(_root).Pending());
    }
}
