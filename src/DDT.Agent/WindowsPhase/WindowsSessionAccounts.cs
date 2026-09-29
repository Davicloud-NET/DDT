// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// ISessionAccounts on the Windows the agent runs in, as SYSTEM. Winlogon reads the auto-logon password from the LSA
// secret DefaultPassword when the Winlogon key has none. Only SYSTEM can read that secret, while every user can read
// the key.
[SupportedOSPlatform("windows")]
public sealed unsafe class WindowsSessionAccounts : ISessionAccounts
{
    private const string AutoLogonSecret = "DefaultPassword";
    private const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    public bool Create(string name, string password)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(password);

        int status;

        fixed (char* user = name)
        fixed (char* secret = password)
        {
            SessionNativeMethods.UserInfo1 info = new()
            {
                Name = user,
                Password = secret,
                Privilege = SessionNativeMethods.UserPrivilegeUser,
                Flags = SessionNativeMethods.UfScript | SessionNativeMethods.UfDontExpirePassword | SessionNativeMethods.UfPasswordCannotChange,
            };
            status = SessionNativeMethods.NetUserAdd(null, 1, &info, out _);
        }

        if (status == SessionNativeMethods.NerrUserExists)
        {
            return false;
        }

        Check(status, $"The account {name} could not be created");

        // On a workstation a new account is already in Users. This makes sure of it, because signing in needs it.
        SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
        string group = users.Translate(typeof(NTAccount)).Value.Split('\\')[^1];
        SecurityIdentifier account = Sid(name);
        byte[] sid = new byte[account.BinaryLength];
        account.GetBinaryForm(sid, 0);

        fixed (byte* member = sid)
        {
            nint entry = (nint)member;
            int added = SessionNativeMethods.NetLocalGroupAddMembers(null, group, 0, &entry, 1);

            if (added != SessionNativeMethods.ErrorMemberInAlias)
            {
                Check(added, $"The account {name} could not be added to {group}");
            }
        }

