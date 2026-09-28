// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// Why a console over a pipe went away, as the log names it after the console.
internal static class ConsoleFailure
{
    public static string NotAMessage(ConsoleProtocolException exception) => $"sent something that is not a console message ({exception.Message})";

    public static string ClosedPipe(IOException exception) => $"closed its pipe ({exception.Message})";
}
