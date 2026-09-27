// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Controls;

// One module of the sequence rail: a trough one shade below the panel, filled as its step goes. Done is filled with
// the done tone, failed and skipped are hatched, and the running step fills with blue to its percent under stripes
// that move, the one thing on the screen that does. A running step that gives no percent fills a paler blue. The fill
// grows to a new percent over slow, and a new state fades in over the one before, over slow too.
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

    // The percent the running fill shows, which follows Percent over slow.
    public static readonly StyledProperty<double> FillProperty = AvaloniaProperty.Register<RailModule, double>(nameof(Fill));

    // How far the change from the state before has come, from 0 to 1.
    public static readonly StyledProperty<double> ChangeProperty = AvaloniaProperty.Register<RailModule, double>(nameof(Change), 1);

    // How often the stripes of a running step move, in frames per second: enough to read as motion, little enough for
    // software rendering while an image is applied.
    private const double FramesPerSecond = 30;

    private const double StripeWidth = 6;
    private static readonly double s_repeat = Stripes.Repeat(StripeWidth, StripeWidth);
    private static readonly TimeSpan s_cycle = TimeSpan.FromSeconds(1.1);

    private TimeSpan _lastFrame;
    private double _offset;
    private bool _animating;
    private ConsoleStepState _before;
    private double _fillBefore;
    private bool _softBefore;
    private CancellationTokenSource? _changing;

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
            FailTextProperty,
            FillProperty,
            ChangeProperty);
    }

    public RailModule()
    {
        if (Motion.IsEnabled)
        {
            Transitions = [new DoubleTransition { Property = FillProperty, Duration = Motion.Slow, Easing = Motion.Standard }];
        }
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

    public double Fill
    {
        get => GetValue(FillProperty);
        private set => SetValue(FillProperty, value);
    }

    public double Change
    {
        get => GetValue(ChangeProperty);
        private set => SetValue(ChangeProperty, value);
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

        if (Change < 1)
        {
            DrawState(context, bounds, trough, _before, _fillBefore, _softBefore);

            using (context.PushOpacity(Change))
            {
                DrawState(context, bounds, trough, State, Fill, Percent is null);
            }
        }
        else
        {
            DrawState(context, bounds, trough, State, Fill, Percent is null);
        }
    }

    // soft is a running step that gives no percent, which fills a paler blue.
    private void DrawState(DrawingContext context, Rect bounds, RoundedRect trough, ConsoleStepState state, double fill, bool soft)
    {
        const double radius = 2;

        using (context.PushClip(trough))
        {
            switch (state)
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
                    Rect filled = soft ? bounds : new Rect(0, 0, bounds.Width * Math.Clamp(fill, 0, 100) / 100, bounds.Height);

                    context.DrawRectangle(soft ? RunSoft : Run, null, filled);
                    context.DrawRectangle(Stripes.Brush(ColorOf(Stripe), StripeWidth, Colors.Transparent, StripeWidth, _offset), null, filled);
                    break;
            }
        }

        // The running module's edge is the run tone, every other one the rail's edge.
        IBrush? edge = state == ConsoleStepState.Running ? Run : Edge;
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
            ChangeFrom(change.GetOldValue<ConsoleStepState>());
            Animate();
        }
        else if (change.Property == PercentProperty)
        {
            Fill = Percent ?? 0;
        }
    }

    // The new state fades in over the one before, where the module is on the screen already.
    private void ChangeFrom(ConsoleStepState before)
    {
        _changing?.Cancel();
        _changing?.Dispose();
        _changing = null;

        if (!Motion.IsEnabled || !this.IsAttachedToVisualTree())
        {
            Change = 1;

            return;
        }

        _before = before;
        _fillBefore = Fill;
        _softBefore = Percent is null;
        _changing = new CancellationTokenSource();
        Change = 0;

        Animation fade = new()
        {
            Duration = Motion.Slow,
            Easing = Motion.Standard,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ChangeProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ChangeProperty, 1d) } },
            },
        };

        _ = fade.RunAsync(this, _changing.Token);
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
