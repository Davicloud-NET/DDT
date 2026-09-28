// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace DDT.MachineConsole.Controls;

// The dimming under an overlay. It fades by its colour's alpha, not by Opacity: Opacity renders into a layer of its own
// first, which over the whole screen doubles the software renderer's work.
public sealed class Backdrop : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Backdrop, IBrush?>(nameof(Fill));

    // From 0, clear, to 1, the fill as it is.
    public static readonly StyledProperty<double> StrengthProperty = AvaloniaProperty.Register<Backdrop, double>(nameof(Strength), 1);

    static Backdrop()
    {
        AffectsRender<Backdrop>(FillProperty, StrengthProperty);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Fill is ISolidColorBrush fill && Strength > 0)
        {
            context.FillRectangle(new ImmutableSolidColorBrush(fill.Color, fill.Opacity * Math.Min(Strength, 1)), new Rect(Bounds.Size));
        }
    }
}
