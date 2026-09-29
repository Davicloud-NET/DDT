// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DDT.MachineConsole.Controls;

// The band of ink and yellow stripes. Only the screen that erases a disk shows it.
public sealed class HazardBand : Control
{
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<HazardBand, IBrush?>(nameof(Ink));

    public static readonly StyledProperty<IBrush?> AttentionProperty = AvaloniaProperty.Register<HazardBand, IBrush?>(nameof(Attention));

    static HazardBand()
    {
        AffectsRender<HazardBand>(InkProperty, AttentionProperty);
    }

    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public IBrush? Attention
    {
        get => GetValue(AttentionProperty);
        set => SetValue(AttentionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Color ink = Ink is ISolidColorBrush inkBrush ? inkBrush.Color : Colors.Black;
        Color attention = Attention is ISolidColorBrush attentionBrush ? attentionBrush.Color : Colors.Gold;

        context.DrawRectangle(Stripes.Brush(ink, 9, attention, 9), null, new Rect(Bounds.Size));
    }
}
