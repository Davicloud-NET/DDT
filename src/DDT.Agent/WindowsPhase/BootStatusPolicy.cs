// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// bootstatuspolicy of the boot entry Windows started from, read from the system store Windows keeps loaded under
// HKLM\BCD00000000. bcdedit's listing would do, but its words follow the display language.
public static class BootStatusPolicy
{
    // BcdOSLoaderInteger_BootStatusPolicy.
    public const string Element = "250000E0";

    // Null when the entry has no value. Throws when the entry or its value cannot be read.
    public static int? Current()
    {
        string entry = $@"BCD00000000\Objects\{BootNativeMethods.BootEntry():B}";
        using RegistryKey objectKey = Registry.LocalMachine.OpenSubKey(entry) ?? throw new InvalidDataException($"The system store has no boot entry {entry}.");
        using RegistryKey? element = objectKey.OpenSubKey($@"Elements\{Element}");

        return element is null ? null : ValueOf(element.GetValue("Element"));
    }

    // An integer element is eight bytes, least significant first.
    public static int ValueOf(object? value)
    {
        if (value is not byte[] { Length: > 0 and <= 8 } bytes)
        {
            throw new InvalidDataException("The boot entry's bootstatuspolicy is no integer.");
        }

        byte[] padded = new byte[8];
        bytes.CopyTo(padded, 0);
        ulong number = BinaryPrimitives.ReadUInt64LittleEndian(padded);

        return number <= int.MaxValue ? (int)number : throw new InvalidDataException($"The boot entry's bootstatuspolicy is {number}.");
    }
}
