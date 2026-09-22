// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

public enum RegistrationRefusal
{
    None,

    // A machine the server has not seen before, while too many nobody approved are waiting.
    TooManyWaiting,

    // The service in Windows, with no run to continue: it removes itself.
    NothingToContinue,
}
