// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Applications;

// The Windows Installer function that tells whether a product is installed. The installed Windows has msi.dll.
internal static partial class MsiNativeMethods
{
    // INSTALLSTATE_DEFAULT: installed for the machine, or for the user asking, which SYSTEM never installs for.
    public const int InstallStateDefault = 5;

    [LibraryImport("msi.dll", EntryPoint = "MsiQueryProductStateW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MsiQueryProductState(string product);
}
