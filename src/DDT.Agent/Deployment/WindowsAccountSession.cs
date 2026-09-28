// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using DDT.Contracts.Agents;
using static DDT.Agent.Deployment.AccountNativeMethods;

namespace DDT.Agent.Deployment;

// An account signed in for a step. An administrator gets its linked token, without User Account Control's filter, as
// a script run from an elevated prompt would.
[SupportedOSPlatform("windows")]
public sealed unsafe class WindowsAccountSession : IAccountSession
{
    private readonly SafeKernelHandle _token;
    private readonly nint _profile;
    private bool _disposed;

    private WindowsAccountSession(string userName, SafeKernelHandle token, nint profile, SecurityIdentifier logonSid)
    {
        UserName = userName;
        _token = token;
        _profile = profile;
        LogonSid = logonSid;
    }

    public string UserName { get; }

    // The SID of this one logon, which the window station, the desktop and the step's files admit, rather than every
    // logon of the account.
    public SecurityIdentifier LogonSid { get; }

    internal SafeKernelHandle Token => _token;

    // DOMAIN\user signs in to DOMAIN, a UPN as it is, and a bare name as a local account. The profile takes the name
    // alone.
    public static (string User, string? Domain, string ProfileName) Split(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);

        int slash = userName.IndexOf('\\', StringComparison.Ordinal);

        if (slash >= 0)
        {
            string user = userName[(slash + 1)..];

            return (user, userName[..slash], user);
        }

        int at = userName.IndexOf('@', StringComparison.Ordinal);

        return at >= 0 ? (userName, null, userName[..at]) : (userName, ".", userName);
    }

    internal static WindowsAccountSession LogOn(AgentAccount account, AgentLog log)
    {
        (string user, string? domain, string profileName) = Split(account.UserName);

        if (!LogonUser(user, domain, account.Password, LogonInteractive, LogonProviderDefault, out SafeKernelHandle token))
        {
            DeploymentStepException failure = DeploymentStepException.ForLastWin32Error($"{account.UserName} could not sign in on this computer");
            token.Dispose();

            throw failure;
        }

        try
        {
            if (LinkedToken(token) is { } elevated)
            {
                token.Dispose();
                token = elevated;
                log.Information($"{account.UserName} is an administrator, so the script runs with an administrator's full rights.");
            }

            SecurityIdentifier logonSid = LogonSidOf(token);
            nint profile;

            fixed (char* name = profileName)
            {
                ProfileInfo info = new() { Size = (uint)sizeof(ProfileInfo), Flags = ProfileNoUi, UserName = name };

                if (!LoadUserProfile(token, &info))
                {
                    throw DeploymentStepException.ForLastWin32Error($"The profile of {account.UserName} could not be loaded");
                }

                profile = info.Profile;
            }

            log.Information($"Signed in as {account.UserName}.");

            return new WindowsAccountSession(account.UserName, token, profile, logonSid);
        }
        catch
        {
            token.Dispose();

            throw;
        }
    }

    public Task<T> ImpersonateAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return DedicatedThread.RunAsync(
            () =>
            {
                if (!ImpersonateLoggedOnUser(_token))
                {
                    throw DeploymentStepException.ForLastWin32Error($"DDT could not act as {UserName}");
                }

                try
                {
                    return action();
                }
                finally
                {
                    // Should it fail, the thread ends with this call all the same, and the account with it.
                    _ = RevertToSelf();
                }
            },
            "DDT acting as an account");
    }

    // Modify with everything inside, including what is there already, which SetAccessControl hands down.
    public void Admit(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        DirectoryInfo info = new(directory);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            LogonSid,
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_profile != 0)
        {
            _ = UnloadUserProfile(_token, _profile);
        }

        _token.Dispose();
    }

    // The token with every right, for a token User Account Control filtered; null for any other.
    private static SafeKernelHandle? LinkedToken(SafeKernelHandle token)
    {
        int type;

        if (!GetTokenInformation(token, TokenElevationType, &type, sizeof(int), out _) || type != TokenElevationTypeLimited)
        {
            return null;
        }

        nint linked;

        if (!GetTokenInformation(token, TokenLinkedToken, &linked, (uint)sizeof(nint), out _))
        {
            throw DeploymentStepException.ForLastWin32Error("The administrator's rights of the account could not be taken up");
        }

        return new SafeKernelHandle(linked);
    }

    private static SecurityIdentifier LogonSidOf(SafeKernelHandle token)
    {
        _ = GetTokenInformation(token, TokenGroups, null, 0, out uint length);
        byte[] buffer = new byte[length];

        fixed (byte* groups = buffer)
        {
            if (!GetTokenInformation(token, TokenGroups, groups, length, out _))
            {
                throw DeploymentStepException.ForLastWin32Error("The groups of the account could not be read");
            }

            TokenGroupsHeader* header = (TokenGroupsHeader*)groups;
            SidAndAttributes* entries = &header->First;

            for (uint index = 0; index < header->GroupCount; index++)
            {
                if ((entries[index].Attributes & GroupLogonId) == GroupLogonId)
                {
                    return new SecurityIdentifier(entries[index].Sid);
                }
            }
        }

        throw new DeploymentStepException("The sign-in of the account has no logon SID, which its window station needs.");
    }
}
