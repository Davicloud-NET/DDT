// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Keyboard;
using DDT.MachineConsole.Native;

namespace DDT.MachineConsole.Input;

// One keyboard under /dev/input. Grabbed, the kernel's terminal no longer sees its keys, so Ctrl+Alt+Del and the
// switch to another terminal do nothing. A grab ends with the process, so a killed console leaves the keyboard working.
internal sealed class EvdevDevice : IDisposable
{
    // EVIOCGBIT, EVIOCGKEY, EVIOCGLED and EVIOCGRAB of linux/input.h, for the sizes below.
    private const uint ReadDirection = 0x80000000;
    private const int KeyBytes = 96;
    private const int TypeBytes = 4;
    private const int LedBytes = 2;
    private const nuint GrabRequest = 0x40044590;

    private EvdevDevice(string path, int descriptor, bool repeats)
    {
        Path = path;
        Descriptor = descriptor;
        Repeats = repeats;
    }

    public string Path { get; }

    public int Descriptor { get; private set; }

    // The kernel repeats a held key itself.
    public bool Repeats { get; }

    public bool Grabbed { get; private set; }

    // The device, if it is a keyboard the console can read.
    public static unsafe EvdevDevice? Open(string path)
    {
        int descriptor = Libc.Open(path, Libc.ReadWrite | Libc.NonBlocking | Libc.CloseOnExec);

        if (descriptor < 0)
        {
            descriptor = Libc.Open(path, Libc.ReadOnly | Libc.NonBlocking | Libc.CloseOnExec);
        }

        if (descriptor < 0)
        {
            return null;
        }

        byte* types = stackalloc byte[TypeBytes];
        byte* keys = stackalloc byte[KeyBytes];
        Span<byte> typeBits = new(types, TypeBytes);
        Span<byte> keyBits = new(keys, KeyBytes);
        typeBits.Clear();
        keyBits.Clear();

        if (Libc.Ioctl(descriptor, Bits(0, TypeBytes), types) < 0
            || Libc.Ioctl(descriptor, Bits(EvdevEvents.Key, KeyBytes), keys) < 0
            || !EvdevBits.IsKeyboard(typeBits, keyBits))
        {
            _ = Libc.Close(descriptor);

            return null;
        }

        return new EvdevDevice(path, descriptor, EvdevBits.IsSet(typeBits, EvdevBits.Repeat));
    }

    // Grabs only while no key is down, so the terminal never sees half a key press. False to try again later.
    public bool Grab()
    {
        if (!Grabbed && Held().Count == 0 && Libc.Ioctl(Descriptor, GrabRequest, 1) == 0)
        {
            Grabbed = true;
        }

        return Grabbed;
    }

    public void Release()
    {
        if (Grabbed)
        {
            _ = Libc.Ioctl(Descriptor, GrabRequest, 0);
            Grabbed = false;
        }
    }

    // What one read gives; empty when nothing waits, null when the device is gone.
    public unsafe IReadOnlyList<EvdevEvent>? Read()
    {
        byte* buffer = stackalloc byte[EvdevEvents.Size * 64];
        nint read = Libc.Read(Descriptor, buffer, EvdevEvents.Size * 64);

        if (read >= 0)
        {
            return EvdevEvents.Parse(new ReadOnlySpan<byte>(buffer, (int)read));
        }

        return Libc.Error is Libc.TryAgain or Libc.Interrupted ? [] : null;
    }

    public unsafe IReadOnlyCollection<ushort> Held()
    {
        byte* keys = stackalloc byte[KeyBytes];

        return Libc.Ioctl(Descriptor, State(0x18, KeyBytes), keys) < 0 ? [] : EvdevBits.Codes(new ReadOnlySpan<byte>(keys, KeyBytes));
    }

    // The keyboard's Num Lock light, which the firmware may have turned on.
    public unsafe bool NumLockLit()
    {
        byte* leds = stackalloc byte[LedBytes];

        return Libc.Ioctl(Descriptor, State(0x19, LedBytes), leds) == 0
            && EvdevBits.IsSet(new ReadOnlySpan<byte>(leds, LedBytes), EvdevEvents.LedNumLock);
    }

    public void Light(bool capsLock, bool numLock)
    {
        Write(new EvdevEvent(EvdevEvents.Led, EvdevEvents.LedCapsLock, capsLock ? 1 : 0));
        Write(new EvdevEvent(EvdevEvents.Led, EvdevEvents.LedNumLock, numLock ? 1 : 0));
        Write(new EvdevEvent(EvdevEvents.Sync, EvdevEvents.SyncReport, 0));
    }

    public void Dispose()
    {
        if (Descriptor >= 0)
        {
            Release();
            _ = Libc.Close(Descriptor);
            Descriptor = -1;
        }
    }

    private unsafe void Write(EvdevEvent input)
    {
        fixed (byte* bytes = EvdevEvents.Write(input))
        {
            _ = Libc.Write(Descriptor, bytes, EvdevEvents.Size);
        }
    }

    private static nuint Bits(int type, int size) => ReadDirection | ((uint)size << 16) | 0x4500u | (uint)(0x20 + type);

    private static nuint State(int number, int size) => ReadDirection | ((uint)size << 16) | 0x4500u | (uint)number;
}
