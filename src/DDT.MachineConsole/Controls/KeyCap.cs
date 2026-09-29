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
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace DDT.MachineConsole.Controls;

// A key cap, such as F1, that goes down while its key is held. It comes up only after reaching the bottom, so a quick
// tap shows too.
public sealed class KeyCap : Border
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<KeyCap, string?>(nameof(Key));

    public static readonly StyledProperty<IBrush?> ShadeProperty = AvaloniaProperty.Register<KeyCap, IBrush?>(nameof(Shade));

    // How far the cap is pressed. 0 is up, 1 is a pixel down and darkened.
    public static readonly StyledProperty<double> DepthProperty = AvaloniaProperty.Register<KeyCap, double>(nameof(Depth));

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _shade = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly TranslateTransform _sink = new();
    private TopLevel? _top;
    private Task _goingDown = Task.CompletedTask;
    private CancellationTokenSource? _moving;
    private int _presses;

    public KeyCap()
    {
        _text.Classes.Add("keyCap");
        Child = new Panel { Children = { _shade, _text } };
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

    // True from the key press until the cap starts moving up again.
    public bool IsDown { get; private set; }

    // Whether this cap stands for that key. F1 to F12 match by name, the other keys by the mark the screens show.
    public static bool Names(string? cap, Key key) => cap switch
    {
        "Esc" => key == Avalonia.Input.Key.Escape,
        "Enter" => key == Avalonia.Input.Key.Enter,
        "Tab" => key == Avalonia.Input.Key.Tab,
        "Shift" => key is Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift,
        "End" => key == Avalonia.Input.Key.End,
        "↑" => key == Avalonia.Input.Key.Up,
        "↓" => key == Avalonia.Input.Key.Down,
        "←" => key == Avalonia.Input.Key.Left,
        "→" => key == Avalonia.Input.Key.Right,
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

        // A key pressed here and released in another window, like the command prompt Shift+F10 opens, never sends its
        // key up here.
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

        _presses++;
        IsDown = false;
        _moving?.Cancel();
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
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsDown || !Names(Key, e.Key) || !IsEffectivelyVisible || !IsEffectivelyEnabled)
        {
            return;
        }

        IsDown = true;
        _presses++;
        _goingDown = MoveAsync(1);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (IsDown && Names(Key, e.Key))
        {
            _ = UpAsync();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (IsDown)
        {
            _presses++;
            IsDown = false;
            _ = MoveAsync(0);
        }
    }

    // The cap comes up only after it reached the bottom, which a quick tap hasn't yet. It stays down if the key was
    // pressed again in the meantime.
    private async Task UpAsync()
    {
        int press = _presses;
        await _goingDown.ConfigureAwait(true);

        if (press == _presses && IsDown)
        {
            IsDown = false;
            await MoveAsync(0).ConfigureAwait(true);
        }
    }

    private async Task MoveAsync(double to)
    {
        _moving?.Cancel();
        _moving?.Dispose();
        _moving = null;

        if (!Motion.IsEnabled || !this.IsAttachedToVisualTree())
        {
            Depth = to;

            return;
        }

        CancellationTokenSource moving = new();
        _moving = moving;

        // FillMode.Forward keeps the depth it reached, so the next move starts from there.
        Animation move = new()
        {
            Duration = Motion.Press,
            Easing = Motion.Standard,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(DepthProperty, Depth) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(DepthProperty, to) } },
            },
        };

        await move.RunAsync(this, moving.Token).ConfigureAwait(true);

        if (!moving.IsCancellationRequested)
        {
            Depth = to;
        }
    }
}
