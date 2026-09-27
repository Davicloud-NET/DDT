// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Controls;

// One module of the sequence rail: a trough one shade below the panel, filled as its step goes. Done is filled with
// the done tone, failed and skipped are hatched, and the running step fills with blue to its percent under stripes
// that move, the one thing on the screen that does. A running step that gives no percent fills a paler blue.
public sealed class RailModule : Control
{
    public static readonly StyledProperty<ConsoleStepState> StateProperty =
        AvaloniaProperty.Register<RailModule, ConsoleStepState>(nameof(State));

    public static readonly StyledProperty<int?> PercentProperty = AvaloniaProperty.Register<RailModule, int?>(nameof(Percent));

    public static readonly StyledProperty<IBrush?> TroughProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(Trough));

    public static readonly StyledProperty<IBrush?> EdgeProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(Edge));

    public static readonly StyledProperty<IBrush?> DoneProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(Done));

    public static readonly StyledProperty<IBrush?> RunProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(Run));

    public static readonly StyledProperty<IBrush?> RunSoftProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(RunSoft));

    public static readonly StyledProperty<IBrush?> StripeProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(Stripe));

    public static readonly StyledProperty<IBrush?> FailProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(Fail));

    public static readonly StyledProperty<IBrush?> FailTextProperty = AvaloniaProperty.Register<RailModule, IBrush?>(nameof(FailText));

    // How often the stripes of a running step move, in frames per second: enough to read as motion, little enough for
    // software rendering while an image is applied.
    private const double FramesPerSecond = 30;

    private const double StripeWidth = 6;
    private static readonly double s_repeat = Stripes.Repeat(StripeWidth, StripeWidth);
    private static readonly TimeSpan s_cycle = TimeSpan.FromSeconds(1.1);

    private TimeSpan _lastFrame;
    private double _offset;
    private bool _animating;

    static RailModule()
    {
        AffectsRender<RailModule>(
            StateProperty,
            PercentProperty,
            TroughProperty,
            EdgeProperty,
            DoneProperty,
            RunProperty,
            RunSoftProperty,
            StripeProperty,
            FailProperty,
            FailTextProperty);
    }

    public ConsoleStepState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public int? Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public IBrush? Trough
    {
        get => GetValue(TroughProperty);
        set => SetValue(TroughProperty, value);
    }

    public IBrush? Edge
    {
        get => GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    public IBrush? Done
    {
        get => GetValue(DoneProperty);
        set => SetValue(DoneProperty, value);
    }

    public IBrush? Run
    {
        get => GetValue(RunProperty);
        set => SetValue(RunProperty, value);
    }

    public IBrush? RunSoft
    {
        get => GetValue(RunSoftProperty);
        set => SetValue(RunSoftProperty, value);
    }

    public IBrush? Stripe
    {
        get => GetValue(StripeProperty);
        set => SetValue(StripeProperty, value);
    }

    public IBrush? Fail
    {
        get => GetValue(FailProperty);
        set => SetValue(FailProperty, value);
    }

    public IBrush? FailText
    {
        get => GetValue(FailTextProperty);
        set => SetValue(FailTextProperty, value);
    }

    // Tests draw the stripes where they start.
    public static bool Animates { get; set; } = true;

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Rect bounds = new(Bounds.Size);
        const double radius = 2;
        RoundedRect trough = new(bounds, radius);

        context.DrawRectangle(Trough, null, trough);

        using (context.PushClip(trough))
        {
            switch (State)
            {
                case ConsoleStepState.Done:
                    context.DrawRectangle(Done, null, bounds);
                    break;
                case ConsoleStepState.Failed:
                    context.DrawRectangle(Stripes.Brush(ColorOf(Fail), 4, ColorOf(FailText), 3), null, bounds);
                    break;
                case ConsoleStepState.Skipped:
                    context.DrawRectangle(Stripes.Brush(ColorOf(Edge), 2, Colors.Transparent, 4), null, bounds);
                    break;
                case ConsoleStepState.Running:
                    Rect fill = Percent is { } percent
                        ? new Rect(0, 0, bounds.Width * Math.Clamp(percent, 0, 100) / 100, bounds.Height)
                        : bounds;

                    context.DrawRectangle(Percent is null ? RunSoft : Run, null, fill);
                    context.DrawRectangle(Stripes.Brush(ColorOf(Stripe), StripeWidth, Colors.Transparent, StripeWidth, _offset), null, fill);
                    break;
            }
        }

        // The running module's edge is the run tone, every other one the rail's edge.
        IBrush? edge = State == ConsoleStepState.Running ? Run : Edge;
        context.DrawRectangle(null, new Pen(edge, 1), new RoundedRect(bounds.Deflate(0.5), radius));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StateProperty)
        {
            Animate();
        }
    }

    private static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    private void Animate()
    {
        if (_animating || !Animates || State != ConsoleStepState.Running || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _animating = true;
        top.RequestAnimationFrame(Frame);
    }

    private void Frame(TimeSpan time)
    {
        _animating = false;

        if (State != ConsoleStepState.Running || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        if (time - _lastFrame >= TimeSpan.FromSeconds(1 / FramesPerSecond))
        {
            _lastFrame = time;
            _offset = time.TotalSeconds % s_cycle.TotalSeconds / s_cycle.TotalSeconds * s_repeat;
            InvalidateVisual();
        }

        _animating = true;
        top.RequestAnimationFrame(Frame);
    }
}
