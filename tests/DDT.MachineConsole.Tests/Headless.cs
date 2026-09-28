// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Headless;
using DDT.MachineConsole.Controls;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console's App, drawn by Skia into memory, so a test can press keys on a real window and capture it. One session
// on its own UI thread serves every test.
public static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> s_session = new(() =>
    {
        // Motion is off, so everything a test checks has settled. The motion tests turn it on themselves.
        RailModule.Animates = false;
        Motion.IsEnabled = false;

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
