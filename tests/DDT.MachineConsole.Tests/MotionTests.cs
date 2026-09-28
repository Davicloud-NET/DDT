// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.Views;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console's motion on the real views, on a test clock with a frame every 16 ms. With DDT_CONSOLE_FILMSTRIPS naming
// a folder, the frames of each transition are saved there for a person to look at.
public sealed class MotionTests
{
    private static readonly TimeSpan s_frame = TimeSpan.FromMilliseconds(16);

    // A few frames more than a transition takes: one that follows another starts on the frame after.
    private static readonly TimeSpan s_settle = TimeSpan.FromMilliseconds(64);

    private static string? FilmFolder => Environment.GetEnvironmentVariable("DDT_CONSOLE_FILMSTRIPS");

    [Fact]
    public Task LetsTheScreenOfANewStageEnterAsTheOneBeforeLeaves() => Moving(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.WaitingForSequence));
        MainWindow window = Open(console);
        ManualClock clock = new(window);
        ContentPresenter before = Page(window);

        console.Show(Scenarios.Running);
        Frame();
        ContentPresenter after = Page(window);

        // The new screen waits, clear, while the old one leaves, and that takes no click meanwhile.
        Assert.NotSame(before, after);
        Assert.Equal(0, after.Opacity);
        Assert.True(before.IsVisible);
        Assert.False(before.IsHitTestVisible);

        clock.Play(Motion.Fast + Motion.Normal + s_settle);

        Assert.Equal(1, after.Opacity);
        Assert.Null(after.RenderTransform);
        Assert.False(before.IsVisible);
        window.Close();
    });

    [Fact]
    public Task LetsAnOverlayEnterAndLeaveBeforeItIsHidden() => Moving(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);
        MainWindow window = Open(console);
        ManualClock clock = new(window);
        Panel overlay = window.Find<Panel>("Overlay");
        Backdrop backdrop = window.Find<Backdrop>("OverlayBackdrop");

        Assert.False(overlay.IsVisible);

        console.Model.Press(Key.F1);
        Frame();
        Assert.True(overlay.IsVisible);
        Assert.True(overlay.Opacity < 1, $"The overlay starts at {overlay.Opacity}.");
        Assert.True(backdrop.Strength < 1, $"The backdrop starts at {backdrop.Strength}.");

        clock.Play(Motion.Normal + s_settle);
        Assert.Equal(1, overlay.Opacity);
        Assert.Null(overlay.RenderTransform);
        Assert.Equal(1, backdrop.Strength);

        console.Model.Press(Key.Escape);
        Frame();
        Assert.True(overlay.IsVisible);
        Assert.False(overlay.IsHitTestVisible);

        clock.Play(Motion.Fast + s_settle);
        Assert.False(overlay.IsVisible);
        Assert.False(backdrop.IsVisible);
        Assert.True(overlay.IsHitTestVisible);
        window.Close();
    });

    [Fact]
    public Task PutsAKeyCapDownWithItsKeyAndLetsItUpOnlyFromTheBottom() => Moving(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(4, Scenarios.Sequences);
        MainWindow window = Open(console);
        ManualClock clock = new(window);
        KeyCap down = Caps(window, "↓").Single();
        double deepest = 0;
        down.PropertyChanged += (_, change) => deepest = change.Property == KeyCap.DepthProperty ? Math.Max(deepest, down.Depth) : deepest;

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.True(down.IsDown);

        // A quick tap: however soon the key is up again, the cap goes all the way down first.
        window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.True(down.IsDown);

        clock.Play(Motion.Press + Motion.Press + s_settle);

        Assert.Equal(1, deepest);
        Assert.False(down.IsDown);
        Assert.Equal(0, down.Depth);
        Assert.Null(down.RenderTransform);
        window.Close();
    });

    [Fact]
    public Task LeavesTheCapOfAKeyThatCannotBePressedUp() => Moving(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(7, Scenarios.Erase);
        MainWindow window = Open(console);
        KeyCap enter = Caps(window, "Enter").Single();
        KeyCap escape = Caps(window, "Esc").Single();

        // Erase works only once the word is typed, so its Enter stays up; Esc goes back and goes down.
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(enter.IsDown);
        Assert.True(escape.IsDown);
        window.Close();
    });

    [Fact]
    public Task FlashesAStepThatFinishedButNoStepOfARunShownFirst() => Moving(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);
        MainWindow window = Open(console);
        ManualClock clock = new(window);
        Border[] flashes = Flashes(window);

        Assert.All(flashes, flash => Assert.Equal(0, flash.Opacity));

        console.Show(NextStep);
        Frame();
        clock.Play(TimeSpan.FromMilliseconds(60));

        Assert.True(flashes[1].Opacity > 0, "Step 2 finished and flashes.");
        Assert.Contains("done", flashes[1].Classes);
        Assert.Equal(0, flashes[0].Opacity);
        Assert.Equal(0, flashes[2].Opacity);
        window.Close();
    });

    [Fact]
    public Task ChangesEveryColourAtOnceForANewTheme() => Moving(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(6, Scenarios.ComputerName("No."));
        MainWindow window = Open(console);
        TextBox name = window.Find<TextBox>("NameBox");
        Border edge = name.GetVisualDescendants().OfType<Border>().First(border => border.Name == "border");

        Tap(window, Key.F4);

        Assert.Equal(Color.Parse("#B3262B"), Assert.IsAssignableFrom<ISolidColorBrush>(edge.BorderBrush).Color);
        window.Close();
    });

    [Fact]
    public Task DrawsTheFilmstrips() => Moving(() =>
    {
        Assert.SkipWhen(FilmFolder is null, "DDT_CONSOLE_FILMSTRIPS names no folder to draw the filmstrips into.");

        int[] entering = [0, 40, 80, 120, 160, 240];
        int[] changing = [0, 40, 80, 120, 160, 200, 240, 280];

        Film("stage-change", changing, () => new TestConsole().Show(Scenarios.State(ConsoleStage.WaitingForSequence)), (console, _) => console.Show(Scenarios.Running));
        Film("question", changing, () => new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(4, Scenarios.Sequences), (console, _) => console.Ask(5, Scenarios.Disks));
        Film("overlay-open", entering, () => new TestConsole().Show(Scenarios.Running).Receive(new LogMessage(Scenarios.Lines)), (_, window) => Tap(window, Key.F1));
        Film("overlay-close", [0, 30, 60, 90, 120, 160], () => Opened(new TestConsole().Show(Scenarios.Running).Receive(new LogMessage(Scenarios.Lines))), (_, window) => Tap(window, Key.Escape));
        Film("key-press", [0, 20, 40, 70, 110, 140], () => new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(4, Scenarios.Sequences), (_, window) => Tap(window, Key.Down));
        Film("step-done", [0, 40, 80, 160, 240, 700, 1400], () => new TestConsole().Show(Scenarios.Running), (console, _) => console.Show(NextStep));
        Film("wrong-password", entering, SignInAsked, (console, _) => console.Ask(3, new SignInQuestion(SignInField.Password, "anna", "Wrong user name or password.")));
        Film("agent-ended", entering, () => new TestConsole().Show(Scenarios.Running), (console, _) => console.Model.Ended(LinkEnd.Closed));
        Film("restart-asked", entering, Ended, (_, window) => Tap(window, Key.F8));
        Film("button-hover", [0, 40, 80, 120, 160], () => new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(7, Scenarios.Erase), (_, window) => PointAt(window, "Back", press: false));
        Film("button-press", [0, 20, 40, 70, 110], () => new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(5, Scenarios.Disks), (_, window) => PointAt(window, "Back", press: true));

        static TestConsole SignInAsked() => new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(2, new SignInQuestion(SignInField.Password, "anna", null));

        static TestConsole Ended()
        {
            TestConsole console = new(canRestart: true);
            console.Show(Scenarios.Finished);
            console.Model.Ended(LinkEnd.Closed);

            return console;
        }

        static TestConsole Opened(TestConsole console)
        {
            console.Model.Press(Key.F1);

            return console;
        }
    });

    // Step 2 of the running scenario done, step 3 running.
    private static ConsoleState NextStep => Scenarios.Running with
    {
        Run = Scenarios.Run([ConsoleStepState.Done, ConsoleStepState.Done, ConsoleStepState.Running, .. Enumerable.Repeat(ConsoleStepState.Pending, 5)], 2, 8),
    };

    // With motion on, as the console runs it, for this test only.
    private static Task Moving(Action test) => Headless.RunAsync(() =>
    {
        Motion.IsEnabled = true;

        try
        {
            test();
        }
        finally
        {
            Motion.IsEnabled = false;
        }
    });

    private static MainWindow Open(TestConsole console)
    {
        MainWindow window = new(console.Model, fullScreen: false) { Width = 1024, Height = 768 };
        window.Show();
        Frame();

        return window;
    }

    // One frame: what was posted runs, layout, and the animations take their next step.
    private static void Frame()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // Frames for that long, in real time, as the console would draw them.
    private static void Run(TimeSpan time)
    {
        Stopwatch clock = Stopwatch.StartNew();

        while (clock.Elapsed < time)
        {
            Thread.Sleep(8);
            Frame();
        }
    }

    private static void Tap(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    // Moves the pointer onto the key of that name, and presses it.
    private static void PointAt(Window window, string name, bool press)
    {
        Button button = window.Find<Button>(name);
        Point centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window) ?? default;
        window.MouseMove(centre, RawInputModifiers.None);

        if (press)
        {
            window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        }
    }

    // The presenter that shows the screen now, not the one that is leaving.
    private static ContentPresenter Page(MainWindow window) =>
        window.Find<TransitioningContentControl>("Screen")
            .GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(presenter => presenter.TemplatedParent is TransitioningContentControl { Name: "Screen" }
                && ReferenceEquals(presenter.Content, ((ViewModels.MainViewModel)window.DataContext!).Screen));

    private static KeyCap[] Caps(Window window, string key) =>
        [.. window.GetVisualDescendants().OfType<KeyCap>().Where(cap => cap.Key == key && cap.IsEffectivelyVisible)];

    private static Border[] Flashes(Window window) =>
        [.. window.Find<SequenceRail>("Rail").Children.OfType<Border>().Where(border => border.Classes.Contains("flash")).Reverse()];

    // The frames of a transition at those times after it starts, saved as <name>/<ms>.png. Its animations run on a
    // clock of the test's own, so each frame is exactly where the time says.
    private static void Film(string name, int[] times, Func<TestConsole> before, Action<TestConsole, MainWindow> change)
    {
        string folder = Path.Combine(FilmFolder!, name);
        Directory.CreateDirectory(folder);

        TestConsole console = before();
        MainWindow window = Open(console);
        Run(TimeSpan.FromMilliseconds(300));

        ManualClock clock = new(window);
        change(console, window);
        Frame();

        // A picture at each of the times.
        foreach (int time in times)
        {
            clock.Play(TimeSpan.FromMilliseconds(time) - clock.Now);

            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
            frame.Save(Path.Combine(folder, $"{time:0000}.png"), PngBitmapEncoderOptions.Default);
        }

        window.Close();
    }

    // A clock that moves only when told, for everything that animates in the window. Avalonia keeps its clocks to
    // itself, so the test reaches them by reflection.
    private sealed class ManualClock
    {
        private static readonly Type s_type = typeof(Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase")!;
        private static readonly MethodInfo s_pulse = s_type.GetMethod("Pulse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly AvaloniaProperty s_property =
            (AvaloniaProperty)typeof(Avalonia.Animation.Animatable).GetField("ClockProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

        private readonly object _clock = Activator.CreateInstance(s_type, nonPublic: true)!;

        public ManualClock(Window window)
        {
            window.SetValue(s_property, _clock);
            s_pulse.Invoke(_clock, [TimeSpan.Zero]);
        }

        public TimeSpan Now { get; private set; }

        // A frame every 16 ms for that long, as at 60 frames a second, with what each frame posts run after it.
        public void Play(TimeSpan time)
        {
            TimeSpan until = Now + time;

            while (Now < until)
            {
                Now = Now + s_frame < until ? Now + s_frame : until;
                s_pulse.Invoke(_clock, [Now]);
                Frame();
            }
        }
    }
}
