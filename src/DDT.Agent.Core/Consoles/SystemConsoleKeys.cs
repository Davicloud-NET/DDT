// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Consoles;

// The process's own console.
internal sealed class SystemConsoleKeys : IConsoleKeys
{
    public bool IsRedirected => Console.IsInputRedirected;

    public bool KeyAvailable => Console.KeyAvailable;

    public void TakeKey() => Console.ReadKey(intercept: true);
}
