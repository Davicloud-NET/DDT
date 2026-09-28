// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DDT.MachineConsole.Controls;

// The console's motion, timed by the motion tokens. Only opacity and a TranslateTransform change, which software
// rendering draws cheaply, and a settled element has no transform left, so it renders as without motion.
public static class Motion
{
    // Set on an element that should enter when it is shown and leave before it is hidden, in place of IsVisible.
    public static readonly AttachedProperty<bool> ShownProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Shown", typeof(Motion), defaultValue: true);

    // False on what only fades, such as the backdrop under an overlay.
    public static readonly AttachedProperty<bool> RisesProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Rises", typeof(Motion), defaultValue: true);

    private static readonly ConditionalWeakTable<Visual, CancellationTokenSource> s_running = [];

    static Motion()
    {
        ShownProperty.Changed.AddClassHandler<Visual>((visual, change) => Show(visual, change.GetNewValue<bool>()));
    }

    // Tests turn motion off, so that what they look at has settled; the rail's stripes have their own switch.
    public static bool IsEnabled { get; set; } = true;

    public static TimeSpan Press => Resource("SgMotionPress", TimeSpan.FromMilliseconds(70));

    public static TimeSpan Fast => Resource("SgMotionFast", TimeSpan.FromMilliseconds(120));

    public static TimeSpan Normal => Resource("SgMotionNormal", TimeSpan.FromMilliseconds(160));

    public static TimeSpan Slow => Resource("SgMotionSlow", TimeSpan.FromMilliseconds(240));

    public static TimeSpan Flash => Resource("SgMotionFlash", TimeSpan.FromMilliseconds(1400));

    public static double Distance => Resource("SgMotionDistance", 6d);

    public static Easing Standard => Resource<Easing>("SgMotionEasing", new SplineEasing(0.2, 0, 0.13, 1));

    public static Easing Entering => Resource<Easing>("SgMotionEnter", new SplineEasing(0, 0, 0.2, 1));

    public static Easing Leaving => Resource<Easing>("SgMotionExit", new SplineEasing(0.4, 0, 1, 1));

    public static bool GetShown(Visual visual) => visual.GetValue(ShownProperty);

    public static void SetShown(Visual visual, bool value) => visual.SetValue(ShownProperty, value);

    public static bool GetRises(Visual visual) => visual.GetValue(RisesProperty);

    public static void SetRises(Visual visual, bool value) => visual.SetValue(RisesProperty, value);

    // Fades the element in from that strength and lets it rise by what is left of the distance, over normal.
    public static Task EnterAsync(Visual visual, double from, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(visual);

        double rise = GetRises(visual) ? Distance * (1 - from) : 0;

        return RunAsync(visual, new Movement(Normal, Entering, from, 1, rise), cancellation);
    }

    // Fades the element out over fast. It stays clear, for whoever hides it next.
    public static Task LeaveAsync(Visual visual, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(visual);

        return RunAsync(visual, new Movement(Fast, Leaving, visual.GetValue(Fading(visual)), 0, 0), cancellation);
    }

    // A change the agent pushed to what is on screen, such as the next step: the element enters again from nothing.
    public static void Renew(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        if (IsEnabled && visual.IsAttachedToVisualTree())
        {
            _ = EnterAsync(visual, 0, Restart(visual));
        }
    }

