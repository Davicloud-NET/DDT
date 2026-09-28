// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace DDT.Server.Certificates;

internal static class PemFiles
{
    // Writes next to the target and renames the file into place, so a reader never sees half a file. A key is
    // owner-only from the moment it exists, not after a later change of mode.
    public static void Write(string path, string pem, bool isKey)
    {
        string temporary = path + ".tmp";
        File.Delete(temporary);

        using (StreamWriter writer = new(Create(temporary, isKey), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(pem);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static FileStream Create(string path, bool isKey)
    {
        if (isKey && OperatingSystem.IsWindows())
        {
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, OwnerOnly());
        }

        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        if (isKey && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    // Grants access only to the account DDT runs as, SYSTEM and administrators. Inheritance is off because a folder
    // on a data drive passes read and write access for every signed-in user down to the files created in it.
    [SupportedOSPlatform("windows")]
    private static FileSecurity OwnerOnly()
    {
        using WindowsIdentity account = WindowsIdentity.GetCurrent();
        FileSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (SecurityIdentifier identity in new[]
        {
            account.User!,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
        })
        {
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }
}
