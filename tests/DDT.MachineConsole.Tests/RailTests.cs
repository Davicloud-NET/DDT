// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.ViewModels;
using DDT.MachineConsole.Views;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The step a run waits at for someone is current but not running: yellow says someone has to act, and only what runs
// moves, so its module is filled with the attention colour without stripes, and its number is not the run's blue.
public sealed class RailTests
{
    [Fact]
    public void MarksThePausedStepAsAwaitingSomeoneRatherThanRunning()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Paused);
        RailStep paused = Assert.IsType<RunViewModel>(console.Model.Screen).Steps[3];

        Assert.Equal(("Check the BIOS", ConsoleStepState.Running), (paused.Name, paused.State));
        Assert.True(paused.AwaitsSomeone);
        Assert.False(paused.IsRunning);
        Assert.Equal("Step 4, Check the BIOS: Paused", paused.Description);

        // Once the run goes on, the step runs again as any step does.
        console.Show(Scenarios.Paused with { Run = Scenarios.Paused.Run! with { Activity = ConsoleActivity.Step } });
        RailStep running = Assert.IsType<RunViewModel>(console.Model.Screen).Steps[3];

        Assert.False(running.AwaitsSomeone);
        Assert.True(running.IsRunning);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DrawsThePausedStepInTheAttentionColourWithoutStripes(bool dark) => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.Paused).Ask(11, Scenarios.Pause);
        console.Model.IsDark = dark;
        MainWindow window = new(console.Model, fullScreen: false) { Width = 1024, Height = 768 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            RailModule module = window.GetVisualDescendants().OfType<RailModule>().ElementAt(3);
            TextBlock number = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("numeral") && text.Text == "04");
            ThemeVariant theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Color attention = Resource(window, "SgAttentionColor", theme);

            Assert.True(module.AwaitsSomeone);
            Assert.False(module.IsMoving);
            Assert.Equal(Resource(window, "SgAttentionTextColor", theme), Assert.IsAssignableFrom<ISolidColorBrush>(number.Foreground).Color);
            Assert.DoesNotContain("running", number.Classes);

            // Across the whole module, away from its rounded corners: the one colour, no stripe anywhere.
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
            Point origin = module.TranslatePoint(default, window) ?? throw new InvalidOperationException("The module is not on the window.");
            int y = (int)(origin.Y + module.Bounds.Height / 2);

            for (int x = (int)origin.X + 4; x < origin.X + module.Bounds.Width - 4; x += 3)
            {
                Assert.Equal(attention, Pixel(frame, x, y));
            }
        }
        finally
        {
            window.Close();
        }
    });

    private static Color Resource(Window window, string key, ThemeVariant theme) =>
        window.TryFindResource(key, theme, out object? value) && value is Color color
            ? color
            : throw new InvalidOperationException($"No {key} in the {theme} theme.");

    private static Color Pixel(WriteableBitmap frame, int x, int y)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        int[] pixel = new int[1];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, pixel, 0, 1);
        uint value = unchecked((uint)pixel[0]);

        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromArgb((byte)(value >> 24), (byte)value, (byte)(value >> 8), (byte)(value >> 16))
            : Color.FromUInt32(value);
    }
}
