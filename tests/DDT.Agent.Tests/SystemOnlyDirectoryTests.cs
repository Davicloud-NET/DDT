// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;
using System.Security.Principal;
using DDT.Agent.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class SystemOnlyDirectoryTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("ddt-acl-").FullName;

    public void Dispose()
    {
        Reopen(Path.Combine(_parent, "DDT"));
        Directory.Delete(_parent, recursive: true);
    }

    // The test's account created the directory, so as its owner it may still read and change the DACL, and puts one
    // back that lets it delete the directory.
    internal static void Reopen(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        DirectorySecurity open = new();
        open.AddAccessRule(new FileSystemAccessRule(
            identity.User!,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(open);
    }

    [Fact]
    public void CreatesADirectoryOnlySystemCanOpen()
    {
        string directory = Path.Combine(_parent, "DDT");

        SystemOnlyDirectory.Create(directory);

        DirectorySecurity security = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access);
        Assert.True(security.AreAccessRulesProtected);
        FileSystemAccessRule rule = Assert.IsType<FileSystemAccessRule>(Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier))));
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), rule.IdentityReference);
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
        Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(directory, "token"), "run-token"));
    }
}
