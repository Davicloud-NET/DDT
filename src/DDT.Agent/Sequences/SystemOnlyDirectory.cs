// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;

namespace DDT.Agent.Sequences;

// A directory only SYSTEM can open, for the run's token and session password. It gets a protected DACL, because the
// volume root's default lets Authenticated Users create folders. S-1-5-18 is SYSTEM in both WinPE and the installed
// Windows.
public static class SystemOnlyDirectory
{
    public const string Sddl = "D:P(A;OICI;FA;;;SY)";

    // Created with the DACL, so the directory is never open, not even for a moment.
    public static void Create(string path)
    {
        DirectorySecurity security = new();
        security.SetSecurityDescriptorSddlForm(Sddl);
        new DirectoryInfo(path).Create(security);
    }
}