        return true;
    }

    // Looks up the bare name. Windows searches the local accounts before any domain's, and while setup renames the
    // computer, the new computer name may not qualify the local accounts yet.
    public SecurityIdentifier Sid(string name) => (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));

    public bool Exists(string name) => AccountExists(name);

    public static bool AccountExists(string name)
    {
        try
        {
            _ = new NTAccount(name).Translate(typeof(SecurityIdentifier));

            return true;
        }
        catch (IdentityNotMappedException)
        {
            return false;
        }
    }

    public void SetPassword(string name, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        fixed (char* secret = password)
        {
            // USER_INFO_1003 is the password alone.
            char* info = secret;
            Check(SessionNativeMethods.NetUserSetInfo(null, name, 1003, &info, out _), $"The password of {name} could not be set");
        }
    }

    public void Disable(string name, string password)
    {
        SetPassword(name, password);

        // USER_INFO_1008 is the flags alone.
        uint flags = SessionNativeMethods.UfScript | SessionNativeMethods.UfAccountDisable | SessionNativeMethods.UfDontExpirePassword
            | SessionNativeMethods.UfPasswordCannotChange;
        Check(SessionNativeMethods.NetUserSetInfo(null, name, 1008, &flags, out _), $"The account {name} could not be disabled");
    }

    public bool Delete(string name)
    {
        int status = SessionNativeMethods.NetUserDel(null, name);

        if (status == SessionNativeMethods.NerrUserNotFound)
        {
            return false;
        }

        Check(status, $"The account {name} could not be deleted");

        return true;
    }

    public string? AutoLogonPassword()
    {
        nint policy = OpenPolicy();

        try
        {
            fixed (char* name = AutoLogonSecret)
            {
                SessionNativeMethods.LsaUnicodeString key = Unicode(name, AutoLogonSecret.Length);
                uint status = SessionNativeMethods.LsaRetrievePrivateData(policy, &key, out SessionNativeMethods.LsaUnicodeString* data);

                if (status == SessionNativeMethods.StatusObjectNameNotFound)
                {
                    return null;
                }

                CheckLsa(status, "The auto-logon password could not be read");

                try
                {
                    return data is null || data->Buffer is null ? null : new string(data->Buffer, 0, data->Length / sizeof(char));
                }
                finally
                {
                    _ = SessionNativeMethods.LsaFreeMemory(data);
                }
            }
        }
        finally
        {
            _ = SessionNativeMethods.LsaClose(policy);
        }
    }

    public void SetAutoLogonPassword(string? password)
    {
        nint policy = OpenPolicy();

        try
        {
            fixed (char* name = AutoLogonSecret)
            fixed (char* secret = password)
            {
                SessionNativeMethods.LsaUnicodeString key = Unicode(name, AutoLogonSecret.Length);
                SessionNativeMethods.LsaUnicodeString value = Unicode(secret, password?.Length ?? 0);
                uint status = SessionNativeMethods.LsaStorePrivateData(policy, &key, password is null ? null : &value);

                if (password is null && status == SessionNativeMethods.StatusObjectNameNotFound)
                {
                    return;
                }

                CheckLsa(status, "The auto-logon password could not be stored");
            }
        }
        finally
        {
            _ = SessionNativeMethods.LsaClose(policy);
        }
    }

    public string CreateProfile(SecurityIdentifier sid, string name)
    {
        ArgumentNullException.ThrowIfNull(sid);

        const int capacity = 260;
        char* path = stackalloc char[capacity];
        int result = SessionNativeMethods.CreateProfile(sid.Value, name, path, capacity);

        if (result == SessionNativeMethods.ProfileExists)
        {
            using RegistryKey? profile = Registry.LocalMachine.OpenSubKey($@"{ProfileListPath}\{sid.Value}");

            return profile?.GetValue("ProfileImagePath") is string existing
                ? Environment.ExpandEnvironmentVariables(existing)
                : throw new InvalidOperationException($"The profile of {name} exists, but Windows does not say where.");
        }

        Marshal.ThrowExceptionForHR(result);

        return new string(path);
    }

    public bool DeleteProfile(SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(sid);

        if (SessionNativeMethods.DeleteProfile(sid.Value, null, null))
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();

        if (error is SessionNativeMethods.ErrorFileNotFound or SessionNativeMethods.ErrorPathNotFound)
        {
            return false;
        }

        throw new Win32Exception(error);
    }

    public IReadOnlyList<int> Sessions(string name) =>
        [
            .. AllSessions()
                .Where(session => string.Equals(session.User, name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(session.Domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                .Select(session => session.Id),
        ];

    // The user names of every session, for RegistrySetupProbe. Empty when they can't be listed.
    public static IEnumerable<string> SignedInUserNames()
    {
        try
        {
            return [.. AllSessions().Select(session => session.User).OfType<string>().Where(user => user.Length > 0)];
        }
        catch (Win32Exception)
        {
            return [];
        }
    }

    private static List<(int Id, string? User, string? Domain)> AllSessions()
    {
        if (!SessionNativeMethods.WtsEnumerateSessions(SessionNativeMethods.WtsCurrentServer, 0, 1, out SessionNativeMethods.WtsSessionInfo* sessions, out uint count))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        List<(int Id, string? User, string? Domain)> found = [];

        try
        {
            for (uint index = 0; index < count; index++)
            {
                uint id = sessions[index].SessionId;
                found.Add(((int)id, Query(id, SessionNativeMethods.WtsUserName), Query(id, SessionNativeMethods.WtsDomainName)));
            }
        }
        finally
        {
            SessionNativeMethods.WtsFreeMemory(sessions);
        }

        return found;
    }

    public void SignOut(int sessionId)
    {
        if (!SessionNativeMethods.WtsLogoffSession(SessionNativeMethods.WtsCurrentServer, (uint)sessionId, wait: true))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static string? Query(uint sessionId, int infoClass)
    {
        if (!SessionNativeMethods.WtsQuerySessionInformation(SessionNativeMethods.WtsCurrentServer, sessionId, infoClass, out char* buffer, out _))
        {
            return null;
        }

        try
        {
            return new string(buffer);
        }
        finally
        {
            SessionNativeMethods.WtsFreeMemory(buffer);
        }
    }

    private static nint OpenPolicy()
    {
        SessionNativeMethods.LsaObjectAttributes attributes = new() { Length = (uint)sizeof(SessionNativeMethods.LsaObjectAttributes) };
        uint status = SessionNativeMethods.LsaOpenPolicy(
            null,
            &attributes,
            SessionNativeMethods.PolicyGetPrivateInformation | SessionNativeMethods.PolicyCreateSecret,
            out nint policy);
        CheckLsa(status, "The local security authority could not be opened");

        return policy;
    }

    private static SessionNativeMethods.LsaUnicodeString Unicode(char* text, int length) => new()
    {
        Length = (ushort)(length * sizeof(char)),
        MaximumLength = (ushort)(length * sizeof(char)),
        Buffer = text,
    };

    private static void Check(int status, string what)
    {
        if (status != SessionNativeMethods.NerrSuccess)
        {
            throw new Win32Exception(status, $"{what} (error {status}).");
        }
    }

    private static void CheckLsa(uint status, string what)
    {
        if (status != 0)
        {
            int error = SessionNativeMethods.LsaNtStatusToWinError(status);

            throw new Win32Exception(error, $"{what} (error {error}).");
        }
    }
}
