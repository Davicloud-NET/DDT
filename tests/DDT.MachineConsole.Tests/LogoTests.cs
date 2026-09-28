// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DDT.MachineConsole.Views;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The organisation's logo, which the agent downloads and passes as a path. It shows at the right end of the header.
public sealed class LogoTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-console-logo-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // A logo of the kind the header wants: light on a transparent background.
    public static string Write(string path, int width = 240, int height = 64)
    {
        using RenderTargetBitmap logo = new(new PixelSize(width, height));

        using (DrawingContext context = logo.CreateDrawingContext())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xED)), null, new Rect(0, 0, height, height), 10, 10);
            context.DrawText(
                new FormattedText("Contoso", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, height * 0.55, Brushes.White),
                new Point(height + height * 0.25, height * 0.18));
        }

        logo.Save(path, PngBitmapEncoderOptions.Default);

        return path;
    }

    [Fact]
    public async Task ShowsTheLogoTheAgentNamesInTheHeader()
    {
        string path = Path.Combine(_directory, "console-logo-0123456789ab.png");

        (bool hasLogo, bool shown, double height, double width) = await Headless.RunAsync(() =>
        {
            TestConsole console = new TestConsole().Show(Scenarios.Running with { Logo = Write(path) });
            MainWindow window = new(console.Model, fullScreen: false) { Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                Image image = window.GetVisualDescendants().OfType<Image>().Single(control => control.Name == "CustomLogo");

                return (console.Model.Header.HasLogo, image.IsEffectivelyVisible, image.Bounds.Height, image.Bounds.Width);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(hasLogo);
        Assert.True(shown);
        Assert.Equal(32, height, 1);
        Assert.Equal(120, width, 1);
    }

    // A logo is never kept larger than the header draws it.
    [Fact]
    public async Task KeepsATallLogoSmall()
    {
        string path = Path.Combine(_directory, "tall.png");

        PixelSize size = await Headless.RunAsync(() =>
        {
            TestConsole console = new TestConsole().Show(Scenarios.Running with { Logo = Write(path, 400, 1000) });

            return console.Model.Header.Logo!.PixelSize;
        });

        Assert.Equal(new PixelSize(51, 128), size);
    }

    [Fact]
    public async Task ShowsNoneForAFileThatIsNotAPictureOrNoLogo()
    {
        string broken = Path.Combine(_directory, "broken.png");
        await File.WriteAllBytesAsync(broken, RandomNumberGenerator.GetBytes(500), TestContext.Current.CancellationToken);
        string good = Path.Combine(_directory, "good.png");

        (bool broke, bool before, bool after) = await Headless.RunAsync(() =>
        {
            TestConsole console = new TestConsole().Show(Scenarios.Running with { Logo = broken });
            bool broke = console.Model.Header.HasLogo;

            console.Show(Scenarios.Running with { Logo = Write(good) });
            bool before = console.Model.Header.HasLogo;

            console.Show(Scenarios.Running);

            return (broke, before, console.Model.Header.HasLogo);
        });

        Assert.False(broke);
        Assert.True(before);
        Assert.False(after);
    }
}
