// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DDT.MachineConsole;

public sealed partial class App : Application
{
    public const string BodyFont = "avares://ddt-console/Assets/Fonts#Archivo 400 100";

    // Program sets this before Avalonia starts. Tests create their windows themselves.
    public static ConsoleStartup? Startup { get; set; }

    public static FontManagerOptions FontOptions => new() { DefaultFamilyName = BodyFont };

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Startup is { } startup)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = startup.Open(this, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
