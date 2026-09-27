// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Controls;

// The band of ink and yellow stripes over what erases a disk, and nowhere else.
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

// A square, stamped state tag in capitals: filled where something is under way or someone has to act, outlined where
// it rests. The tone comes as a class, styled in Surfaces.axaml.
public sealed class StateTag : Border
{
    public static readonly StyledProperty<Tag?> ValueProperty = AvaloniaProperty.Register<StateTag, Tag?>(nameof(Value));

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    public StateTag()
    {
        _text.Classes.Add("tag");
        Child = _text;
    }

    public Tag? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            Tag? tag = Value;
            _text.Text = tag?.Text;
            IsVisible = tag is not null;
            Classes.Set("run", tag?.Tone == TagTone.Run);
            Classes.Set("attention", tag?.Tone == TagTone.Attention);
            Classes.Set("fail", tag?.Tone == TagTone.Fail);
            Classes.Set("ok", tag?.Tone == TagTone.Ok);
            Classes.Set("idle", tag?.Tone == TagTone.Idle);
        }
    }
}

// A key cap, such as F1 or Enter, as the footer and the keys on the screen show it.
public sealed class KeyCap : Border
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<KeyCap, string?>(nameof(Key));

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    public KeyCap()
    {
        _text.Classes.Add("keyCap");
        Child = _text;
    }

    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KeyProperty)
        {
            _text.Text = Key;
        }
    }
}
