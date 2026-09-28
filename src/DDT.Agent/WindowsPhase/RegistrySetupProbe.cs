// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// Reads where Windows setup is from the values it keeps under root: it runs while SystemSetupInProgress or OOBEInProgress
// is set, until the image state is IMAGE_STATE_COMPLETE, and then as defaultuser0, whose updates may restart Windows.
// signedIn lists the sessions' user names; setupUserExists says whether defaultuser0 exists.
public sealed class RegistrySetupProbe(
    RegistryKey root,
    Func<IEnumerable<string>>? signedIn = null,
    Func<bool>? setupUserExists = null,
    TimeProvider? timeProvider = null) : IWindowsSetupProbe
{
    public const string Complete = "IMAGE_STATE_COMPLETE";

    public const string SetupKeyPath = @"SYSTEM\Setup";
    public const string StateKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\State";

    public const string SetupUser = "defaultuser0";

    // defaultuser0, or defaultuser100000 and the like on some builds.
    public const string SetupUserPrefix = "defaultuser";

    public static readonly TimeSpan LeftoverAfter = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long? _accountWithoutSession;

    public RegistrySetupProbe()
        : this(Registry.LocalMachine, WindowsSessionAccounts.SignedInUserNames, () => WindowsSessionAccounts.AccountExists(SetupUser))
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

            if (image != Complete)
            {
                return $"the image state is {image ?? "not set"}, not {Complete}";
            }

            return FirstUserPending();
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return $"setup's state cannot be read ({exception.Message})";
        }
    }

    // Setup deletes defaultuser0 once done. Without a session, the account is setup between a restart and its next
    // sign-in, or, after LeftoverAfter, one setup left behind, which no longer holds the run up.
    private string? FirstUserPending()
    {
        List<string> names = [.. signedIn?.Invoke() ?? []];

        // Setup signs in as DDT's session's account only at its very end, as the answer file says.
        if (names.Any(name => name.Equals(DeploySession.AccountName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (names.Any(name => name.StartsWith(SetupUserPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            _accountWithoutSession = null;

            return "Windows still sets up its first user";
        }

        if (setupUserExists?.Invoke() != true)
        {
            return null;
        }

        _accountWithoutSession ??= _time.GetTimestamp();

        return _time.GetElapsedTime(_accountWithoutSession.Value) < LeftoverAfter
            ? "Windows still sets up its first user, and signs it in again"
            : null;
    }
}
