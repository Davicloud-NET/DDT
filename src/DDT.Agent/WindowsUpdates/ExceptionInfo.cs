// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsUpdates;

// EXCEPINFO. Windows Update puts its own error into SCode when Invoke answers DISP_E_EXCEPTION.
[StructLayout(LayoutKind.Sequential)]
internal struct ExceptionInfo
{
    public ushort Code;
    public ushort Reserved;
    public nint Source;
    public nint Description;
    public nint HelpFile;
    public uint HelpContext;
    public nint ReservedPointer;
    public nint DeferredFillIn;
    public int SCode;
}
