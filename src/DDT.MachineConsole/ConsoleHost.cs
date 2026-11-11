// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole;

// What differs by the system the console runs on. TrustSessionAgent throws UnauthorizedAccessException for a pipe that
// isn't the agent's.
public sealed record ConsoleHost(
    UiLanguage StartLanguage,
    IMachinePower Power,
    ICommandPrompt Prompt,
    Action SignOut,
    Action<NamedPipeClientStream> TrustSessionAgent)
{
    public HostSystem System { get; init; }

    // A head that types by its own tables takes the layout the agent names. False for a layout it doesn't know.
    public Func<string?, bool> UseKeyboardLayout { get; init; } = _ => true;
}
