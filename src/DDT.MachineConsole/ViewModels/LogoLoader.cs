// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Media.Imaging;

namespace DDT.MachineConsole.ViewModels;

// Reads the organisation's logo for the right end of the header.
public static class LogoLoader
{
    // Taller than the header ever draws it. The smaller copy also limits what a large picture costs.
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
            // The file isn't a picture or is gone. The header shows no logo, and the run continues.
            return null;
        }
    }
}
