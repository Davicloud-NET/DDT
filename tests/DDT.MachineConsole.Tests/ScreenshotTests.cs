// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
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

// Draws every screen like the console does, in both themes and languages and at other screen sizes. If
// DDT_CONSOLE_SHOTS names a folder, the pictures are saved there for a person to look at.
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
        ["25-media-keys"] = console =>
        {
            console.Show(Scenarios.Running);
            console.Model.Press(Avalonia.Input.Key.VolumeMute);
        },
        ["26-close-refused"] = console =>
        {
            console.Show(Scenarios.Running);
            console.Model.RefuseClose();
        },
        ["27-logo"] = console => console.Show(Scenarios.Running with
        {
            Logo = LogoTests.Write(Path.Combine(Path.GetTempPath(), $"ddt-console-logo-{Guid.NewGuid():N}.png")),
        }),
        ["28-inputs"] = console =>
        {
            console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(9, Scenarios.Inputs());
            Answer((InputsViewModel)console.Model.Question!);
        },
        ["29-inputs-refused"] = console =>
        {
            console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(9, Scenarios.Inputs());
            InputsViewModel inputs = (InputsViewModel)console.Model.Question!;
            Answer(inputs);
            ((TextFieldViewModel)inputs.Fields[1]).Text = "annaa";
            inputs.SubmitCommand.Execute(null);
            console.Ask(10, Scenarios.Inputs(refused: true));
        },
        ["30-pause"] = console => console.Show(Scenarios.Paused).Ask(11, Scenarios.Pause),
        ["31-waiting-for-inputs"] = console => console.Show(Scenarios.WaitingForInputs),
        ["32-running-tree"] = console => console.Show(Scenarios.RunningTree),
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
        { "28-inputs", 1920, 1080 },
        { "30-pause", 1920, 1080 },
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
                PixelSize drawn = await Headless.RunAsync(() => Render(shot, language, dark, new PixelSize(1024, 768), name));

                Assert.Equal(new PixelSize(1024, 768), drawn);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task ScalesTheScreenToTheScreenSize(string shot, int width, int height)
    {
        PixelSize drawn = await Headless.RunAsync(
            () => Render(shot, UiLanguage.English, dark: true, new PixelSize(width, height), $"{shot}-dark-en-{width}x{height}"));

        Assert.Equal(new PixelSize(width, height), drawn);
    }

    private static PixelSize Render(string shot, UiLanguage language, bool dark, PixelSize size, string name)
    {
        TestConsole console = new(language, canRestart: true);
        console.Model.IsDark = dark;
        s_shots[shot](console);

        MainWindow window = new(console.Model, fullScreen: false) { Width = size.Width, Height = size.Height };
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

            return frame.PixelSize;
        }
        finally
        {
            window.Close();
        }
    }

    // What a technician types into the inputs: an owner and the account's user name and password.
    private static void Answer(InputsViewModel inputs)
    {
        ((TextFieldViewModel)inputs.Fields[1]).Text = "anna.berger";
        AccountFieldViewModel account = (AccountFieldViewModel)inputs.Fields[^1];
        account.UserName = @"LAB\svc-join";
        account.Password = "correct horse";
    }

    // Checks for more than a handful of colours, because a screen that failed to draw is a single colour.
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
