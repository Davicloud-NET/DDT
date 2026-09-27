// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Headless;
using DDT.MachineConsole.Controls;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console's App with its styles, fonts and tokens, drawn by Skia into memory instead of a window, so a test can
// press keys on a screen and save what it shows. One session serves every test, on its own UI thread.
public static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> s_session = new(() =>
    {
        RailModule.Animates = false;

        return HeadlessUnitTestSession.StartNew(typeof(Headless));
    });

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(App.FontOptions);

    public static Task RunAsync(Action action) => s_session.Value.Dispatch(action, TestContext.Current.CancellationToken);

    public static Task<T> RunAsync<T>(Func<T> action) => s_session.Value.Dispatch(action, TestContext.Current.CancellationToken);

    public static Task<T> RunAsync<T>(Func<Task<T>> action) => s_session.Value.Dispatch(action, TestContext.Current.CancellationToken);
}
