// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using DDT.MachineConsole.Views;
using Xunit;

namespace DDT.MachineConsole.Tests;

// Every screen drawn by Skia as the console draws it, at 1024 x 768 in both themes and both languages, and the run and
// the sign-in at the other screen sizes the console has to fill. With DDT_CONSOLE_SHOTS set to a folder, the pictures
// are saved there as PNG for a person to look at.
public sealed class ScreenshotTests
{
    private static readonly Dictionary<string, Action<TestConsole>> s_shots = new()
    {
        ["01-starting"] = console => console.Show(Scenarios.State(ConsoleStage.Starting)),
        ["02-connecting-failed"] = console => console.Show(Scenarios.Unreachable),
        ["03-sign-in-user-name"] = console => console
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(1, new SignInQuestion(SignInField.UserName, null, null)),
        ["04-sign-in-password-wrong"] = console => console
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(2, new SignInQuestion(SignInField.Password, "anna", "Wrong user name or password.")),
        ["05-sign-in-code"] = console => console
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(3, new SignInQuestion(SignInField.Code, "anna", null)),
        ["06-waiting-for-approval"] = console => console
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization) with { SignedInBy = "anna" }),
        ["07-waiting-for-sequence"] = console => console.Show(Scenarios.State(ConsoleStage.WaitingForSequence)),
        ["08-choose-sequence"] = console => console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(4, Scenarios.Sequences),
        ["09-choose-disk"] = console => console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(5, Scenarios.Disks),
        ["10-computer-name"] = console => console
            .Show(Scenarios.State(ConsoleStage.Choosing))
            .Ask(6, Scenarios.ComputerName("LAB-PC-0142-WEST has 16 characters; a computer name holds at most 15.")),
        ["11-erase"] = console => console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(7, Scenarios.Erase),
        ["12-erase-typed"] = console =>
        {
            console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(7, Scenarios.Erase);
            ((EraseViewModel)console.Model.Question!).Typed = "ERASE";
        },
        ["13-secure-boot"] = console => console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(8, Scenarios.SecureBoot),
        ["14-running"] = console => console.Show(Scenarios.Running),
        ["15-preparing"] = console => console.Show(Scenarios.Preparing),
        ["16-restarting"] = console => console.Show(Scenarios.Restarting),
        ["17-finished"] = console => console.Show(Scenarios.Finished),
        ["18-failed"] = console => console.Show(Scenarios.Failed),
        ["19-stopped"] = console => console.Show(Scenarios.Stopped),
        ["20-agent-ended"] = console =>
        {
            console.Show(Scenarios.Running);
            console.Model.Ended(LinkEnd.Closed);
        },
        ["21-log"] = console =>
        {
            console.Show(Scenarios.Running).Receive(new LogMessage(Scenarios.Lines));
            console.Model.OverlayShown = Overlay.Log;
        },
        ["22-machine"] = console =>
        {
            console.Show(Scenarios.Running);
            console.Model.OverlayShown = Overlay.Machine;
        },
        ["23-licences"] = console =>
        {
            console.Show(Scenarios.Running);
            console.Model.OverlayShown = Overlay.Licences;
        },
        ["24-restart-asked"] = console =>
        {
            console.Show(Scenarios.Finished);
            console.Model.Ended(LinkEnd.Closed);
            console.Model.Press(Avalonia.Input.Key.F8);
        },
    };

    public static TheoryData<string> Shots => [.. s_shots.Keys];

    public static TheoryData<string, int, int> Sizes => new()
    {
        { "14-running", 1280, 800 },
        { "14-running", 1920, 1080 },
        { "14-running", 2560, 1440 },
        { "03-sign-in-user-name", 1280, 800 },
        { "03-sign-in-user-name", 1920, 1080 },
        { "03-sign-in-user-name", 2560, 1440 },
        { "08-choose-sequence", 1920, 1080 },
        { "21-log", 1920, 1080 },
    };

    private static string? Folder => Environment.GetEnvironmentVariable("DDT_CONSOLE_SHOTS");

    [Theory]
    [MemberData(nameof(Shots))]
    public async Task DrawsTheScreenInBothThemesAndLanguages(string shot)
    {
        foreach (bool dark in new[] { true, false })
        {
            foreach (UiLanguage language in new[] { UiLanguage.English, UiLanguage.German })
            {
                string name = $"{shot}-{(dark ? "dark" : "light")}-{(language == UiLanguage.German ? "de" : "en")}";
                (int width, int height) = await Headless.RunAsync(() => Render(shot, language, dark, 1024, 768, name));

                Assert.Equal((1024, 768), (width, height));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task ScalesTheScreenToTheScreenSize(string shot, int width, int height)
    {
        (int drawnWidth, int drawnHeight) = await Headless.RunAsync(
            () => Render(shot, UiLanguage.English, dark: true, width, height, $"{shot}-dark-en-{width}x{height}"));

        Assert.Equal((width, height), (drawnWidth, drawnHeight));
    }

    private static (int Width, int Height) Render(string shot, UiLanguage language, bool dark, int width, int height, string name)
    {
        TestConsole console = new(language, canRestart: true);
        console.Model.IsDark = dark;
        s_shots[shot](console);

        MainWindow window = new(console.Model, fullScreen: false) { Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            using WriteableBitmap frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Nothing was drawn.");

            if (Folder is { } folder)
            {
                Directory.CreateDirectory(folder);
                frame.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
            }

            Assert.True(HasContent(frame), $"{name} is a blank picture.");

            return (frame.PixelSize.Width, frame.PixelSize.Height);
        }
        finally
        {
            window.Close();
        }
    }

    // More than a handful of colours: a screen that failed to draw is one colour.
    private static bool HasContent(WriteableBitmap frame)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        HashSet<int> colours = [];
        int[] row = new int[buffer.Size.Width];

        for (int y = 0; y < buffer.Size.Height && colours.Count < 16; y += 7)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);

            foreach (int pixel in row)
            {
                colours.Add(pixel);
            }
        }

        return colours.Count >= 16;
    }
}
