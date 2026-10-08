// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// Keeps the agent's screen from being held. Scroll Lock and Ctrl+S stop what a terminal shows until a key lets it go
// on, and a program that writes to the terminal waits with it: a run would stand still without a word.
public sealed class LinuxTerminal(Action? release = null)
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    private readonly Action _release = release ?? Release;

    // Lets the terminal go on once a second, until the answer is disposed.
    public ITimer KeepFlowing(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return timeProvider.CreateTimer(_ => _release(), null, Every, Every);
    }

    // The kernel lets go on only what the same call stopped, so the terminal is stopped that way for a moment first.
    private static void Release()
    {
        _ = TerminalNativeMethods.ioctl(TerminalNativeMethods.StandardOutput, TerminalNativeMethods.FlowControl, TerminalNativeMethods.OutputOff);
        _ = TerminalNativeMethods.ioctl(TerminalNativeMethods.StandardOutput, TerminalNativeMethods.FlowControl, TerminalNativeMethods.OutputOn);
    }
}
