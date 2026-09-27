// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DDT.MachineConsole.Controls;

// A key cap, such as F1 or Enter, as the footer and the keys on the screen show it. While its key is down on the
// keyboard the cap goes down with it, as a key does: it sinks by a pixel and darkens over press, and it comes up only
// once it has been all the way down, so a quick tap shows too. A cap on a key that cannot be pressed stays up.
public sealed class KeyCap : Border
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<KeyCap, string?>(nameof(Key));

    public static readonly StyledProperty<IBrush?> ShadeProperty = AvaloniaProperty.Register<KeyCap, IBrush?>(nameof(Shade));

    // How far the cap is down, from 0, up, to 1, a pixel down and darkened.
    public static readonly StyledProperty<double> DepthProperty = AvaloniaProperty.Register<KeyCap, double>(nameof(Depth));

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _shade = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly TranslateTransform _sink = new();
    private TopLevel? _top;
    private bool _releasing;
    private int _presses;

    public KeyCap()
    {
        _text.Classes.Add("keyCap");
        Child = new Panel { Children = { _shade, _text } };

        if (Motion.IsEnabled)
        {
            Transitions = [new DoubleTransition { Property = DepthProperty, Duration = Motion.Press, Easing = Motion.Standard }];
        }
    }

    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public IBrush? Shade
    {
        get => GetValue(ShadeProperty);
        set => SetValue(ShadeProperty, value);
    }

    public double Depth
    {
        get => GetValue(DepthProperty);
        private set => SetValue(DepthProperty, value);
    }

    // True from the key going down until the cap is on its way up again.
    public bool IsDown { get; private set; }

    // Whether this cap stands for that key: F1 to F12 by name, and the keys the screens show by their marks.
    public static bool Names(string? cap, Key key) => cap switch
    {
        "Esc" => key == Avalonia.Input.Key.Escape,
        "Enter" => key == Avalonia.Input.Key.Enter,
        "Shift" => key is Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift,
        "End" => key == Avalonia.Input.Key.End,
        "↑" => key == Avalonia.Input.Key.Up,
        "↓" => key == Avalonia.Input.Key.Down,
        ['F', _, ..] => Enum.TryParse(cap, out Key named) && named is >= Avalonia.Input.Key.F1 and <= Avalonia.Input.Key.F24 && named == key,
        _ => false,
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _top = TopLevel.GetTopLevel(this);

        if (_top is null)
        {
            return;
        }

        _top.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);

        // A key that goes down here and up in another window, such as the command prompt Shift+F10 opens, never
        // comes up here.
        if (_top is WindowBase window)
        {
            window.Deactivated += OnDeactivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is not null)
        {
            _top.RemoveHandler(KeyDownEvent, OnKeyDown);
            _top.RemoveHandler(KeyUpEvent, OnKeyUp);

            if (_top is WindowBase window)
            {
                window.Deactivated -= OnDeactivated;
            }

            _top = null;
        }

        IsDown = false;
        _releasing = false;
        Depth = 0;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KeyProperty)
        {
            _text.Text = Key;
        }
        else if (change.Property == ShadeProperty)
        {
            _shade.Background = Shade;
        }
        else if (change.Property == CornerRadiusProperty)
        {
            _shade.CornerRadius = CornerRadius;
        }
        else if (change.Property == PaddingProperty)
        {
            // The shade covers the whole face inside the edge, padding and all.
            Thickness padding = Padding;
            _shade.Margin = new Thickness(-padding.Left, -padding.Top, -padding.Right, -padding.Bottom);
        }
        else if (change.Property == DepthProperty)
        {
            // A cap at rest has no transform, so its text is drawn exactly as the text around it.
            double depth = change.GetNewValue<double>();
            _sink.Y = depth;
            RenderTransform = depth > 0 ? _sink : null;
            _shade.Opacity = depth;

            // Up again once the way down is over, after this change: going up from inside it would start the next
            // transition while the one down still ends.
            if (_releasing && depth >= 1)
            {
                int press = _presses;
                _releasing = false;
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (press == _presses && IsDown)
                        {
                            Release();
                        }
                    },
                    DispatcherPriority.Background);
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsDown || !Names(Key, e.Key) || !IsEffectivelyVisible || !IsEffectivelyEnabled)
        {
            return;
        }

        IsDown = true;
        _releasing = false;
        _presses++;
        Depth = 1;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!IsDown || !Names(Key, e.Key))
        {
            return;
        }

        // Up only from the bottom, which a quick tap has not reached yet.
        if (Depth >= 1)
        {
            Release();
        }
        else
        {
            _releasing = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (IsDown)
        {
            Release();
        }
    }

    private void Release()
    {
        IsDown = false;
        _releasing = false;
        Depth = 0;
    }
}