    private static void Show(Visual visual, bool shown)
    {
        CancellationToken cancellation = Restart(visual);

        if (!IsEnabled || !visual.IsAttachedToVisualTree())
        {
            visual.IsVisible = shown;
            TakesInput(visual, true);
            visual.ClearValue(Fading(visual));
            visual.RenderTransform = null;

            return;
        }

        if (shown)
        {
            // Clear at once, and entering only once it is laid out: the first frame of what opens, such as the log,
            // can take longer than the others, and the entrance should not lose that time.
            double from = visual.IsVisible ? visual.GetValue(Fading(visual)) : 0;
            visual.SetValue(Fading(visual), from);

            if (GetRises(visual) && from < 1)
            {
                Shift(visual, Distance * (1 - from));
            }

            visual.IsVisible = true;
            TakesInput(visual, true);
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        _ = EnterAsync(visual, from, cancellation);
                    }
                },
                DispatcherPriority.Background);
        }
        else if (visual.IsVisible)
        {
            // Nothing that is leaving takes a click any more.
            TakesInput(visual, false);
            _ = HideAfterAsync(visual, cancellation);
        }
    }

    private static async Task HideAfterAsync(Visual visual, CancellationToken cancellation)
    {
        await LeaveAsync(visual, cancellation).ConfigureAwait(true);

        if (!cancellation.IsCancellationRequested)
        {
            visual.IsVisible = false;
            TakesInput(visual, true);
            visual.ClearValue(Fading(visual));
        }
    }

    // Stops what the element was doing, so that a new change starts from where it is.
    private static CancellationToken Restart(Visual visual)
    {
        if (s_running.TryGetValue(visual, out CancellationTokenSource? running))
        {
            running.Cancel();
            running.Dispose();
        }

        CancellationTokenSource next = new();
        s_running.AddOrUpdate(visual, next);

        return next.Token;
    }

    private static async Task RunAsync(Visual visual, Movement movement, CancellationToken cancellation)
    {
        StyledProperty<double> fading = Fading(visual);

        // Set at once, before the first frame of the animation, so the element never shows where it is going first.
        visual.SetValue(fading, movement.From);
        TranslateTransform? shift = movement.Rise != 0 ? Shift(visual, movement.Rise) : null;

        if (!IsEnabled || !visual.IsAttachedToVisualTree())
        {
            Settle(visual, fading, movement.To, shift);

            return;
        }

        // Forward keeps the last value when the animation ends or is stopped, so nothing jumps back for a frame.
        Animation animation = new()
        {
            Duration = movement.Duration,
            Easing = movement.Easing,
            FillMode = FillMode.Forward,
            Children =
            {
                Frame(0, fading, movement.From, shift is null ? null : movement.Rise),
                Frame(1, fading, movement.To, shift is null ? null : 0),
            },
        };

        await animation.RunAsync(visual, cancellation).ConfigureAwait(true);

        if (!cancellation.IsCancellationRequested)
        {
            Settle(visual, fading, movement.To, shift);
        }
    }

    private static void Settle(Visual visual, StyledProperty<double> fading, double to, TranslateTransform? shift)
    {
        visual.SetValue(fading, to);

        if (shift is not null && ReferenceEquals(visual.RenderTransform, shift))
        {
            visual.RenderTransform = null;
        }
    }

    // A backdrop fades by the strength of its colour, anything else by its opacity.
    private static StyledProperty<double> Fading(Visual visual) => visual is Backdrop ? Backdrop.StrengthProperty : Visual.OpacityProperty;

    private static KeyFrame Frame(double cue, StyledProperty<double> fading, double strength, double? rise)
    {
        KeyFrame frame = new() { Cue = new Cue(cue), Setters = { new Setter(fading, strength) } };

        if (rise is { } offset)
        {
            frame.Setters.Add(new Setter(TranslateTransform.YProperty, offset));
        }

        return frame;
    }

    private static void TakesInput(Visual visual, bool takes)
    {
        if (visual is InputElement element)
        {
            element.IsHitTestVisible = takes;
        }
    }

    private static TranslateTransform Shift(Visual visual, double y)
    {
        if (visual.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            visual.RenderTransform = shift;
        }

        shift.Y = y;

        return shift;
    }

    private static T Resource<T>(string key, T fallback) =>
        Application.Current is { } application && application.TryGetResource(key, null, out object? value) && value is T typed
            ? typed
            : fallback;

    // From and To are strengths of the fading, Rise how far below its place the element starts.
    private readonly record struct Movement(TimeSpan Duration, Easing Easing, double From, double To, double Rise);
}
