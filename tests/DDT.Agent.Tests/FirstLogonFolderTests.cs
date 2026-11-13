// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;
using System.Security.Principal;
using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class FirstLogonFolderTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("ddt-acl-").FullName;

    private string Folder => Path.Combine(_parent, "DDT", "FirstLogon");

    public void Dispose()
    {
        SystemOnlyDirectoryTests.Reopen(Folder);
        Directory.Delete(_parent, recursive: true);
    }

    [Fact]
    public void CreatesAFolderOnlySystemAndAdministratorsCanOpen()
    {
        FirstLogonFolder.Create(Folder);

        DirectorySecurity security = new DirectoryInfo(Folder).GetAccessControl(AccessControlSections.Access);
        Assert.True(security.AreAccessRulesProtected);
        FileSystemAccessRule[] rules = [.. security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];
        Assert.Equal(
            [new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)],
            rules.Select(rule => rule.IdentityReference));
        Assert.All(rules, rule =>
        {
            Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
            Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        });
    }

    [Fact]
    public void ReplacesAFolderFoundThere()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "planted.cmd"), "echo planted");

        FirstLogonFolder.Create(Folder);

        Assert.True(new DirectoryInfo(Folder).GetAccessControl(AccessControlSections.Access).AreAccessRulesProtected);
        SystemOnlyDirectoryTests.Reopen(Folder);
        Assert.False(File.Exists(Path.Combine(Folder, "planted.cmd")));
    }
}
