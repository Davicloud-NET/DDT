// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

public enum SettingFieldKind
{
    // One value: a text, a number, a switch, or a list kept as comma separated text.
    Value,

    // A map or a list, which configuration sets and locks as a whole, never entry by entry.
    Collection,

    // Written, never read back. It is stored encrypted, and the page only learns whether it is set.
    Secret,
}
