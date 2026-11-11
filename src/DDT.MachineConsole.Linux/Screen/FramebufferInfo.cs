// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Screen;

// What the kernel says about the framebuffer.
internal sealed record FramebufferInfo(string Id, int Width, int Height, int BitsPerPixel)
{
    // A DRM driver's emulated framebuffer, which reaches the screen through deferred I/O at about 20 frames a second.
    public bool IsEmulated => Id.EndsWith("drmfb", StringComparison.Ordinal);

    public override string ToString() => $"{Id} {Width}x{Height} {BitsPerPixel} bpp";
}
