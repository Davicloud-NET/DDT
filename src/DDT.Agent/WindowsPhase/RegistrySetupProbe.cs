// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// Reads where Windows setup is from the values setup itself keeps under root, HKEY_LOCAL_MACHINE unless a test says
// otherwise: it runs while SystemSetupInProgress or OOBEInProgress is set, and has finished once the image state is
// IMAGE_STATE_COMPLETE, which it only becomes after the out-of-box experience.
public sealed class RegistrySetupProbe(RegistryKey root) : IWindowsSetupProbe
{
    public const string Complete = "IMAGE_STATE_COMPLETE";

    public const string SetupKeyPath = @"SYSTEM\Setup";
    public const string StateKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\State";

    public RegistrySetupProbe()
        : this(Registry.LocalMachine)
    {
    }

    public string? Pending()
    {
        try
        {
            using (RegistryKey? setup = root.OpenSubKey(SetupKeyPath))
            {
                if (setup?.GetValue("SystemSetupInProgress") is int system && system != 0)
                {
                    return "Windows setup is still running";
                }

                if (setup?.GetValue("OOBEInProgress") is int oobe && oobe != 0)
                {
                    return "the out-of-box experience is still running";
                }
            }

            using RegistryKey? state = root.OpenSubKey(StateKeyPath);
            string? image = state?.GetValue("ImageState") as string;

            return image == Complete ? null : $"the image state is {image ?? "not set"}, not {Complete}";
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return $"setup's state cannot be read ({exception.Message})";
        }
    }
}
