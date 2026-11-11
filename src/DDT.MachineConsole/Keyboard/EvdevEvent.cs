// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Keyboard;

// One input_event of a /dev/input/event* device, without its time.
public readonly record struct EvdevEvent(ushort Type, ushort Code, int Value);
