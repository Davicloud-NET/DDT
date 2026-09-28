// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Controls;

// A state tag, whose tone comes as a class that Surfaces.axaml styles.
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
