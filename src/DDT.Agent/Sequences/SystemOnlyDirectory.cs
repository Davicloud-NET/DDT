// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;

namespace DDT.Agent.Sequences;

// A directory only SYSTEM can open: a protected DACL, so nothing is inherited from the volume's root, whose default
// lets Authenticated Users create folders, with one entry that everything created inside inherits. The agent runs as
// SYSTEM in Windows PE, and S-1-5-18 is the same account in the installed Windows, so the run's token stays closed to
// everyone else there.
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
