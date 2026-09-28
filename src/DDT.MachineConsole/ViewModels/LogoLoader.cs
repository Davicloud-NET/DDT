// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Media.Imaging;

namespace DDT.MachineConsole.ViewModels;

// Reads the organisation's logo for the right end of the header.
public static class LogoLoader
{
    // Taller than the header ever draws it; the smaller copy also bounds what a large picture costs.
    private const int MaxHeight = 128;

    public static Bitmap? Read(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            Bitmap full = new(file);

            if (full.PixelSize.Height <= MaxHeight)
            {
                return full;
            }

            using (full)
            {
                int width = Math.Max(1, (int)((long)full.PixelSize.Width * MaxHeight / full.PixelSize.Height));

                return full.CreateScaledBitmap(new PixelSize(width, MaxHeight), BitmapInterpolationMode.HighQuality);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Not a picture, or gone: the header shows none, and the run goes on.
            return null;
        }
    }
}
