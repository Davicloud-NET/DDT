// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// Reads where Windows setup is from the values setup itself keeps under root, HKEY_LOCAL_MACHINE unless a test says
// otherwise: it runs while SystemSetupInProgress or OOBEInProgress is set, and has finished once the image state is
// IMAGE_STATE_COMPLETE, which it only becomes after the machine's part of the out-of-box experience.
//
// The first user's part comes after that, as defaultuser0, a temporary account setup signs in as, which looks for
// updates and may restart Windows; a step that ran then would be cut off. Setup deletes the account when it is done.
// While the account is signed in, setup still runs. The account without a session is setup between a restart and its
// next sign-in, for moments, or, after some minutes, an account setup left behind, which no longer holds the run up.
// Setup signs in as DDT's session's account only once it is done, so that session ends the wait at once.
// signedIn lists the user names of the sessions there are; setupUserExists says whether defaultuser0 exists.
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
