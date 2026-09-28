// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;

namespace DDT.Agent.Sequences;

// A directory only SYSTEM can open, for the run's token and session password: a protected DACL, as the volume root's
// default lets Authenticated Users create folders. S-1-5-18 is SYSTEM in Windows PE and in the installed Windows alike.
public static class SystemOnlyDirectory
{
    public const string Sddl = "D:P(A;OICI;FA;;;SY)";

    // Created with the DACL, so the directory is never open for a moment.
    public static void Create(string path)
    {
        DirectorySecurity security = new();
        security.SetSecurityDescriptorSddlForm(Sddl);
        new DirectoryInfo(path).Create(security);
    }
}
