// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Media;

namespace DDT.MachineConsole.Controls;

// Diagonal stripes as the web's repeating-linear-gradient(-45deg, ...) draws them. The widths are measured across the
// stripes, and offset moves them sideways.
public static class Stripes
{
    public static IBrush Brush(Color first, double firstWidth, Color second, double secondWidth, double offset = 0)
    {
        double period = firstWidth + secondWidth;
        double step = period / Math.Sqrt(2);
        double split = firstWidth / period;

        return new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new RelativePoint(offset, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(offset + step, step, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(first, 0),
                new GradientStop(first, split),
                new GradientStop(second, split),
                new GradientStop(second, 1),
            },
        };
    }

    // How far the stripes move sideways in one period, which is where they look as they started.
    public static double Repeat(double firstWidth, double secondWidth) => (firstWidth + secondWidth) * Math.Sqrt(2);
}
