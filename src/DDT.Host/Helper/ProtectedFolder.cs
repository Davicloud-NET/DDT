// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;
using System.Security.Principal;

namespace DDT.Host.Helper;

// The helper's work folder, which SYSTEM and administrators alone can open. The build empties and fills it as SYSTEM,
// so nobody else may put a link into it.
public static class ProtectedFolder
{
    public static void Create(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            return;
        }

        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        using WindowsIdentity helper = WindowsIdentity.GetCurrent();

        // The account the helper runs as is SYSTEM, except in a test
        foreach (SecurityIdentifier? account in new[]
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            helper.User,
        })
        {
            if (account is not null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    account,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
        }

        DirectoryInfo folder = new(path);

        if (folder.Exists)
        {
            if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException($"{path} is a link, and the helper works only in a real folder.");
            }

            folder.SetAccessControl(security);
        }
        else
        {
            folder.Create(security);
        }
    }
}
