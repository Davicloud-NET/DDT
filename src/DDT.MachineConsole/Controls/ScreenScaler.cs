// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DDT.MachineConsole.Controls;

// Lays the console out on a canvas of at least 1024 x 768 and scales it to the screen by its size in pixels, not by
// its DPI: a technician reads the console from where they stand, so a larger screen shows the same screen larger. The
// canvas grows in width where the screen is wider than 4:3.
public sealed class ScreenScaler : Decorator
{
    public const double BaseWidth = 1024;
    public const double BaseHeight = 768;

    private double _scale = 1;

    // The scale for a screen of that many pixels.
    public static double ScaleFor(Size pixels) => Math.Max(0.25, Math.Min(pixels.Width / BaseWidth, pixels.Height / BaseHeight));

    public double Scale => _scale;

    protected override Size MeasureOverride(Size availableSize)
    {
        _scale = ScaleFrom(availableSize);
        Child?.Measure(new Size(availableSize.Width / _scale, availableSize.Height / _scale));

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _scale = ScaleFrom(finalSize);

        if (Child is { } child)
        {
            child.RenderTransformOrigin = RelativePoint.TopLeft;
            child.RenderTransform = new ScaleTransform(_scale, _scale);
            child.Arrange(new Rect(0, 0, finalSize.Width / _scale, finalSize.Height / _scale));
        }

        return finalSize;
    }

    // The layout is in device-independent units; the screen's pixels are those times the render scaling.
    private double ScaleFrom(Size size)
    {
        if (double.IsInfinity(size.Width) || double.IsInfinity(size.Height) || size.Width <= 0 || size.Height <= 0)
        {
            return 1;
        }

        double rendering = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        return ScaleFor(new Size(size.Width * rendering, size.Height * rendering)) / rendering;
    }
}
