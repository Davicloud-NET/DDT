// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Input;

namespace DDT.MachineConsole.Keyboard;

// A key going down or up, as Avalonia gets it, and what it types. With Key.None it only types: a dead key's accent let
// go before a key that types nothing.
public sealed record KeyStroke(bool Down, Key Key, PhysicalKey PhysicalKey, KeyModifiers Modifiers, string? Text);
