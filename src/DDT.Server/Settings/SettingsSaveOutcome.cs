// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

public enum SettingsSaveOutcome
{
    Saved,

    // Someone saved the section since the version the update names.
    Conflict,

    // Problems, a refused secret or an unconfirmed warning: nothing was saved.
    Invalid,

    // The update changes fields that need a fresh proof of identity, which Fields names.
    Reauthenticate,

    // This process cannot read the key ring of the stored secrets, so it saves nothing.
    KeyRingUnreadable,
}
