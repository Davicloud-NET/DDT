// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;

namespace DDT.Agent.WindowsPhase;

// The first-logon script's folder, which only SYSTEM and administrators can open. %ProgramData% lets users create
// folders, so the DACL is protected and a folder found there is replaced.
public static class FirstLogonFolder
{
    public const string Sddl = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

    public static void Create(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? path);

        DirectorySecurity security = new();
        security.SetSecurityDescriptorSddlForm(Sddl);
        new DirectoryInfo(path).Create(security);
    }
}
