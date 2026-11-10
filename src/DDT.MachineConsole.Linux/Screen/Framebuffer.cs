// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.MachineConsole.Native;

namespace DDT.MachineConsole.Screen;

// Asks the framebuffer what it is before Avalonia opens it: Avalonia throws a bare Exception, and takes any depth but
// 16 bits for 32-bit BGRA.
internal static class Framebuffer
{
    public const string Device = "/dev/fb0";

    // FBIOGET_VSCREENINFO and FBIOGET_FSCREENINFO of linux/fb.h.
    private const nuint GetVariable = 0x4600;
    private const nuint GetFixed = 0x4602;

    // fb_var_screeninfo is 160 bytes and fb_fix_screeninfo 80. The buffers leave room.
    private const int BufferSize = 256;

    // Null with the reason when there's no screen the console can draw on.
    public static unsafe FramebufferInfo? Probe(out string problem)
    {
        int descriptor = Libc.Open(Device, Libc.ReadWrite | Libc.CloseOnExec);

        if (descriptor < 0)
        {
            problem = $"{Device}: {Libc.Describe("open")}";

            return null;
        }

        try
        {
            byte* variable = stackalloc byte[BufferSize];
            byte* fixedPart = stackalloc byte[BufferSize];

            if (Libc.Ioctl(descriptor, GetVariable, variable) < 0 || Libc.Ioctl(descriptor, GetFixed, fixedPart) < 0)
            {
                problem = $"{Device}: {Libc.Describe("ioctl")}";

                return null;
            }

            FramebufferInfo info = Parse(new ReadOnlySpan<byte>(variable, BufferSize), new ReadOnlySpan<byte>(fixedPart, BufferSize));
            problem = info.BitsPerPixel is 16 or 32 ? "" : $"{Device} has {info.BitsPerPixel} bits per pixel; the console draws 16 or 32.";

            return problem.Length == 0 ? info : null;
        }
        finally
        {
            _ = Libc.Close(descriptor);
        }
    }

    // Offsets as in linux/fb.h.
    private static FramebufferInfo Parse(ReadOnlySpan<byte> variable, ReadOnlySpan<byte> fixedPart)
    {
        ReadOnlySpan<byte> id = fixedPart[..16];
        int end = id.IndexOf((byte)0);

        return new FramebufferInfo(
            Encoding.ASCII.GetString(end < 0 ? id : id[..end]),
            BinaryPrimitives.ReadInt32LittleEndian(variable),
            BinaryPrimitives.ReadInt32LittleEndian(variable[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(variable[24..]));
    }
}
