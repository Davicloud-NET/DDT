// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Restarts the installed Windows for the run with shutdown.exe. It's a planned restart for maintenance, with a reason
// that says why in the event log. The restart stops the service on its way.
public sealed class WindowsRebooter(IToolRunner tools) : IRebooter
{
    public const string Reason = "DDT restarts Windows for its task sequence.";

    public static string ShutdownPath => Path.Combine(Environment.SystemDirectory, "shutdown.exe");

    public static IReadOnlyList<string> Arguments { get; } = ["/r", "/t", "0", "/d", "p:4:1", "/c", Reason];

    public Task RebootAsync(RestartInto into, CancellationToken cancellationToken) =>
        tools.RunAsync(ShutdownPath, Arguments, cancellationToken);
}
