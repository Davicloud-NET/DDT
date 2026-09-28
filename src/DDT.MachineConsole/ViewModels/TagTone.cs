// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.ViewModels;

// How a state tag looks. It's filled for something under way or waiting for someone, and outlined for something at
// rest.
public enum TagTone
{
    Run,
    Attention,
    Fail,
    Ok,
    Idle,
}
